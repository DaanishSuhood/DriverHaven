using DriverUpdater.Core.Infrastructure;

namespace DriverUpdater.Core.Services;

/// <summary>
/// Readable messages for common Windows Update Agent HRESULTs.
/// See https://learn.microsoft.com/windows/deployment/update/windows-update-error-reference
/// </summary>
public static class WindowsUpdateErrors
{
    private const string NoConnection = "Can't reach Windows Update. Check your internet connection and try again.";

    public const string RestartPending =
        "Restart your PC first. Windows is waiting for a restart to finish an earlier update or driver change.";

    private static readonly Dictionary<uint, string> Messages = new()
    {
        [0x8024402C] = NoConnection,                   // WU_E_PT_WINHTTP_NAME_NOT_RESOLVED
        [0x80072EE7] = NoConnection,                   // WININET_E_NAME_NOT_RESOLVED
        [0x80072EFD] = NoConnection,                   // WININET_E_CANNOT_CONNECT
        [0x80072EFE] = NoConnection,                   // WININET_E_CONNECTION_ABORTED
        [0x80240438] = NoConnection,                   // WU_E_PT_ENDPOINT_UNREACHABLE
        [0x8024401C] = "Windows Update timed out. Try again in a few minutes.",
        [0x80072EE2] = "Windows Update timed out. Try again in a few minutes.",
        [0x80244022] = "Windows Update servers are temporarily unavailable. Try again later.",
        [0x80072F8F] = "A secure connection to Windows Update failed. Check that the PC's date and time are correct.",
        [0x8024002E] = "Windows Update access is disabled by policy on this PC.",
        [0x8024500C] = "Connecting to Windows Update is blocked by policy on this PC.",
        [0x80070422] = "The Windows Update service is disabled. Enable it in Services and try again.",
        [0x8024A000] = "The Windows Update service isn't running.",
        [0x8024001E] = "The Windows Update service is shutting down. Try again shortly.",
        [0x80240016] = "Windows Update can't install right now: another update is being installed, or a restart is pending. Wait for it to finish or restart your PC, then try again.",
        [0x80240044] = "Administrator rights are required to perform this Windows Update operation.",
        [0x80240024] = "There are no updates to install.",
        [0x80240032] = "The update search criteria were rejected by Windows Update.",
        [0x8024000C] = "Windows Update had nothing to do.",
        [0x80240022] = "The update failed on all selected items.",

        // Download and install.
        [0x80240017] = "This update doesn't apply to this PC.",                         // WU_E_NOT_APPLICABLE
        [0x80070103] = "Windows already has an equal or better driver installed for this device.", // ERROR_NO_MORE_ITEMS (driver)
        [0x8024000B] = "The operation was cancelled.",                                  // WU_E_CALL_CANCELLED
        [0x80242014] = "A restart is needed to finish a previous update.",              // WU_E_UH_POSTREBOOTSTILLPENDING
        [0x8024200B] = "The driver installer reported a failure.",                      // WU_E_UH_INSTALLERFAILURE
        [0x80246007] = "The update hasn't been downloaded.",                            // WU_E_DM_NOTDOWNLOADED
        [0x80246008] = "The Background Intelligent Transfer Service (BITS) couldn't start the download.", // WU_E_DM_FAILTOCONNECTTOBITS
        [0x80070070] = "There isn't enough free disk space.",                           // ERROR_DISK_FULL
        [0x80070652] = "Another installation is already in progress. Wait for it to finish and try again.", // ERROR_INSTALL_ALREADY_RUNNING
        [0x80070005] = "Access was denied. Run " + ProductInfo.Name + " as an administrator.",     // E_ACCESSDENIED
    };

    public static string Describe(int hresult)
    {
        var code = unchecked((uint)hresult);
        return Messages.TryGetValue(code, out var message)
            ? $"{message} (0x{code:X8})"
            : $"Windows Update error 0x{code:X8}.";
    }
}

public sealed class WindowsUpdateException : Exception
{
    public WindowsUpdateException(string message, int hresult, Exception? inner = null) : base(message, inner)
    {
        HResult = hresult;
    }
}
