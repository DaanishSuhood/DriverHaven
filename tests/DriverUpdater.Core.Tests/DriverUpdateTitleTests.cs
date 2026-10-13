using DriverUpdater.Core.Services;

namespace DriverUpdater.Core.Tests;

public class DriverUpdateTitleTests
{
    [Theory]
    [InlineData("Intel Corporation - Display - 31.0.101.4502", "31.0.101.4502")]
    [InlineData("Realtek Semiconductor Corp. - Extension - 10.0.22621.31238", "10.0.22621.31238")]
    [InlineData("Intel - net - 22.5.7.1", "22.5.7.1")]
    [InlineData("Logitech - HIDClass - 1.10", "1.10")]
    [InlineData("Advanced Micro Devices, Inc. - Display - 32.0.11021.1011 (WHQL)", "32.0.11021.1011")]
    [InlineData("Surround 5.1 audio driver 4.10.42.816", "4.10.42.816")]
    public void Extracts_version(string title, string expected)
    {
        Assert.Equal(expected, DriverUpdateTitle.ParseVersion(title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Contoso - Firmware")]
    [InlineData("Surround 5.1 audio driver")]
    public void Returns_null_when_no_version(string? title)
    {
        Assert.Null(DriverUpdateTitle.ParseVersion(title));
    }

    [Theory]
    [InlineData("Intel Corporation - Display - 31.0.101.4502", true)]
    [InlineData("Logitech - HIDClass - 1.10", true)]
    [InlineData("Realtek Driver Update (1.0.934.0)", true)]
    [InlineData("Contoso - Firmware", false)]
    [InlineData("Contoso - Firmware - beta", false)]
    [InlineData("Surround 5.1 audio driver 4.10.42.816", false)]
    [InlineData(null, false)]
    public void Recognises_driver_titles(string? title, bool expected)
    {
        Assert.Equal(expected, DriverUpdateTitle.IsDriverTitle(title));
    }
}
