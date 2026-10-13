using System.Management;
using DriverUpdater.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DriverUpdater.Core.Services;

/// <summary>Reads the usual pending-restart registry values, and asks WMI for devices that need a restart.</summary>
public sealed class WindowsPendingRestartService(ILogger<WindowsPendingRestartService> logger) : IPendingRestartService
{
    private const string ComponentServicingKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
    private const string WindowsUpdateKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";
    private const string SessionManagerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager";

    // CM_PROB_NEED_RESTART.
    private const string DevicesNeedingRestartQuery = "SELECT Name, DeviceID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 14";

    public Task<PendingRestartSignals> GetSignalsAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var signals = new PendingRestartSignals(
                ComponentServicingRebootPending: KeyExists(ComponentServicingKey),
                WindowsUpdateRebootRequired: KeyExists(WindowsUpdateKey),
                PendingFileRenames: PendingFileRenames(),
                DevicesNeedingRestart: DevicesNeedingRestart(cancellationToken));

            logger.LogInformation(
                "Pending restart signals: component servicing {Cbs}, Windows Update {Wu}, {Renames} pending file renames ({DriverRenames} driver files), devices needing restart: {Devices}",
                signals.ComponentServicingRebootPending, signals.WindowsUpdateRebootRequired, signals.PendingFileRenames!.Count,
                signals.PendingFileRenames.Count(PendingRestartEvaluator.IsDriverFile),
                signals.DevicesNeedingRestart!.Count == 0 ? "none" : string.Join(", ", signals.DevicesNeedingRestart));
            return signals;
        }, cancellationToken);

    private static bool KeyExists(string path)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key is not null;
    }

    /// <summary>Pairs of source and target paths; an empty target means delete. Only the non-empty paths matter here.</summary>
    private static List<string> PendingFileRenames()
    {
        using var key = Registry.LocalMachine.OpenSubKey(SessionManagerKey);
        return key?.GetValue("PendingFileRenameOperations") is string[] entries
            ? entries.Where(e => !string.IsNullOrWhiteSpace(e)).ToList()
            : [];
    }

    private static List<string> DevicesNeedingRestart(CancellationToken cancellationToken)
    {
        using var searcher = new ManagementObjectSearcher(@"root\cimv2", DevicesNeedingRestartQuery);
        using var results = searcher.Get();

        var names = new List<string>();
        foreach (var obj in results)
        {
            using (obj)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((obj["Name"] as string ?? obj["DeviceID"] as string) is { } name)
                    names.Add(name);
            }
        }
        return names;
    }
}
