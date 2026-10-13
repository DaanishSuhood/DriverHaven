using DriverUpdater.Core.Models;

namespace DriverUpdater.App.ViewModels;

/// <summary>One row in the results dialog.</summary>
public sealed record InstallResultRow(string Device, string Update, string Result, string Detail, bool IsFailure);

/// <summary>The install results dialog: a heading, notes about the safety steps, and a row per driver.</summary>
public sealed class InstallResultsViewModel(InstallResult result)
{
    public InstallResult Result { get; } = result;

    public string Heading => Result.Outcome switch
    {
        InstallOutcome.DeclinedWithoutRestorePoint or InstallOutcome.BackupFailed or InstallOutcome.DeclinedPendingRestart
            => "Nothing was installed",
        InstallOutcome.Cancelled => Result.SucceededCount == 0 ? "Install cancelled" : $"Install cancelled after {Count(Result.SucceededCount)}",
        _ when Result.FailedCount == 0 => $"{Count(Result.SucceededCount)} installed",
        _ when Result.SucceededCount == 0 => $"{Count(Result.FailedCount)} failed to install",
        _ => $"{Count(Result.SucceededCount)} installed, {Result.FailedCount} failed",
    };

    /// <summary>Shows "Restart now": after installs that need one, or when the user stopped to restart first.</summary>
    public bool RebootRequired => Result.RebootRequired || Result.Outcome == InstallOutcome.DeclinedPendingRestart;

    public string Notes => string.Join("\n", NoteLines());

    public IReadOnlyList<InstallResultRow> Rows { get; } = result.Items.Select(ToRow).ToList();

    private IEnumerable<string> NoteLines()
    {
        if (Result.Message is { } message)
            yield return message;
        if (Result.RestorePoint is { } restorePoint)
            yield return $"Restore point: {restorePoint.Message}";
        if (Result.Backup is { } backup)
            yield return $"Backup of replaced drivers: {backup.Folder}";
        if (Result.RebootRequired)
            yield return "Restart the PC to finish installing. Until then, the old drivers may stay in use.";
    }

    private static InstallResultRow ToRow(DriverInstallItemResult item)
    {
        var match = item.Match;
        var device = match.PrimaryDevice?.DeviceName ?? match.Update.DriverModel ?? "New device";
        var result = item.Status switch
        {
            DriverInstallStatus.Installed => item.RebootRequired ? "Installed, restart required" : "Installed",
            DriverInstallStatus.InstalledWithErrors => item.RebootRequired ? "Installed with errors, restart required" : "Installed with errors",
            DriverInstallStatus.DownloadFailed => "Download failed",
            DriverInstallStatus.InstallFailed => "Install failed",
            _ => "Not installed",
        };
        return new InstallResultRow(device, match.Update.Title, result, item.Detail ?? "", item.Failed);
    }

    private static string Count(int drivers) => $"{drivers} driver{(drivers == 1 ? "" : "s")}";
}
