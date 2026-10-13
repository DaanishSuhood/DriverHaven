using System.IO;
using System.Text.Json;
using System.Windows;
using DriverUpdater.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace DriverUpdater.App.Services;

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);

/// <summary>
/// Remembers the main window's size and position per user (%LocalAppData%\DriverHaven\window.json).
/// Unreadable or off-screen placements are ignored, so a missing monitor can't strand the window.
/// </summary>
public sealed class WindowPlacementStore(ILogger<WindowPlacementStore> logger)
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name, "window.json");

    public void Restore(Window window)
    {
        var placement = Load();
        if (placement is null || !IsUsable(placement, window))
            return;

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = placement.Left;
        window.Top = placement.Top;
        window.Width = placement.Width;
        window.Height = placement.Height;
        if (placement.Maximized)
            window.WindowState = WindowState.Maximized;
    }

    public void Save(Window window)
    {
        // RestoreBounds is the normal-state size even while maximized or minimized.
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
            : window.RestoreBounds;
        if (bounds.IsEmpty)
            return;

        var placement = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            window.WindowState == WindowState.Maximized);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(placement));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not save the window placement to {Path}", _path);
        }
    }

    private WindowPlacement? Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(_path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Ignoring unreadable window placement at {Path}", _path);
            return null;
        }
    }

    /// <summary>At least the top-left area of the window (where the title bar is) must be on the desktop.</summary>
    private static bool IsUsable(WindowPlacement p, Window window)
    {
        if (double.IsNaN(p.Width) || p.Width < window.MinWidth || double.IsNaN(p.Height) || p.Height < window.MinHeight)
            return false;

        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return desktop.Contains(new Point(p.Left + 50, p.Top + 10)) && desktop.Contains(new Point(p.Left + 150, p.Top + 30));
    }
}
