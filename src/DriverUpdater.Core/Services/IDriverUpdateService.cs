using DriverUpdater.Core.Models;

namespace DriverUpdater.Core.Services;

public interface IDriverUpdateService
{
    /// <summary>Searches Windows Update for applicable driver updates that aren't installed or hidden.</summary>
    /// <exception cref="WindowsUpdateException">The search failed; the message is user-readable.</exception>
    Task<DriverSearchResult> SearchAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads one update (accepting its EULA if it has one). Windows Update failures are returned, not thrown.
    /// Once started it runs to completion: the token is only checked before starting.
    /// </summary>
    Task<UpdateOperationResult> DownloadAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs one previously downloaded update. Windows Update failures are returned, not thrown.
    /// Once started it runs to completion: the token is only checked before starting.
    /// </summary>
    Task<UpdateOperationResult> InstallAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Driver updates that are hidden. Reads Windows Update's local cache, so it is quick but only current after a
    /// <see cref="SearchAsync"/>.
    /// </summary>
    /// <exception cref="WindowsUpdateException">The search failed; the message is user-readable.</exception>
    Task<IReadOnlyList<DriverUpdateInfo>> GetHiddenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an update hidden in Windows Update itself, so later searches (and Windows Update) stop offering it.
    /// </summary>
    /// <exception cref="WindowsUpdateException">The update couldn't be found or changed; the message is user-readable.</exception>
    Task HideAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default);

    /// <summary>Clears the hidden mark, so the update is offered again.</summary>
    /// <exception cref="WindowsUpdateException">The update couldn't be found or changed; the message is user-readable.</exception>
    Task UnhideAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default);

    /// <summary>Driver installs and uninstalls recorded in the Windows Update history, newest first.</summary>
    /// <exception cref="WindowsUpdateException">The history couldn't be read; the message is user-readable.</exception>
    Task<IReadOnlyList<DriverHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default);
}

/// <param name="DriversExcludedByPolicy">
/// True when the "Do not include drivers with Windows Updates" policy is set, which can hide driver offers.
/// </param>
public sealed record DriverSearchResult(IReadOnlyList<DriverUpdateInfo> Updates, bool DriversExcludedByPolicy);
