using System.Windows;
using DriverUpdater.App.ViewModels;

namespace DriverUpdater.App.Views;

/// <summary>Per-driver install results. DialogResult is true when the user chose "Restart now".</summary>
public partial class InstallResultsWindow : Window
{
    public InstallResultsWindow(InstallResultsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        if (viewModel.RebootRequired)
            CloseButton.Content = "Later";
    }

    private void OnRestartNow(object sender, RoutedEventArgs e) => DialogResult = true;
}
