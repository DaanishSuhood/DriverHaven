using DriverUpdater.Core.Models;
using DriverUpdater.Core.Services;

namespace DriverUpdater.Core.Tests;

public class DriverHistoryTests
{
    private const string DefenderProductCategoryId = "8c3fcc84-7410-4a95-8b89-a166a0190486";

    private static UpdateHistoryRecord Record(string? title, string[]? categories = null, int operation = 1,
        int resultCode = 2, int hresult = 0, DateTime? dateUtc = null, string? client = "DriverHaven") =>
        new(dateUtc ?? new DateTime(2026, 10, 1, 12, 0, 0), title, Guid.NewGuid().ToString(), operation, resultCode, hresult,
            client, categories ?? [DriverHistory.DriversCategoryId]);

    [Fact]
    public void Keeps_driver_entries_and_drops_the_rest()
    {
        // Shaped like a real PC's history: driver installs carry no categories, while Defender does.
        var entries = DriverHistory.FromRecords(
        [
            Record("SAMSUNG Electronics Co., Ltd.  - Net - 2.21.0.0", categories: [], client: "MoUpdateOrchestrator"),
            Record("Realtek Driver Update (1.0.934.0)", categories: []),
            Record("Classified as a driver", [DriverHistory.DriversCategoryId]),
            Record("Security Intelligence Update for Microsoft Defender Antivirus - KB2267602 (Version 1.401.0.0)",
                [DefenderProductCategoryId]),
            Record("2026-09 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB5065426) (26100.6584)", []),
            Record("9NRZT3Q9R3DL-Microsoft.WindowsAppRuntime.2", []),
        ]);

        Assert.Equal(
            ["SAMSUNG Electronics Co., Ltd.  - Net - 2.21.0.0", "Realtek Driver Update (1.0.934.0)", "Classified as a driver"],
            entries.Select(e => e.Title));
    }

    [Fact]
    public void Category_match_ignores_case()
    {
        Assert.True(DriverHistory.IsDriver(Record("Anything", [DriverHistory.DriversCategoryId.ToUpperInvariant()])));
    }

    [Theory]
    [InlineData("Realtek Semiconductor Corp. - Extension - 10.0.22621.31238", true)]
    [InlineData("Intel - net - 22.5.7.1", true)]
    [InlineData("Security Intelligence Update for Microsoft Defender Antivirus - KB2267602 (Version 1.401.0.0)", false)]
    [InlineData("2026-09 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB5030000)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Without_categories_falls_back_to_the_driver_title_shape(string? title, bool expected)
    {
        Assert.Equal(expected, DriverHistory.IsDriver(Record(title, categories: [])));
    }

    [Fact]
    public void Sorts_newest_first_and_converts_to_local_time()
    {
        var older = new DateTime(2026, 9, 1, 8, 0, 0);
        var newer = new DateTime(2026, 10, 2, 8, 0, 0);

        var entries = DriverHistory.FromRecords([Record("A - B - 1.0", dateUtc: older), Record("C - D - 2.0", dateUtc: newer)]);

        Assert.Equal(["C - D - 2.0", "A - B - 1.0"], entries.Select(e => e.Title));
        Assert.Equal(DateTimeKind.Local, entries[0].Date.Kind);
        Assert.Equal(newer, entries[0].Date.ToUniversalTime());
    }

    [Theory]
    [InlineData(1, 2, "Installed")]
    [InlineData(1, 3, "Installed with errors")]
    [InlineData(1, 4, "Failed")]
    [InlineData(1, 5, "Cancelled")]
    [InlineData(2, 2, "Uninstalled")]
    [InlineData(2, 4, "Uninstall failed")]
    public void Describes_the_result(int operation, int resultCode, string expected)
    {
        Assert.Equal(expected, DriverHistory.ToEntry(Record("A - B - 1.0", operation: operation, resultCode: resultCode)).Result);
    }

    [Fact]
    public void Failure_gets_a_readable_error_message()
    {
        var entry = DriverHistory.ToEntry(Record("A - B - 1.0", resultCode: 4, hresult: unchecked((int)0x80070103)));

        Assert.False(entry.Succeeded);
        Assert.Equal(WindowsUpdateErrors.Describe(unchecked((int)0x80070103)), entry.ErrorMessage);
        Assert.Contains("equal or better driver", entry.ErrorMessage);
    }

    [Fact]
    public void Success_has_no_error_message_even_with_an_hresult()
    {
        var entry = DriverHistory.ToEntry(Record("A - B - 1.0", resultCode: 2, hresult: unchecked((int)0x80240017)));

        Assert.True(entry.Succeeded);
        Assert.Null(entry.ErrorMessage);
    }

    [Fact]
    public void Blank_client_application_becomes_null_and_title_is_trimmed()
    {
        var entry = DriverHistory.ToEntry(Record("  A - B - 1.0 ", client: " "));

        Assert.Null(entry.ClientApplicationId);
        Assert.Null(entry.InstalledBy);
        Assert.Equal("A - B - 1.0", entry.Title);
    }

    [Theory]
    [InlineData("MoUpdateOrchestrator", "Windows Update")]
    [InlineData("UpdateOrchestrator", "Windows Update")]
    [InlineData("Update;MoUpdateOrchestratorDeviceScan-MoUsoCoreWorker-SearchForAllUpdatesWithUpdateOptionsAsync", "Windows Update")]
    [InlineData("Device Driver Retrieval Client", "Windows device setup")]
    [InlineData("DriverHaven", "DriverHaven")]
    [InlineData("SomeTool", "SomeTool")]
    public void Names_the_client_that_installed_it(string client, string expected)
    {
        Assert.Equal(expected, DriverHistory.ToEntry(Record("A - B - 1.0", client: client)).InstalledBy);
    }
}
