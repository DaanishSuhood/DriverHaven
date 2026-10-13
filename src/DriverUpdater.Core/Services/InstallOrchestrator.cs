using DriverUpdater.Core.Infrastructure;
using DriverUpdater.Core.Models;
using Microsoft.Extensions.Logging;

namespace DriverUpdater.Core.Services;

/// <summary>
/// Runs an install: pending-restart check → restore point → backup of the drivers being replaced → download each →
/// install each.
/// Downloading everything first means a cancel or a download failure never leaves a half-installed batch.
/// Cancellation is honoured only between steps; a download or install that has started always finishes.
/// </summary>
public sealed class InstallOrchestrator(
    IPendingRestartService pendingRestart,
    IRestorePointService restorePoints,
    IDriverBackupService driverBackup,
    IDriverUpdateService driverUpdates,
    ILogger<InstallOrchestrator> logger)
{
    /// <param name="continueWithPendingRestart">
    /// Asked when Windows is waiting for a restart; return false to stop before anything changes.
    /// </param>
    /// <param name="continueWithoutRestorePoint">
    /// Asked when no usable restore point exists; return false to stop before anything changes.
    /// </param>
    /// <remarks>Both callbacks are invoked on the caller's synchronization context, so they may show UI.</remarks>
    public async Task<InstallResult> RunAsync(
        IReadOnlyList<MatchedDriverUpdate> updates,
        Func<PendingRestartCheck, bool> continueWithPendingRestart,
        Func<RestorePointResult, bool> continueWithoutRestorePoint,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(updates.Count);

        var run = new Run(updates, progress);
        logger.LogInformation("Installing {Count} driver update(s): {Titles}",
            updates.Count, string.Join("; ", updates.Select(u => $"{u.Update.Title} [{u.Update.UpdateId}]")));

        // 0. A pending restart can make installs fail with confusing errors, so check once up front.
        var restartCheck = await CheckPendingRestartAsync(cancellationToken);
        if (restartCheck.IsPending && !continueWithPendingRestart(restartCheck))
            return run.Finish(InstallOutcome.DeclinedPendingRestart, null, null,
                "Stopped so you can restart the PC first. Nothing was changed.");
        if (cancellationToken.IsCancellationRequested)
            return run.Finish(InstallOutcome.Cancelled, null, null, "Cancelled before a restore point was created. Nothing was changed.");

        // 1. Restore point.
        run.Report(InstallStage.RestorePoint, "Creating a system restore point…");
        RestorePointResult restorePoint;
        try
        {
            restorePoint = await restorePoints.CreateAsync(RestorePointDescription(updates.Count), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return run.Finish(InstallOutcome.Cancelled, null, null, "Cancelled before a restore point was created. Nothing was changed.");
        }
        run.Step(InstallStage.RestorePoint, restorePoint.Message);

        if (!restorePoint.HasUsableRestorePoint && !continueWithoutRestorePoint(restorePoint))
            return run.Finish(InstallOutcome.DeclinedWithoutRestorePoint, restorePoint, null,
                "Stopped because no restore point could be created. Nothing was changed.");
        if (cancellationToken.IsCancellationRequested)
            return run.Cancelled(restorePoint, null);

        // 2. Back up the drivers about to be replaced.
        var devices = updates.SelectMany(u => u.Devices).DistinctBy(d => d.DeviceId, StringComparer.OrdinalIgnoreCase).ToList();
        DriverBackupResult? backup = null;
        if (devices.Count == 0)
        {
            run.Step(InstallStage.Backup, "No installed drivers will be replaced, so there is nothing to back up.");
        }
        else
        {
            run.Report(InstallStage.Backup, $"Backing up the current drivers of {Plural(devices.Count, "device")}…");
            try
            {
                backup = await driverBackup.BackupAsync(devices, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return run.Cancelled(restorePoint, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Driver backup failed; installing nothing");
                return run.Finish(InstallOutcome.BackupFailed, restorePoint, null,
                    $"The current drivers could not be backed up, so nothing was installed: {ex.Message}");
            }
            run.Step(InstallStage.Backup,
                $"Backup: {Plural(backup.ExportedCount, "driver package")} exported, {backup.SkippedCount} skipped, " +
                $"{backup.FailedCount} failed ({backup.Folder}).");
        }

        // 3. Download each.
        for (var i = 0; i < updates.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                return run.Cancelled(restorePoint, backup);

            var name = Describe(updates[i]);
            run.Report(InstallStage.Download, $"Downloading {i + 1} of {updates.Count}: {name}…");
            var result = await RunStepAsync(() => driverUpdates.DownloadAsync(updates[i].Update));
            if (result.Succeeded)
            {
                run.Step(InstallStage.Download, $"Downloaded {name}.");
            }
            else
            {
                run.Items[i] = run.Items[i] with { Status = DriverInstallStatus.DownloadFailed, HResult = result.HResult, Detail = Reason(result) };
                run.Step(InstallStage.Download, $"Download failed: {name}. {Reason(result)}");
                run.SkipStep(); // its install
            }
        }

        // 4. Install each one that downloaded.
        var installCount = run.Items.Count(item => item.Status == DriverInstallStatus.NotAttempted);
        var installNumber = 0;
        for (var i = 0; i < updates.Count; i++)
        {
            if (run.Items[i].Status != DriverInstallStatus.NotAttempted)
                continue;
            if (cancellationToken.IsCancellationRequested)
                return run.Cancelled(restorePoint, backup);

            var name = Describe(updates[i]);
            run.Report(InstallStage.Install, $"Installing {++installNumber} of {installCount}: {name}…");
            var result = await RunStepAsync(() => driverUpdates.InstallAsync(updates[i].Update));
            if (result.Succeeded)
            {
                var withErrors = result.ResultCode == OperationResultCode.SucceededWithErrors;
                run.Items[i] = run.Items[i] with
                {
                    Status = withErrors ? DriverInstallStatus.InstalledWithErrors : DriverInstallStatus.Installed,
                    RebootRequired = result.RebootRequired,
                    HResult = result.HResult,
                    Detail = withErrors ? "Installed, but Windows Update reported errors." : null,
                };
                run.Step(InstallStage.Install, $"Installed {name}{(result.RebootRequired ? " (restart required)" : "")}.");
            }
            else
            {
                run.Items[i] = run.Items[i] with { Status = DriverInstallStatus.InstallFailed, HResult = result.HResult, Detail = Reason(result) };
                run.Step(InstallStage.Install, $"Install failed: {name}. {Reason(result)}");
            }
        }

        return run.Finish(InstallOutcome.Completed, restorePoint, backup, null);
    }

    /// <summary>A failed check must not block installs: Windows Update still refuses one that truly needs a restart first.</summary>
    private async Task<PendingRestartCheck> CheckPendingRestartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var check = PendingRestartEvaluator.Evaluate(await pendingRestart.GetSignalsAsync(cancellationToken));
            if (check.IsPending)
                logger.LogWarning("A restart is pending: {Reasons}", string.Join(" ", check.Reasons));
            return check;
        }
        catch (OperationCanceledException)
        {
            return PendingRestartCheck.None; // the caller sees the cancellation
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't check for a pending restart; continuing");
            return PendingRestartCheck.None;
        }
    }

    /// <summary>A short label for logs and progress: the device name (or update title) plus the new version.</summary>
    public static string Describe(MatchedDriverUpdate match)
    {
        var name = match.PrimaryDevice?.DeviceName ?? match.Update.DriverModel ?? match.Update.Title;
        return match.Update.DriverVersion is { } version && !name.Contains(version, StringComparison.Ordinal)
            ? $"{name} {version}"
            : name;
    }

    private static string RestorePointDescription(int count) =>
        $"{ProductInfo.Name} - before installing {Plural(count, "driver update")}";

    private static string Reason(UpdateOperationResult result) =>
        result.Message
        ?? (result.HResult != 0 ? WindowsUpdateErrors.Describe(result.HResult) : $"Windows Update reported \"{result.ResultCode}\".");

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    /// <summary>One update's failure (even an unexpected exception) must not stop the others.</summary>
    private async Task<UpdateOperationResult> RunStepAsync(Func<Task<UpdateOperationResult>> step)
    {
        try
        {
            return await step();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Driver update step failed unexpectedly");
            return UpdateOperationResult.Failed(ex.HResult, ex is WindowsUpdateException ? ex.Message : $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>Mutable state of one run: per-update results and the progress counter.</summary>
    private sealed class Run(IReadOnlyList<MatchedDriverUpdate> updates, IProgress<InstallProgress>? progress)
    {
        private readonly int _totalSteps = 2 + updates.Count * 2;
        private int _completedSteps;

        public DriverInstallItemResult[] Items { get; } =
            updates.Select(u => new DriverInstallItemResult(u, DriverInstallStatus.NotAttempted)).ToArray();

        public void Report(InstallStage stage, string message) =>
            progress?.Report(new InstallProgress(stage, _completedSteps, _totalSteps, message));

        public void Step(InstallStage stage, string message)
        {
            _completedSteps++;
            Report(stage, message);
        }

        public void SkipStep() => _completedSteps++;

        public InstallResult Cancelled(RestorePointResult? restorePoint, DriverBackupResult? backup)
        {
            var installed = Items.Count(i => i.Succeeded);
            return Finish(InstallOutcome.Cancelled, restorePoint, backup, installed == 0
                ? "Cancelled. No drivers were installed."
                : $"Cancelled after installing {installed} of {Items.Length} updates.");
        }

        public InstallResult Finish(InstallOutcome outcome, RestorePointResult? restorePoint, DriverBackupResult? backup, string? message)
        {
            var result = new InstallResult(outcome, Items.ToList(), restorePoint, backup, message);
            _completedSteps = _totalSteps;
            Report(InstallStage.Finished, message ?? Summarize(result));
            return result;
        }

        private static string Summarize(InstallResult result) =>
            $"Finished: {result.SucceededCount} installed, {result.FailedCount} failed."
            + (result.RebootRequired ? " Restart the PC to finish." : "");
    }
}
