using DriverUpdater.Core.Models;
using DriverUpdater.Core.Services;

namespace DriverUpdater.Core.Tests;

public class DeviceInventoryMapperTests
{
    private static PnpEntityRecord Entity(
        string id, string? name = "Device", string? pnpClass = "Net", string? manufacturer = "Contoso",
        string[]? hardwareIds = null, uint problem = 0, bool? present = true, string[]? compatibleIds = null) =>
        new(id, name, pnpClass, manufacturer, hardwareIds, compatibleIds, problem, present);

    private static SignedDriverRecord Driver(
        string id, string? version = "1.2.3.4", string? date = "20230512000000.******+000",
        string? inf = "oem12.inf", string? provider = "Contoso Ltd", string? hardwareId = null) =>
        new(id, DeviceName: null, DeviceClass: null, Manufacturer: null, provider, version, date, inf, hardwareId);

    [Fact]
    public void Joins_driver_details_onto_entity_by_device_id_case_insensitively()
    {
        var devices = DeviceInventoryMapper.Map(
            [Entity(@"PCI\VEN_8086&DEV_15F3\ABC", name: "Intel Ethernet", hardwareIds: [@"PCI\VEN_8086&DEV_15F3&SUBSYS_0001", @"PCI\VEN_8086&DEV_15F3"])],
            [Driver(@"pci\ven_8086&dev_15f3\abc")]);

        var device = Assert.Single(devices);
        Assert.Equal("Intel Ethernet", device.DeviceName);
        Assert.Equal("1.2.3.4", device.DriverVersion);
        Assert.Equal(new DateOnly(2023, 5, 12), device.DriverDate);
        Assert.Equal("oem12.inf", device.InfName);
        Assert.Equal("Contoso Ltd", device.DriverProvider);
        Assert.Equal([@"PCI\VEN_8086&DEV_15F3&SUBSYS_0001", @"PCI\VEN_8086&DEV_15F3"], device.HardwareIds);
        Assert.True(device.IsThirdPartyDriver);
        Assert.False(device.HasProblem);
        Assert.Equal("Working", device.Status);
    }

    [Fact]
    public void Device_without_driver_is_kept_with_problem_code()
    {
        var devices = DeviceInventoryMapper.Map(
            [Entity("USB\\VID_1234", name: null, pnpClass: null, manufacturer: null, problem: 28)],
            []);

        var device = Assert.Single(devices);
        Assert.Equal("USB\\VID_1234", device.DeviceName);
        Assert.Equal(DeviceInventoryMapper.UnknownClass, device.DeviceClass);
        Assert.False(device.HasDriver);
        Assert.Null(device.DriverVersion);
        Assert.True(device.HasProblem);
        Assert.Equal("Drivers are not installed (code 28)", device.Status);
    }

    [Fact]
    public void Skips_non_present_and_blank_devices_but_keeps_unknown_presence()
    {
        var devices = DeviceInventoryMapper.Map(
            [Entity("A", present: false), Entity(" "), Entity("B", present: null)],
            []);

        Assert.Equal("B", Assert.Single(devices).DeviceId);
    }

    [Fact]
    public void Falls_back_to_driver_record_fields_when_entity_fields_are_blank()
    {
        var driver = new SignedDriverRecord("X", "Driver Name", "Display", "Fabrikam", null, "2.0", null, "display.inf", @"PCI\VEN_1002");

        var device = Assert.Single(DeviceInventoryMapper.Map(
            [Entity("X", name: " ", pnpClass: "", manufacturer: null, hardwareIds: [])], [driver]));

        Assert.Equal("Driver Name", device.DeviceName);
        Assert.Equal("Display", device.DeviceClass);
        Assert.Equal("Fabrikam", device.Manufacturer);
        Assert.Equal([@"PCI\VEN_1002"], device.HardwareIds);
        Assert.False(device.IsThirdPartyDriver);
    }

    [Fact]
    public void Copies_compatible_ids_without_blanks()
    {
        var device = Assert.Single(DeviceInventoryMapper.Map(
            [Entity("X", compatibleIds: [@"USB\Class_03", "", @"USB\Class_03&SubClass_01"])], []));

        Assert.Equal([@"USB\Class_03", @"USB\Class_03&SubClass_01"], device.CompatibleIds);
    }

    [Fact]
    public void Duplicate_driver_records_use_the_first()
    {
        var device = Assert.Single(DeviceInventoryMapper.Map(
            [Entity("X")], [Driver("X", version: "1.0"), Driver("X", version: "9.9")]));

        Assert.Equal("1.0", device.DriverVersion);
    }

    [Fact]
    public void Sorts_by_class_then_name()
    {
        var devices = DeviceInventoryMapper.Map(
            [
                Entity("1", name: "Zeta", pnpClass: "Net"),
                Entity("2", name: "beta", pnpClass: "Display"),
                Entity("3", name: "Alpha", pnpClass: "Net"),
            ],
            []);

        Assert.Equal(["beta", "Alpha", "Zeta"], devices.Select(d => d.DeviceName));
    }
}
