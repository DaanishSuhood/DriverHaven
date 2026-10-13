namespace DriverUpdater.App.Services;

/// <summary>Command-line options, parsed once at startup.</summary>
/// <param name="SampleMode">
/// Debug builds only (<c>--sample</c>): made-up updates for real devices, and a simulated restore point, backup,
/// download, install and restart. Nothing on the PC is changed.
/// </param>
/// <param name="SampleRestorePointFails">With <c>--sample-no-restore-point</c>: simulate System Protection being off.</param>
/// <param name="SampleRestartPending">With <c>--sample-restart-pending</c>: simulate Windows waiting for a restart.</param>
public sealed record AppOptions(bool SampleMode = false, bool SampleRestorePointFails = false, bool SampleRestartPending = false)
{
    public static AppOptions Parse(IReadOnlyCollection<string> args)
    {
#if DEBUG
        var noRestorePoint = args.Contains("--sample-no-restore-point", StringComparer.OrdinalIgnoreCase);
        var restartPending = args.Contains("--sample-restart-pending", StringComparer.OrdinalIgnoreCase);
        return new AppOptions(noRestorePoint || restartPending || args.Contains("--sample", StringComparer.OrdinalIgnoreCase),
            noRestorePoint, restartPending);
#else
        return new AppOptions();
#endif
    }
}
