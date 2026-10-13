using DriverUpdater.Core.Models;
using DriverUpdater.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverUpdater.Core.Tests;

public class DriverRestoreTests
{
    private static BackedUpDriver Driver(string version = "1.0.0.0", string? infPath = @"C:\Backups\x\oem5\contoso.inf") =>
        new(@"C:\Backups\x", new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero),
            new DriverBackupEntry("Camera", "USB\\CAM\\1", "oem5.inf", "Contoso", version, null, [@"USB\VID_1&PID_2"],
                DriverBackupStatus.Exported, null, "oem5"),
            infPath);

    private static DeviceDriverInfo Device(string? version) =>
        new() { DeviceId = "USB\\CAM\\1", DeviceName = "Camera", DriverVersion = version, InfName = "oem9.inf" };

    [Fact]
    public void Verify_reports_restored_when_the_device_uses_the_backed_up_version()
    {
        var result = DriverRestoreVerifier.Verify(Driver("1.0.0.0"), Device("1.0.0.0"), rebootRequired: false);

        Assert.Equal(DriverRestoreOutcome.Restored, result.Outcome);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Verify_reports_not_in_use_with_device_manager_steps_when_windows_kept_the_newer_driver()
    {
        var result = DriverRestoreVerifier.Verify(Driver("1.0.0.0"), Device("2.0.0.0"), rebootRequired: false);

        Assert.Equal(DriverRestoreOutcome.NotInUse, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal("2.0.0.0", result.CurrentVersion);
        Assert.Contains("Let me pick from a list", result.Message);
    }

    [Fact]
    public void Verify_asks_for_a_restart_without_judging_the_version_yet()
    {
        var result = DriverRestoreVerifier.Verify(Driver("1.0.0.0"), Device("2.0.0.0"), rebootRequired: true);

        Assert.Equal(DriverRestoreOutcome.RestartRequired, result.Outcome);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Verify_handles_a_device_that_disappeared()
    {
        Assert.Equal(DriverRestoreOutcome.NotInUse, DriverRestoreVerifier.Verify(Driver(), null, rebootRequired: false).Outcome);
    }

    [Theory]
    [InlineData(unchecked((int)0xE000020B), true)]  // no such device
    [InlineData(unchecked((int)0xE0000228), true)]  // no compatible drivers
    [InlineData(unchecked((int)0xE0000203), true)]  // no driver selected
    [InlineData(259, true)]                          // no more items
    [InlineData(5, false)]                           // access denied
    [InlineData(unchecked((int)0x800B0100), false)]  // not signed
    public void Only_hardware_id_mismatches_move_on_to_the_next_id(int error, bool expected)
    {
        Assert.Equal(expected, DriverInstallErrors.MeansNotThisHardwareId(error));
    }

    [Fact]
    public void Describes_known_and_unknown_errors()
    {
        Assert.Equal("The backed-up driver isn't signed, so Windows won't install it. (0x800B0100)",
            DriverInstallErrors.Describe(unchecked((int)0x800B0100)));
        Assert.Equal("Windows reported error 0x12345678.", DriverInstallErrors.Describe(0x12345678));
    }

    [Fact]
    public async Task Orchestrator_creates_a_restore_point_first_and_attaches_it()
    {
        var calls = new List<string>();
        var restorePoints = new FakeRestorePoints(calls, RestorePointOutcome.Created);
        var restore = new FakeRestore(calls, new DriverRestoreResult(DriverRestoreOutcome.Restored, "ok"));
        var orchestrator = new RestoreOrchestrator(restorePoints, restore, NullLogger<RestoreOrchestrator>.Instance);

        var result = await orchestrator.RunAsync(Driver(), _ => false);

        Assert.Equal(["restore-point", "restore"], calls);
        Assert.Equal(DriverRestoreOutcome.Restored, result.Outcome);
        Assert.NotNull(result.RestorePoint);
    }

    [Fact]
    public async Task Orchestrator_changes_nothing_when_the_user_declines_going_on_without_a_restore_point()
    {
        var calls = new List<string>();
        var orchestrator = new RestoreOrchestrator(new FakeRestorePoints(calls, RestorePointOutcome.Disabled),
            new FakeRestore(calls, new DriverRestoreResult(DriverRestoreOutcome.Restored, "ok")),
            NullLogger<RestoreOrchestrator>.Instance);
        RestorePointResult? asked = null;

        var result = await orchestrator.RunAsync(Driver(), rp => { asked = rp; return false; });

        Assert.NotNull(asked);
        Assert.Equal(["restore-point"], calls);
        Assert.Equal(DriverRestoreOutcome.DeclinedWithoutRestorePoint, result.Outcome);
    }

    [Fact]
    public async Task Orchestrator_turns_an_unexpected_exception_into_a_failure()
    {
        var calls = new List<string>();
        var orchestrator = new RestoreOrchestrator(new FakeRestorePoints(calls, RestorePointOutcome.Created),
            new FakeRestore(calls, null), NullLogger<RestoreOrchestrator>.Instance);

        var result = await orchestrator.RunAsync(Driver(), _ => true);

        Assert.Equal(DriverRestoreOutcome.Failed, result.Outcome);
        Assert.Contains("boom", result.Message);
    }

    private sealed class FakeRestorePoints(List<string> calls, RestorePointOutcome outcome) : IRestorePointService
    {
        public Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken = default)
        {
            calls.Add("restore-point");
            return Task.FromResult(new RestorePointResult(outcome, outcome.ToString()));
        }
    }

    /// <summary>Returns the given result, or throws when it is null.</summary>
    private sealed class FakeRestore(List<string> calls, DriverRestoreResult? result) : IDriverRestoreService
    {
        public Task<DriverRestoreResult> RestoreAsync(BackedUpDriver driver, CancellationToken cancellationToken = default)
        {
            calls.Add("restore");
            return result is null ? throw new InvalidOperationException("boom") : Task.FromResult(result);
        }
    }
}
