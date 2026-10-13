using DriverUpdater.Core.Models;
using DriverUpdater.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverUpdater.Core.Tests;

public class InstallOrchestratorTests
{
    private readonly List<string> _calls = [];
    private readonly FakeRestorePoints _restorePoints;
    private readonly FakeBackup _backup;
    private readonly FakeDriverUpdates _driverUpdates;
    private readonly FakePendingRestart _pendingRestart;
    private readonly InstallOrchestrator _orchestrator;

    public InstallOrchestratorTests()
    {
        _restorePoints = new FakeRestorePoints(_calls);
        _backup = new FakeBackup(_calls);
        _driverUpdates = new FakeDriverUpdates(_calls);
        _pendingRestart = new FakePendingRestart(_calls);
        _orchestrator = new InstallOrchestrator(_pendingRestart, _restorePoints, _backup, _driverUpdates,
            NullLogger<InstallOrchestrator>.Instance);
    }

    private static DeviceDriverInfo Device(string id) => new() { DeviceId = id, DeviceName = $"Device {id}", InfName = "oem1.inf" };

    private static MatchedDriverUpdate Match(string updateId, params DeviceDriverInfo[] devices) =>
        new(new DriverUpdateInfo { UpdateId = updateId, Title = $"Vendor - Class - {updateId}" }, devices);

    private Task<InstallResult> RunAsync(IReadOnlyList<MatchedDriverUpdate> updates, bool continueWithoutRestorePoint = false,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default,
        bool continueWithPendingRestart = false) =>
        _orchestrator.RunAsync(updates, _ => continueWithPendingRestart, _ => continueWithoutRestorePoint, progress, cancellationToken);

    [Fact]
    public async Task Runs_restore_point_then_backup_then_all_downloads_then_all_installs()
    {
        var result = await RunAsync([Match("A", Device("1")), Match("B", Device("2"))]);

        Assert.Equal(["restart-check", "restore", "backup:1,2", "download:A", "download:B", "install:A", "install:B"], _calls);
        Assert.Equal(InstallOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.SucceededCount);
        Assert.All(result.Items, i => Assert.Equal(DriverInstallStatus.Installed, i.Status));
    }

    [Fact]
    public async Task Backs_up_each_shared_device_once_and_skips_backup_for_unmatched_updates()
    {
        var shared = Device("1");
        await RunAsync([Match("A", shared), Match("B", Device("1"), Device("2"))]);
        Assert.Contains("backup:1,2", _calls);

        _calls.Clear();
        var result = await RunAsync([Match("C")]);
        Assert.DoesNotContain(_calls, c => c.StartsWith("backup", StringComparison.Ordinal));
        Assert.Null(result.Backup);
        Assert.Equal(InstallOutcome.Completed, result.Outcome);
    }

    [Fact]
    public async Task Stops_before_any_change_when_a_restart_is_pending_and_user_declines()
    {
        _pendingRestart.Signals = new PendingRestartSignals(WindowsUpdateRebootRequired: true);
        PendingRestartCheck? asked = null;

        var result = await _orchestrator.RunAsync([Match("A", Device("1"))], check => { asked = check; return false; }, _ => true);

        Assert.True(asked?.IsPending);
        Assert.Equal(["restart-check"], _calls);
        Assert.Equal(InstallOutcome.DeclinedPendingRestart, result.Outcome);
        Assert.Null(result.RestorePoint);
        Assert.Contains("Nothing was changed", result.Message);
    }

    [Fact]
    public async Task Continues_with_a_pending_restart_when_user_agrees()
    {
        _pendingRestart.Signals = new PendingRestartSignals(DevicesNeedingRestart: ["Camera"]);

        var result = await RunAsync([Match("A", Device("1"))], continueWithPendingRestart: true);

        Assert.Equal(InstallOutcome.Completed, result.Outcome);
        Assert.Contains("install:A", _calls);
    }

    [Fact]
    public async Task Does_not_ask_about_a_restart_for_unrelated_pending_file_renames()
    {
        _pendingRestart.Signals = new PendingRestartSignals(PendingFileRenames: [@"\??\C:\Users\me\AppData\Local\Temp\setup.tmp"]);
        var asked = false;

        var result = await _orchestrator.RunAsync([Match("A", Device("1"))], _ => asked = true, _ => true);

        Assert.False(asked);
        Assert.Equal(InstallOutcome.Completed, result.Outcome);
    }

    [Fact]
    public async Task A_failed_restart_check_does_not_block_the_install()
    {
        _pendingRestart.Throw = new InvalidOperationException("WMI is broken");
        var asked = false;

        var result = await _orchestrator.RunAsync([Match("A", Device("1"))], _ => asked = true, _ => true);

        Assert.False(asked);
        Assert.Equal(InstallOutcome.Completed, result.Outcome);
    }

    [Fact]
    public async Task Stops_before_any_change_when_restore_point_fails_and_user_declines()
    {
        _restorePoints.Result = new RestorePointResult(RestorePointOutcome.Disabled, "System Protection is off.");
        RestorePointResult? asked = null;

        var result = await _orchestrator.RunAsync([Match("A", Device("1"))], _ => true, rp => { asked = rp; return false; });

        Assert.Same(_restorePoints.Result, asked);
        Assert.Equal(["restart-check", "restore"], _calls);
        Assert.Equal(InstallOutcome.DeclinedWithoutRestorePoint, result.Outcome);
        Assert.Equal(DriverInstallStatus.NotAttempted, Assert.Single(result.Items).Status);
    }

    [Fact]
    public async Task Continues_without_restore_point_when_user_agrees()
    {
        _restorePoints.Result = new RestorePointResult(RestorePointOutcome.Failed, "Failed.");

        var result = await RunAsync([Match("A", Device("1"))], continueWithoutRestorePoint: true);

        Assert.Equal(InstallOutcome.Completed, result.Outcome);
        Assert.Contains("install:A", _calls);
    }

    [Fact]
    public async Task Does_not_ask_when_a_recent_restore_point_exists()
    {
        _restorePoints.Result = new RestorePointResult(RestorePointOutcome.SkippedRecentExists, "Recent one exists.");
        var asked = false;

        var result = await _orchestrator.RunAsync([Match("A", Device("1"))], _ => true, _ => asked = true);

        Assert.False(asked);
        Assert.Equal(InstallOutcome.Completed, result.Outcome);
    }

    [Fact]
    public async Task Cancel_during_a_download_finishes_it_but_installs_nothing()
    {
        using var cts = new CancellationTokenSource();
        _driverUpdates.OnDownload = id => { if (id == "A") cts.Cancel(); };

        var result = await RunAsync([Match("A", Device("1")), Match("B", Device("2"))], cancellationToken: cts.Token);

        Assert.Equal(["restart-check", "restore", "backup:1,2", "download:A"], _calls);
        Assert.Equal(InstallOutcome.Cancelled, result.Outcome);
        Assert.All(result.Items, i => Assert.Equal(DriverInstallStatus.NotAttempted, i.Status));
        Assert.NotNull(result.Backup);
    }

    [Fact]
    public async Task Cancel_during_an_install_lets_it_finish_and_skips_the_rest()
    {
        using var cts = new CancellationTokenSource();
        _driverUpdates.OnInstall = id => { if (id == "A") cts.Cancel(); };

        var result = await RunAsync([Match("A", Device("1")), Match("B", Device("2"))], cancellationToken: cts.Token);

        Assert.Equal("install:A", _calls[^1]);
        Assert.Equal(InstallOutcome.Cancelled, result.Outcome);
        Assert.Equal([DriverInstallStatus.Installed, DriverInstallStatus.NotAttempted], result.Items.Select(i => i.Status));
        Assert.Contains("1 of 2", result.Message);
    }

    [Fact]
    public async Task Cancel_while_creating_the_restore_point_changes_nothing()
    {
        using var cts = new CancellationTokenSource();
        _restorePoints.OnCreate = () => { cts.Cancel(); cts.Token.ThrowIfCancellationRequested(); };

        var result = await RunAsync([Match("A", Device("1"))], cancellationToken: cts.Token);

        Assert.Equal(InstallOutcome.Cancelled, result.Outcome);
        Assert.Equal(["restart-check", "restore"], _calls);
        Assert.Null(result.RestorePoint);
    }

    [Fact]
    public async Task Download_failure_skips_that_install_only()
    {
        _driverUpdates.DownloadResults["A"] = UpdateOperationResult.Failed(unchecked((int)0x8024402C));

        var result = await RunAsync([Match("A", Device("1")), Match("B", Device("2"))]);

        Assert.DoesNotContain("install:A", _calls);
        Assert.Contains("install:B", _calls);
        var failed = result.Items[0];
        Assert.Equal(DriverInstallStatus.DownloadFailed, failed.Status);
        Assert.Equal(unchecked((int)0x8024402C), failed.HResult);
        Assert.StartsWith("Can't reach Windows Update", failed.Detail);
        Assert.Equal((1, 1), (result.SucceededCount, result.FailedCount));
    }

    [Fact]
    public async Task Install_failure_and_unexpected_exception_are_recorded_per_update()
    {
        _driverUpdates.InstallResults["A"] = UpdateOperationResult.Failed(0, "Custom reason.");
        _driverUpdates.OnInstall = id => { if (id == "B") throw new InvalidOperationException("boom"); };

        var result = await RunAsync([Match("A", Device("1")), Match("B", Device("2")), Match("C", Device("3"))]);

        Assert.Equal(InstallOutcome.Completed, result.Outcome);
        Assert.Equal([DriverInstallStatus.InstallFailed, DriverInstallStatus.InstallFailed, DriverInstallStatus.Installed],
            result.Items.Select(i => i.Status));
        Assert.Equal("Custom reason.", result.Items[0].Detail);
        Assert.Contains("boom", result.Items[1].Detail);
    }

    [Fact]
    public async Task Aggregates_reboot_required_and_marks_succeeded_with_errors()
    {
        _driverUpdates.InstallResults["A"] = new UpdateOperationResult(OperationResultCode.Succeeded, RebootRequired: true);
        _driverUpdates.InstallResults["B"] = new UpdateOperationResult(OperationResultCode.SucceededWithErrors);

        var result = await RunAsync([Match("A", Device("1")), Match("B", Device("2"))]);

        Assert.True(result.RebootRequired);
        Assert.True(result.Items[0].RebootRequired);
        Assert.Equal(DriverInstallStatus.InstalledWithErrors, result.Items[1].Status);
        Assert.Equal(2, result.SucceededCount);
    }

    [Fact]
    public async Task No_reboot_required_when_no_update_asks_for_one()
    {
        var result = await RunAsync([Match("A", Device("1"))]);

        Assert.False(result.RebootRequired);
    }

    [Fact]
    public async Task Backup_write_failure_installs_nothing()
    {
        _backup.Throw = new IOException("Disk full");

        var result = await RunAsync([Match("A", Device("1"))]);

        Assert.Equal(InstallOutcome.BackupFailed, result.Outcome);
        Assert.DoesNotContain(_calls, c => c.StartsWith("download", StringComparison.Ordinal));
        Assert.Contains("Disk full", result.Message);
    }

    [Fact]
    public async Task Reports_progress_from_zero_to_complete()
    {
        var reports = new List<InstallProgress>();
        var progress = new SynchronousProgress(reports.Add);

        await RunAsync([Match("A", Device("1")), Match("B", Device("2"))], progress: progress);

        Assert.Equal(0, reports[0].CompletedSteps);
        Assert.Equal(InstallStage.RestorePoint, reports[0].Stage);
        Assert.All(reports, r => Assert.Equal(6, r.TotalSteps));
        Assert.Equal(reports.Select(r => r.CompletedSteps).Order(), reports.Select(r => r.CompletedSteps));
        Assert.Equal(InstallStage.Finished, reports[^1].Stage);
        Assert.Equal(1.0, reports[^1].Fraction);
        Assert.Contains(reports, r => r.Message == "Installing 2 of 2: Device 2…");
    }

    [Fact]
    public async Task Progress_stays_monotonic_when_a_download_fails()
    {
        _driverUpdates.DownloadResults["A"] = UpdateOperationResult.Failed(1);
        var reports = new List<InstallProgress>();

        await RunAsync([Match("A", Device("1")), Match("B", Device("2"))], progress: new SynchronousProgress(reports.Add));

        Assert.Equal(reports.Select(r => r.CompletedSteps).Order(), reports.Select(r => r.CompletedSteps));
        Assert.Contains(reports, r => r.Message.StartsWith("Installing 1 of 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejects_an_empty_selection()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => RunAsync([]));
    }

    [Theory]
    [InlineData("Intel UHD Graphics", "31.0.101.5000", "Intel UHD Graphics 31.0.101.5000")]
    [InlineData(null, "1.2.3", "Model X 1.2.3")]
    [InlineData(null, null, "Model X")]
    public void Describe_uses_device_name_and_version(string? deviceName, string? version, string expected)
    {
        var devices = deviceName is null ? Array.Empty<DeviceDriverInfo>() : [new DeviceDriverInfo { DeviceId = "1", DeviceName = deviceName }];
        var match = new MatchedDriverUpdate(
            new DriverUpdateInfo { UpdateId = "A", Title = "t", DriverModel = "Model X", DriverVersion = version }, devices);

        Assert.Equal(expected, InstallOrchestrator.Describe(match));
    }

    /// <summary>Progress&lt;T&gt; posts asynchronously; tests need reports in order and complete on return.</summary>
    private sealed class SynchronousProgress(Action<InstallProgress> report) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => report(value);
    }

    private sealed class FakePendingRestart(List<string> calls) : IPendingRestartService
    {
        public PendingRestartSignals Signals { get; set; } = PendingRestartSignals.None;

        public Exception? Throw { get; set; }

        public Task<PendingRestartSignals> GetSignalsAsync(CancellationToken cancellationToken = default)
        {
            calls.Add("restart-check");
            if (Throw is not null)
                throw Throw;
            return Task.FromResult(Signals);
        }
    }

    private sealed class FakeRestorePoints(List<string> calls) : IRestorePointService
    {
        public RestorePointResult Result { get; set; } = new(RestorePointOutcome.Created, "Restore point created.");

        public Action? OnCreate { get; set; }

        public Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken = default)
        {
            calls.Add("restore");
            OnCreate?.Invoke();
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeBackup(List<string> calls) : IDriverBackupService
    {
        public Exception? Throw { get; set; }

        public Task<DriverBackupResult> BackupAsync(IReadOnlyCollection<DeviceDriverInfo> devices, CancellationToken cancellationToken = default)
        {
            calls.Add($"backup:{string.Join(',', devices.Select(d => d.DeviceId))}");
            if (Throw is not null)
                throw Throw;
            return Task.FromResult(new DriverBackupResult(@"C:\backup", @"C:\backup\manifest.json", []));
        }
    }

    private sealed class FakeDriverUpdates(List<string> calls) : IDriverUpdateService
    {
        public Dictionary<string, UpdateOperationResult> DownloadResults { get; } = [];

        public Dictionary<string, UpdateOperationResult> InstallResults { get; } = [];

        public Action<string>? OnDownload { get; set; }

        public Action<string>? OnInstall { get; set; }

        public Task<DriverSearchResult> SearchAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UpdateOperationResult> DownloadAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default)
        {
            calls.Add($"download:{update.UpdateId}");
            OnDownload?.Invoke(update.UpdateId);
            return Task.FromResult(DownloadResults.GetValueOrDefault(update.UpdateId, new(OperationResultCode.Succeeded)));
        }

        public Task<UpdateOperationResult> InstallAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default)
        {
            calls.Add($"install:{update.UpdateId}");
            OnInstall?.Invoke(update.UpdateId);
            return Task.FromResult(InstallResults.GetValueOrDefault(update.UpdateId, new(OperationResultCode.Succeeded)));
        }

        public Task<IReadOnlyList<DriverUpdateInfo>> GetHiddenAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task HideAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UnhideAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DriverHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
