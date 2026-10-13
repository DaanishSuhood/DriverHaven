using DriverUpdater.Core.Infrastructure;

namespace DriverUpdater.Core.Models;

public enum GpuVendor
{
    Nvidia,
    Amd,
    Intel,
}

public enum GpuDriverNoticeKind
{
    /// <summary>The vendor has a newer driver than the installed one.</summary>
    NewerAvailable,

    UpToDate,

    /// <summary>The vendor's latest version can't be looked up, so only a link to its driver page is offered.</summary>
    LinkOnly,

    /// <summary>The lookup failed (offline, or the vendor changed its service); only a link is offered.</summary>
    CheckFailed,
}

/// <summary>What a graphics card maker's own website says about a newer driver. DriverHaven only links to it.</summary>
/// <param name="InstalledVersion">In the vendor's own format where it differs from Windows', e.g. NVIDIA's 617.42.</param>
public sealed record GpuDriverNotice(
    GpuDriverNoticeKind Kind,
    GpuVendor Vendor,
    string DeviceName,
    string? InstalledVersion,
    string? LatestVersion,
    DateOnly? ReleaseDate,
    string Url)
{
    public string VendorName => Vendor switch
    {
        GpuVendor.Nvidia => "NVIDIA",
        GpuVendor.Amd => "AMD",
        _ => "Intel",
    };

    public string Message => Kind switch
    {
        GpuDriverNoticeKind.NewerAvailable =>
            $"{VendorName} has a newer driver for your {DeviceName}: {LatestVersion}"
            + (ReleaseDate is { } date ? $", released {date:yyyy-MM-dd}" : "")
            + $". You have {InstalledVersion}. Windows Update usually offers it later; to get it now, download it from {VendorName}.",
        GpuDriverNoticeKind.UpToDate => $"Your {DeviceName} has {VendorName}'s latest driver ({InstalledVersion}).",
        GpuDriverNoticeKind.LinkOnly =>
            $"{VendorName} doesn't publish its latest driver version in a way {ProductInfo.Name} can check. "
            + $"To see whether there's a newer driver for your {DeviceName}, visit {VendorName}'s driver page.",
        _ => $"Couldn't check {VendorName}'s website for a newer {DeviceName} driver. You can check its driver page yourself.",
    };

    public string LinkText => $"Open {VendorName}'s driver page";
}
