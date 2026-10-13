using DriverUpdater.Core.Models;

namespace DriverUpdater.Core.Services;

public interface IRestorePointService
{
    /// <summary>Asks Windows for a restore point. Never throws for expected failures; inspect the result.</summary>
    Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken = default);
}
