namespace DriverUpdater.Core.Infrastructure;

public static class SystemTools
{
    /// <summary>
    /// pnputil.exe only exists in the native System32. A 32-bit process on 64-bit Windows is redirected to
    /// SysWOW64, so it has to go through the Sysnative alias instead.
    /// </summary>
    public static string PnpUtilPath => Path.Combine(
        Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative")
            : Environment.SystemDirectory,
        "pnputil.exe");

    public static string ShutdownPath => Path.Combine(Environment.SystemDirectory, "shutdown.exe");
}
