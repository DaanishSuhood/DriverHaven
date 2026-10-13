using System.Globalization;
using DriverUpdater.Core.Infrastructure;

namespace DriverUpdater.Core.Tests;

public class ByteSizeTests
{
    [Theory]
    [InlineData(0, "—")]
    [InlineData(-5, "—")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10 * 1024 * 1024, "10 MB")]
    [InlineData(345L * 1024 * 1024, "345 MB")]
    [InlineData(1288490189L, "1.2 GB")]
    public void Formats(long bytes, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, ByteSize.Format(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
