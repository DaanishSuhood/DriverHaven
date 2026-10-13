using DriverUpdater.Core.Models;

namespace DriverUpdater.Core.Tests;

public class DeviceProblemCodesTests
{
    [Theory]
    [InlineData(0, "Working")]
    [InlineData(22, "Device is disabled (code 22)")]
    [InlineData(43, "Windows stopped this device because it reported problems (code 43)")]
    [InlineData(999, "Problem (code 999)")]
    public void Describes_codes(int code, string expected)
    {
        Assert.Equal(expected, DeviceProblemCodes.Describe(code));
    }
}
