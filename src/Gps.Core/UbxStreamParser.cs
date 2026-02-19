namespace Gps.Core;

internal sealed class UbxStreamParser
{
    private const byte SyncA = 0xB5;
    private const byte SyncB = 0x62;
    private const int HeaderLength = 6;
    private const int ChecksumLength = 2;
    private const int MaxPayloadLength = 4096;

    private readonly ByteRingBuffer _buffer;

    public UbxStreamParser(int capacity)
    {
        _buffer = new ByteRingBuffer(capacity);
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        _buffer.Append(data);
    }

    public bool TryReadFrame(out UbxFrame frame)
    {
        while (true)
        {
            frame = default;

            if (_buffer.Count < 2)
            {
                return false;
            }

            var syncIndex = _buffer.FindSyncPair(SyncA, SyncB);
            if (syncIndex < 0)
            {
                _buffer.Consume(_buffer.Count - 1);
                return false;
            }

            if (syncIndex > 0)
            {
                _buffer.Consume(syncIndex);
                continue;
            }

            if (_buffer.Count < HeaderLength)
            {
                return false;
            }

            var cls = _buffer.PeekByte(2);
            var id = _buffer.PeekByte(3);
            var payloadLength = _buffer.PeekByte(4) | (_buffer.PeekByte(5) << 8);

            if (payloadLength > MaxPayloadLength)
            {
                _buffer.Consume(2);
                continue;
            }

            var frameLength = HeaderLength + payloadLength + ChecksumLength;
            if (_buffer.Count < frameLength)
            {
                return false;
            }

            var isValid = ValidateChecksum(payloadLength);
            if (!isValid)
            {
                _buffer.Consume(2);
                continue;
            }

            var payload = new byte[payloadLength];
            _buffer.ReadBytes(HeaderLength, payload);
            _buffer.Consume(frameLength);

            frame = new UbxFrame(cls, id, payload);
            return true;
        }
    }

    private bool ValidateChecksum(int payloadLength)
    {
        byte ckA = 0;
        byte ckB = 0;

        for (var i = 2; i < HeaderLength + payloadLength; i++)
        {
            var value = _buffer.PeekByte(i);
            ckA += value;
            ckB += ckA;
        }

        var expectedCkA = _buffer.PeekByte(HeaderLength + payloadLength);
        var expectedCkB = _buffer.PeekByte(HeaderLength + payloadLength + 1);

        return ckA == expectedCkA && ckB == expectedCkB;
    }
}
