using System.Globalization;

namespace Gps.Core;

public sealed class FixCsvWriter : IDisposable
{
    private const double MetersPerLatitudeDegree = 111_132.92;
    private const double MetersPerLongitudeDegreeAtEquator = 111_320.0;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly StreamWriter _writer;
    private double? _originLatitudeDeg;
    private double? _originLongitudeDeg;

    public FixCsvWriter(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fileExists = File.Exists(path);
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };

        if (!fileExists)
        {
            _writer.WriteLine("timestamp,lat,lon,speed_mps,num_sv,fix_type,lat_m,lon_m");
        }
    }

    public void Write(Fix fix)
    {
        ArgumentNullException.ThrowIfNull(fix);

        _originLatitudeDeg ??= fix.LatitudeDeg;
        _originLongitudeDeg ??= fix.LongitudeDeg;

        var latitudeMeters = fix.LatitudeMeters
            ?? (fix.LatitudeDeg - _originLatitudeDeg.Value) * MetersPerLatitudeDegree;

        var cosLat0 = Math.Cos(_originLatitudeDeg.Value * (Math.PI / 180.0));
        var longitudeMeters = fix.LongitudeMeters
            ?? (fix.LongitudeDeg - _originLongitudeDeg.Value) * MetersPerLongitudeDegreeAtEquator * cosLat0;

        var speed = fix.SpeedMps.HasValue ? fix.SpeedMps.Value.ToString("F2", Inv) : string.Empty;
        var numSv = fix.NumSv.HasValue ? fix.NumSv.Value.ToString(Inv) : string.Empty;
        var fixType = fix.FixType ?? string.Empty;

        _writer.WriteLine(string.Join(",",
            fix.Timestamp.ToString("o"),
            fix.LatitudeDeg.ToString("F7", Inv),
            fix.LongitudeDeg.ToString("F7", Inv),
            speed,
            numSv,
            fixType,
            latitudeMeters.ToString("F2", Inv),
            longitudeMeters.ToString("F2", Inv)));
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}
