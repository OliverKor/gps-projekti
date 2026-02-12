namespace Gps.Core;

public sealed class UbxNavPvtStreamDecoder
{
    private readonly UbxStreamParser _parser;
    private readonly LocalMetersProjector _projector;
    private DateTimeOffset? _lastTimestamp;

    public UbxNavPvtStreamDecoder(int capacity = 8192)
    {
        _parser = new UbxStreamParser(capacity);
        _projector = new LocalMetersProjector();
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
            var (latitudeMeters, longitudeMeters) = _projector.Project(fix.LatitudeDeg, fix.LongitudeDeg);

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
