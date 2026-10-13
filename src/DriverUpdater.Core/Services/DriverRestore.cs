using System.Runtime.InteropServices;
using DriverUpdater.Core.Models;
using Microsoft.Extensions.Logging;

namespace DriverUpdater.Core.Services;

public interface IDriverRestoreService
{
    /// <summary>
    /// Installs a backed-up driver on its device even though Windows ranks the current one higher, then checks which
    /// driver the device ends up using. Failures are returned, not thrown. Once started it runs to completion.
    /// </summary>
    Task<DriverRestoreResult> RestoreAsync(BackedUpDriver driver, CancellationToken cancellationToken = default);
}

/// <summary>
/// Uses UpdateDriverForPlugAndPlayDevices (newdev.dll) with INSTALLFLAG_FORCE: it stages the package and installs it on
/// every present device with the hardware ID, whatever Windows' driver ranking prefers. Nothing is deleted, so the
/// newer driver stays in the driver store.
/// </summary>
public sealed partial class PnpDriverRestoreService(IDeviceInventoryService inventory, ILogger<PnpDriverRestoreService> logger)
    : IDriverRestoreService
{
    private const uint InstallFlagForce = 0x1;
    private const uint InstallFlagNonInteractive = 0x4; // fail rather than show UI

    public async Task<DriverRestoreResult> RestoreAsync(BackedUpDriver driver, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (driver.InfPath is null)
            return new DriverRestoreResult(DriverRestoreOutcome.Failed, "The backup's driver files are missing.");

        var install = await Task.Factory.StartNew(() => ForceInstall(driver), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        if (install.Error is { } error)
            return new DriverRestoreResult(DriverRestoreOutcome.Failed, error);

        IReadOnlyList<DeviceDriverInfo> devices;
        try
        {
            devices = await inventory.GetDevicesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't re-read devices after restoring {InfPath}", driver.InfPath);
            return new DriverRestoreResult(install.RebootRequired ? DriverRestoreOutcome.RestartRequired : DriverRestoreOutcome.NotInUse,
                "Windows installed the backed-up driver, but the device's driver couldn't be checked afterwards. "
                + "Check its version on the All devices tab after the next scan.");
        }

        var device = devices.FirstOrDefault(d => d.DeviceId.Equals(driver.Entry.DeviceId, StringComparison.OrdinalIgnoreCase));
        var result = DriverRestoreVerifier.Verify(driver, device, install.RebootRequired);
        logger.LogInformation("Restore of {Device} to {Version}: {Outcome}, device now on {Current}",
            driver.Entry.DeviceName, driver.Entry.DriverVersion, result.Outcome, result.CurrentVersion);
        return result;
    }

    private (string? Error, bool RebootRequired) ForceInstall(BackedUpDriver driver)
    {
        // Most specific first, as in the manifest. An ID the package doesn't list, or that no present device has,
        // is skipped; the first one that installs wins.
        int lastSkippedError = 0;
        foreach (var hardwareId in driver.Entry.HardwareIds)
        {
            logger.LogInformation("Restoring {InfPath} for hardware ID {HardwareId} (forced)", driver.InfPath, hardwareId);
            if (UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, hardwareId, driver.InfPath!,
                    InstallFlagForce | InstallFlagNonInteractive, out var reboot))
            {
                logger.LogInformation("Restored {InfPath} for {HardwareId}; reboot required {Reboot}", driver.InfPath, hardwareId, reboot);
                return (null, reboot);
            }

            var error = Marshal.GetLastPInvokeError();
            logger.LogInformation("Hardware ID {HardwareId}: error 0x{Error:X8}", hardwareId, error);
            if (!DriverInstallErrors.MeansNotThisHardwareId(error))
                return (DriverInstallErrors.Describe(error), false);
            lastSkippedError = error;
        }

        return (lastSkippedError == DriverInstallErrors.NoSuchDevice
            ? "The device isn't connected. Connect it and scan again, then try once more."
            : "The backed-up driver doesn't match any of this device's hardware IDs, so Windows can't install it on it.", false);
    }

    [LibraryImport("newdev.dll", EntryPoint = "UpdateDriverForPlugAndPlayDevicesW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateDriverForPlugAndPlayDevices(IntPtr hwndParent, string hardwareId, string fullInfPath,
        uint installFlags, [MarshalAs(UnmanagedType.Bool)] out bool rebootRequired);
}

/// <summary>Decides how a restore went from the device as re-read afterwards.</summary>
public static class DriverRestoreVerifier
{
    public static DriverRestoreResult Verify(BackedUpDriver driver, DeviceDriverInfo? deviceAfter, bool rebootRequired)
    {
        var wanted = driver.Entry.DriverVersion;
        var current = deviceAfter?.DriverVersion;

        if (rebootRequired)
            return new DriverRestoreResult(DriverRestoreOutcome.RestartRequired,
                $"Windows installed driver {wanted}. Restart the PC to finish; until then the device may keep using the current driver.",
                current);
        if (deviceAfter is null)
            return new DriverRestoreResult(DriverRestoreOutcome.NotInUse,
                $"Windows installed driver {wanted}, but the device wasn't found afterwards, so it couldn't be checked.");
        if (string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
            return new DriverRestoreResult(DriverRestoreOutcome.Restored, $"{deviceAfter.DeviceName} now uses driver {wanted}.", current);

        return new DriverRestoreResult(DriverRestoreOutcome.NotInUse,
            $"Windows added driver {wanted}, but {deviceAfter.DeviceName} still uses version {current ?? "unknown"}. "
            + "To switch, open Device Manager, right-click the device, choose Update driver → Browse my computer → "
            + $"Let me pick from a list, and choose version {wanted}.",
            current);
    }
}

/// <summary>Readable messages for the SetupAPI errors a driver install can return.</summary>
public static class DriverInstallErrors
{
    public const int NoSuchDevice = unchecked((int)0xE000020B);       // ERROR_NO_SUCH_DEVINST
    private const int NoCompatDrivers = unchecked((int)0xE0000228);   // ERROR_NO_COMPAT_DRIVERS
    private const int NoDriverSelected = unchecked((int)0xE0000203);  // ERROR_NO_DRIVER_SELECTED
    private const int NoMoreItems = 259;                              // ERROR_NO_MORE_ITEMS

    private static readonly Dictionary<uint, string> Messages = new()
    {
        [0xE000020B] = "The device isn't connected.",
        [0xE0000228] = "The backed-up driver isn't compatible with this device.",
        [0xE0000247] = "Windows couldn't add the backed-up driver to its driver store.", // ERROR_DRIVER_STORE_ADD_FAILED
        [0xE0000235] = "Drivers can't be installed from a 32-bit process on 64-bit Windows.", // ERROR_IN_WOW64
        [0x800B0100] = "The backed-up driver isn't signed, so Windows won't install it.",    // TRUST_E_NOSIGNATURE
        [0x800B0109] = "The backed-up driver's signature isn't trusted on this PC.",         // CERT_E_UNTRUSTEDROOT
        [0xE0000242] = "The backed-up driver's signature isn't trusted on this PC.",         // ERROR_AUTHENTICODE_TRUST_NOT_ESTABLISHED
        [5] = "Access was denied. Run the app as an administrator.",                        // ERROR_ACCESS_DENIED
        [2] = "The backup's driver files are missing.",                                     // ERROR_FILE_NOT_FOUND
        [3] = "The backup's driver files are missing.",                                     // ERROR_PATH_NOT_FOUND
        [1618] = "Another installation is in progress. Wait for it to finish and try again.", // ERROR_INSTALL_ALREADY_RUNNING
    };

    /// <summary>Errors that only mean this hardware ID doesn't apply, so the next one is worth trying.</summary>
    public static bool MeansNotThisHardwareId(int error) =>
        error is NoSuchDevice or NoCompatDrivers or NoDriverSelected or NoMoreItems;

    public static string Describe(int error)
    {
        var code = unchecked((uint)error);
        return Messages.TryGetValue(code, out var message) ? $"{message} (0x{code:X8})" : $"Windows reported error 0x{code:X8}.";
    }
}
