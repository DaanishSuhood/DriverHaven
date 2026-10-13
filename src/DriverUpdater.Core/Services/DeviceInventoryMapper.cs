using DriverUpdater.Core.Infrastructure;
using DriverUpdater.Core.Models;

namespace DriverUpdater.Core.Services;

/// <summary>Raw fields read from Win32_PnPEntity.</summary>
public sealed record PnpEntityRecord(
    string? DeviceId,
    string? Name,
    string? PnpClass,
    string? Manufacturer,
    IReadOnlyList<string>? HardwareIds,
    IReadOnlyList<string>? CompatibleIds,
    uint ConfigManagerErrorCode,
    bool? Present);

/// <summary>Raw fields read from Win32_PnPSignedDriver.</summary>
public sealed record SignedDriverRecord(
    string? DeviceId,
    string? DeviceName,
    string? DeviceClass,
    string? Manufacturer,
    string? DriverProviderName,
    string? DriverVersion,
    string? DriverDate,
    string? InfName,
    string? HardwareId);

/// <summary>
/// Joins PnP entities (every device, including ones with no driver) with signed-driver records
/// (driver details) on DeviceID. Kept free of WMI so it can be unit tested.
/// </summary>
public static class DeviceInventoryMapper
{
    /// <summary>Device Manager's heading for devices Windows couldn't classify.</summary>
    public const string UnknownClass = "Other devices";

    public static IReadOnlyList<DeviceDriverInfo> Map(
        IEnumerable<PnpEntityRecord> entities, IEnumerable<SignedDriverRecord> drivers)
    {
        var driversById = new Dictionary<string, SignedDriverRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var driver in drivers)
        {
            if (!string.IsNullOrWhiteSpace(driver.DeviceId))
                driversById.TryAdd(driver.DeviceId, driver);
        }

        var devices = new List<DeviceDriverInfo>();
        foreach (var entity in entities)
        {
            // Non-present ("ghost") devices are hidden by Device Manager too.
            if (string.IsNullOrWhiteSpace(entity.DeviceId) || entity.Present == false)
                continue;

            driversById.TryGetValue(entity.DeviceId, out var driver);

            devices.Add(new DeviceDriverInfo
            {
                DeviceId = entity.DeviceId,
                DeviceName = FirstNonBlank(entity.Name, driver?.DeviceName) ?? entity.DeviceId,
                DeviceClass = FirstNonBlank(entity.PnpClass, driver?.DeviceClass) ?? UnknownClass,
                Manufacturer = FirstNonBlank(entity.Manufacturer, driver?.Manufacturer) ?? "",
                DriverProvider = FirstNonBlank(driver?.DriverProviderName),
                DriverVersion = FirstNonBlank(driver?.DriverVersion),
                DriverDate = CimDateTime.ParseDate(driver?.DriverDate),
                InfName = FirstNonBlank(driver?.InfName),
                HardwareIds = HardwareIdsFor(entity, driver),
                CompatibleIds = entity.CompatibleIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList() ?? [],
                ProblemCode = (int)entity.ConfigManagerErrorCode,
            });
        }

        return devices
            .OrderBy(d => d.DeviceClass, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(d => d.DeviceName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> HardwareIdsFor(PnpEntityRecord entity, SignedDriverRecord? driver)
    {
        var ids = entity.HardwareIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (ids is { Count: > 0 })
            return ids;

        return string.IsNullOrWhiteSpace(driver?.HardwareId) ? [] : [driver.HardwareId];
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
