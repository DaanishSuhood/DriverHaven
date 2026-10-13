using DriverUpdater.Core.Models;

namespace DriverUpdater.Core.Services;

/// <summary>
/// One IUpdateHistoryEntry as read from COM. Kept as plain data so the filtering and mapping can be tested.
/// </summary>
/// <param name="DateUtc">Windows Update records history times in UTC.</param>
/// <param name="CategoryIds">IUpdateHistoryEntry2.Categories; empty on older agents and for some entries.</param>
public sealed record UpdateHistoryRecord(
    DateTime DateUtc,
    string? Title,
    string? UpdateId,
    int Operation,
    int ResultCode,
    int HResult,
    string? ClientApplicationId,
    IReadOnlyList<string> CategoryIds);

/// <summary>Turns the Windows Update history into the driver entries shown on the History tab.</summary>
public static class DriverHistory
{
    /// <summary>The "Drivers" update classification.</summary>
    public const string DriversCategoryId = "ebfc1fc5-71a4-4f7b-9aca-3b9a503104a0";

    /// <summary>Driver entries only, newest first.</summary>
    public static IReadOnlyList<DriverHistoryEntry> FromRecords(IEnumerable<UpdateHistoryRecord> records) =>
        records.Where(IsDriver).Select(ToEntry).OrderByDescending(e => e.Date).ToList();

    /// <summary>
    /// In practice Windows Update records no categories for driver installs (only for products such as Defender),
    /// so the "Provider - Class - Version" title is what usually identifies them. The Drivers category is honoured
    /// too, for agents that do record it.
    /// </summary>
    public static bool IsDriver(UpdateHistoryRecord record) =>
        !string.IsNullOrWhiteSpace(record.Title)
        && (record.CategoryIds.Contains(DriversCategoryId, StringComparer.OrdinalIgnoreCase)
            || DriverUpdateTitle.IsDriverTitle(record.Title));

    /// <summary>A readable name for the client application ID that asked Windows Update for the install.</summary>
    public static string? DescribeClient(string? clientApplicationId) => clientApplicationId?.Trim() switch
    {
        null or "" => null,
        var id when id.Contains("UpdateOrchestrator", StringComparison.OrdinalIgnoreCase)
                    || id.Contains("MoUso", StringComparison.OrdinalIgnoreCase) => "Windows Update",
        var id when id.Equals("Device Driver Retrieval Client", StringComparison.OrdinalIgnoreCase) => "Windows device setup",
        var id => id,
    };

    public static DriverHistoryEntry ToEntry(UpdateHistoryRecord record)
    {
        var operation = (UpdateHistoryOperation)record.Operation;
        var resultCode = (OperationResultCode)record.ResultCode;
        var succeeded = resultCode is OperationResultCode.Succeeded or OperationResultCode.SucceededWithErrors;

        return new DriverHistoryEntry
        {
            Date = DateTime.SpecifyKind(record.DateUtc, DateTimeKind.Utc).ToLocalTime(),
            Title = record.Title!.Trim(),
            UpdateId = record.UpdateId,
            Operation = operation,
            ResultCode = resultCode,
            HResult = record.HResult,
            ClientApplicationId = string.IsNullOrWhiteSpace(record.ClientApplicationId) ? null : record.ClientApplicationId,
            InstalledBy = DescribeClient(record.ClientApplicationId),
            Result = DescribeResult(operation, resultCode),
            ErrorMessage = !succeeded && record.HResult != 0 ? WindowsUpdateErrors.Describe(record.HResult) : null,
        };
    }

    private static string DescribeResult(UpdateHistoryOperation operation, OperationResultCode resultCode) =>
        (operation, resultCode) switch
        {
            (UpdateHistoryOperation.Uninstallation, OperationResultCode.Succeeded) => "Uninstalled",
            (UpdateHistoryOperation.Uninstallation, OperationResultCode.Failed) => "Uninstall failed",
            (_, OperationResultCode.Succeeded) => "Installed",
            (_, OperationResultCode.SucceededWithErrors) => "Installed with errors",
            (_, OperationResultCode.Failed) => "Failed",
            (_, OperationResultCode.Aborted) => "Cancelled",
            (_, OperationResultCode.InProgress) => "In progress",
            _ => "Not started",
        };
}
