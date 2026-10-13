using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DriverUpdater.Core.Models;
using DriverUpdater.Core.Services;
using Microsoft.Extensions.Logging;

namespace DriverUpdater.App.Sample;

/// <summary>
/// Sample mode: offers made-up updates for a few real devices (so matching and the grid work as usual) plus one for
/// a device that isn't present, and simulates downloading and installing them. Each update is given a fixed scenario
/// so every result the UI can show comes up: success, success that needs a restart, install failure and download failure.
/// Hiding is remembered for the session, and the history is a few made-up entries plus this session's installs.
/// </summary>
public sealed class SampleDriverUpdateService(IDeviceInventoryService inventory, ILogger<SampleDriverUpdateService> logger)
    : IDriverUpdateService
{
    private const int SampledDeviceCount = 4;

    private static readonly string[] PreferredClasses = ["Display", "Net", "MEDIA", "Bluetooth", "System", "USB"];

    private static readonly Scenario[] DeviceScenarios =
        [Scenario.NeedsRestart, Scenario.Succeeds, Scenario.InstallFails, Scenario.Succeeds];

    private readonly ConcurrentDictionary<string, Scenario> _scenarios = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _installed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DriverUpdateInfo> _hidden = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<UpdateHistoryRecord> _history = new(PastHistory());

    private enum Scenario
    {
        Succeeds,
        NeedsRestart,
        InstallFails,
        DownloadFails,
    }

    public async Task<DriverSearchResult> SearchAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
        var devices = await inventory.GetDevicesAsync(cancellationToken);

        var picks = devices
            .Where(d => d.HasDriver && !d.HasProblem && d.HardwareIds.Count > 0)
            .GroupBy(d => d.DeviceClass, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => PreferredRank(g.Key))
            .Select(g => g.First())
            .Take(SampledDeviceCount)
            .ToList();

        var updates = picks.Select((device, i) => Offer(ForDevice(device), DeviceScenarios[i])).ToList();
        updates.Add(Offer(NotPresentDevice(), Scenario.DownloadFails));
        updates.RemoveAll(u => _installed.ContainsKey(u.UpdateId) || _hidden.ContainsKey(u.UpdateId));

        logger.LogInformation("Sample mode: offering {Count} made-up driver updates", updates.Count);
        return new DriverSearchResult(updates, DriversExcludedByPolicy: false);
    }

    public async Task<UpdateOperationResult> DownloadAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var megabytes = update.MaxDownloadSize / (1024 * 1024);
        await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(800 + megabytes * 4, 4000)), CancellationToken.None);

        var result = ScenarioOf(update) == Scenario.DownloadFails
            ? UpdateOperationResult.Failed(unchecked((int)0x8024402C))
            : new UpdateOperationResult(OperationResultCode.Succeeded);
        logger.LogInformation("Sample mode: simulated download of {Title}: {ResultCode}", update.Title, result.ResultCode);
        return result;
    }

    public async Task<UpdateOperationResult> InstallAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(TimeSpan.FromSeconds(2.5), CancellationToken.None);

        var result = ScenarioOf(update) switch
        {
            Scenario.InstallFails => UpdateOperationResult.Failed(unchecked((int)0x80070103)),
            Scenario.NeedsRestart => new UpdateOperationResult(OperationResultCode.Succeeded, RebootRequired: true),
            _ => new UpdateOperationResult(OperationResultCode.Succeeded),
        };
        if (result.Succeeded)
            _installed[update.UpdateId] = true;
        _history.Enqueue(new UpdateHistoryRecord(DateTime.UtcNow, update.Title, update.UpdateId, Operation: 1,
            (int)result.ResultCode, result.HResult, "DriverHaven", [DriverHistory.DriversCategoryId]));
        logger.LogInformation("Sample mode: simulated install of {Title}: {ResultCode}, reboot required {Reboot}",
            update.Title, result.ResultCode, result.RebootRequired);
        return result;
    }

    public async Task<IReadOnlyList<DriverUpdateInfo>> GetHiddenAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
        return _hidden.Values.ToList();
    }

    public async Task HideAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(TimeSpan.FromMilliseconds(400), CancellationToken.None);
        _hidden[update.UpdateId] = update;
        logger.LogInformation("Sample mode: simulated hiding {Title}", update.Title);
    }

    public async Task UnhideAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(TimeSpan.FromMilliseconds(400), CancellationToken.None);
        _hidden.TryRemove(update.UpdateId, out _);
        logger.LogInformation("Sample mode: simulated unhiding {Title}", update.Title);
    }

    public async Task<IReadOnlyList<DriverHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
        return DriverHistory.FromRecords(_history);
    }

    /// <summary>A success, a failure, an install by Windows itself, and a non-driver entry that the History tab filters out.</summary>
    private static IEnumerable<UpdateHistoryRecord> PastHistory()
    {
        var now = DateTime.UtcNow;
        string[] drivers = [DriverHistory.DriversCategoryId];
        return
        [
            new(now.AddDays(-40), "Contoso - Sensor - 2.3.1.0", Guid.NewGuid().ToString(), 1, 2, 0, "DriverHaven", drivers),
            new(now.AddDays(-12), "Contoso - Bluetooth - 1.2.0.5", Guid.NewGuid().ToString(), 1, 4, unchecked((int)0x80070103),
                "DriverHaven", drivers),
            new(now.AddDays(-5), "Contoso - USB - 4.0.0.12", Guid.NewGuid().ToString(), 1, 2, 0, "UpdateOrchestrator", drivers),
            new(now.AddDays(-3), "Sample cumulative update (not a driver)", Guid.NewGuid().ToString(), 1, 2, 0,
                "UpdateOrchestrator", ["28bc880e-0592-4cbf-8f95-c79b17911d5f"]),
        ];
    }

    private DriverUpdateInfo Offer(DriverUpdateInfo update, Scenario scenario)
    {
        _scenarios[update.UpdateId] = scenario;
        return update;
    }

    private Scenario ScenarioOf(DriverUpdateInfo update) => _scenarios.GetValueOrDefault(update.UpdateId, Scenario.Succeeds);

    private static int PreferredRank(string deviceClass)
    {
        var index = Array.FindIndex(PreferredClasses, c => c.Equals(deviceClass, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? PreferredClasses.Length : index;
    }

    private static DriverUpdateInfo ForDevice(DeviceDriverInfo device)
    {
        var version = NextVersion(device.DriverVersion);
        var provider = device.DriverProvider ?? device.Manufacturer;
        var isDisplay = device.DeviceClass.Equals("Display", StringComparison.OrdinalIgnoreCase);
        var hash = Hash(device.DeviceId);

        return new DriverUpdateInfo
        {
            UpdateId = new Guid(hash.AsSpan(0, 16)).ToString(),
            RevisionNumber = 1,
            Title = $"{provider} - {device.DeviceClass} - {version}",
            Description = "Sample update (not real).",
            DriverProvider = provider,
            DriverManufacturer = device.Manufacturer,
            DriverModel = device.DeviceName,
            DriverClass = device.DeviceClass,
            DriverVersion = version,
            DriverDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-21)),
            HardwareIds = [device.HardwareIds[0]],
            MaxDownloadSize = isDisplay ? 640L * 1024 * 1024 : (hash[16] % 24 + 1) * 1024L * 1024,
        };
    }

    private static DriverUpdateInfo NotPresentDevice() => new()
    {
        UpdateId = "5a3c9e1f-0000-4d1e-9a51-5a3c9e1f0001",
        RevisionNumber = 1,
        Title = "Contoso - Sensor - 2.4.0.7",
        Description = "Sample update (not real) for a device that isn't connected.",
        DriverProvider = "Contoso",
        DriverManufacturer = "Contoso",
        DriverModel = "Contoso Sample Sensor",
        DriverClass = "Sensor",
        DriverVersion = "2.4.0.7",
        DriverDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-60)),
        HardwareIds = [@"SAMPLE\CONTOSO_SENSOR"],
        MaxDownloadSize = 3L * 1024 * 1024,
    };

    private static string NextVersion(string? current) =>
        Version.TryParse(current, out var v)
            ? new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0) + 1).ToString()
            : "1.0.0.1";

    private static byte[] Hash(string text) => SHA256.HashData(Encoding.UTF8.GetBytes(text));
}
