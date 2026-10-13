using DriverUpdater.Core.Models;
using DriverUpdater.Core.Services;

namespace DriverUpdater.Core.Tests;

public class PendingRestartEvaluatorTests
{
    [Fact]
    public void Nothing_pending_when_no_signals()
    {
        var check = PendingRestartEvaluator.Evaluate(PendingRestartSignals.None);

        Assert.False(check.IsPending);
        Assert.Empty(check.Reasons);
    }

    [Fact]
    public void Each_strong_signal_gives_a_reason()
    {
        var check = PendingRestartEvaluator.Evaluate(new PendingRestartSignals(
            ComponentServicingRebootPending: true,
            WindowsUpdateRebootRequired: true,
            PendingFileRenames: [@"\??\C:\Windows\System32\drivers\oldcam.sys"],
            DevicesNeedingRestart: ["USB Camera"]));

        Assert.True(check.IsPending);
        Assert.Equal(4, check.Reasons.Count);
        Assert.Contains(check.Reasons, r => r.Contains("Windows Update"));
        Assert.Contains(check.Reasons, r => r.Contains("Device Manager reports that a device needs a restart: USB Camera."));
    }

    [Theory]
    [InlineData(@"\??\C:\Windows\System32\drivers\oldcam.sys", true)]
    [InlineData(@"\??\C:\WINDOWS\system32\DriverStore\FileRepository\cam.inf_amd64_1234\cam.sys", true)]
    [InlineData(@"\??\C:\Program Files\SomeApp\update.dll", false)]
    [InlineData(@"\??\C:\Windows\Temp\drivers\x.sys", false)]
    public void Only_driver_files_count_among_pending_file_renames(string path, bool expected)
    {
        Assert.Equal(expected, PendingRestartEvaluator.IsDriverFile(path));
        Assert.Equal(expected, PendingRestartEvaluator.Evaluate(new PendingRestartSignals(PendingFileRenames: [path])).IsPending);
    }

    [Fact]
    public void Lists_at_most_three_devices()
    {
        var check = PendingRestartEvaluator.Evaluate(new PendingRestartSignals(DevicesNeedingRestart: ["A", "B", "C", "D", "E"]));

        Assert.Equal("Device Manager reports that devices need a restart: A, B, C and 2 more.", Assert.Single(check.Reasons));
    }

    [Fact]
    public void Message_lists_each_reason()
    {
        var check = PendingRestartEvaluator.Evaluate(new PendingRestartSignals(WindowsUpdateRebootRequired: true));

        Assert.StartsWith("Windows is waiting for a restart:\n• Windows Update", check.Message);
    }
}
