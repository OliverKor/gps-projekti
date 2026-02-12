using System.Buffers.Binary;

namespace Gps.Core.Tests;

public class UbxNavPvtStreamDecoderTests
{
    [Fact]
    public void TryAppend_EmitsFixForValidFrame()
    {
        var decoder = new Gps.Core.UbxNavPvtStreamDecoder();
        var output = new List<Gps.Core.Fix>();
        var frame = CreateNavPvtFrame(
            second: 6,
            latitudeDeg: 62.7905840,
            longitudeDeg: 22.8185170,
            speedMps: 0.05,
            numSv: 6,
            fixType: 3);

        var emitted = decoder.TryAppend(frame, output);

        Assert.True(emitted);
        Assert.Single(output);
        Assert.Equal(new DateTimeOffset(2026, 2, 4, 14, 15, 6, TimeSpan.Zero), output[0].Timestamp);
        Assert.Equal(62.7905840, output[0].LatitudeDeg, 7);
        Assert.Equal(22.8185170, output[0].LongitudeDeg, 7);
        Assert.NotNull(output[0].SpeedMps);
        Assert.Equal(0.05, output[0].SpeedMps!.Value, 2);
        Assert.Equal(6, output[0].NumSv);
        Assert.Equal("3D", output[0].FixType);
        Assert.NotNull(output[0].LatitudeMeters);
        Assert.NotNull(output[0].LongitudeMeters);
        Assert.Equal(0.0, output[0].LatitudeMeters!.Value, 2);
        Assert.Equal(0.0, output[0].LongitudeMeters!.Value, 2);
    }

    [Fact]
    public void TryAppend_SkipsInvalidValidityFlags()
    {
        var decoder = new Gps.Core.UbxNavPvtStreamDecoder();
        var output = new List<Gps.Core.Fix>();
        var frame = CreateNavPvtFrame(validFlags: 0x03);

        var emitted = decoder.TryAppend(frame, output);

        Assert.False(emitted);
        Assert.Empty(output);
    }

    [Fact]
    public void TryAppend_SkipsFrameWithoutGnssFixFlag()
    {
        var decoder = new Gps.Core.UbxNavPvtStreamDecoder();
        var output = new List<Gps.Core.Fix>();
        var frame = CreateNavPvtFrame(fixStatusFlags: 0x00);

        var emitted = decoder.TryAppend(frame, output);

        Assert.False(emitted);
        Assert.Empty(output);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)5)]
    public void TryAppend_SkipsNonPositionalFixTypes(byte fixType)
    {
        var decoder = new Gps.Core.UbxNavPvtStreamDecoder();
        var output = new List<Gps.Core.Fix>();
        var frame = CreateNavPvtFrame(fixType: fixType);

        var emitted = decoder.TryAppend(frame, output);

        Assert.False(emitted);
        Assert.Empty(output);
    }

    [Fact]
    public void TryAppend_RecoversAfterChecksumFailure()
    {
        var decoder = new Gps.Core.UbxNavPvtStreamDecoder();
        var output = new List<Gps.Core.Fix>();
        var corrupted = CreateNavPvtFrame(second: 7);
        corrupted[^1] ^= 0xFF;
        var valid = CreateNavPvtFrame(second: 8);
        var stream = corrupted.Concat(valid).ToArray();

        var emitted = decoder.TryAppend(stream, output);

        Assert.True(emitted);
        Assert.Single(output);
        Assert.Equal(new DateTimeOffset(2026, 2, 4, 14, 15, 8, TimeSpan.Zero), output[0].Timestamp);
    }

    [Fact]
    public void TryAppend_HandlesNoiseBetweenFrames()
    {
        var decoder = new Gps.Core.UbxNavPvtStreamDecoder();
        var output = new List<Gps.Core.Fix>();
        var frame1 = CreateNavPvtFrame(second: 9, latitudeDeg: 62.7905840, longitudeDeg: 22.8185170);
        var frame2 = CreateNavPvtFrame(second: 10, latitudeDeg: 62.7905900, longitudeDeg: 22.8185200);
        var stream = new byte[] { 0x11, 0x22, 0x33, 0x44 }
            .Concat(frame1)
            .Concat(new byte[] { 0x4E, 0x4D, 0x45, 0x41, 0x0D, 0x0A })
            .Concat(frame2)
            .ToArray();

        var emitted = decoder.TryAppend(stream, output);

        Assert.True(emitted);
        Assert.Equal(2, output.Count);
        Assert.Equal(new DateTimeOffset(2026, 2, 4, 14, 15, 9, TimeSpan.Zero), output[0].Timestamp);
        Assert.Equal(new DateTimeOffset(2026, 2, 4, 14, 15, 10, TimeSpan.Zero), output[1].Timestamp);
        Assert.NotNull(output[1].LatitudeMeters);
        Assert.True(output[1].LatitudeMeters > 0);
    }

    [Fact]
    public void TryAppend_DeduplicatesSameTimestamp()
    {
        var decoder = new Gps.Core.UbxNavPvtStreamDecoder();
        var output = new List<Gps.Core.Fix>();
        var frame1 = CreateNavPvtFrame(second: 11, latitudeDeg: 62.7905840, longitudeDeg: 22.8185170);
        var frame2 = CreateNavPvtFrame(second: 11, latitudeDeg: 62.7905900, longitudeDeg: 22.8185200);
        var stream = frame1.Concat(frame2).ToArray();

        var emitted = decoder.TryAppend(stream, output);

        Assert.True(emitted);
        Assert.Single(output);
        Assert.Equal(62.7905840, output[0].LatitudeDeg, 7);
    }

    [Fact]
    public void TryAppend_HandlesFrameSplitAcrossAppends()
    {
        var decoder = new Gps.Core.UbxNavPvtStreamDecoder();
        var output = new List<Gps.Core.Fix>();
        var frame = CreateNavPvtFrame(second: 12);

        var emittedFirstChunk = decoder.TryAppend(frame.AsSpan(0, 18), output);
        var emittedSecondChunk = decoder.TryAppend(frame.AsSpan(18), output);

        Assert.False(emittedFirstChunk);
        Assert.True(emittedSecondChunk);
        Assert.Single(output);
        Assert.Equal(new DateTimeOffset(2026, 2, 4, 14, 15, 12, TimeSpan.Zero), output[0].Timestamp);
    }

    private static byte[] CreateNavPvtFrame(
        int year = 2026,
        int month = 2,
        int day = 4,
        int hour = 14,
        int minute = 15,
        int second = 6,
        byte validFlags = 0x07,
        byte fixStatusFlags = 0x01,
        double latitudeDeg = 62.7905840,
        double longitudeDeg = 22.8185170,
        double speedMps = 0.05,
        byte numSv = 6,
        byte fixType = 3)
    {
        var payload = new byte[92];

        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), (ushort)year);
        payload[6] = (byte)month;
        payload[7] = (byte)day;
        payload[8] = (byte)hour;
        payload[9] = (byte)minute;
        payload[10] = (byte)second;
        payload[11] = validFlags;
        payload[20] = fixType;
        payload[21] = fixStatusFlags;
        payload[23] = numSv;

        var longitudeRaw = (int)Math.Round(longitudeDeg * 1e7);
        var latitudeRaw = (int)Math.Round(latitudeDeg * 1e7);
        var speedRaw = (int)Math.Round(speedMps * 1000.0);

        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(24, 4), longitudeRaw);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(28, 4), latitudeRaw);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(60, 4), speedRaw);

        return WrapUbxFrame(0x01, 0x07, payload);
    }

    private static byte[] WrapUbxFrame(byte cls, byte id, byte[] payload)
    {
        var frame = new byte[6 + payload.Length + 2];
        frame[0] = 0xB5;
        frame[1] = 0x62;
        frame[2] = cls;
        frame[3] = id;
        frame[4] = (byte)(payload.Length & 0xFF);
        frame[5] = (byte)((payload.Length >> 8) & 0xFF);
        payload.CopyTo(frame, 6);

        byte ckA = 0;
        byte ckB = 0;
        for (var i = 2; i < 6 + payload.Length; i++)
        {
            ckA += frame[i];
            ckB += ckA;
        }

        frame[6 + payload.Length] = ckA;
        frame[7 + payload.Length] = ckB;
        return frame;
    }
}
