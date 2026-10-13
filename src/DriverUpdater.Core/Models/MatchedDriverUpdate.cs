namespace DriverUpdater.Core.Models;

/// <summary>A Windows Update driver linked to the installed device(s) it would replace.</summary>
public sealed record MatchedDriverUpdate(DriverUpdateInfo Update, IReadOnlyList<DeviceDriverInfo> Devices)
{
    public DeviceDriverInfo? PrimaryDevice => Devices.Count > 0 ? Devices[0] : null;

    /// <summary>No present device reports any of the update's hardware IDs.</summary>
    public bool IsUnmatched => Devices.Count == 0;

    /// <summary>
    /// Intel dates many chipset and system INFs 1968-07-18 so they never outrank more specific drivers.
    /// The date says nothing about how new the driver is.
    /// </summary>
    public static readonly DateOnly IntelPlaceholderDate = new(1968, 7, 18);

    /// <summary>
    /// Windows Update ranks drivers by signature and hardware-ID specificity, not date, so it can
    /// occasionally offer a driver dated before the installed one. Surface that to the user.
    /// A placeholder-dated offer is compared by version instead, and only against the same provider's driver.
    /// </summary>
    public bool OffersOlderDriver =>
        PrimaryDevice is { } device && Update.DriverDate is { } offered
        && (offered == IntelPlaceholderDate
            ? IsOlderVersionFromSameProvider(device)
            : device.DriverDate is { } current && offered < current);

    private bool IsOlderVersionFromSameProvider(DeviceDriverInfo device) =>
        device.DriverProvider is not null
        && string.Equals(device.DriverProvider, Update.DriverProvider, StringComparison.OrdinalIgnoreCase)
        && Version.TryParse(device.DriverVersion, out var current)
        && Version.TryParse(Update.DriverVersion, out var offered)
        && offered < current;
}
