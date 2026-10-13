namespace DriverUpdater.Core.Models;

/// <summary>WUA OperationResultCode, as reported for a download or an install.</summary>
public enum OperationResultCode
{
    NotStarted = 0,
    InProgress = 1,
    Succeeded = 2,
    SucceededWithErrors = 3,
    Failed = 4,
    Aborted = 5,
}

/// <summary>The outcome of downloading or installing one update through Windows Update.</summary>
/// <param name="HResult">The error Windows Update reported, or 0.</param>
/// <param name="Message">A readable reason, when the caller has a better one than the HRESULT's.</param>
public sealed record UpdateOperationResult(
    OperationResultCode ResultCode,
    int HResult = 0,
    bool RebootRequired = false,
    string? Message = null)
{
    public bool Succeeded => ResultCode is OperationResultCode.Succeeded or OperationResultCode.SucceededWithErrors;

    public static UpdateOperationResult Failed(int hresult, string? message = null) =>
        new(OperationResultCode.Failed, hresult, Message: message);
}
