using DriverUpdater.Core.Models;

namespace DriverUpdater.Core.Services;

/// <summary>Links Windows Update drivers to installed devices by hardware ID, then compatible ID.</summary>
public static class UpdateMatcher
{
    public static IReadOnlyList<MatchedDriverUpdate> Match(
        IEnumerable<DriverUpdateInfo> updates, IReadOnlyCollection<DeviceDriverInfo> devices)
    {
        var byHardwareId = BuildIndex(devices, d => d.HardwareIds);
        var byCompatibleId = BuildIndex(devices, d => d.CompatibleIds);

        return updates
            .Select(update =>
            {
                var matches = FindDevices(update, byHardwareId);
                if (matches.Count == 0)
                    matches = FindDevices(update, byCompatibleId);
                return new MatchedDriverUpdate(update, matches);
            })
            .OrderBy(m => m.IsUnmatched)
            .ThenBy(m => m.PrimaryDevice?.DeviceClass ?? m.Update.DriverClass, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.PrimaryDevice?.DeviceName ?? m.Update.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, List<DeviceDriverInfo>> BuildIndex(
        IEnumerable<DeviceDriverInfo> devices, Func<DeviceDriverInfo, IEnumerable<string>> ids)
    {
        var index = new Dictionary<string, List<DeviceDriverInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices)
        {
            foreach (var id in ids(device))
            {
                if (!index.TryGetValue(id, out var list))
                    index[id] = list = [];
                list.Add(device);
            }
        }
        return index;
    }

    private static List<DeviceDriverInfo> FindDevices(
        DriverUpdateInfo update, Dictionary<string, List<DeviceDriverInfo>> index) =>
        update.HardwareIds
            .SelectMany(id => index.TryGetValue(id, out var found) ? found : [])
            .DistinctBy(d => d.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
