using DriverUpdater.Core.Models;

namespace DriverUpdater.Core.Services;

/// <summary>Turns the raw pending-restart signals into reasons worth telling the user about.</summary>
public static class PendingRestartEvaluator
{
    private const int MaxDevicesListed = 3;

    // Uninstalling a device in Device Manager ("Attempt to remove the driver", then "Restart later") leaves its
    // in-use driver files queued for deletion here, and sets none of the other signals.
    private static readonly string[] DriverFolders = [@"\System32\drivers\", @"\System32\DriverStore\"];

    public static PendingRestartCheck Evaluate(PendingRestartSignals signals)
    {
        var reasons = new List<string>();
        if (signals.WindowsUpdateRebootRequired)
            reasons.Add("Windows Update installed updates that need a restart to finish.");
        if (signals.ComponentServicingRebootPending)
            reasons.Add("Windows is finishing installing an update or a Windows feature.");
        if (signals.PendingFileRenames?.Any(IsDriverFile) == true)
            reasons.Add("Driver files are waiting to be replaced or removed, for example after a device was uninstalled in Device Manager.");

        if (signals.DevicesNeedingRestart is { Count: > 0 } devices)
        {
            var names = string.Join(", ", devices.Take(MaxDevicesListed))
                + (devices.Count > MaxDevicesListed ? $" and {devices.Count - MaxDevicesListed} more" : "");
            reasons.Add($"Device Manager reports that {(devices.Count == 1 ? "a device needs" : "devices need")} a restart: {names}.");
        }

        return reasons.Count == 0 ? PendingRestartCheck.None : new PendingRestartCheck(reasons);
    }

    public static bool IsDriverFile(string path) =>
        DriverFolders.Any(folder => path.Contains(folder, StringComparison.OrdinalIgnoreCase));
}
