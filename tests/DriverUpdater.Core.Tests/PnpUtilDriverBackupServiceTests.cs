using System.Text.Json;
using DriverUpdater.Core.Infrastructure;
using DriverUpdater.Core.Models;
using DriverUpdater.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverUpdater.Core.Tests;

public sealed class PnpUtilDriverBackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DriverUpdaterTests", Guid.NewGuid().ToString("N"));
    private readonly FakePnpUtil _pnpUtil = new();
    private readonly PnpUtilDriverBackupService _service;

    public PnpUtilDriverBackupServiceTests()
    {
        _service = new PnpUtilDriverBackupService(_pnpUtil, new AppPaths(_root),
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 11, 14, 30, 5, TimeSpan.Zero)),
            NullLogger<PnpUtilDriverBackupService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static DeviceDriverInfo Device(string id, string? inf) => new()
    {
        DeviceId = id,
        DeviceName = $"Device {id}",
        InfName = inf,
        DriverVersion = inf is null ? null : "1.0.0.0",
        HardwareIds = [$@"PCI\{id}"],
    };

    [Fact]
    public async Task Exports_third_party_drivers_and_skips_inbox_and_driverless_devices()
    {
        var result = await _service.BackupAsync([Device("A", "oem12.inf"), Device("B", "usb.inf"), Device("C", null)]);

        Assert.Equal(1, result.ExportedCount);
        Assert.Equal(2, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal([DriverBackupStatus.Exported, DriverBackupStatus.SkippedInbox, DriverBackupStatus.SkippedNoDriver],
            result.Entries.Select(e => e.Status));
        Assert.True(File.Exists(Path.Combine(result.Folder, "oem12", "oem12.inf")));
        Assert.Equal(["oem12.inf"], _pnpUtil.ExportedInfs);
    }

    [Fact]
    public async Task Exports_each_shared_package_once()
    {
        var result = await _service.BackupAsync([Device("A", "oem5.inf"), Device("B", "OEM5.INF")]);

        Assert.Equal(["oem5.inf"], _pnpUtil.ExportedInfs);
        Assert.All(result.Entries, e => Assert.Equal("oem5", e.ExportFolder));
    }

    [Fact]
    public async Task Records_pnputil_failure_with_its_message()
    {
        _pnpUtil.FailInfs.Add("oem7.inf");

        var result = await _service.BackupAsync([Device("A", "oem7.inf"), Device("B", "oem8.inf")]);

        var failed = result.Entries.Single(e => e.InfName == "oem7.inf");
        Assert.Equal(DriverBackupStatus.Failed, failed.Status);
        Assert.Equal("Failed to export driver package: Access is denied.", failed.Detail);
        Assert.Equal(1, result.ExportedCount);
    }

    [Fact]
    public async Task Success_exit_code_without_inf_file_counts_as_failure()
    {
        _pnpUtil.SilentInfs.Add("oem9.inf");

        var result = await _service.BackupAsync([Device("A", "oem9.inf")]);

        Assert.Equal(DriverBackupStatus.Failed, Assert.Single(result.Entries).Status);
    }

    [Fact]
    public async Task Writes_manifest_with_timestamped_folder_and_unique_suffix()
    {
        var first = await _service.BackupAsync([Device("A", "oem1.inf")]);
        var second = await _service.BackupAsync([Device("A", "oem1.inf")]);

        Assert.Equal(Path.Combine(_root, "Backups", "20261011-143005"), first.Folder);
        Assert.Equal(first.Folder + "-2", second.Folder);

        var json = await File.ReadAllTextAsync(first.ManifestPath);
        Assert.Contains("\"<this folder>\\\\<ExportFolder>", json);

        using var manifest = JsonDocument.Parse(json);
        var entry = manifest.RootElement.GetProperty("entries")[0];
        Assert.Equal("oem1.inf", entry.GetProperty("infName").GetString());
        Assert.Equal("Exported", entry.GetProperty("status").GetString());
        Assert.Equal("1.0.0.0", entry.GetProperty("driverVersion").GetString());
        Assert.Contains("pnputil /add-driver", manifest.RootElement.GetProperty("restoreInstructions").GetString());
    }

    [Fact]
    public async Task Backups_read_back_through_the_catalog()
    {
        await _service.BackupAsync([Device("A", "oem12.inf"), Device("B", "usb.inf")]);
        var catalog = new DriverBackupCatalog(new AppPaths(_root), NullLogger<DriverBackupCatalog>.Instance);

        var driver = Assert.Single(await catalog.ListAsync());

        Assert.Equal("A", driver.Entry.DeviceId);
        Assert.Equal("1.0.0.0", driver.Entry.DriverVersion);
        Assert.Equal([@"PCI\A"], driver.Entry.HardwareIds);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 14, 30, 5, TimeSpan.Zero), driver.BackedUpAt);
        Assert.EndsWith(Path.Combine("oem12", "oem12.inf"), driver.InfPath);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.BackupAsync([Device("A", "oem1.inf")], cts.Token));
    }

    /// <summary>Simulates pnputil /export-driver by writing a fake .inf into the target folder.</summary>
    private sealed class FakePnpUtil : IProcessRunner
    {
        public List<string> ExportedInfs { get; } = [];
        public HashSet<string> FailInfs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SilentInfs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Assert.EndsWith("pnputil.exe", fileName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("/export-driver", arguments[0]);
            var (inf, target) = (arguments[1], arguments[2]);
            ExportedInfs.Add(inf);

            if (FailInfs.Contains(inf))
                return Task.FromResult(new ProcessResult(5, "Microsoft PnP Utility\r\n\r\nFailed to export driver package: Access is denied.\r\n", ""));

            if (!SilentInfs.Contains(inf))
                File.WriteAllText(Path.Combine(target, inf.ToLowerInvariant()), "[Version]");
            return Task.FromResult(new ProcessResult(0, "Exporting driver package... done.", ""));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
