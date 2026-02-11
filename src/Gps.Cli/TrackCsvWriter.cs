using System.Globalization;

namespace Gps.Cli;

internal sealed class TrackCsvWriter : IDisposable
{
    // Approximate meters per degree (good for short tracks).
    private const double MetersPerLatitudeDegree = 111_132.92;
    private const double MetersPerLongitudeDegreeAtEquator = 111_320.0;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly StreamWriter _writer;
    private double? _originLatitudeDeg;
    private double? _originLongitudeDeg;

    public TrackCsvWriter(string path)
    {
        var fileExists = File.Exists(path);
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };

        if (!fileExists)
        {
            _writer.WriteLine("timestamp,lat,lon,speed_mps,num_sv,fix_type,lat_m,lon_m");
            Console.WriteLine($"Created {path}");
        }
    }

    public void Write(TrackSample sample)
    {
        _originLatitudeDeg ??= sample.LatitudeDeg;
        _originLongitudeDeg ??= sample.LongitudeDeg;

        var latitudeMeters = (sample.LatitudeDeg - _originLatitudeDeg.Value) * MetersPerLatitudeDegree;
        var cosLat0 = Math.Cos(_originLatitudeDeg.Value * (Math.PI / 180.0));
        var longitudeMeters = (sample.LongitudeDeg - _originLongitudeDeg.Value) * MetersPerLongitudeDegreeAtEquator * cosLat0;

        _writer.WriteLine(string.Format(Inv,
            "{0},{1:F7},{2:F7},{3:F2},{4},{5},{6:F2},{7:F2}",
            sample.Timestamp.ToString("o"),
            sample.LatitudeDeg,
            sample.LongitudeDeg,
            sample.SpeedMps,
            sample.NumSv,
            sample.FixType,
            latitudeMeters,
            longitudeMeters));
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}
