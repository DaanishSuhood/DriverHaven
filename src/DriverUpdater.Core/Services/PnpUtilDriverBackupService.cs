using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DriverUpdater.Core.Infrastructure;
using DriverUpdater.Core.Models;
using Microsoft.Extensions.Logging;

namespace DriverUpdater.Core.Services;

/// <summary>Backs up driver packages with <c>pnputil /export-driver</c>.</summary>
public sealed class PnpUtilDriverBackupService(
    IProcessRunner processRunner,
    AppPaths paths,
    TimeProvider time,
    ILogger<PnpUtilDriverBackupService> logger) : IDriverBackupService
{
    public const string ManifestFileName = "manifest.json";

    public const string RestoreInstructions =
        "To reinstall a backed-up driver, run in an elevated prompt: pnputil /add-driver \"<this folder>\\<ExportFolder>\\*.inf\" /install";

    private static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(3);

    /// <summary>Also used by <see cref="DriverBackupCatalog"/> to read manifests back.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // The manifest is a local file meant to be read by people; don't escape quotes and <> as " etc.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<DriverBackupResult> BackupAsync(IReadOnlyCollection<DeviceDriverInfo> devices,
        CancellationToken cancellationToken = default)
    {
        var folder = CreateBackupFolder();
        logger.LogInformation("Backing up drivers for {Count} devices to {Folder}", devices.Count, folder);

        // Identical devices share one package; export each INF only once.
        var exports = new Dictionary<string, (DriverBackupStatus Status, string? Detail, string? ExportFolder)>(
            StringComparer.OrdinalIgnoreCase);
        var entries = new List<DriverBackupEntry>();

        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (DriverBackupStatus Status, string? Detail, string? ExportFolder) outcome;
            if (!device.HasDriver)
            {
                outcome = (DriverBackupStatus.SkippedNoDriver, "No driver is installed", null);
            }
            else if (!device.IsThirdPartyDriver)
            {
                outcome = (DriverBackupStatus.SkippedInbox, "Inbox driver that ships with Windows; nothing to back up", null);
            }
            else if (!exports.TryGetValue(device.InfName!, out outcome))
            {
                outcome = await ExportAsync(device.InfName!, folder, cancellationToken);
                exports[device.InfName!] = outcome;
            }

            entries.Add(new DriverBackupEntry(device.DeviceName, device.DeviceId, device.InfName, device.DriverProvider,
                device.DriverVersion, device.DriverDate, device.HardwareIds, outcome.Status, outcome.Detail, outcome.ExportFolder));
        }

        var manifestPath = Path.Combine(folder, ManifestFileName);
        var manifest = new DriverBackupManifest(time.GetLocalNow(), Environment.MachineName,
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString(), RestoreInstructions, entries);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);

        var result = new DriverBackupResult(folder, manifestPath, entries);
        logger.LogInformation("Backup finished: {Exported} exported, {Skipped} skipped, {Failed} failed",
            result.ExportedCount, result.SkippedCount, result.FailedCount);
        return result;
    }

    private async Task<(DriverBackupStatus, string?, string?)> ExportAsync(string infName, string backupFolder,
        CancellationToken cancellationToken)
    {
        var exportFolder = Path.GetFileNameWithoutExtension(infName);
        var target = Path.Combine(backupFolder, exportFolder);
        Directory.CreateDirectory(target);

        try
        {
            var result = await processRunner.RunAsync(SystemTools.PnpUtilPath, ["/export-driver", infName, target],
                ExportTimeout, cancellationToken);

            if (result.Succeeded && Directory.EnumerateFiles(target, "*.inf").Any())
                return (DriverBackupStatus.Exported, null, exportFolder);

            var reason = result.Succeeded ? "pnputil reported success but exported no .inf file" : result.LastMessageLine;
            logger.LogWarning("Export of {InfName} failed: {Reason}", infName, reason);
            return (DriverBackupStatus.Failed, reason, null);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException
                                       or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "Export of {InfName} failed", infName);
            return (DriverBackupStatus.Failed, ex.Message, null);
        }
    }

    private string CreateBackupFolder()
    {
        var baseFolder = paths.BackupFolderFor(time.GetLocalNow().DateTime);
        var folder = baseFolder;
        for (var suffix = 2; Directory.Exists(folder); suffix++)
            folder = $"{baseFolder}-{suffix}";

        Directory.CreateDirectory(folder);
        return folder;
    }
}
