using DriverUpdater.Core.Models;

namespace DriverUpdater.Core.Services;

public interface IPendingRestartService
{
    /// <summary>Reads the signals Windows leaves when a restart is pending. Read-only.</summary>
    Task<PendingRestartSignals> GetSignalsAsync(CancellationToken cancellationToken = default);
}
