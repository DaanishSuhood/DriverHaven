namespace DriverUpdater.Core.Models;

/// <summary>
/// Human-readable text for Device Manager problem codes (ConfigManagerErrorCode / CM_PROB_*).
/// See https://learn.microsoft.com/windows-hardware/drivers/install/device-manager-error-messages
/// </summary>
public static class DeviceProblemCodes
{
    public const int DriverNotInstalled = 28;

    private static readonly Dictionary<int, string> Descriptions = new()
    {
        [1] = "Not configured correctly",
        [3] = "Driver may be corrupted, or the system is low on memory",
        [10] = "Device cannot start",
        [12] = "Not enough free resources",
        [14] = "Restart required",
        [18] = "Drivers need to be reinstalled",
        [19] = "Configuration in the registry is incomplete or damaged",
        [21] = "Windows is removing this device",
        [22] = "Device is disabled",
        [24] = "Device is not present, not working properly, or missing drivers",
        [28] = "Drivers are not installed",
        [29] = "Disabled by firmware",
        [31] = "Windows cannot load the drivers for this device",
        [32] = "Driver service is disabled",
        [37] = "Driver failed to initialize",
        [39] = "Driver is corrupted or missing",
        [40] = "Driver service registry key is invalid",
        [41] = "Driver loaded but the hardware was not found",
        [43] = "Windows stopped this device because it reported problems",
        [45] = "Device is not connected",
        [47] = "Prepared for safe removal",
        [48] = "Driver is blocked because of known problems",
        [49] = "Registry size limit exceeded",
        [52] = "Driver's digital signature could not be verified",
    };

    public static string Describe(int code) => code switch
    {
        0 => "Working",
        _ when Descriptions.TryGetValue(code, out var text) => $"{text} (code {code})",
        _ => $"Problem (code {code})",
    };
}
