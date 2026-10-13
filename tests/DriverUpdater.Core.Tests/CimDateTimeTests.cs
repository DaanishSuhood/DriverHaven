using DriverUpdater.Core.Infrastructure;

namespace DriverUpdater.Core.Tests;

public class CimDateTimeTests
{
    [Theory]
    [InlineData("20230512000000.******+000", 2023, 5, 12)]
    [InlineData("20060621", 2006, 6, 21)]
    public void Parses_date_portion(string value, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), CimDateTime.ParseDate(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2023")]
    [InlineData("20231340000000.******+000")]
    [InlineData("notadate")]
    public void Returns_null_for_invalid_values(string? value)
    {
        Assert.Null(CimDateTime.ParseDate(value));
    }
}
