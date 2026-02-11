namespace Gps.Core;

public sealed class UbxNavPvtStreamDecoder
{
    private const double MetersPerLatitudeDegree = 111_132.92;
    private const double MetersPerLongitudeDegreeAtEquator = 111_320.0;

    private readonly UbxStreamParser _parser;
    private DateTimeOffset? _lastTimestamp;
    private double? _originLatitudeDeg;
    private double? _originLongitudeDeg;

    public UbxNavPvtStreamDecoder(int capacity = 8192)
    {
        _parser = new UbxStreamParser(capacity);
    }

    public bool TryAppend(ReadOnlySpan<byte> bytes, ICollection<Fix> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (bytes.Length == 0)
        {
            return false;
        }

        _parser.Append(bytes);

        var emitted = false;
        while (_parser.TryReadFrame(out var frame))
        {
            if (!NavPvtDecoder.TryDecode(frame, out var fix))
            {
                continue;
            }

            if (_lastTimestamp == fix.Timestamp)
            {
                continue;
            }

            _lastTimestamp = fix.Timestamp;
            _originLatitudeDeg ??= fix.LatitudeDeg;
            _originLongitudeDeg ??= fix.LongitudeDeg;

            var latitudeMeters = (fix.LatitudeDeg - _originLatitudeDeg.Value) * MetersPerLatitudeDegree;
            var cosLat0 = Math.Cos(_originLatitudeDeg.Value * (Math.PI / 180.0));
            var longitudeMeters = (fix.LongitudeDeg - _originLongitudeDeg.Value) * MetersPerLongitudeDegreeAtEquator * cosLat0;

            output.Add(fix with
            {
                LatitudeMeters = latitudeMeters,
                LongitudeMeters = longitudeMeters
            });

            emitted = true;
        }

        return emitted;
    }
}
