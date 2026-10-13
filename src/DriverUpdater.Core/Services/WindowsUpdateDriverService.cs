using System.Diagnostics;
using System.Runtime.InteropServices;
using DriverUpdater.Core.Infrastructure;
using DriverUpdater.Core.Models;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DriverUpdater.Core.Services;

/// <summary>
/// Talks to the Windows Update Agent through its COM API (Microsoft.Update.Session). Late binding via
/// <c>dynamic</c> avoids a COMReference, which <c>dotnet build</c> can't resolve.
/// </summary>
public sealed class WindowsUpdateDriverService(ILogger<WindowsUpdateDriverService> logger) : IDriverUpdateService
{
    public const string DriverSearchCriteria = "IsInstalled=0 and Type='Driver' and IsHidden=0";

    public const string HiddenDriverSearchCriteria = "IsInstalled=0 and Type='Driver' and IsHidden=1";

    private const string ClientApplicationId = ProductInfo.Name;

    // ServerSelection.ssWindowsUpdate: query Microsoft directly even if the PC is managed by WSUS.
    private const int ServerSelectionWindowsUpdate = 2;

    // OperationResultCode values.
    private const int OrcSucceededWithErrors = 3;
    private const int OrcFailed = 4;

    private const int DownloadPriorityHigh = 3;

    // WU_E_INSTALL_NOT_ALLOWED: another installation is in progress or a mandatory restart is pending.
    private const int WuInstallNotAllowed = unchecked((int)0x80240016);

    public Task<DriverSearchResult> SearchAsync(CancellationToken cancellationToken = default) =>
        RunAbandonable(Search, cancellationToken);

    public Task<IReadOnlyList<DriverUpdateInfo>> GetHiddenAsync(CancellationToken cancellationToken = default) =>
        RunAbandonable(GetHidden, cancellationToken);

    public Task<IReadOnlyList<DriverHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default) =>
        RunAbandonable(GetHistory, cancellationToken);

    public Task HideAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default) =>
        RunToCompletion(() => SetHidden(update, hidden: true), cancellationToken);

    public Task UnhideAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default) =>
        RunToCompletion(() => SetHidden(update, hidden: false), cancellationToken);

    /// <summary>
    /// For read-only WUA calls. They block for a long time, so they get their own (MTA) thread instead of a pool
    /// thread. They can't be aborted; on cancel we stop waiting and let them finish in the background, observing any
    /// late failure so it isn't reported as unobserved.
    /// </summary>
    private async Task<T> RunAbandonable<T>(Func<T> work, CancellationToken cancellationToken)
    {
        var task = Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        _ = task.ContinueWith(t => logger.LogDebug(t.Exception, "Background Windows Update query ended with an error"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

        return await task.WaitAsync(cancellationToken);
    }

    private DriverSearchResult Search()
    {
        var stopwatch = Stopwatch.StartNew();
        var excludedByPolicy = DriversExcludedByPolicy();
        if (excludedByPolicy)
            logger.LogWarning("Policy 'ExcludeWUDriversInQualityUpdate' is set; Windows Update may not offer drivers");

        try
        {
            dynamic session = CreateSession();
            session.ClientApplicationID = ClientApplicationId;

            dynamic searcher = session.CreateUpdateSearcher();
            searcher.ServerSelection = ServerSelectionWindowsUpdate;
            searcher.Online = true;

            logger.LogInformation("Searching Windows Update for drivers: {Criteria}", DriverSearchCriteria);
            dynamic result = searcher.Search(DriverSearchCriteria);

            int resultCode = result.ResultCode;
            if (resultCode >= OrcFailed)
                throw new WindowsUpdateException($"Windows Update search did not complete (result code {resultCode}).", 0);
            if (resultCode == OrcSucceededWithErrors)
                logger.LogWarning("Windows Update search succeeded with errors");

            List<DriverUpdateInfo> updates = ToDriverUpdates(result.Updates, "Offered");
            logger.LogInformation("Windows Update offered {Count} driver updates in {ElapsedMs} ms",
                updates.Count, stopwatch.ElapsedMilliseconds);
            return new DriverSearchResult(updates, excludedByPolicy);
        }
        catch (COMException ex)
        {
            logger.LogError(ex, "Windows Update search failed with 0x{HResult:X8}", ex.HResult);
            throw new WindowsUpdateException(WindowsUpdateErrors.Describe(ex.HResult), ex.HResult, ex);
        }
    }

    /// <summary>
    /// Offline: the online search that runs first has just refreshed the agent's cache, and going online again would
    /// take as long as that search.
    /// </summary>
    private IReadOnlyList<DriverUpdateInfo> GetHidden()
    {
        try
        {
            dynamic session = CreateSession();
            session.ClientApplicationID = ClientApplicationId;

            dynamic searcher = session.CreateUpdateSearcher();
            searcher.ServerSelection = ServerSelectionWindowsUpdate;
            searcher.Online = false;

            dynamic result = searcher.Search(HiddenDriverSearchCriteria);
            List<DriverUpdateInfo> updates = ToDriverUpdates(result.Updates, "Hidden");
            logger.LogInformation("{Count} driver updates are hidden", updates.Count);
            return updates;
        }
        catch (COMException ex)
        {
            logger.LogError(ex, "Hidden update search failed with 0x{HResult:X8}", ex.HResult);
            throw new WindowsUpdateException(WindowsUpdateErrors.Describe(ex.HResult), ex.HResult, ex);
        }
    }

    private List<DriverUpdateInfo> ToDriverUpdates(dynamic collection, string logLabel)
    {
        var updates = new List<DriverUpdateInfo>();
        int count = collection.Count;
        for (var i = 0; i < count; i++)
        {
            object item = collection[i];
            var update = ToDriverUpdate(item);
            updates.Add(update);
            logger.LogInformation("{Label}: {Title} [{UpdateId}] date {DriverDate}, hardware IDs {HardwareIds}",
                logLabel, update.Title, update.UpdateId, update.DriverDate, string.Join(", ", update.HardwareIds));
        }
        return updates;
    }

    private void SetHidden(DriverUpdateInfo update, bool hidden)
    {
        var action = hidden ? "Hide" : "Unhide";
        try
        {
            dynamic session = CreateSession();
            session.ClientApplicationID = ClientApplicationId;

            // An update that is already hidden only turns up when the criteria ask for hidden ones.
            object? found = FindUpdate(session, update, hidden ? null : "IsHidden=1")
                ?? throw new WindowsUpdateException(
                    "Windows Update no longer offers this update. Scan again to refresh the list.", 0);
            dynamic comUpdate = found;

            comUpdate.IsHidden = hidden;
            logger.LogInformation("{Action}: {Title} [{UpdateId}]", action, update.Title, update.UpdateId);
        }
        catch (COMException ex)
        {
            logger.LogError(ex, "{Action} of {Title} [{UpdateId}] failed with 0x{HResult:X8}",
                action, update.Title, update.UpdateId, ex.HResult);
            throw new WindowsUpdateException(WindowsUpdateErrors.Describe(ex.HResult), ex.HResult, ex);
        }
    }

    private IReadOnlyList<DriverHistoryEntry> GetHistory()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            dynamic session = CreateSession();
            session.ClientApplicationID = ClientApplicationId;
            dynamic searcher = session.CreateUpdateSearcher();

            var records = new List<UpdateHistoryRecord>();
            int total = searcher.GetTotalHistoryCount();
            if (total > 0)
            {
                dynamic history = searcher.QueryHistory(0, total);
                int count = history.Count;
                for (var i = 0; i < count; i++)
                {
                    object entry = history[i];
                    records.Add(ToHistoryRecord(entry));
                }
            }

            var entries = DriverHistory.FromRecords(records);
            logger.LogInformation("Read {Total} Windows Update history entries, {Drivers} for drivers, in {ElapsedMs} ms",
                records.Count, entries.Count, stopwatch.ElapsedMilliseconds);
            return entries;
        }
        catch (COMException ex)
        {
            logger.LogError(ex, "Reading the Windows Update history failed with 0x{HResult:X8}", ex.HResult);
            throw new WindowsUpdateException(WindowsUpdateErrors.Describe(ex.HResult), ex.HResult, ex);
        }
    }

    private UpdateHistoryRecord ToHistoryRecord(object comEntry)
    {
        dynamic entry = comEntry;

        var categoryIds = new List<string>();
        var categories = TryGet<object>(() => entry.Categories);
        if (categories is not null)
        {
            dynamic list = categories;
            int count = list.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic category = list[i];
                if (TryGet<string>(() => category.CategoryID) is { } id)
                    categoryIds.Add(id);
            }
        }

        return new UpdateHistoryRecord(
            DateUtc: TryGet<DateTime?>(() => entry.Date) ?? DateTime.MinValue,
            Title: TryGet<string>(() => entry.Title),
            UpdateId: TryGet<string>(() => entry.UpdateIdentity.UpdateID),
            Operation: TryGet<int?>(() => entry.Operation) ?? 0,
            ResultCode: TryGet<int?>(() => entry.ResultCode) ?? 0,
            HResult: TryGet<int?>(() => entry.HResult) ?? 0,
            ClientApplicationId: TryGet<string>(() => entry.ClientApplicationID),
            CategoryIds: categoryIds);
    }

    public Task<UpdateOperationResult> DownloadAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default) =>
        RunToCompletion(() => Download(update), cancellationToken);

    public Task<UpdateOperationResult> InstallAsync(DriverUpdateInfo update, CancellationToken cancellationToken = default) =>
        RunToCompletion(() => Install(update), cancellationToken);

    /// <summary>
    /// Downloads and installs can't be aborted safely, so unlike search they are never abandoned: the token only
    /// stops one from starting.
    /// </summary>
    private static Task<UpdateOperationResult> RunToCompletion(Func<UpdateOperationResult> work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static Task RunToCompletion(Action work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private UpdateOperationResult Download(DriverUpdateInfo update)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            dynamic session = CreateSession();
            session.ClientApplicationID = ClientApplicationId;

            object? found = FindUpdate(session, update);
            if (found is null)
                return NotOffered(update);
            dynamic comUpdate = found;

            if (TryGet<bool?>(() => comUpdate.IsDownloaded) == true)
            {
                logger.LogInformation("Already downloaded: {Title} [{UpdateId}]", update.Title, update.UpdateId);
                return new UpdateOperationResult(OperationResultCode.Succeeded);
            }

            AcceptEulaIfNeeded(comUpdate, update);

            dynamic downloader = session.CreateUpdateDownloader();
            downloader.ClientApplicationID = ClientApplicationId;
            downloader.Priority = DownloadPriorityHigh;
            downloader.Updates = ToCollection(comUpdate);

            logger.LogInformation("Downloading {Title} [{UpdateId}], up to {Bytes} bytes",
                update.Title, update.UpdateId, update.MaxDownloadSize);
            dynamic result = downloader.Download();

            UpdateOperationResult outcome = ToOperationResult(result, rebootRequired: false);
            LogOutcome("Download", update, outcome, stopwatch);
            return outcome;
        }
        catch (Exception ex) when (ex is COMException or WindowsUpdateException)
        {
            logger.LogError(ex, "Download of {Title} [{UpdateId}] failed with 0x{HResult:X8}", update.Title, update.UpdateId, ex.HResult);
            return UpdateOperationResult.Failed(ex.HResult, ex is WindowsUpdateException ? ex.Message : null);
        }
    }

    private UpdateOperationResult Install(DriverUpdateInfo update)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            dynamic session = CreateSession();
            session.ClientApplicationID = ClientApplicationId;

            object? found = FindUpdate(session, update);
            if (found is null)
                return NotOffered(update);
            dynamic comUpdate = found;

            AcceptEulaIfNeeded(comUpdate, update);

            dynamic installer = session.CreateUpdateInstaller();
            installer.ClientApplicationID = ClientApplicationId;
            installer.AllowSourcePrompts = false;
            if ((bool)installer.IsBusy)
                return UpdateOperationResult.Failed(WuInstallNotAllowed);
            if (TryGet<bool?>(() => installer.RebootRequiredBeforeInstallation) == true)
            {
                logger.LogWarning("Windows needs a restart before {Title} [{UpdateId}] can be installed", update.Title, update.UpdateId);
                return UpdateOperationResult.Failed(WuInstallNotAllowed, WindowsUpdateErrors.RestartPending);
            }
            installer.Updates = ToCollection(comUpdate);

            logger.LogInformation("Installing {Title} [{UpdateId}]", update.Title, update.UpdateId);
            dynamic result = installer.Install();

            bool rebootRequired = result.RebootRequired;
            UpdateOperationResult outcome = ToOperationResult(result, rebootRequired);
            LogOutcome("Install", update, outcome, stopwatch);
            return outcome;
        }
        catch (Exception ex) when (ex is COMException or WindowsUpdateException)
        {
            logger.LogError(ex, "Install of {Title} [{UpdateId}] failed with 0x{HResult:X8}", update.Title, update.UpdateId, ex.HResult);
            return UpdateOperationResult.Failed(ex.HResult, ex is WindowsUpdateException ? ex.Message : null);
        }
    }

    /// <summary>
    /// Re-finds the COM IUpdate by ID, so callers only need to hold plain <see cref="DriverUpdateInfo"/> records.
    /// The offline search reads the agent's local cache (fast); the online one covers a cache that has been cleared.
    /// </summary>
    private object? FindUpdate(dynamic session, DriverUpdateInfo update, string? extraCriteria = null)
    {
        if (!Guid.TryParse(update.UpdateId, out var updateId))
            throw new WindowsUpdateException($"\"{update.UpdateId}\" is not a valid update ID.", 0);

        var criteria = $"UpdateID='{updateId:D}'" + (update.RevisionNumber > 0 ? $" and RevisionNumber={update.RevisionNumber}" : "")
            + (extraCriteria is null ? "" : $" and {extraCriteria}");
        foreach (var online in new[] { false, true })
        {
            dynamic searcher = session.CreateUpdateSearcher();
            searcher.ServerSelection = ServerSelectionWindowsUpdate;
            searcher.Online = online;

            logger.LogDebug("Looking up update ({Mode}): {Criteria}", online ? "online" : "offline", criteria);
            dynamic result;
            try
            {
                result = searcher.Search(criteria);
            }
            catch (COMException ex) when (!online)
            {
                logger.LogDebug("Offline lookup failed with 0x{HResult:X8}; trying online", ex.HResult);
                continue;
            }

            dynamic updates = result.Updates;
            if ((int)updates.Count > 0)
                return updates[0];
        }
        return null;
    }

    private UpdateOperationResult NotOffered(DriverUpdateInfo update)
    {
        logger.LogWarning("Windows Update no longer offers {Title} [{UpdateId}]", update.Title, update.UpdateId);
        return UpdateOperationResult.Failed(0, "Windows Update no longer offers this update. Scan again to refresh the list.");
    }

    private void AcceptEulaIfNeeded(dynamic comUpdate, DriverUpdateInfo update)
    {
        if ((bool)comUpdate.EulaAccepted)
            return;
        logger.LogInformation("Accepting the license terms of {Title} [{UpdateId}]", update.Title, update.UpdateId);
        comUpdate.AcceptEula();
    }

    private static object ToCollection(object comUpdate)
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")
            ?? throw new WindowsUpdateException("The Windows Update Agent is not available on this PC.", 0);
        dynamic collection = Activator.CreateInstance(type)!;
        collection.Add(comUpdate);
        return collection;
    }

    /// <summary>Prefers the per-update result (index 0, the only update) over the job's overall one.</summary>
    private UpdateOperationResult ToOperationResult(dynamic jobResult, bool rebootRequired)
    {
        int resultCode = jobResult.ResultCode;
        int hresult = jobResult.HResult;

        var perUpdate = TryGet<object>(() => jobResult.GetUpdateResult(0));
        if (perUpdate is not null)
        {
            dynamic item = perUpdate;
            resultCode = item.ResultCode;
            hresult = item.HResult;
            rebootRequired |= TryGet<bool?>(() => item.RebootRequired) ?? false;
        }

        return new UpdateOperationResult((OperationResultCode)resultCode, hresult, rebootRequired);
    }

    private void LogOutcome(string operation, DriverUpdateInfo update, UpdateOperationResult outcome, Stopwatch stopwatch) =>
        logger.Log(outcome.Succeeded ? LogLevel.Information : LogLevel.Warning,
            "{Operation} of {Title} [{UpdateId}]: {ResultCode}, HRESULT 0x{HResult:X8}, reboot required {RebootRequired}, in {ElapsedMs} ms",
            operation, update.Title, update.UpdateId, outcome.ResultCode, outcome.HResult, outcome.RebootRequired,
            stopwatch.ElapsedMilliseconds);

    private static dynamic CreateSession()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session")
            ?? throw new WindowsUpdateException("The Windows Update Agent is not available on this PC.", 0);
        return Activator.CreateInstance(type)!;
    }

    private DriverUpdateInfo ToDriverUpdate(object comUpdate)
    {
        dynamic update = comUpdate;
        string title = update.Title;
        string? driverHardwareId = TryGet<string>(() => update.DriverHardwareID);

        return new DriverUpdateInfo
        {
            UpdateId = update.Identity.UpdateID,
            RevisionNumber = update.Identity.RevisionNumber,
            Title = title,
            Description = TryGet<string>(() => update.Description),
            DriverProvider = TryGet<string>(() => update.DriverProvider),
            DriverManufacturer = TryGet<string>(() => update.DriverManufacturer),
            DriverModel = TryGet<string>(() => update.DriverModel),
            DriverClass = TryGet<string>(() => update.DriverClass),
            DriverVersion = DriverUpdateTitle.ParseVersion(title),
            DriverDate = TryGet<DateTime?>(() => update.DriverVerDate) is { } date ? DateOnly.FromDateTime(date) : null,
            HardwareIds = CollectHardwareIds(comUpdate, driverHardwareId),
            MaxDownloadSize = TryGet<decimal?>(() => update.MaxDownloadSize) is { } size ? (long)size : 0,
            IsDownloaded = TryGet<bool?>(() => update.IsDownloaded) ?? false,
        };
    }

    /// <summary>
    /// One update can apply to several devices; IWindowsDriverUpdate4.WindowsDriverUpdateEntries lists each
    /// target. Older agents only expose the single DriverHardwareID.
    /// </summary>
    private IReadOnlyList<string> CollectHardwareIds(object comUpdate, string? driverHardwareId)
    {
        dynamic update = comUpdate;
        var ids = new List<string>();
        if (!string.IsNullOrWhiteSpace(driverHardwareId))
            ids.Add(driverHardwareId);

        var entries = TryGet<object>(() => update.WindowsDriverUpdateEntries);
        if (entries is not null)
        {
            dynamic list = entries;
            int count = list.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic entry = list[i];
                var id = TryGet<string>(() => entry.DriverHardwareID);
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id);
            }
        }

        return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <remarks>
    /// The COM binder turns E_INVALIDARG into <see cref="ArgumentException"/>, not <see cref="COMException"/>;
    /// WUA returns it for some properties on some updates (e.g. IsDownloaded on a Samsung RNDIS driver).
    /// </remarks>
    private T? TryGet<T>(Func<object?> read)
    {
        try
        {
            return read() is T value ? value : default;
        }
        catch (Exception ex) when (ex is RuntimeBinderException or COMException or InvalidCastException or ArgumentException)
        {
            logger.LogDebug("Optional Windows Update property unavailable: {Message}", ex.Message);
            return default;
        }
    }

    private static bool DriversExcludedByPolicy()
    {
        return IsSet(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate")
            || IsSet(@"SOFTWARE\Microsoft\PolicyManager\current\device\Update");

        static bool IsSet(string path)
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue("ExcludeWUDriversInQualityUpdate") is int value && value == 1;
        }
    }
}
