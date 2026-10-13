namespace DriverUpdater.Core.Models;

public enum RestorePointOutcome
{
    Created,

    /// <summary>Windows skipped creation because a restore point already exists from the last 24 hours.</summary>
    SkippedRecentExists,

    /// <summary>System Protection is turned off (or disabled by policy).</summary>
    Disabled,

    Failed,
}

public sealed record RestorePointInfo(uint SequenceNumber, string Description, DateTimeOffset CreatedAt);

/// <param name="RestorePoint">The point that was created, or the recent one that made Windows skip creation.</param>
public sealed record RestorePointResult(RestorePointOutcome Outcome, string Message, RestorePointInfo? RestorePoint = null)
{
    /// <summary>True when a restore point usable for undoing upcoming changes exists.</summary>
    public bool HasUsableRestorePoint => Outcome is RestorePointOutcome.Created or RestorePointOutcome.SkippedRecentExists;
}
