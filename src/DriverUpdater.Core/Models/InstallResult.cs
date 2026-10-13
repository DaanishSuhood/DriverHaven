namespace DriverUpdater.Core.Models;

public enum InstallOutcome
{
    /// <summary>Every selected update was attempted; individual ones may still have failed.</summary>
    Completed,

    /// <summary>The user cancelled; updates not yet reached are <see cref="DriverInstallStatus.NotAttempted"/>.</summary>
    Cancelled,

    /// <summary>No restore point could be made and the user chose not to continue. Nothing was changed.</summary>
    DeclinedWithoutRestorePoint,

    /// <summary>Windows was waiting for a restart and the user chose to restart first. Nothing was changed.</summary>
    DeclinedPendingRestart,

    /// <summary>The backup folder couldn't be written, so nothing was installed.</summary>
    BackupFailed,
}

public enum DriverInstallStatus
{
    NotAttempted,
    Installed,
    InstalledWithErrors,
    DownloadFailed,
    InstallFailed,
}

/// <summary>What happened to one selected update.</summary>
public sealed record DriverInstallItemResult(
    MatchedDriverUpdate Match,
    DriverInstallStatus Status,
    bool RebootRequired = false,
    int HResult = 0,
    string? Detail = null)
{
    public bool Succeeded => Status is DriverInstallStatus.Installed or DriverInstallStatus.InstalledWithErrors;

    public bool Failed => Status is DriverInstallStatus.DownloadFailed or DriverInstallStatus.InstallFailed;
}

/// <param name="RestorePoint">Null only when the run was cancelled before a restore point was made.</param>
/// <param name="Backup">Null when no update targeted an installed device, or the run stopped before the backup.</param>
/// <param name="Message">Why the run stopped early, for outcomes other than <see cref="InstallOutcome.Completed"/>.</param>
public sealed record InstallResult(
    InstallOutcome Outcome,
    IReadOnlyList<DriverInstallItemResult> Items,
    RestorePointResult? RestorePoint,
    DriverBackupResult? Backup,
    string? Message = null)
{
    public bool RebootRequired => Items.Any(i => i.RebootRequired);

    public int SucceededCount => Items.Count(i => i.Succeeded);

    public int FailedCount => Items.Count(i => i.Failed);

    public int NotAttemptedCount => Items.Count(i => i.Status == DriverInstallStatus.NotAttempted);
}
