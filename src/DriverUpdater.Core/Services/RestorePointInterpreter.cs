using System.Globalization;
using System.Runtime.InteropServices;
using DriverUpdater.Core.Models;

namespace DriverUpdater.Core.Services;

/// <summary>
/// Turns the raw CreateRestorePoint return value plus the newest restore point before/after the call into an
/// outcome. CreateRestorePoint returns 0 even when Windows silently skips creation (by default one point per
/// 24 hours), so comparing sequence numbers is the only reliable way to know whether a point was made.
/// </summary>
public static class RestorePointInterpreter
{
    /// <summary>Windows' default when SystemRestorePointCreationFrequency isn't set.</summary>
    public static readonly TimeSpan DefaultCreationFrequency = TimeSpan.FromHours(24);

    private const uint ErrorServiceDisabled = 1058;
    private const uint HResultServiceDisabled = 0x80070422;

    public const string DisabledMessage =
        "System Protection is turned off, so Windows can't create restore points. " +
        "Turn it on in Control Panel > System > System Protection to be able to undo driver changes.";

    /// <summary>True when Windows will skip creation because <paramref name="latest"/> is inside the frequency window.</summary>
    public static bool IsWithinFrequencyWindow(RestorePointInfo? latest, DateTimeOffset now, TimeSpan creationFrequency) =>
        latest is not null && creationFrequency > TimeSpan.Zero && now - latest.CreatedAt < creationFrequency;

    public static RestorePointResult Interpret(uint returnValue, RestorePointInfo? before, RestorePointInfo? after,
        DateTimeOffset now, TimeSpan creationFrequency)
    {
        if (returnValue is ErrorServiceDisabled or HResultServiceDisabled)
            return new(RestorePointOutcome.Disabled, DisabledMessage);

        if (returnValue != 0)
        {
            var reason = Marshal.GetPInvokeErrorMessage(unchecked((int)returnValue));
            return new(RestorePointOutcome.Failed, $"Windows could not create a restore point: {reason} (code {returnValue}).");
        }

        if (after is not null && (before is null || after.SequenceNumber > before.SequenceNumber))
            return new(RestorePointOutcome.Created, $"Created restore point \"{after.Description}\".", after);

        if (IsWithinFrequencyWindow(after, now, creationFrequency))
        {
            return new(RestorePointOutcome.SkippedRecentExists,
                $"Windows allows only one restore point every {Describe(creationFrequency)}, so a new one wasn't created. " +
                $"The most recent one, \"{after!.Description}\" from {after.CreatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)}, " +
                "can still be used to undo changes.",
                after);
        }

        return new(RestorePointOutcome.Failed,
            "Windows reported success but no new restore point appeared. System Protection may be turned off for the system drive.");
    }

    private static string Describe(TimeSpan frequency) =>
        frequency.TotalMinutes % 60 == 0
            ? $"{frequency.TotalHours:0} hour{(frequency.TotalHours == 1 ? "" : "s")}"
            : $"{frequency.TotalMinutes:0} minute{(frequency.TotalMinutes == 1 ? "" : "s")}";
}
