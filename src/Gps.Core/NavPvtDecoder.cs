using System.Buffers.Binary;

namespace Gps.Core;

internal static class NavPvtDecoder
{
    private const byte NavClass = 0x01;
    private const byte NavPvtId = 0x07;
    private const int NavPvtPayloadLength = 92;
    private const byte ValidDateFlag = 0x01;
    private const byte ValidTimeFlag = 0x02;
    private const byte FullyResolvedFlag = 0x04;
    private const byte GnssFixOkFlag = 0x01;

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
        var validDate = (validFlags & ValidDateFlag) != 0;
        var validTime = (validFlags & ValidTimeFlag) != 0;
        var fullyResolved = (validFlags & FullyResolvedFlag) != 0;

        if (!validDate || !validTime || !fullyResolved)
        {
            return false;
        }

        var fixTypeValue = payload[20];
        var navigationStatusFlags = payload[21];
        var hasGnssFix = (navigationStatusFlags & GnssFixOkFlag) != 0;
        var hasPositionFixType = fixTypeValue is 2 or 3 or 4;

        if (!hasGnssFix || !hasPositionFixType)
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

        var fixType = fixTypeValue switch
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
