using System.IO;
using System.Text.Json;
using DriverUpdater.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace DriverUpdater.App.Services;

/// <param name="CheckGpuVendorDrivers">
/// Off by default: when on, NVIDIA's servers are asked about the PC's graphics card after each scan.
/// </param>
public sealed record AppSettings(bool CheckGpuVendorDrivers = false);

/// <summary>User choices, per user (%LocalAppData%\DriverHaven\settings.json). Unreadable settings fall back to defaults.</summary>
public sealed class AppSettingsStore(ILogger<AppSettingsStore> logger)
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name, "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Ignoring unreadable settings at {Path}", _path);
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not save settings to {Path}", _path);
        }
    }
}
