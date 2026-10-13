using DriverUpdater.Core.Models;
using DriverUpdater.Core.Services;

namespace DriverUpdater.Core.Tests;

public class UpdateMatcherTests
{
    private static DeviceDriverInfo Device(string id, string name, string[] hardwareIds,
        string[]? compatibleIds = null, string cls = "Net", DateOnly? date = null) => new()
    {
        DeviceId = id,
        DeviceName = name,
        DeviceClass = cls,
        HardwareIds = hardwareIds,
        CompatibleIds = compatibleIds ?? [],
        InfName = "oem1.inf",
        DriverVersion = "1.0",
        DriverDate = date,
    };

    private static DriverUpdateInfo Update(string title, params string[] hardwareIds) => new()
    {
        UpdateId = Guid.NewGuid().ToString(),
        Title = title,
        HardwareIds = hardwareIds,
        DriverDate = new DateOnly(2025, 1, 1),
    };

    [Fact]
    public void Matches_by_hardware_id_case_insensitively()
    {
        var nic = Device("1", "Ethernet", [@"PCI\VEN_8086&DEV_15F3&SUBSYS_00011028", @"PCI\VEN_8086&DEV_15F3"]);

        var match = Assert.Single(UpdateMatcher.Match([Update("Intel - Net - 2.0", @"pci\ven_8086&dev_15f3")], [nic]));

        Assert.Same(nic, match.PrimaryDevice);
        Assert.False(match.IsUnmatched);
    }

    [Fact]
    public void Falls_back_to_compatible_id_only_when_no_hardware_id_matches()
    {
        var specific = Device("1", "Specific", [@"USB\VID_1&PID_2"]);
        var generic = Device("2", "Generic", [@"USB\VID_9&PID_9"], compatibleIds: [@"USB\Class_03"]);

        var viaHardware = UpdateMatcher.Match([Update("A", @"USB\VID_1&PID_2", @"USB\Class_03")], [specific, generic]);
        var viaCompatible = UpdateMatcher.Match([Update("B", @"USB\Class_03")], [specific, generic]);

        Assert.Equal(["Specific"], viaHardware.Single().Devices.Select(d => d.DeviceName));
        Assert.Equal(["Generic"], viaCompatible.Single().Devices.Select(d => d.DeviceName));
    }

    [Fact]
    public void One_update_can_match_several_devices_without_duplicates()
    {
        var a = Device("A", "Port A", [@"USB\ROOT_HUB30", @"USB\ROOT_HUB"]);
        var b = Device("B", "Port B", [@"USB\ROOT_HUB30"]);

        var match = Assert.Single(UpdateMatcher.Match([Update("Hub", @"USB\ROOT_HUB30", @"USB\ROOT_HUB")], [a, b]));

        Assert.Equal(["A", "B"], match.Devices.Select(d => d.DeviceId));
    }

    [Fact]
    public void Unmatched_updates_are_kept_and_sorted_last()
    {
        var device = Device("1", "Zeta", [@"PCI\X"], cls: "Display");

        var matches = UpdateMatcher.Match([Update("Orphan", @"PCI\NOPE"), Update("Match", @"PCI\X")], [device]);

        Assert.Equal(["Match", "Orphan"], matches.Select(m => m.Update.Title));
        Assert.True(matches[1].IsUnmatched);
        Assert.Null(matches[1].PrimaryDevice);
    }

    [Fact]
    public void Flags_offers_dated_before_the_installed_driver()
    {
        var newer = Device("1", "Has newer", [@"PCI\A"], date: new DateOnly(2026, 1, 1));
        var older = Device("2", "Has older", [@"PCI\B"], date: new DateOnly(2020, 1, 1));

        var matches = UpdateMatcher.Match([Update("A", @"PCI\A"), Update("B", @"PCI\B")], [newer, older]);

        Assert.True(matches.Single(m => m.Update.Title == "A").OffersOlderDriver);
        Assert.False(matches.Single(m => m.Update.Title == "B").OffersOlderDriver);
    }

    private static MatchedDriverUpdate PlaceholderOffer(string? installedProvider, string installedVersion,
        string offeredVersion, string offeredProvider = "Intel")
    {
        var device = Device("1", "PCI Express Root Port", [@"PCI\VEN_8086&DEV_A74D"], date: new DateOnly(2006, 6, 21))
            with { DriverProvider = installedProvider, DriverVersion = installedVersion };
        var update = Update($"INTEL - System - {offeredVersion}", @"PCI\VEN_8086&DEV_A74D") with
        {
            DriverProvider = offeredProvider,
            DriverVersion = offeredVersion,
            DriverDate = MatchedDriverUpdate.IntelPlaceholderDate,
        };
        return Assert.Single(UpdateMatcher.Match([update], [device]));
    }

    [Fact]
    public void Placeholder_dated_offer_over_an_inbox_driver_is_not_flagged()
    {
        // Seen on a real PC: Intel 10.1.49.10 offered for a root port running inbox pci.inf (2006-06-21).
        Assert.False(PlaceholderOffer("Microsoft", "10.0.26100.1", "10.1.49.10").OffersOlderDriver);
    }

    [Theory]
    [InlineData("10.1.50.2", "10.1.49.10", true)]
    [InlineData("10.1.49.10", "10.1.49.10", false)]
    [InlineData("10.1.1.44", "10.1.49.10", false)]
    public void Placeholder_dated_offer_compares_versions_from_the_same_provider(
        string installedVersion, string offeredVersion, bool expectedOlder)
    {
        Assert.Equal(expectedOlder, PlaceholderOffer("INTEL", installedVersion, offeredVersion).OffersOlderDriver);
    }

    [Fact]
    public void Placeholder_dated_offer_with_unknown_provider_or_version_is_not_flagged()
    {
        Assert.False(PlaceholderOffer(null, "10.1.50.2", "10.1.49.10", offeredProvider: null!).OffersOlderDriver);
        Assert.False(PlaceholderOffer("Intel", "10.1.50.2", "See details").OffersOlderDriver);
    }

    [Fact]
    public void Real_dates_are_still_compared_when_the_installed_driver_is_placeholder_dated()
    {
        var device = Device("1", "SMBus", [@"PCI\X"], date: MatchedDriverUpdate.IntelPlaceholderDate);

        var match = Assert.Single(UpdateMatcher.Match([Update("A", @"PCI\X")], [device]));

        Assert.False(match.OffersOlderDriver);
    }
}
