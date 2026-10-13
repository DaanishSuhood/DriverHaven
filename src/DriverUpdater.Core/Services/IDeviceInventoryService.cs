using DriverUpdater.Core.Models;

namespace DriverUpdater.Core.Services;

public interface IDeviceInventoryService
{
    /// <summary>Returns every present device with its current driver, sorted by class then name.</summary>
    Task<IReadOnlyList<DeviceDriverInfo>> GetDevicesAsync(CancellationToken cancellationToken = default);
}
