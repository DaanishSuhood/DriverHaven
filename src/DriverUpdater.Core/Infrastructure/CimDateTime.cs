using System.Globalization;

namespace DriverUpdater.Core.Infrastructure;

/// <summary>Parses WMI/CIM DATETIME strings such as "20230512000000.******+000".</summary>
public static class CimDateTime
{
    public static DateOnly? ParseDate(string? value)
    {
        if (value is null || value.Length < 8)
            return null;

        return DateOnly.TryParseExact(value.AsSpan(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
