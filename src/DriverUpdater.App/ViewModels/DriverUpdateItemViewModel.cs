using CommunityToolkit.Mvvm.ComponentModel;
using DriverUpdater.Core.Infrastructure;
using DriverUpdater.Core.Models;

namespace DriverUpdater.App.ViewModels;

/// <summary>One row in the "Updates available" grid.</summary>
public partial class DriverUpdateItemViewModel(MatchedDriverUpdate match) : ObservableObject
{
    public MatchedDriverUpdate Match { get; } = match;

    public DriverUpdateInfo Update => Match.Update;

    public string DeviceName => Match.Devices switch
    {
        [] => Update.DriverModel ?? Update.Title,
        [var only] => only.DeviceName,
        [var first, ..] => $"{first.DeviceName} (+{Match.Devices.Count - 1} more)",
    };

    public string DeviceClass => Match.PrimaryDevice?.DeviceClass ?? Update.DriverClass ?? "";

    public string CurrentVersion => Match.PrimaryDevice switch
    {
        null => "—",
        { HasDriver: false } => "Not installed",
        var device => device.DriverVersion ?? "Unknown",
    };

    public DateOnly? CurrentDate => Match.PrimaryDevice?.DriverDate;

    public string NewVersion => Update.DriverVersion ?? "See details";

    public DateOnly? NewDate => Update.DriverDate;

    public string Provider => Update.DriverProvider ?? Update.DriverManufacturer ?? "";

    public string Size => ByteSize.Format(Update.MaxDownloadSize);

    public string Note =>
        Match.IsUnmatched ? "No installed device reports this hardware ID"
        : Match.OffersOlderDriver ? "Dated earlier than the installed driver"
        : "";

    public string Details =>
        $"{Update.Title}\nHardware IDs: {string.Join(", ", Update.HardwareIds)}"
        + (Match.Devices.Count > 1 ? $"\nDevices: {string.Join(", ", Match.Devices.Select(d => d.DeviceName))}" : "");

    [ObservableProperty]
    private bool _isSelected;
}
