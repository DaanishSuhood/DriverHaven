using DriverUpdater.Core.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverUpdater.Core.Tests;

public class ProcessRunnerTests
{
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    [Fact]
    public async Task Captures_output_and_exit_code()
    {
        var result = await _runner.RunAsync(Cmd, ["/c", "echo hello & echo oops 1>&2 & exit 3"], TimeSpan.FromSeconds(30));

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.Contains("hello", result.StandardOutput);
        Assert.Contains("oops", result.StandardError);
        Assert.Equal("oops", result.LastMessageLine);
    }

    [Fact]
    public async Task Kills_process_and_throws_on_timeout()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            _runner.RunAsync(Cmd, ["/c", "ping -n 30 127.0.0.1 >nul"], TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task Cancellation_throws_operation_cancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _runner.RunAsync(Cmd, ["/c", "ping -n 30 127.0.0.1 >nul"], TimeSpan.FromSeconds(30), cts.Token));
    }

    [Fact]
    public void LastMessageLine_falls_back_to_stdout_then_exit_code()
    {
        Assert.Equal("last", new ProcessResult(1, "first\r\nlast\r\n\r\n", "  ").LastMessageLine);
        Assert.Equal("exit code 7", new ProcessResult(7, "", "").LastMessageLine);
    }

    [Fact]
    public void PnpUtil_path_points_at_an_existing_file()
    {
        Assert.True(File.Exists(SystemTools.PnpUtilPath), SystemTools.PnpUtilPath);
    }
}
