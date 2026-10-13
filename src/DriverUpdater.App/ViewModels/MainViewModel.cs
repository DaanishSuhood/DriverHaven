using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriverUpdater.App.Services;
using DriverUpdater.Core.Infrastructure;
using DriverUpdater.Core.Models;
using DriverUpdater.Core.Services;
using Microsoft.Extensions.Logging;

namespace DriverUpdater.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int UpdatesTab = 0;

    private readonly IDeviceInventoryService _inventory;
    private readonly IDriverUpdateService _driverUpdates;
    private readonly IRestorePointService _restorePoints;
    private readonly IDriverBackupService _driverBackup;
    private readonly InstallOrchestrator _installer;
    private readonly IDriverBackupCatalog _backupCatalog;
    private readonly RestoreOrchestrator _restorer;
    private readonly IGpuDriverCheckService _gpuDriverCheck;
    private readonly AppSettingsStore _settings;
    private readonly ISystemRestartService _restart;
    private readonly IDialogService _dialogs;
    private readonly AppPaths _paths;
    private readonly ILogger<MainViewModel> _logger;
    private bool _installCancelRequested;

    public MainViewModel(
        IDeviceInventoryService inventory,
        IDriverUpdateService driverUpdates,
        IRestorePointService restorePoints,
        IDriverBackupService driverBackup,
        InstallOrchestrator installer,
        IDriverBackupCatalog backupCatalog,
        RestoreOrchestrator restorer,
        IGpuDriverCheckService gpuDriverCheck,
        AppSettingsStore settings,
        ISystemRestartService restart,
        IDialogService dialogs,
        AppOptions options,
        AppPaths paths,
        ILogger<MainViewModel> logger)
    {
        _inventory = inventory;
        _driverUpdates = driverUpdates;
        _restorePoints = restorePoints;
        _driverBackup = driverBackup;
        _installer = installer;
        _backupCatalog = backupCatalog;
        _restorer = restorer;
        _gpuDriverCheck = gpuDriverCheck;
        _settings = settings;
        _checkGpuVendorDrivers = settings.Load().CheckGpuVendorDrivers;
        _restart = restart;
        _dialogs = dialogs;
        _paths = paths;
        _logger = logger;
        IsElevated = Elevation.IsAdministrator();
        IsSampleMode = options.SampleMode;
        Title = $"{ProductInfo.Name} {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}"
            + (IsSampleMode ? " - SAMPLE MODE" : "");

        DevicesView = CollectionViewSource.GetDefaultView(Devices);
        DevicesView.Filter = MatchesDeviceFilter;
    }

    public string Title { get; }

    /// <summary>Debug-only <c>--sample</c>: made-up updates and simulated installs; nothing on the PC changes.</summary>
    public bool IsSampleMode { get; }

    public bool IsElevated { get; }

    public string ElevationText => IsElevated ? "Running as administrator" : "Not running as administrator";

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CreateRestorePointCommand), nameof(BackupDriversCommand),
        nameof(InstallSelectedCommand), nameof(HideInstallPanelCommand), nameof(HideSelectedCommand),
        nameof(UnhideSelectedCommand), nameof(RestoreSelectedCommand))]
    private bool _isBusy;

    private bool CanRunOperation() => !IsBusy;

    private bool CanInstall() => !IsBusy && SelectedUpdateCount > 0;

    private bool CanUnhide() => !IsBusy && SelectedHiddenCount > 0;

    private bool CanRestore() => !IsBusy && SelectedBackup?.CanRestore == true;

    /// <summary>True while an install is running; closing the window is blocked then.</summary>
    public bool IsInstalling => InstallSelectedCommand.IsRunning;

    [ObservableProperty]
    private int _selectedTabIndex = UpdatesTab;

    #region Devices

    public ObservableCollection<DeviceDriverInfo> Devices { get; } = [];

    public ICollectionView DevicesView { get; }

    public int ProblemDeviceCount => Devices.Count(d => d.HasProblem);

    public string DevicesTabHeader => Devices.Count == 0
        ? "All devices"
        : ProblemDeviceCount == 0 ? $"All devices ({Devices.Count})" : $"All devices ({Devices.Count}, {ProblemDeviceCount} with problems)";

    [ObservableProperty]
    private string _deviceFilter = "";

    [ObservableProperty]
    private bool _showOnlyProblemDevices;

    /// <summary>Shown in place of the devices grid: before the first scan, or when the filter hides everything.</summary>
    public string DevicesMessage =>
        Devices.Count == 0 ? (IsBusy ? "Scanning devices…" : "No devices found yet. Click Scan for updates to list them.")
        : DevicesView.IsEmpty ? "No devices match the filter."
        : "";

    public bool ShowDevicesMessage => DevicesMessage.Length > 0;

    partial void OnDeviceFilterChanged(string value) => RefreshDevicesView();

    partial void OnShowOnlyProblemDevicesChanged(bool value) => RefreshDevicesView();

    private void RefreshDevicesView()
    {
        DevicesView.Refresh();
        OnPropertyChanged(nameof(DevicesMessage));
        OnPropertyChanged(nameof(ShowDevicesMessage));
    }

    #endregion

    #region Updates

    public ObservableCollection<DriverUpdateItemViewModel> Updates { get; } = [];

    public string UpdatesTabHeader => HasSearchedUpdates ? $"Updates available ({Updates.Count})" : "Updates available";

    /// <summary>True once a Windows Update search has completed (successfully or not) at least once.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatesTabHeader), nameof(UpdatesMessage), nameof(ShowUpdatesMessage))]
    private bool _hasSearchedUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatesMessage), nameof(ShowUpdatesMessage))]
    private string? _updateSearchError;

    [ObservableProperty]
    private string? _policyWarning;

    /// <summary>Message shown in place of the grid: before the first search, on error, or when up to date.</summary>
    public string UpdatesMessage =>
        UpdateSearchError is not null ? UpdateSearchError
        : !HasSearchedUpdates ? (IsBusy ? "Searching Windows Update for driver updates…" : "Click Scan to search Windows Update for driver updates.")
        : Updates.Count == 0 ? "Your drivers are up to date. Windows Update has no driver updates for this PC."
            + (HiddenUpdates.Count > 0 ? $" ({Plural(HiddenUpdates.Count, "hidden update")} not shown; see the Hidden updates tab.)" : "")
        : "";

    public bool ShowUpdatesMessage => UpdatesMessage.Length > 0;

    public bool HasUpdates => Updates.Count > 0;

    public int SelectedUpdateCount => Updates.Count(u => u.IsSelected);

    public string SelectionSummary
    {
        get
        {
            var selected = Updates.Where(u => u.IsSelected).ToList();
            return selected.Count == 0
                ? "No updates selected"
                : $"{selected.Count} selected · up to {ByteSize.Format(selected.Sum(u => u.Update.MaxDownloadSize))} to download";
        }
    }

    /// <summary>Header "select all" checkbox: true/false when all/none are selected, null when mixed.</summary>
    public bool? AllUpdatesSelected
    {
        get => Updates.Count == 0 || SelectedUpdateCount == 0 ? false
            : SelectedUpdateCount == Updates.Count ? true
            : null;
        set
        {
            if (value is not { } selectAll)
                return;
            foreach (var update in Updates)
                update.IsSelected = selectAll;
        }
    }

    #endregion

    #region Hidden updates

    /// <summary>Updates hidden in Windows Update. Scans skip them, here and in Windows Update itself.</summary>
    public ObservableCollection<DriverUpdateItemViewModel> HiddenUpdates { get; } = [];

    public string HiddenTabHeader => HiddenUpdates.Count == 0 ? "Hidden updates" : $"Hidden updates ({HiddenUpdates.Count})";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HiddenUpdatesMessage), nameof(ShowHiddenUpdatesMessage))]
    private bool _hasLoadedHiddenUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HiddenUpdatesMessage), nameof(ShowHiddenUpdatesMessage))]
    private string? _hiddenUpdatesError;

    public string HiddenUpdatesMessage =>
        HiddenUpdatesError is not null ? HiddenUpdatesError
        : !HasLoadedHiddenUpdates ? "Hidden driver updates are listed here after a scan."
        : HiddenUpdates.Count == 0 ? "No driver updates are hidden. To stop Windows Update offering one, tick it on the "
                                     + "Updates available tab and click Hide selected."
        : "";

    public bool ShowHiddenUpdatesMessage => HiddenUpdatesMessage.Length > 0;

    public int SelectedHiddenCount => HiddenUpdates.Count(u => u.IsSelected);

    public string HiddenSelectionSummary => SelectedHiddenCount == 0 ? "No updates selected" : $"{SelectedHiddenCount} selected";

    #endregion

    #region History

    /// <summary>Driver installs from the Windows Update history, newest first.</summary>
    public ObservableCollection<DriverHistoryEntry> History { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HistoryMessage), nameof(ShowHistoryMessage))]
    private bool _hasLoadedHistory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HistoryMessage), nameof(ShowHistoryMessage))]
    private string? _historyError;

    public string HistoryMessage =>
        HistoryError is not null ? HistoryError
        : !HasLoadedHistory ? "Driver installs from the Windows Update history are listed here after a scan."
        : History.Count == 0 ? "Windows Update has no record of installing drivers on this PC."
        : "";

    public bool ShowHistoryMessage => HistoryMessage.Length > 0;

    #endregion

    #region Graphics card makers

    /// <summary>
    /// Opt-in: after each scan, ask NVIDIA (and link to AMD and Intel) about a newer graphics driver. Only links are
    /// shown; nothing is downloaded or installed from them.
    /// </summary>
    [ObservableProperty]
    private bool _checkGpuVendorDrivers;

    /// <summary>One per graphics card, while <see cref="CheckGpuVendorDrivers"/> is on.</summary>
    public ObservableCollection<GpuDriverNotice> GpuNotices { get; } = [];

    partial void OnCheckGpuVendorDriversChanged(bool value)
    {
        _settings.Save(_settings.Load() with { CheckGpuVendorDrivers = value });
        _logger.LogInformation("Checking graphics card makers' websites turned {State}", value ? "on" : "off");
        if (value)
            _ = CheckGpuDriversAsync(CancellationToken.None);
        else
            GpuNotices.Clear();
    }

    /// <summary>Failures are already turned into notices by the service; this only guards against the unexpected.</summary>
    private async Task CheckGpuDriversAsync(CancellationToken cancellationToken)
    {
        if (!CheckGpuVendorDrivers || Devices.Count == 0)
            return;
        try
        {
            var notices = await _gpuDriverCheck.CheckAsync(Devices.ToList(), cancellationToken);
            GpuNotices.Clear();
            if (CheckGpuVendorDrivers)
            {
                foreach (var notice in notices)
                    GpuNotices.Add(notice);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Checking graphics card makers' websites failed");
        }
    }

    /// <summary>
    /// Through explorer.exe, so the browser opens as the signed-in user instead of inheriting this app's administrator
    /// rights. Only https links are opened.
    /// </summary>
    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{uri.AbsoluteUri}\"") { UseShellExecute = true });
    }

    #endregion

    #region Driver backups

    /// <summary>Backed-up drivers, newest first, next to what each device uses now.</summary>
    public ObservableCollection<DriverBackupRowViewModel> Backups { get; } = [];

    public string BackupsTabHeader => Backups.Count == 0 ? "Driver backups" : $"Driver backups ({Backups.Count})";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreSelectedCommand))]
    private DriverBackupRowViewModel? _selectedBackup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackupsMessage), nameof(ShowBackupsMessage))]
    private bool _hasLoadedBackups;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackupsMessage), nameof(ShowBackupsMessage))]
    private string? _backupsError;

    public string BackupsMessage =>
        BackupsError is not null ? BackupsError
        : !HasLoadedBackups ? "Backed-up drivers are listed here after a scan."
        : Backups.Count == 0 ? $"No drivers have been backed up yet. {ProductInfo.Name} backs up the drivers it replaces before "
                               + "installing updates. You can also use Back up drivers on the All devices tab."
        : "";

    public bool ShowBackupsMessage => BackupsMessage.Length > 0;

    #endregion

    /// <summary>Inverse of IsBusy, for disabling inputs (like the update checkboxes) while an operation runs.</summary>
    public bool IsIdle => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(UpdatesMessage));
        OnPropertyChanged(nameof(DevicesMessage));
        OnPropertyChanged(nameof(ShowDevicesMessage));
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRunOperation))]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            if (!await ScanDevicesAsync(cancellationToken))
                return;
            await SearchUpdatesAsync(cancellationToken);
            // These are local and quick, so they don't change the status text.
            await LoadHiddenUpdatesAsync(cancellationToken);
            await LoadHistoryAsync(cancellationToken);
            await LoadBackupsAsync(cancellationToken);
            await CheckGpuDriversAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> ScanDevicesAsync(CancellationToken cancellationToken)
    {
        StatusText = "Scanning devices…";
        try
        {
            var devices = await _inventory.GetDevicesAsync(cancellationToken);

            Devices.Clear();
            foreach (var device in devices)
                Devices.Add(device);

            OnPropertyChanged(nameof(ProblemDeviceCount));
            OnPropertyChanged(nameof(DevicesTabHeader));
            RefreshDevicesView();
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device scan failed");
            StatusText = $"Device scan failed: {ex.Message}";
        }
        return false;
    }

    private async Task SearchUpdatesAsync(CancellationToken cancellationToken)
    {
        StatusText = $"Found {Devices.Count} devices. Searching Windows Update for driver updates (this can take a minute)…";
        UpdateSearchError = null;
        try
        {
            var result = await _driverUpdates.SearchAsync(cancellationToken);
            var matches = UpdateMatcher.Match(result.Updates, Devices);

            ReplaceUpdates(matches.Select(m => new DriverUpdateItemViewModel(m)));
            PolicyWarning = result.DriversExcludedByPolicy
                ? "A Windows Update policy on this PC excludes drivers (\"Do not include drivers with Windows Updates\"), so some driver updates may not be offered."
                : null;
            HasSearchedUpdates = true;

            StatusText = Updates.Count == 0
                ? $"Scan complete: {Devices.Count} devices, drivers are up to date."
                : $"Scan complete: {Devices.Count} devices, {Updates.Count} driver update{(Updates.Count == 1 ? "" : "s")} available.";
            if (Updates.Count > 0)
                SelectedTabIndex = UpdatesTab;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Windows Update search cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Driver update search failed");
            UpdateSearchError = ex is WindowsUpdateException ? ex.Message : $"Windows Update search failed: {ex.Message}";
            HasSearchedUpdates = true;
            StatusText = "Windows Update search failed.";
        }
    }

    private async Task LoadHiddenUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var hidden = await _driverUpdates.GetHiddenAsync(cancellationToken);
            ReplaceHiddenUpdates(Rematch(hidden, keepSelectedFrom: []));
            HiddenUpdatesError = null;
            HasLoadedHiddenUpdates = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Listing hidden driver updates failed");
            HiddenUpdatesError = $"Hidden updates couldn't be listed. {ex.Message}";
            HasLoadedHiddenUpdates = true;
        }
    }

    private async Task LoadHistoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entries = await _driverUpdates.GetHistoryAsync(cancellationToken);
            History.Clear();
            foreach (var entry in entries)
                History.Add(entry);
            HistoryError = null;
            HasLoadedHistory = true;
            OnPropertyChanged(nameof(HistoryMessage));
            OnPropertyChanged(nameof(ShowHistoryMessage));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reading the Windows Update history failed");
            HistoryError = $"The Windows Update history couldn't be read. {ex.Message}";
            HasLoadedHistory = true;
        }
    }

    /// <summary>Needs a device scan first, to show what each device uses now.</summary>
    private async Task LoadBackupsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var drivers = await _backupCatalog.ListAsync(cancellationToken);
            var devices = Devices.ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);
            Backups.Clear();
            foreach (var driver in drivers)
                Backups.Add(new DriverBackupRowViewModel(driver, devices.GetValueOrDefault(driver.Entry.DeviceId)));
            BackupsError = null;
            HasLoadedBackups = true;
            OnPropertyChanged(nameof(BackupsTabHeader));
            OnPropertyChanged(nameof(BackupsMessage));
            OnPropertyChanged(nameof(ShowBackupsMessage));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Listing driver backups failed");
            BackupsError = $"The driver backups couldn't be listed. {ex.Message}";
            HasLoadedBackups = true;
        }
    }

    #region Restore

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreSelectedAsync()
    {
        if (SelectedBackup is not { } row)
            return;

        if (!_dialogs.Confirm("Restore driver",
                (IsSampleMode ? "SAMPLE MODE: nothing will actually be restored.\n\n" : "")
                + $"Put back driver {row.BackedUpVersion} on {row.DeviceName}?\n\n"
                + $"It replaces version {row.CurrentVersion}. It was backed up on {row.BackedUpAt:yyyy-MM-dd HH:mm}.\n\n"
                + "First, a system restore point is created. Windows then installs the backed-up driver even though it "
                + "prefers the current one. The device may stop working for a moment, and a restart may be needed.\n\n"
                + "Windows Update may offer or install the newer driver again later. To stop that, hide it on the "
                + "Updates available tab after the next scan."))
            return;

        IsBusy = true;
        StatusText = $"Restoring the {row.DeviceName} driver (this can take a minute)…";
        DriverRestoreResult result;
        try
        {
            result = await _restorer.RunAsync(row.Driver, ConfirmRestoreWithoutRestorePoint);
            if (result.Outcome != DriverRestoreOutcome.DeclinedWithoutRestorePoint)
            {
                // Show the device's driver as it is now.
                await ScanDevicesAsync(CancellationToken.None);
                await LoadBackupsAsync(CancellationToken.None);
            }
        }
        finally
        {
            IsBusy = false;
        }

        StatusText = result.Outcome switch
        {
            DriverRestoreOutcome.Restored => $"Restored the {row.DeviceName} driver.",
            DriverRestoreOutcome.RestartRequired => $"Restored the {row.DeviceName} driver. Restart required.",
            DriverRestoreOutcome.DeclinedWithoutRestorePoint => "Restore stopped. Nothing was changed.",
            _ => $"The {row.DeviceName} driver wasn't restored.",
        };

        var restorePointLine = result.RestorePoint is { } rp ? $"\n\nRestore point: {rp.Message}" : "";
        switch (result.Outcome)
        {
            case DriverRestoreOutcome.Restored:
                _dialogs.ShowInfo("Restore driver", result.Message + restorePointLine);
                break;
            case DriverRestoreOutcome.RestartRequired:
                if (_dialogs.Confirm("Restore driver", $"{result.Message}{restorePointLine}\n\nRestart now?"))
                    await RestartAsync();
                break;
            case DriverRestoreOutcome.DeclinedWithoutRestorePoint:
                break;
            default:
                _dialogs.ShowWarning("Restore driver", result.Message + restorePointLine);
                break;
        }
    }

    private bool ConfirmRestoreWithoutRestorePoint(RestorePointResult restorePoint) =>
        _dialogs.Confirm("No restore point",
            $"{restorePoint.Message}\n\nRestore the driver without a restore point? The current driver stays in Windows' "
            + "driver store, so it can be put back from Device Manager (Update driver → Browse my computer → Let me pick "
            + "from a list).",
            defaultToNo: true);

    #endregion

    #region Hide and unhide

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task HideSelectedAsync()
    {
        var selected = Updates.Where(u => u.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        var them = selected.Count == 1 ? "it" : "them";
        if (!_dialogs.Confirm("Hide driver updates",
                $"Hide {Plural(selected.Count, "driver update")}?\n\n{ListUpdates(selected)}\n\n"
                + $"Windows Update won't offer {them} again until you unhide {them} on the Hidden updates tab."))
            return;

        IsBusy = true;
        StatusText = $"Hiding {Plural(selected.Count, "driver update")}…";
        try
        {
            var (done, failures) = await ForEachUpdateAsync(selected, "Hide", u => _driverUpdates.HideAsync(u));

            ReplaceUpdates(Updates.Except(done).ToList());
            ReplaceHiddenUpdates(Rematch(HiddenUpdates.Concat(done).Select(u => u.Update), keepSelectedFrom: HiddenUpdates));

            StatusText = $"Hid {Plural(done.Count, "driver update")}." + (failures.Count > 0 ? $" {failures.Count} failed." : "");
            if (failures.Count > 0)
                _dialogs.ShowWarning("Hide driver updates", $"These updates couldn't be hidden:\n\n{string.Join('\n', failures)}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUnhide))]
    private async Task UnhideSelectedAsync()
    {
        var selected = HiddenUpdates.Where(u => u.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        IsBusy = true;
        StatusText = $"Unhiding {Plural(selected.Count, "driver update")}…";
        try
        {
            var (done, failures) = await ForEachUpdateAsync(selected, "Unhide", u => _driverUpdates.UnhideAsync(u));

            ReplaceHiddenUpdates(HiddenUpdates.Except(done).ToList());
            ReplaceUpdates(Rematch(Updates.Concat(done).Select(u => u.Update), keepSelectedFrom: Updates));

            StatusText = $"Unhid {Plural(done.Count, "driver update")}; {(done.Count == 1 ? "it is" : "they are")} on the Updates available tab again."
                + (failures.Count > 0 ? $" {failures.Count} failed." : "");
            if (failures.Count > 0)
                _dialogs.ShowWarning("Unhide driver updates", $"These updates couldn't be unhidden:\n\n{string.Join('\n', failures)}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Runs one Windows Update change per row, collecting the rows that worked and a line per failure.</summary>
    private async Task<(List<DriverUpdateItemViewModel> Done, List<string> Failures)> ForEachUpdateAsync(
        IEnumerable<DriverUpdateItemViewModel> items, string action, Func<DriverUpdateInfo, Task> change)
    {
        var done = new List<DriverUpdateItemViewModel>();
        var failures = new List<string>();
        foreach (var item in items)
        {
            try
            {
                await change(item.Update);
                done.Add(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Action} of {Title} failed", action, item.Update.Title);
                failures.Add($"• {item.DeviceName}: {ex.Message}");
            }
        }
        return (done, failures);
    }

    /// <summary>Re-matches updates to devices so a list keeps the matcher's order, keeping the ticks of rows that stay.</summary>
    private List<DriverUpdateItemViewModel> Rematch(IEnumerable<DriverUpdateInfo> updates,
        IEnumerable<DriverUpdateItemViewModel> keepSelectedFrom)
    {
        var selectedIds = keepSelectedFrom.Where(u => u.IsSelected).Select(u => u.Update.UpdateId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return UpdateMatcher.Match(updates, Devices)
            .Select(m => new DriverUpdateItemViewModel(m) { IsSelected = selectedIds.Contains(m.Update.UpdateId) })
            .ToList();
    }

    #endregion

    #region Install

    /// <summary>Timestamped lines for the install log pane.</summary>
    public ObservableCollection<string> InstallLog { get; } = [];

    [ObservableProperty]
    private bool _showInstallPanel;

    /// <summary>Overall install progress, 0 to 1.</summary>
    [ObservableProperty]
    private double _installProgressValue;

    [ObservableProperty]
    private string _installStepText = "";

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanInstall))]
    private async Task InstallSelectedAsync(CancellationToken cancellationToken)
    {
        var selected = Updates.Where(u => u.IsSelected).ToList();
        if (selected.Count == 0 || !_dialogs.Confirm("Install driver updates", BuildInstallConfirmation(selected)))
            return;

        IsBusy = true;
        _installCancelRequested = false;
        InstallLog.Clear();
        InstallProgressValue = 0;
        InstallStepText = "";
        ShowInstallPanel = true;
        StatusText = $"Installing {Plural(selected.Count, "driver update")}…";

        // Cancel is honoured between steps only, so say so instead of looking unresponsive.
        using var cancelRegistration = cancellationToken.Register(() =>
        {
            _installCancelRequested = true;
            InstallStepText += " (cancelling after this step)";
            StatusText = "Cancelling after the current step finishes…";
        });

        InstallResult result;
        try
        {
            result = await _installer.RunAsync(selected.Select(u => u.Match).ToList(), ConfirmContinueWithPendingRestart,
                ConfirmContinueWithoutRestorePoint, new Progress<InstallProgress>(OnInstallProgress), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Install run failed");
            AppendInstallLog($"Install stopped unexpectedly: {ex.Message}");
            StatusText = "Install stopped unexpectedly.";
            _dialogs.ShowWarning("Install driver updates", $"The install stopped unexpectedly: {ex.Message}");
            return;
        }
        finally
        {
            IsBusy = false;
        }

        StatusText = result.Outcome == InstallOutcome.Completed
            ? $"Install finished: {result.SucceededCount} installed, {result.FailedCount} failed."
              + (result.RebootRequired ? " Restart required." : "")
            : result.Message ?? "Install stopped.";
        _logger.LogInformation(
            "Install run ended: {Outcome}, {Succeeded} installed, {Failed} failed, {NotAttempted} not attempted, reboot required {Reboot}",
            result.Outcome, result.SucceededCount, result.FailedCount, result.NotAttemptedCount, result.RebootRequired);

        if (result.Backup is not null)
            await LoadBackupsAsync(CancellationToken.None);

        if (_dialogs.ShowInstallResults(result))
        {
            await RestartAsync();
            return;
        }

        // Refresh installed versions and drop updates Windows no longer offers.
        if (result.SucceededCount > 0)
            ScanCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private void HideInstallPanel() => ShowInstallPanel = false;

    private string BuildInstallConfirmation(IReadOnlyList<DriverUpdateItemViewModel> selected) =>
        (IsSampleMode ? "SAMPLE MODE: nothing will actually be downloaded or installed.\n\n" : "")
            + $"Install {Plural(selected.Count, "driver update")} from Windows Update?\n\n"
            + ListUpdates(selected)
            + $"\n\nFirst, {ProductInfo.Name} will:\n"
            + "  1. Create a system restore point.\n"
            + $"  2. Back up the drivers being replaced to {_paths.Backups}\n\n"
            + "The screen may flicker or go blank briefly while display drivers install. Some drivers need a restart to finish.";

    private static string ListUpdates(IReadOnlyList<DriverUpdateItemViewModel> updates)
    {
        const int maxListed = 12;
        var lines = updates.Take(maxListed)
            .Select(u => $"• {u.DeviceName}: {u.CurrentVersion} → {u.NewVersion}"
                         + (u.Match.OffersOlderDriver ? " (dated earlier than the installed driver)" : ""))
            .ToList();
        if (updates.Count > maxListed)
            lines.Add($"• …and {updates.Count - maxListed} more");
        return string.Join("\n", lines);
    }

    private bool ConfirmContinueWithPendingRestart(PendingRestartCheck check) =>
        _dialogs.Confirm("Restart pending",
            $"{check.Message}\n\nInstall anyway? Choose No to stop now, with nothing changed, so you can restart first.",
            defaultToNo: true);

    private bool ConfirmContinueWithoutRestorePoint(RestorePointResult restorePoint) =>
        _dialogs.Confirm("No restore point",
            $"{restorePoint.Message}\n\nContinue without a restore point? The drivers being replaced will still be backed up, " +
            "so they can be reinstalled from the backups folder.",
            defaultToNo: true);

    private void OnInstallProgress(InstallProgress progress)
    {
        InstallProgressValue = progress.Fraction;
        InstallStepText = _installCancelRequested && progress.Stage != InstallStage.Finished
            ? $"{progress.Message} (cancelling after this step)"
            : progress.Message;
        AppendInstallLog(progress.Message);
    }

    private void AppendInstallLog(string message) => InstallLog.Add($"{DateTime.Now:HH:mm:ss}  {message}");

    private async Task RestartAsync()
    {
        StatusText = "Restarting Windows…";
        try
        {
            await _restart.RestartNowAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(ex, "Restart failed");
            StatusText = "Restart failed.";
            _dialogs.ShowWarning("Restart", $"{ex.Message}\n\nRestart the PC from the Start menu to finish installing.");
        }
    }

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    #endregion

    #region Safety tools

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private async Task CreateRestorePointAsync()
    {
        IsBusy = true;
        StatusText = "Creating a system restore point (this can take a minute)…";
        try
        {
            var result = await _restorePoints.CreateAsync($"{ProductInfo.Name} - manual restore point");
            StatusText = result.Outcome switch
            {
                RestorePointOutcome.Created => "Restore point created.",
                RestorePointOutcome.SkippedRecentExists => "Restore point skipped: a recent one already exists.",
                _ => "Restore point could not be created.",
            };

            if (result.HasUsableRestorePoint)
                _dialogs.ShowInfo("System restore point", result.Message);
            else
                _dialogs.ShowWarning("System restore point", result.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Backs up the selected devices' drivers, or every third-party driver when nothing is selected.</summary>
    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private async Task BackupDriversAsync(System.Collections.IList? selectedItems)
    {
        var devices = selectedItems?.OfType<DeviceDriverInfo>().ToList() ?? [];
        if (devices.Count == 0)
        {
            devices = Devices.Where(d => d.IsThirdPartyDriver).ToList();
            if (devices.Count == 0)
            {
                _dialogs.ShowInfo("Back up drivers", "No third-party drivers are installed, so there is nothing to back up.");
                return;
            }
            if (!_dialogs.Confirm("Back up drivers",
                    $"No devices are selected. Back up all {devices.Count} devices that use third-party drivers?"))
                return;
        }

        IsBusy = true;
        StatusText = $"Backing up drivers for {devices.Count} device{(devices.Count == 1 ? "" : "s")}…";
        try
        {
            var result = await _driverBackup.BackupAsync(devices);

            var summary = $"{result.ExportedCount} driver package{(result.ExportedCount == 1 ? "" : "s")} exported, " +
                          $"{result.SkippedCount} skipped, {result.FailedCount} failed.";
            StatusText = $"Backup finished: {summary}";

            var failures = result.Entries.Where(e => e.Status == DriverBackupStatus.Failed)
                .Select(e => $"• {e.DeviceName} ({e.InfName}): {e.Detail}").Distinct().ToList();
            var message = $"{summary}\n\nSaved to:\n{result.Folder}"
                + (result.SkippedCount > 0 ? "\n\nInbox drivers that ship with Windows are skipped because Windows keeps them." : "")
                + (failures.Count > 0 ? $"\n\nFailures:\n{string.Join('\n', failures)}" : "");

            await LoadBackupsAsync(CancellationToken.None);
            if (result.FailedCount > 0)
                _dialogs.ShowWarning("Back up drivers", message);
            else
                _dialogs.ShowInfo("Back up drivers", message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Driver backup failed");
            StatusText = "Driver backup failed.";
            _dialogs.ShowWarning("Back up drivers", $"The backup could not be written: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenBackupsFolder()
    {
        _paths.EnsureCreated();
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.Backups}\"") { UseShellExecute = true });
    }

    #endregion

    private void ReplaceUpdates(IEnumerable<DriverUpdateItemViewModel> items)
    {
        foreach (var old in Updates)
            old.PropertyChanged -= OnUpdateItemChanged;
        Updates.Clear();

        foreach (var item in items)
        {
            item.PropertyChanged += OnUpdateItemChanged;
            Updates.Add(item);
        }

        OnSelectionChanged();
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(UpdatesTabHeader));
        OnPropertyChanged(nameof(UpdatesMessage));
        OnPropertyChanged(nameof(ShowUpdatesMessage));
    }

    private void ReplaceHiddenUpdates(IEnumerable<DriverUpdateItemViewModel> items)
    {
        foreach (var old in HiddenUpdates)
            old.PropertyChanged -= OnHiddenItemChanged;
        HiddenUpdates.Clear();

        foreach (var item in items)
        {
            item.PropertyChanged += OnHiddenItemChanged;
            HiddenUpdates.Add(item);
        }

        OnHiddenSelectionChanged();
        OnPropertyChanged(nameof(HiddenTabHeader));
        OnPropertyChanged(nameof(HiddenUpdatesMessage));
        OnPropertyChanged(nameof(ShowHiddenUpdatesMessage));
        OnPropertyChanged(nameof(UpdatesMessage));
    }

    private void OnHiddenItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DriverUpdateItemViewModel.IsSelected))
            OnHiddenSelectionChanged();
    }

    private void OnHiddenSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedHiddenCount));
        OnPropertyChanged(nameof(HiddenSelectionSummary));
        UnhideSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OnUpdateItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DriverUpdateItemViewModel.IsSelected))
            OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedUpdateCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(AllUpdatesSelected));
        InstallSelectedCommand.NotifyCanExecuteChanged();
        HideSelectedCommand.NotifyCanExecuteChanged();
    }

    private bool MatchesDeviceFilter(object item)
    {
        if (item is not DeviceDriverInfo device)
            return false;
        if (ShowOnlyProblemDevices && !device.HasProblem)
            return false;

        var filter = DeviceFilter.Trim();
        if (filter.Length == 0)
            return true;

        return Contains(device.DeviceName) || Contains(device.DeviceClass) || Contains(device.Manufacturer)
            || Contains(device.DriverProvider) || Contains(device.DriverVersion) || Contains(device.InfName)
            || device.HardwareIds.Any(Contains);

        bool Contains(string? value) => value?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) == true;
    }
}
