using System.Net;
using DriverUpdater.Core.Models;
using DriverUpdater.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverUpdater.Core.Tests;

public class GpuDriverCheckTests
{
    // Trimmed from a real response for an RTX 4060 Ti (device 2803) on 2026-10-12.
    private const string NvidiaResponse = """
        {
            "criteria": { "IsDispDriverNewer": { "state": "true", "latestDispDriverVersion": "617.42" } },
            "DriverAttributes": {
                "ID": 280103,
                "Name": "GeForce Game Ready Driver",
                "ReleaseDateTime": "2026-10-06T00:00:00+0000",
                "Version": "617.42",
                "IsWHQL": 1,
                "DetailsURL": "https://www.nvidia.com/en-us/drivers/details/280103/",
                "DownloadURL": "https://international-gfe.download.nvidia.com/Windows/617.42/617.42-desktop-win10-win11-64bit-international-dch-whql-g.exe"
            }
        }
        """;

    private static DeviceDriverInfo Gpu(string hardwareId, string version, string name = "GeForce RTX 4060 Ti", string cls = "Display") =>
        new() { DeviceId = hardwareId + @"\1", DeviceName = name, DeviceClass = cls, InfName = "oem3.inf", DriverVersion = version, HardwareIds = [hardwareId] };

    [Theory]
    [InlineData("32.0.16.1742", "617.42")]
    [InlineData("31.0.15.0899", "508.99")]
    [InlineData("32.0.15.6094", "560.94")]
    [InlineData("32.0.16.0100", "601.00")]
    [InlineData("1.0", null)]
    [InlineData(null, null)]
    public void Converts_windows_versions_to_nvidia_versions(string? windows, string? expected)
    {
        Assert.Equal(expected, NvidiaDriver.FromWindowsVersion(windows));
    }

    [Fact]
    public void Parses_the_lookup_and_links_to_the_driver_details_page()
    {
        var notice = NvidiaDriver.ParseLookup(NvidiaResponse, "GeForce RTX 4060 Ti", "612.10")!;

        Assert.Equal(GpuDriverNoticeKind.NewerAvailable, notice.Kind);
        Assert.Equal("617.42", notice.LatestVersion);
        Assert.Equal(new DateOnly(2026, 10, 6), notice.ReleaseDate);
        Assert.Equal("https://www.nvidia.com/en-us/drivers/details/280103/", notice.Url);
        Assert.Contains("NVIDIA has a newer driver for your GeForce RTX 4060 Ti: 617.42, released 2026-10-06. You have 612.10.", notice.Message);
    }

    [Theory]
    [InlineData("617.42")]
    [InlineData("620.01")]
    public void Same_or_newer_installed_version_is_up_to_date(string installed)
    {
        Assert.Equal(GpuDriverNoticeKind.UpToDate, NvidiaDriver.ParseLookup(NvidiaResponse, "GPU", installed)!.Kind);
    }

    [Fact]
    public void Compares_versions_as_numbers_not_text()
    {
        // As text, "99.10" > "617.42"; as numbers it's older.
        Assert.Equal(GpuDriverNoticeKind.NewerAvailable, NvidiaDriver.ParseLookup(NvidiaResponse, "GPU", "99.10")!.Kind);
    }

    [Theory]
    [InlineData("""{ "DriverAttributes": { "Version": "" } }""")]
    [InlineData("""{ "DriverAttributes": null }""")]
    [InlineData("""{ "Something": "else" }""")]
    public void Returns_null_for_a_response_without_a_version(string json)
    {
        Assert.Null(NvidiaDriver.ParseLookup(json, "GPU", "617.42"));
    }

    [Fact]
    public void Ignores_a_details_url_that_is_not_https()
    {
        var json = NvidiaResponse.Replace("https://www.nvidia.com/en-us/drivers/details/280103/", "javascript:alert(1)");

        Assert.Equal(VendorGpuDriverCheckService.NvidiaDriversPage, NvidiaDriver.ParseLookup(json, "GPU", "617.42")!.Url);
    }

    [Fact]
    public void Lookup_url_asks_for_the_device_id()
    {
        var url = Uri.UnescapeDataString(NvidiaDriver.LookupUrl("2803"));

        Assert.StartsWith("https://gfwsl.geforce.com/", url);
        Assert.Contains("\"dIDa\":[\"2803_10DE\"]", url);
    }

    [Fact]
    public void Finds_one_gpu_per_model_from_known_vendors_only()
    {
        var gpus = GpuDetector.FindGpus(
        [
            Gpu(@"PCI\VEN_10DE&DEV_2803&SUBSYS_271719DA&REV_A1", "32.0.16.1742"),
            Gpu(@"PCI\VEN_10DE&DEV_2803&SUBSYS_00000000&REV_A1", "32.0.16.1742", "Second identical card"),
            Gpu(@"PCI\VEN_1002&DEV_744C&SUBSYS_0E3A1002", "32.0.21025.10016", "Radeon RX 7900 XTX"),
            Gpu(@"PCI\VEN_8086&DEV_A780&SUBSYS_7D251462", "32.0.101.6913", "Intel UHD Graphics 770"),
            Gpu(@"PCI\VEN_1234&DEV_1111", "1.0", "Basic display"),
            Gpu(@"PCI\VEN_10DE&DEV_22BC", "1.0", "NVIDIA audio", cls: "MEDIA"),
            Gpu(@"ROOT\BasicDisplay", "10.0", "Microsoft Basic Display Adapter"),
        ]);

        Assert.Equal(
            [(GpuVendor.Nvidia, "2803"), (GpuVendor.Amd, "744C"), (GpuVendor.Intel, "A780")],
            gpus.Select(g => (g.Vendor, g.DeviceId)));
    }

    [Fact]
    public async Task Checks_nvidia_online_and_links_amd_and_intel()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(NvidiaResponse) });
        var service = new VendorGpuDriverCheckService(new HttpClient(handler), NullLogger<VendorGpuDriverCheckService>.Instance);

        var notices = await service.CheckAsync(
        [
            Gpu(@"PCI\VEN_10DE&DEV_2803", "32.0.16.1210"),
            Gpu(@"PCI\VEN_1002&DEV_744C", "32.0.21025.10016", "Radeon RX 7900 XTX"),
            Gpu(@"PCI\VEN_8086&DEV_A780", "32.0.101.6913", "Intel UHD Graphics 770"),
        ]);

        Assert.Equal([GpuDriverNoticeKind.NewerAvailable, GpuDriverNoticeKind.LinkOnly, GpuDriverNoticeKind.LinkOnly],
            notices.Select(n => n.Kind));
        Assert.Equal("612.10", notices[0].InstalledVersion);
        Assert.Equal(VendorGpuDriverCheckService.AmdDriversPage, notices[1].Url);
        Assert.Equal(VendorGpuDriverCheckService.IntelDriversPage, notices[2].Url);
        Assert.Single(handler.Requests); // only NVIDIA is contacted
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "<html>not json</html>")]
    [InlineData(HttpStatusCode.OK, """{ "DriverAttributes": {} }""")]
    public async Task A_failed_nvidia_lookup_becomes_a_link(HttpStatusCode status, string body)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });
        var service = new VendorGpuDriverCheckService(new HttpClient(handler), NullLogger<VendorGpuDriverCheckService>.Instance);

        var notice = Assert.Single(await service.CheckAsync([Gpu(@"PCI\VEN_10DE&DEV_2803", "32.0.16.1742")]));

        Assert.Equal(GpuDriverNoticeKind.CheckFailed, notice.Kind);
        Assert.Equal(VendorGpuDriverCheckService.NvidiaDriversPage, notice.Url);
    }

    [Fact]
    public async Task Cancellation_is_not_reported_as_a_failed_lookup()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeHandler(_ => { cts.Cancel(); throw new TaskCanceledException(); });
        var service = new VendorGpuDriverCheckService(new HttpClient(handler), NullLogger<VendorGpuDriverCheckService>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckAsync([Gpu(@"PCI\VEN_10DE&DEV_2803", "32.0.16.1742")], cts.Token));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }
}
