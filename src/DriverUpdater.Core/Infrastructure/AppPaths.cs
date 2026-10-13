using System.Globalization;

namespace DriverUpdater.Core.Infrastructure;

/// <summary>
/// Well-known folders used by the app. Everything lives under %ProgramData%\DriverHaven
/// so logs and backups are shared across users and survive app reinstalls.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
    }

    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductInfo.Name));

    public string Root { get; }

    public string Logs => Path.Combine(Root, "Logs");

    public string Backups => Path.Combine(Root, "Backups");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Backups);
    }

    /// <summary>Folder for a single backup run, e.g. Backups\20261011-143000.</summary>
    public string BackupFolderFor(DateTime timestamp) =>
        Path.Combine(Backups, timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
}
