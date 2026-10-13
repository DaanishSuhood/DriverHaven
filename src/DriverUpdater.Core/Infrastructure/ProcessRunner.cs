using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DriverUpdater.Core.Infrastructure;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>The most useful single line for an error message: last non-blank line of stderr, else stdout.</summary>
    public string LastMessageLine =>
        LastLine(StandardError) ?? LastLine(StandardOutput) ?? $"exit code {ExitCode}";

    private static string? LastLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
}

public interface IProcessRunner
{
    /// <exception cref="TimeoutException">The process didn't exit in time; it has been killed.</exception>
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs console tools (pnputil etc.) hidden, capturing output and logging the whole exchange.</summary>
public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    // Console tools write redirected output in the OEM code page, not UTF-8.
    private static readonly Encoding ConsoleEncoding = CreateConsoleEncoding();

    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ConsoleEncoding,
            StandardErrorEncoding = ConsoleEncoding,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var commandLine = $"{Path.GetFileName(fileName)} {string.Join(' ', arguments.Select(Quote))}";
        logger.LogInformation("Running: {CommandLine}", commandLine);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");

        // Read both streams concurrently so a full pipe buffer can't deadlock the child.
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogWarning("Timed out after {Timeout}: {CommandLine}", timeout, commandLine);
            throw new TimeoutException($"{Path.GetFileName(fileName)} did not finish within {timeout.TotalSeconds:0} seconds.");
        }

        var result = new ProcessResult(process.ExitCode, await stdout, await stderr);
        logger.Log(result.Succeeded ? LogLevel.Information : LogLevel.Warning,
            "{CommandLine} exited with {ExitCode}{NewLine}{Output}{Error}",
            commandLine, result.ExitCode, Environment.NewLine, result.StandardOutput.TrimEnd(), result.StandardError.TrimEnd());
        return result;
    }

    private void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogDebug(ex, "Process already exited or could not be killed");
        }
    }

    private static string Quote(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;

    private static Encoding CreateConsoleEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.Default;
        }
    }
}
