namespace Gps.Cli;

internal sealed class ByteRingBuffer
{
    private readonly byte[] _buffer;
    private int _start;
    private int _count;

    public ByteRingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _buffer = new byte[capacity];
    }

    public int Count => _count;

    public void Append(byte[] data, int length)
    {
        if (length <= 0)
        {
            return;
        }

        if (length > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length >= _buffer.Length)
        {
            Clear();
            WriteSlice(data.AsSpan(length - _buffer.Length, _buffer.Length));
            return;
        }

        var freeSpace = _buffer.Length - _count;
        if (length > freeSpace)
        {
            Consume(length - freeSpace);
        }

        WriteSlice(data.AsSpan(0, length));
    }

    public int FindSyncPair(byte first, byte second)
    {
        for (var i = 0; i < _count - 1; i++)
        {
            if (PeekByte(i) == first && PeekByte(i + 1) == second)
            {
                return i;
            }
        }

        return -1;
    }

    public byte PeekByte(int index)
    {
        if (index < 0 || index >= _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var position = (_start + index) % _buffer.Length;
        return _buffer[position];
    }

    public void ReadBytes(int index, Span<byte> destination)
    {
        if (index < 0 || index + destination.Length > _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = PeekByte(index + i);
        }
    }

    public void Consume(int count)
    {
        if (count <= 0)
        {
            return;
        }

        var clamped = Math.Min(count, _count);
        _start = (_start + clamped) % _buffer.Length;
        _count -= clamped;
    }

    public void Clear()
    {
        _start = 0;
        _count = 0;
    }

    private void WriteSlice(ReadOnlySpan<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            var position = (_start + _count) % _buffer.Length;
            _buffer[position] = data[i];
            _count++;
        }
    }
}
