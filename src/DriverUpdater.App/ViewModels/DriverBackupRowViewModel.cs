using DriverUpdater.Core.Models;

namespace DriverUpdater.App.ViewModels;

/// <summary>One row on the Driver backups tab: a backed-up driver next to the driver its device uses now.</summary>
/// <param name="device">The device as found by the last scan, or null when it isn't connected.</param>
public sealed class DriverBackupRowViewModel(BackedUpDriver driver, DeviceDriverInfo? device)
{
    public BackedUpDriver Driver { get; } = driver;

    public DateTime BackedUpAt => Driver.BackedUpAt.LocalDateTime;

    public string DeviceName => Driver.Entry.DeviceName;

    public string BackedUpVersion => Driver.Entry.DriverVersion ?? "Unknown";

    public DateOnly? BackedUpDate => Driver.Entry.DriverDate;

    public string CurrentVersion => device is null ? "—" : device.DriverVersion ?? "Unknown";

    public string Provider => Driver.Entry.DriverProvider ?? "";

    public bool IsInUse =>
        device is not null && string.Equals(device.DriverVersion, Driver.Entry.DriverVersion, StringComparison.OrdinalIgnoreCase);

    public bool CanRestore => Driver.FilesPresent && device is not null && !IsInUse;

    public string Note =>
        !Driver.FilesPresent ? "The backup's files are missing"
        : device is null ? "Device isn't connected"
        : IsInUse ? "In use now"
        : "";

    public string Details => $"{Driver.BackupFolder}\nINF: {Driver.Entry.InfName}\nHardware IDs: {string.Join(", ", Driver.Entry.HardwareIds)}";
}
