using System.Text.Json;
using DriverUpdater.Core.Infrastructure;
using DriverUpdater.Core.Models;
using Microsoft.Extensions.Logging;

namespace DriverUpdater.Core.Services;

public interface IDriverBackupCatalog
{
    /// <summary>Every exported driver in the backups folder, newest backup first. Unreadable backups are skipped.</summary>
    Task<IReadOnlyList<BackedUpDriver>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads the manifest.json files that <see cref="PnpUtilDriverBackupService"/> writes.</summary>
public sealed class DriverBackupCatalog(AppPaths paths, ILogger<DriverBackupCatalog> logger) : IDriverBackupCatalog
{
    public Task<IReadOnlyList<BackedUpDriver>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => List(cancellationToken), cancellationToken);

    private IReadOnlyList<BackedUpDriver> List(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(paths.Backups))
            return [];

        var drivers = new List<BackedUpDriver>();
        foreach (var folder in Directory.EnumerateDirectories(paths.Backups))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(folder, PnpUtilDriverBackupService.ManifestFileName);
            if (!File.Exists(manifestPath))
                continue;

            DriverBackupManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<DriverBackupManifest>(File.ReadAllText(manifestPath),
                    PnpUtilDriverBackupService.JsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                logger.LogWarning(ex, "Skipping unreadable backup manifest {Path}", manifestPath);
                continue;
            }
            if (manifest?.Entries is null)
                continue;

            foreach (var entry in manifest.Entries)
            {
                if (entry.Status == DriverBackupStatus.Exported && entry.ExportFolder is not null)
                    drivers.Add(new BackedUpDriver(folder, manifest.CreatedAt, entry, FindInf(Path.Combine(folder, entry.ExportFolder))));
            }
        }

        // The same driver is often backed up more than once; keep its newest copy.
        var result = drivers
            .OrderByDescending(d => d.BackedUpAt)
            .ThenBy(d => d.Entry.DeviceName, StringComparer.CurrentCultureIgnoreCase)
            .DistinctBy(d => (d.Entry.DeviceId.ToUpperInvariant(), d.Entry.InfName?.ToUpperInvariant(), d.Entry.DriverVersion))
            .ToList();
        logger.LogInformation("Found {Count} backed-up drivers in {Folder}", result.Count, paths.Backups);
        return result;
    }

    /// <summary>pnputil exports a package as its own .inf plus the files it lists; a package has one .inf.</summary>
    private static string? FindInf(string exportFolder) =>
        Directory.Exists(exportFolder)
            ? Directory.EnumerateFiles(exportFolder, "*.inf").Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            : null;
}
