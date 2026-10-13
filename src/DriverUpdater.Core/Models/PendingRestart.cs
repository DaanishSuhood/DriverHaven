namespace DriverUpdater.Core.Models;

/// <summary>What Windows has recorded about a pending restart, read once before an install.</summary>
/// <param name="ComponentServicingRebootPending">The Component Based Servicing RebootPending key exists.</param>
/// <param name="WindowsUpdateRebootRequired">The Windows Update Auto Update RebootRequired key exists.</param>
/// <param name="PendingFileRenames">
/// The PendingFileRenameOperations paths. Many apps leave entries here, so only driver files count.
/// </param>
/// <param name="DevicesNeedingRestart">Names of devices with problem code 14 (CM_PROB_NEED_RESTART).</param>
public sealed record PendingRestartSignals(
    bool ComponentServicingRebootPending = false,
    bool WindowsUpdateRebootRequired = false,
    IReadOnlyList<string>? PendingFileRenames = null,
    IReadOnlyList<string>? DevicesNeedingRestart = null)
{
    public static PendingRestartSignals None { get; } = new();
}

/// <summary>Whether a restart is pending, with a readable reason per signal.</summary>
public sealed record PendingRestartCheck(IReadOnlyList<string> Reasons)
{
    public static PendingRestartCheck None { get; } = new([]);

    public bool IsPending => Reasons.Count > 0;

    public string Message =>
        "Windows is waiting for a restart:\n"
        + string.Join("\n", Reasons.Select(r => $"• {r}"))
        + "\n\nDrivers installed before the restart may fail to install, or not work until the PC restarts.";
}
