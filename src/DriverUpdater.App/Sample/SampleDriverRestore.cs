using System.IO;
using DriverUpdater.Core.Models;
using DriverUpdater.Core.Services;

namespace DriverUpdater.App.Sample;

/// <summary>
/// Sample mode: made-up backups of older drivers for a few real devices, plus one whose files are missing. Each one's
/// Detail names how its simulated restore turns out, so every result the UI can show comes up.
/// </summary>
public sealed class SampleDriverBackupCatalog(IDeviceInventoryService inventory) : IDriverBackupCatalog
{
    public const string RestoresFine = "Sample: restores fine";
    public const string NeedsRestart = "Sample: needs a restart";
    public const string WindowsKeepsNewer = "Sample: Windows keeps the newer driver";

    private static readonly string[] Scenarios = [RestoresFine, NeedsRestart, WindowsKeepsNewer, "Sample: files missing"];

    private readonly DateTimeOffset _backedUpAt = DateTimeOffset.Now.AddDays(-3);

    public async Task<IReadOnlyList<BackedUpDriver>> ListAsync(CancellationToken cancellationToken = default)
    {
        var devices = await inventory.GetDevicesAsync(cancellationToken);
        var picks = devices
            .Where(d => d.IsThirdPartyDriver && Version.TryParse(d.DriverVersion, out _) && d.HardwareIds.Count > 0)
            .DistinctBy(d => d.DeviceClass, StringComparer.OrdinalIgnoreCase)
            .Take(Scenarios.Length)
            .ToList();

        return picks.Select((device, i) =>
            {
                var entry = new DriverBackupEntry(device.DeviceName, device.DeviceId, device.InfName, device.DriverProvider,
                    PreviousVersion(device.DriverVersion!), device.DriverDate?.AddMonths(-4), device.HardwareIds,
                    DriverBackupStatus.Exported, Scenarios[i], Path.GetFileNameWithoutExtension(device.InfName));
                var filesMissing = i == 3;
                return new BackedUpDriver(@"(sample backup)", _backedUpAt.AddMinutes(-i), entry,
                    filesMissing ? null : $@"(sample backup)\{entry.ExportFolder}\sample.inf");
            })
            .ToList();
    }

    private static string PreviousVersion(string current)
    {
        var v = Version.Parse(current);
        return v.Revision > 0 ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0), v.Revision - 1).ToString()
            : v.Build > 0 ? new Version(v.Major, v.Minor, v.Build - 1).ToString()
            : new Version(Math.Max(v.Major - 1, 0), 0).ToString();
    }
}

/// <summary>Simulates a restore with the outcome the sample catalog planned for it.</summary>
public sealed class SampleDriverRestoreService : IDriverRestoreService
{
    public async Task<DriverRestoreResult> RestoreAsync(BackedUpDriver driver, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);

        var version = driver.Entry.DriverVersion;
        var device = driver.Entry.DeviceName;
        return driver.Entry.Detail switch
        {
            SampleDriverBackupCatalog.NeedsRestart => DriverRestoreVerifier.Verify(driver, null, rebootRequired: true),
            SampleDriverBackupCatalog.WindowsKeepsNewer => new DriverRestoreResult(DriverRestoreOutcome.NotInUse,
                $"Sample mode: Windows added driver {version}, but {device} still uses the newer one. In real use, "
                + "Device Manager's Update driver → Browse my computer → Let me pick from a list switches it."),
            _ => new DriverRestoreResult(DriverRestoreOutcome.Restored,
                $"Sample mode: {device} would now use driver {version}. Nothing was changed."),
        };
    }
}
