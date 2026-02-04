using System.Globalization;

namespace Gps.Core;

public static class CsvFixReader
{
    public static IReadOnlyList<Fix> Read(string path)
    {
        var list = new List<Fix>();
        using var sr = new StreamReader(path);

        string? header = sr.ReadLine();
        if (header is null) return list;

        while (!sr.EndOfStream)
        {
            var line = sr.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var p = line.Split(',');
            if (p.Length < 3) continue;

            var ts = DateTimeOffset.Parse(p[0], CultureInfo.InvariantCulture);
            var lat = double.Parse(p[1], CultureInfo.InvariantCulture);
            var lon = double.Parse(p[2], CultureInfo.InvariantCulture);

            double? speed = p.Length > 3 && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : null;
            int? numSv = p.Length > 4 && int.TryParse(p[4], out var sv) ? sv : null;
            string? fixType = p.Length > 5 ? p[5] : null;

            list.Add(new Fix(ts, lat, lon, speed, numSv, fixType));
        }
        return list;
    }
}
