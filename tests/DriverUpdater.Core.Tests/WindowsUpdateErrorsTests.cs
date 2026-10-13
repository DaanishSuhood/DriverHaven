using DriverUpdater.Core.Services;

namespace DriverUpdater.Core.Tests;

public class WindowsUpdateErrorsTests
{
    [Fact]
    public void Describes_known_network_error()
    {
        var message = WindowsUpdateErrors.Describe(unchecked((int)0x8024402C));

        Assert.StartsWith("Can't reach Windows Update", message);
        Assert.EndsWith("(0x8024402C)", message);
    }

    [Fact]
    public void Describes_driver_already_installed_error()
    {
        Assert.StartsWith("Windows already has an equal or better driver", WindowsUpdateErrors.Describe(unchecked((int)0x80070103)));
    }

    [Fact]
    public void Install_not_allowed_error_mentions_a_pending_restart()
    {
        // WUA returns 0x80240016 both while another install runs and while a restart is pending.
        var message = WindowsUpdateErrors.Describe(unchecked((int)0x80240016));

        Assert.Contains("another update is being installed", message);
        Assert.Contains("restart", message);
    }

    [Fact]
    public void Falls_back_to_hex_code_for_unknown_errors()
    {
        Assert.Equal("Windows Update error 0x80001234.", WindowsUpdateErrors.Describe(unchecked((int)0x80001234)));
    }

    [Fact]
    public void Exception_carries_hresult()
    {
        var ex = new WindowsUpdateException("msg", unchecked((int)0x80240024));

        Assert.Equal(unchecked((int)0x80240024), ex.HResult);
    }
}
