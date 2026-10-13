using System.Text.RegularExpressions;

namespace DriverUpdater.Core.Services;

/// <summary>
/// Windows Update driver titles follow "Provider - Class - Version",
/// e.g. "Intel Corporation - Display - 31.0.101.4502".
/// </summary>
public static partial class DriverUpdateTitle
{
    public static string? ParseVersion(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var lastSegment = title[(title.LastIndexOf(" - ", StringComparison.Ordinal) + 1)..].Trim(' ', '-');
        if (ExactVersion().IsMatch(lastSegment))
            return lastSegment;

        // Fall back to the last dotted number with 3+ parts anywhere in the title. Two-part numbers
        // are skipped because titles also contain things like "5.1" channel counts.
        var matches = EmbeddedVersion().Matches(title);
        return matches.Count > 0 ? matches[^1].Value : null;
    }

    /// <summary>
    /// True for the "Provider - Class - Version" shape that Windows Update gives driver titles, or for the odd vendor
    /// bundle named like "Realtek Driver Update (1.0.934.0)".
    /// </summary>
    public static bool IsDriverTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;
        var parts = title.Split(" - ");
        return (parts.Length >= 3 && ExactVersion().IsMatch(parts[^1].Trim()))
            || title.Contains("Driver Update", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^\d+(\.\d+){1,3}$")]
    private static partial Regex ExactVersion();

    [GeneratedRegex(@"(?<![\d.])\d+(\.\d+){2,3}(?![\d.])")]
    private static partial Regex EmbeddedVersion();
}
