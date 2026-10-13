using System.Globalization;

namespace DriverUpdater.Core.Infrastructure;

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        if (bytes <= 0)
            return "—";

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 || value >= 100 ? "0" : "0.#";
        return $"{value.ToString(format, CultureInfo.CurrentCulture)} {Units[unit]}";
    }
}
