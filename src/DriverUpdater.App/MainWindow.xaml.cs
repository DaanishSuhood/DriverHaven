using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using DriverUpdater.App.Services;
using DriverUpdater.App.ViewModels;
using DriverUpdater.Core.Infrastructure;

namespace DriverUpdater.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel, IDialogService dialogs, WindowPlacementStore placement)
    {
        InitializeComponent();
        DataContext = viewModel;

        placement.Restore(this);
        // Closing, not Closed: RestoreBounds is no longer available once the window is gone.
        Closing += (_, _) => placement.Save(this);

        // The device scan is quick and read-only, so run it as soon as the window appears.
        Loaded += async (_, _) => await viewModel.ScanCommand.ExecuteAsync(null);

        // Keep the newest install log line in view. Scroll later, not inside the notification: this handler can run
        // before the ListBox has seen the new item, and scrolling then makes it measure an inconsistent list.
        viewModel.InstallLog.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?[0] is { } line)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => InstallLogList.ScrollIntoView(line));
        };

        // Exiting mid-install could leave a driver half-installed.
        Closing += (_, e) =>
        {
            if (!viewModel.IsInstalling)
                return;
            e.Cancel = true;
            dialogs.ShowWarning(ProductInfo.Name,
                "Driver updates are being installed. Wait for them to finish (or click \"Cancel remaining\") before closing.");
        };
    }
}
