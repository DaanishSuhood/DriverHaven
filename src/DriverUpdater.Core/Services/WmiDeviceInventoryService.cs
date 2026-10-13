using System.Diagnostics;
using System.Management;
using DriverUpdater.Core.Models;
using Microsoft.Extensions.Logging;

namespace DriverUpdater.Core.Services;

/// <summary>Reads devices and their drivers from WMI (root\cimv2).</summary>
public sealed class WmiDeviceInventoryService(ILogger<WmiDeviceInventoryService> logger) : IDeviceInventoryService
{
    private const string EntityQuery =
        "SELECT DeviceID, Name, PNPClass, Manufacturer, HardwareID, CompatibleID, ConfigManagerErrorCode, Present FROM Win32_PnPEntity";

    private const string DriverQuery =
        "SELECT DeviceID, DeviceName, DeviceClass, Manufacturer, DriverProviderName, DriverVersion, DriverDate, InfName, HardWareID FROM Win32_PnPSignedDriver";

    public Task<IReadOnlyList<DeviceDriverInfo>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();

            var entities = Query(EntityQuery, ToEntityRecord, cancellationToken);
            var drivers = Query(DriverQuery, ToDriverRecord, cancellationToken);
            var devices = DeviceInventoryMapper.Map(entities, drivers);

            var problemDevices = devices.Where(d => d.HasProblem).ToList();
            logger.LogInformation(
                "Device scan found {DeviceCount} present devices ({ProblemCount} with problems) from {EntityCount} PnP entities and {DriverCount} driver records in {ElapsedMs} ms",
                devices.Count, problemDevices.Count, entities.Count, drivers.Count, stopwatch.ElapsedMilliseconds);
            foreach (var device in problemDevices)
                logger.LogInformation("Problem device: {DeviceName} [{DeviceId}] - {Status}", device.DeviceName, device.DeviceId, device.Status);

            return devices;
        }, cancellationToken);

    private static List<T> Query<T>(string wql, Func<ManagementBaseObject, T> map, CancellationToken cancellationToken)
    {
        using var searcher = new ManagementObjectSearcher(@"root\cimv2", wql);
        using var results = searcher.Get();

        var records = new List<T>();
        foreach (var obj in results)
        {
            using (obj)
            {
                cancellationToken.ThrowIfCancellationRequested();
                records.Add(map(obj));
            }
        }
        return records;
    }

    private static PnpEntityRecord ToEntityRecord(ManagementBaseObject obj) => new(
        DeviceId: obj["DeviceID"] as string,
        Name: obj["Name"] as string,
        PnpClass: obj["PNPClass"] as string,
        Manufacturer: obj["Manufacturer"] as string,
        HardwareIds: obj["HardwareID"] as string[],
        CompatibleIds: obj["CompatibleID"] as string[],
        ConfigManagerErrorCode: obj["ConfigManagerErrorCode"] is uint code ? code : 0,
        Present: obj["Present"] as bool?);

    private static SignedDriverRecord ToDriverRecord(ManagementBaseObject obj) => new(
        DeviceId: obj["DeviceID"] as string,
        DeviceName: obj["DeviceName"] as string,
        DeviceClass: obj["DeviceClass"] as string,
        Manufacturer: obj["Manufacturer"] as string,
        DriverProviderName: obj["DriverProviderName"] as string,
        DriverVersion: obj["DriverVersion"] as string,
        DriverDate: obj["DriverDate"] as string,
        InfName: obj["InfName"] as string,
        HardwareId: obj["HardWareID"] as string);
}
