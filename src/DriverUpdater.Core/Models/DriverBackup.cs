namespace DriverUpdater.Core.Models;

public enum DriverBackupStatus
{
    Exported,

    /// <summary>The driver ships with Windows (not an oemNN.inf); Windows keeps it, so there's nothing to save.</summary>
    SkippedInbox,

    SkippedNoDriver,

    Failed,
}

/// <summary>One device's line in a backup's manifest.json.</summary>
public sealed record DriverBackupEntry(
    string DeviceName,
    string DeviceId,
    string? InfName,
    string? DriverProvider,
    string? DriverVersion,
    DateOnly? DriverDate,
    IReadOnlyList<string> HardwareIds,
    DriverBackupStatus Status,
    string? Detail,
    string? ExportFolder);

public sealed record DriverBackupManifest(
    DateTimeOffset CreatedAt,
    string MachineName,
    string? AppVersion,
    string RestoreInstructions,
    IReadOnlyList<DriverBackupEntry> Entries);

public sealed record DriverBackupResult(string Folder, string ManifestPath, IReadOnlyList<DriverBackupEntry> Entries)
{
    public int ExportedCount => Entries.Count(e => e.Status == DriverBackupStatus.Exported);

    public int SkippedCount => Entries.Count(e => e.Status is DriverBackupStatus.SkippedInbox or DriverBackupStatus.SkippedNoDriver);

    public int FailedCount => Entries.Count(e => e.Status == DriverBackupStatus.Failed);
}
