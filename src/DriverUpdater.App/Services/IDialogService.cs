using System.Windows;
using DriverUpdater.App.ViewModels;
using DriverUpdater.App.Views;
using DriverUpdater.Core.Models;

namespace DriverUpdater.App.Services;

/// <summary>Keeps message boxes out of view models.</summary>
public interface IDialogService
{
    void ShowInfo(string title, string message);

    void ShowWarning(string title, string message);

    bool Confirm(string title, string message, bool defaultToNo = false);

    /// <summary>Shows per-driver results. Returns true when the user chose "Restart now".</summary>
    bool ShowInstallResults(InstallResult result);
}

public sealed class MessageBoxDialogService : IDialogService
{
    public void ShowInfo(string title, string message) =>
        Show(title, message, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string title, string message) =>
        Show(title, message, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirm(string title, string message, bool defaultToNo = false) =>
        Show(title, message, MessageBoxButton.YesNo, MessageBoxImage.Question,
            defaultToNo ? MessageBoxResult.No : MessageBoxResult.Yes) == MessageBoxResult.Yes;

    public bool ShowInstallResults(InstallResult result)
    {
        var window = new InstallResultsWindow(new InstallResultsViewModel(result));
        if (Owner is { } owner)
            window.Owner = owner;
        else
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window.ShowDialog() == true;
    }

    private static Window? Owner => Application.Current.MainWindow is { IsVisible: true } owner ? owner : null;

    private static MessageBoxResult Show(string title, string message, MessageBoxButton buttons, MessageBoxImage image,
        MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image, defaultResult)
            : MessageBox.Show(message, title, buttons, image, defaultResult);
}
