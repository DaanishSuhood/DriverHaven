using DriverUpdater.Core.Infrastructure;
using DriverUpdater.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverUpdater.Core.Tests;

public sealed class DriverBackupCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DriverUpdaterTests", Guid.NewGuid().ToString("N"));
    private readonly DriverBackupCatalog _catalog;

    public DriverBackupCatalogTests()
    {
        _catalog = new DriverBackupCatalog(new AppPaths(_root), NullLogger<DriverBackupCatalog>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>Writes a manifest in the documented format, plus an exported .inf for each entry that has a folder.</summary>
    private string Backup(string name, string createdAt, params string[] entries)
    {
        var folder = Path.Combine(_root, "Backups", name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.json"),
            $$"""{ "createdAt": "{{createdAt}}", "machineName": "PC", "appVersion": "0.1.0", "restoreInstructions": "x", "entries": [{{string.Join(",", entries)}}] }""");
        return folder;
    }

    private static string Entry(string deviceId, string version, string status = "Exported", string? exportFolder = "oem5",
        string inf = "oem5.inf") =>
        $$"""
        { "deviceName": "Device {{deviceId}}", "deviceId": "{{deviceId}}", "infName": "{{inf}}", "driverProvider": "Contoso",
          "driverVersion": "{{version}}", "driverDate": "2025-01-02", "hardwareIds": ["PCI\\{{deviceId}}"],
          "status": "{{status}}", "detail": null, "exportFolder": {{(exportFolder is null ? "null" : $"\"{exportFolder}\"")}} }
        """;

    private static void ExportInf(string backupFolder, string exportFolder = "oem5", string infFile = "contoso.inf")
    {
        Directory.CreateDirectory(Path.Combine(backupFolder, exportFolder));
        File.WriteAllText(Path.Combine(backupFolder, exportFolder, infFile), "[Version]");
    }

    [Fact]
    public async Task Lists_exported_drivers_with_their_inf_and_skips_the_rest()
    {
        var folder = Backup("20261001-100000", "2026-10-01T10:00:00+00:00",
            Entry("A", "2.0"), Entry("B", "1.0", status: "SkippedInbox", exportFolder: null, inf: "usb.inf"));
        ExportInf(folder);

        var driver = Assert.Single(await _catalog.ListAsync());

        Assert.Equal("A", driver.Entry.DeviceId);
        Assert.Equal("2.0", driver.Entry.DriverVersion);
        Assert.Equal(new DateOnly(2025, 1, 2), driver.Entry.DriverDate);
        Assert.Equal([@"PCI\A"], driver.Entry.HardwareIds);
        Assert.Equal(Path.Combine(folder, "oem5", "contoso.inf"), driver.InfPath);
        Assert.True(driver.FilesPresent);
    }

    [Fact]
    public async Task Marks_drivers_whose_files_are_gone()
    {
        Backup("20261001-100000", "2026-10-01T10:00:00+00:00", Entry("A", "2.0"));

        var driver = Assert.Single(await _catalog.ListAsync());

        Assert.Null(driver.InfPath);
        Assert.False(driver.FilesPresent);
    }

    [Fact]
    public async Task Keeps_the_newest_copy_of_a_driver_and_lists_newest_first()
    {
        ExportInf(Backup("20261001-100000", "2026-10-01T10:00:00+00:00", Entry("A", "2.0")));
        var newer = Backup("20261005-100000", "2026-10-05T10:00:00+00:00", Entry("A", "2.0"));
        ExportInf(newer);
        ExportInf(Backup("20261003-100000", "2026-10-03T10:00:00+00:00", Entry("A", "1.0")));

        var drivers = await _catalog.ListAsync();

        Assert.Equal(["2.0", "1.0"], drivers.Select(d => d.Entry.DriverVersion));
        Assert.Equal(newer, drivers[0].BackupFolder);
    }

    [Fact]
    public async Task Skips_unreadable_manifests_and_folders_without_one()
    {
        var broken = Path.Combine(_root, "Backups", "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "manifest.json"), "{ not json");
        Directory.CreateDirectory(Path.Combine(_root, "Backups", "empty"));
        ExportInf(Backup("20261001-100000", "2026-10-01T10:00:00+00:00", Entry("A", "2.0")));

        Assert.Single(await _catalog.ListAsync());
    }

    [Fact]
    public async Task No_backups_folder_means_no_backups()
    {
        Assert.Empty(await _catalog.ListAsync());
    }
}
