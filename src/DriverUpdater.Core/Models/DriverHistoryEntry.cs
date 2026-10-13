namespace DriverUpdater.Core.Models;

/// <summary>WUA UpdateOperation, as recorded in the update history.</summary>
public enum UpdateHistoryOperation
{
    Installation = 1,
    Uninstallation = 2,
}

/// <summary>One driver install (or uninstall) recorded in the Windows Update history.</summary>
public sealed record DriverHistoryEntry
{
    /// <summary>Local time.</summary>
    public required DateTime Date { get; init; }

    public required string Title { get; init; }

    public string? UpdateId { get; init; }

    public UpdateHistoryOperation Operation { get; init; } = UpdateHistoryOperation.Installation;

    public OperationResultCode ResultCode { get; init; }

    public int HResult { get; init; }

    /// <summary>The app that asked Windows Update for the operation, e.g. "DriverHaven" or "UpdateOrchestrator".</summary>
    public string? ClientApplicationId { get; init; }

    /// <summary><see cref="ClientApplicationId"/> in words, e.g. "Windows Update" for UpdateOrchestrator.</summary>
    public string? InstalledBy { get; init; }

    /// <summary>What happened, e.g. "Installed" or "Failed".</summary>
    public required string Result { get; init; }

    /// <summary>Why it failed, in words, or null when it succeeded or Windows Update gave no error code.</summary>
    public string? ErrorMessage { get; init; }

    public bool Succeeded => ResultCode is OperationResultCode.Succeeded or OperationResultCode.SucceededWithErrors;
}
