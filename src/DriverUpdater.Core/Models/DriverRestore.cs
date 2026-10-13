namespace DriverUpdater.Core.Models;

/// <summary>One exported driver in a backup folder, as listed on the Driver backups tab.</summary>
/// <param name="InfPath">The exported package's .inf, or null when its files are missing.</param>
public sealed record BackedUpDriver(string BackupFolder, DateTimeOffset BackedUpAt, DriverBackupEntry Entry, string? InfPath)
{
    public bool FilesPresent => InfPath is not null;
}

public enum DriverRestoreOutcome
{
    /// <summary>The device now uses the backed-up driver.</summary>
    Restored,

    /// <summary>Windows installed the driver but needs a restart before the device uses it.</summary>
    RestartRequired,

    /// <summary>Windows installed the driver, but the device still uses another one.</summary>
    NotInUse,

    Failed,

    /// <summary>No restore point could be made and the user chose not to continue. Nothing was changed.</summary>
    DeclinedWithoutRestorePoint,
}

/// <param name="CurrentVersion">The device's driver version afterwards, when it could be read.</param>
public sealed record DriverRestoreResult(
    DriverRestoreOutcome Outcome,
    string Message,
    string? CurrentVersion = null,
    RestorePointResult? RestorePoint = null)
{
    public bool Succeeded => Outcome is DriverRestoreOutcome.Restored or DriverRestoreOutcome.RestartRequired;
}
