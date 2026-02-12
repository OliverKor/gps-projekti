using System.Globalization;

namespace Gps.Core;

public sealed class FixCsvWriter : IDisposable
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly StreamWriter _writer;
    private readonly LocalMetersProjector _projector;

    public FixCsvWriter(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
        _projector = new LocalMetersProjector();

        if (writeHeader)
        {
            _writer.WriteLine("timestamp,lat,lon,speed_mps,num_sv,fix_type,lat_m,lon_m");
        }
    }

    public void Write(Fix fix)
    {
        ArgumentNullException.ThrowIfNull(fix);

        var projected = _projector.Project(fix.LatitudeDeg, fix.LongitudeDeg);
        var latitudeMeters = fix.LatitudeMeters ?? projected.LatitudeMeters;
        var longitudeMeters = fix.LongitudeMeters ?? projected.LongitudeMeters;

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
