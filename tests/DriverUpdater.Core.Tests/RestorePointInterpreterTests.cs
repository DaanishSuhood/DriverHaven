using DriverUpdater.Core.Models;
using DriverUpdater.Core.Services;

namespace DriverUpdater.Core.Tests;

public class RestorePointInterpreterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Daily = RestorePointInterpreter.DefaultCreationFrequency;

    private static RestorePointInfo Point(uint sequence, TimeSpan age) => new(sequence, $"Point {sequence}", Now - age);

    [Fact]
    public void New_sequence_number_means_created()
    {
        var created = Point(8, TimeSpan.FromSeconds(5));

        var result = RestorePointInterpreter.Interpret(0, Point(7, TimeSpan.FromDays(3)), created, Now, Daily);

        Assert.Equal(RestorePointOutcome.Created, result.Outcome);
        Assert.Same(created, result.RestorePoint);
        Assert.True(result.HasUsableRestorePoint);
    }

    [Fact]
    public void First_ever_point_counts_as_created()
    {
        var result = RestorePointInterpreter.Interpret(0, null, Point(1, TimeSpan.Zero), Now, Daily);

        Assert.Equal(RestorePointOutcome.Created, result.Outcome);
    }

    [Fact]
    public void Unchanged_but_recent_point_means_skipped_by_frequency_limit()
    {
        var recent = Point(7, TimeSpan.FromHours(3));

        var result = RestorePointInterpreter.Interpret(0, recent, recent, Now, Daily);

        Assert.Equal(RestorePointOutcome.SkippedRecentExists, result.Outcome);
        Assert.Same(recent, result.RestorePoint);
        Assert.True(result.HasUsableRestorePoint);
        Assert.Contains("every 24 hours", result.Message);
    }

    [Fact]
    public void Custom_frequency_is_used_for_window_and_message()
    {
        var point = Point(7, TimeSpan.FromMinutes(20));

        var within = RestorePointInterpreter.Interpret(0, point, point, Now, TimeSpan.FromMinutes(30));
        var outside = RestorePointInterpreter.Interpret(0, point, point, Now, TimeSpan.FromMinutes(10));

        Assert.Equal(RestorePointOutcome.SkippedRecentExists, within.Outcome);
        Assert.Contains("every 30 minutes", within.Message);
        Assert.Equal(RestorePointOutcome.Failed, outside.Outcome);
    }

    [Fact]
    public void Zero_frequency_means_no_limit_so_unchanged_point_is_a_failure()
    {
        var recent = Point(7, TimeSpan.FromSeconds(30));

        var result = RestorePointInterpreter.Interpret(0, recent, recent, Now, TimeSpan.Zero);

        Assert.Equal(RestorePointOutcome.Failed, result.Outcome);
        Assert.False(RestorePointInterpreter.IsWithinFrequencyWindow(recent, Now, TimeSpan.Zero));
    }

    [Fact]
    public void Unchanged_and_old_point_means_failed()
    {
        var old = Point(7, TimeSpan.FromDays(10));

        var result = RestorePointInterpreter.Interpret(0, old, old, Now, Daily);

        Assert.Equal(RestorePointOutcome.Failed, result.Outcome);
        Assert.False(result.HasUsableRestorePoint);
    }

    [Fact]
    public void Success_with_no_points_at_all_means_failed()
    {
        Assert.Equal(RestorePointOutcome.Failed, RestorePointInterpreter.Interpret(0, null, null, Now, Daily).Outcome);
    }

    [Theory]
    [InlineData(1058u)]
    [InlineData(0x80070422u)]
    public void Service_disabled_codes_mean_disabled(uint code)
    {
        var result = RestorePointInterpreter.Interpret(code, null, null, Now, Daily);

        Assert.Equal(RestorePointOutcome.Disabled, result.Outcome);
        Assert.Equal(RestorePointInterpreter.DisabledMessage, result.Message);
    }

    [Fact]
    public void Other_error_codes_mean_failed_with_code_in_message()
    {
        var result = RestorePointInterpreter.Interpret(5, null, null, Now, Daily);

        Assert.Equal(RestorePointOutcome.Failed, result.Outcome);
        Assert.Contains("code 5", result.Message);
    }
}
