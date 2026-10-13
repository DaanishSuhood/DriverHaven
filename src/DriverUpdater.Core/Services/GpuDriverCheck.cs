using System.Globalization;
using System.Text.Json;
using DriverUpdater.Core.Models;
using Microsoft.Extensions.Logging;

namespace DriverUpdater.Core.Services;

public interface IGpuDriverCheckService
{
    /// <summary>
    /// One notice per graphics card (display device) from NVIDIA, AMD or Intel. Contacts the vendors' servers, so it
    /// only runs when the user has opted in. Never throws for a failed lookup; that becomes a CheckFailed notice.
    /// </summary>
    Task<IReadOnlyList<GpuDriverNotice>> CheckAsync(IReadOnlyCollection<DeviceDriverInfo> devices,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// NVIDIA: the lookup GeForce Experience uses, by PCI device ID. It's undocumented, so any failure falls back to a link.
/// AMD and Intel publish nothing machine-readable (AMD's site needs scraping, Intel's blocks automated requests), so
/// for them it only links to their driver pages.
/// </summary>
public sealed class VendorGpuDriverCheckService(HttpClient http, ILogger<VendorGpuDriverCheckService> logger)
    : IGpuDriverCheckService
{
    public const string NvidiaDriversPage = "https://www.nvidia.com/en-us/drivers/";
    public const string AmdDriversPage = "https://www.amd.com/en/support/download/drivers.html";
    public const string IntelDriversPage = "https://www.intel.com/content/www/us/en/support/detect.html";

    public async Task<IReadOnlyList<GpuDriverNotice>> CheckAsync(IReadOnlyCollection<DeviceDriverInfo> devices,
        CancellationToken cancellationToken = default)
    {
        var notices = new List<GpuDriverNotice>();
        foreach (var gpu in GpuDetector.FindGpus(devices))
        {
            var notice = gpu.Vendor switch
            {
                GpuVendor.Nvidia => await CheckNvidiaAsync(gpu, cancellationToken),
                GpuVendor.Amd => new GpuDriverNotice(GpuDriverNoticeKind.LinkOnly, GpuVendor.Amd, gpu.Device.DeviceName,
                    gpu.Device.DriverVersion, null, null, AmdDriversPage),
                _ => new GpuDriverNotice(GpuDriverNoticeKind.LinkOnly, GpuVendor.Intel, gpu.Device.DeviceName,
                    gpu.Device.DriverVersion, null, null, IntelDriversPage),
            };
            logger.LogInformation("GPU driver check for {Device}: {Kind}, installed {Installed}, latest {Latest}",
                notice.DeviceName, notice.Kind, notice.InstalledVersion, notice.LatestVersion);
            notices.Add(notice);
        }
        return notices;
    }

    private async Task<GpuDriverNotice> CheckNvidiaAsync(DetectedGpu gpu, CancellationToken cancellationToken)
    {
        var installed = NvidiaDriver.FromWindowsVersion(gpu.Device.DriverVersion);
        try
        {
            var json = await http.GetStringAsync(NvidiaDriver.LookupUrl(gpu.DeviceId), cancellationToken);
            return NvidiaDriver.ParseLookup(json, gpu.Device.DeviceName, installed)
                ?? Failed("the response had no driver version");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       && !cancellationToken.IsCancellationRequested)
        {
            return Failed(ex.Message);
        }

        GpuDriverNotice Failed(string reason)
        {
            logger.LogWarning("NVIDIA driver lookup for {Device} failed: {Reason}", gpu.Device.DeviceName, reason);
            return new GpuDriverNotice(GpuDriverNoticeKind.CheckFailed, GpuVendor.Nvidia, gpu.Device.DeviceName, installed,
                null, null, NvidiaDriversPage);
        }
    }
}

public sealed record DetectedGpu(GpuVendor Vendor, string DeviceId, DeviceDriverInfo Device);

public static class GpuDetector
{
    /// <summary>Display devices from NVIDIA (10DE), AMD (1002) or Intel (8086), one per GPU model.</summary>
    public static IReadOnlyList<DetectedGpu> FindGpus(IEnumerable<DeviceDriverInfo> devices) =>
        devices
            .Where(d => d.DeviceClass.Equals("Display", StringComparison.OrdinalIgnoreCase) && d.HasDriver)
            .Select(d => (Device: d, Pci: ParsePciIds(d.HardwareIds)))
            .Where(x => x.Pci is not null)
            .Select(x => new DetectedGpu(x.Pci!.Value.Vendor, x.Pci.Value.DeviceId, x.Device))
            .DistinctBy(g => (g.Vendor, g.DeviceId))
            .ToList();

    /// <summary>Reads VEN_xxxx and DEV_xxxx from an ID like PCI\VEN_10DE&amp;DEV_2803&amp;SUBSYS_271719DA&amp;REV_A1.</summary>
    public static (GpuVendor Vendor, string DeviceId)? ParsePciIds(IEnumerable<string> hardwareIds)
    {
        foreach (var id in hardwareIds)
        {
            if (!id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = id[4..].Split('&');
            var vendor = parts.FirstOrDefault(p => p.StartsWith("VEN_", StringComparison.OrdinalIgnoreCase))?[4..];
            var device = parts.FirstOrDefault(p => p.StartsWith("DEV_", StringComparison.OrdinalIgnoreCase))?[4..];
            if (device is not { Length: 4 })
                continue;

            GpuVendor? known = vendor?.ToUpperInvariant() switch
            {
                "10DE" => GpuVendor.Nvidia,
                "1002" => GpuVendor.Amd,
                "8086" => GpuVendor.Intel,
                _ => null,
            };
            if (known is { } v)
                return (v, device.ToUpperInvariant());
        }
        return null;
    }
}

public static class NvidiaDriver
{
    /// <summary>
    /// NVIDIA's version is the last digit of the third part plus the fourth part of the Windows version:
    /// 32.0.16.1742 is 617.42, and 31.0.15.0899 is 508.99.
    /// </summary>
    public static string? FromWindowsVersion(string? windowsVersion)
    {
        if (!Version.TryParse(windowsVersion, out var v) || v.Build < 0 || v.Revision < 0)
            return null;
        var number = v.Build % 10 * 10000 + v.Revision;
        return $"{number / 100}.{number % 100:00}";
    }

    public static string LookupUrl(string deviceId)
    {
        var query = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["dIDa"] = new[] { $"{deviceId}_10DE" },
            ["osC"] = "10.0", ["osB"] = "", ["is6"] = "1", ["lg"] = "1033", ["iLp"] = "0", ["isDCH"] = "1",
            ["sMode"] = "0", ["sIS"] = "0", ["dch"] = "1", ["upCRD"] = "0", ["isCRD"] = "0", ["dTp"] = "0", ["isWHQL"] = "1",
        });
        return "https://gfwsl.geforce.com/nvidia_web_services/controller.gfeclientcontent.NG.php/"
            + "com.nvidia.services.GFEClientContent_NG.getDispDrvrByDevid/" + Uri.EscapeDataString(query);
    }

    /// <summary>Reads the latest WHQL driver from a lookup response; null when it has no usable version.</summary>
    public static GpuDriverNotice? ParseLookup(string json, string deviceName, string? installedVersion)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("DriverAttributes", out var driver) || driver.ValueKind != JsonValueKind.Object)
            return null;

        var latest = String(driver, "Version");
        if (!TryParse(latest, out var latestNumber))
            return null;

        var url = String(driver, "DetailsURL") is { } details && details.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? details
            : VendorGpuDriverCheckService.NvidiaDriversPage;
        DateOnly? released = DateTimeOffset.TryParse(String(driver, "ReleaseDateTime"), CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? DateOnly.FromDateTime(date.UtcDateTime) : null;

        var newer = TryParse(installedVersion, out var installedNumber) && latestNumber > installedNumber;
        return new GpuDriverNotice(newer ? GpuDriverNoticeKind.NewerAvailable : GpuDriverNoticeKind.UpToDate,
            GpuVendor.Nvidia, deviceName, installedVersion, latest, released, url);

        static string? String(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>"617.42" as 61742, so versions compare as numbers.</summary>
    private static bool TryParse(string? version, out int number)
    {
        number = 0;
        var parts = version?.Split('.');
        return parts is [var major, var minor]
            && int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            && int.TryParse(minor, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            && minor.Length == 2
            && (number = m * 100 + n) > 0;
    }
}
