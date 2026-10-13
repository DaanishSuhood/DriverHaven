namespace DriverUpdater.Core.Models;

/// <summary>A present PnP device and the driver currently bound to it (if any).</summary>
public sealed record DeviceDriverInfo
{
    public required string DeviceId { get; init; }

    public required string DeviceName { get; init; }

    public string DeviceClass { get; init; } = "";

    public string Manufacturer { get; init; } = "";

    public string? DriverProvider { get; init; }

    public string? DriverVersion { get; init; }

    public DateOnly? DriverDate { get; init; }

    /// <summary>The driver package INF, e.g. "oem42.inf" (third-party) or "usb.inf" (inbox).</summary>
    public string? InfName { get; init; }

    /// <summary>Most specific first, as reported by Windows. Used to match Windows Update drivers.</summary>
    public IReadOnlyList<string> HardwareIds { get; init; } = [];

    /// <summary>Less specific IDs (e.g. class-generic). Some Windows Update drivers target these.</summary>
    public IReadOnlyList<string> CompatibleIds { get; init; } = [];

    /// <summary>Device Manager problem code (CM_PROB_*). 0 means the device is working.</summary>
    public int ProblemCode { get; init; }

    public bool HasProblem => ProblemCode != 0;

    public bool HasDriver => !string.IsNullOrEmpty(InfName);

    /// <summary>Third-party packages are staged as oemNN.inf; only these can be exported with pnputil.</summary>
    public bool IsThirdPartyDriver =>
        InfName is not null && InfName.StartsWith("oem", StringComparison.OrdinalIgnoreCase);

    public string Status => HasProblem ? DeviceProblemCodes.Describe(ProblemCode) : "Working";
}
