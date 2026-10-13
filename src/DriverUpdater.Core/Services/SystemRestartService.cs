using DriverUpdater.Core.Infrastructure;

namespace DriverUpdater.Core.Services;

public interface ISystemRestartService
{
    /// <summary>Restarts Windows immediately.</summary>
    /// <exception cref="InvalidOperationException">Windows refused the restart; the message says why.</exception>
    Task RestartNowAsync();
}

/// <summary>Restarts with <c>shutdown /r /t 0</c>.</summary>
public sealed class ShutdownRestartService(IProcessRunner processRunner) : ISystemRestartService
{
    public async Task RestartNowAsync()
    {
        var result = await processRunner.RunAsync(SystemTools.ShutdownPath, ["/r", "/t", "0"], TimeSpan.FromSeconds(30));
        if (!result.Succeeded)
            throw new InvalidOperationException($"Windows did not restart: {result.LastMessageLine}");
    }
}
