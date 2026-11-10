namespace SoundModem.Audio;

internal sealed class ByteRingBuffer
{
    private readonly byte[] _buffer;
    private readonly int _mask;
    private int _readIndex;
    private int _writeIndex;

    public ByteRingBuffer(int capacity)
    {
        if (capacity < 2 || (capacity & (capacity - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be a power of two.");
        }

        _buffer = new byte[capacity];
        _mask = capacity - 1;
    }

    public bool TryWrite(byte value)
    {
        var writeIndex = _writeIndex;
        if (writeIndex - Volatile.Read(ref _readIndex) >= _buffer.Length)
        {
            return false;
        }

        _buffer[writeIndex & _mask] = value;
        Volatile.Write(ref _writeIndex, writeIndex + 1);
        return true;
    }

    public bool TryRead(out byte value)
    {
        var readIndex = _readIndex;
        if (readIndex == Volatile.Read(ref _writeIndex))
        {
            value = 0;
            return false;
        }

        value = _buffer[readIndex & _mask];
        Volatile.Write(ref _readIndex, readIndex + 1);
        return true;
    }
}