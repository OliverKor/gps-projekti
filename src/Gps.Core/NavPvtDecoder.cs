using System.Buffers.Binary;

namespace Gps.Core;

internal static class NavPvtDecoder
{
    private const byte NavClass = 0x01;
    private const byte NavPvtId = 0x07;
    private const int NavPvtPayloadLength = 92;

    public static bool TryDecode(UbxFrame frame, out Fix fix)
    {
        fix = default!;

        if (frame.Class != NavClass || frame.Id != NavPvtId || frame.Payload.Length != NavPvtPayloadLength)
        {
            return false;
        }

        var payload = frame.Payload.Span;

        var year = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
        var month = payload[6];
        var day = payload[7];
        var hour = payload[8];
        var minute = payload[9];
        var second = payload[10];

        var validFlags = payload[11];
        var validDate = (validFlags & 0x01) != 0;
        var validTime = (validFlags & 0x02) != 0;
        var fullyResolved = (validFlags & 0x04) != 0;

        if (!validDate || !validTime || !fullyResolved)
        {
            return false;
        }

        DateTimeOffset timestamp;
        try
        {
            var utc = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
            timestamp = new DateTimeOffset(utc, TimeSpan.Zero);
        }
        catch
        {
            return false;
        }

        var fixType = payload[20] switch
        {
            0 => "NoFix",
            1 => "DR",
            2 => "2D",
            3 => "3D",
            4 => "GNSS+DR",
            5 => "TimeOnly",
            _ => "Unknown"
        };

        var numSv = payload[23];
        var longitudeDeg = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(24, 4)) / 1e7;
        var latitudeDeg = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(28, 4)) / 1e7;
        var speedMps = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(60, 4)) / 1000.0;

        fix = new Fix(timestamp, latitudeDeg, longitudeDeg, speedMps, numSv, fixType);
        return true;
    }
}
