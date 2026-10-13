using DriverUpdater.Core.Models;

namespace DriverUpdater.Core.Services;

public interface IDriverBackupService
{
    /// <summary>
    /// Exports the current third-party driver package of each device into a new timestamped folder and writes a
    /// manifest.json describing every device (including skipped ones). Per-driver failures are recorded, not thrown.
    /// </summary>
    Task<DriverBackupResult> BackupAsync(IReadOnlyCollection<DeviceDriverInfo> devices, CancellationToken cancellationToken = default);
}
