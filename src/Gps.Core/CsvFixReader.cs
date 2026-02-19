using System.Globalization;

namespace Gps.Core;

public static class CsvFixReader
{
    public static IReadOnlyList<Fix> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fixes = new List<Fix>();
        using var reader = new StreamReader(path);

        _ = reader.ReadLine(); // Skip header.

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (!TryParseLine(line, out var fix))
            {
                continue;
            }

            fixes.Add(fix);
        }

        return fixes;
    }

    private static bool TryParseLine(string? line, out Fix fix)
    {
        fix = default!;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var parts = line.Split(',');
        if (parts.Length < 3)
        {
            return false;
        }

        var inv = CultureInfo.InvariantCulture;
        if (!DateTimeOffset.TryParse(parts[0], inv, DateTimeStyles.None, out var timestamp))
        {
            return false;
        }

        if (!double.TryParse(parts[1], NumberStyles.Float, inv, out var latitudeDeg))
        {
            return false;
        }

        if (!double.TryParse(parts[2], NumberStyles.Float, inv, out var longitudeDeg))
        {
            return false;
        }

        var speedMps = TryParseOptionalDouble(parts, 3);
        var numSv = TryParseOptionalInt(parts, 4);
        var fixType = TryParseOptionalText(parts, 5);
        var latitudeMeters = TryParseOptionalDouble(parts, 6);
        var longitudeMeters = TryParseOptionalDouble(parts, 7);

        fix = new Fix(timestamp, latitudeDeg, longitudeDeg, speedMps, numSv, fixType, latitudeMeters, longitudeMeters);
        return true;
    }

    private static double? TryParseOptionalDouble(string[] parts, int index)
    {
        if (index >= parts.Length)
        {
            return null;
        }

        return double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static int? TryParseOptionalInt(string[] parts, int index)
    {
        if (index >= parts.Length)
        {
            return null;
        }

        return int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? TryParseOptionalText(string[] parts, int index)
    {
        if (index >= parts.Length)
        {
            return null;
        }

        var value = parts[index].Trim();
        return value.Length == 0 ? null : value;
    }
}
