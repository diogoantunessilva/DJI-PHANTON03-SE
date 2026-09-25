namespace Phanton3.TelemetryProbe;

internal sealed class DumlStreamParser
{
    private byte[] _buffer = new byte[2048];
    private int _count;

    public int PendingByteCount => _count;

    public DumlParseBatch Feed(ReadOnlySpan<byte> bytes)
    {
        EnsureCapacity(_count + bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_count));
        _count += bytes.Length;

        var frames = new List<DumlFrame>();
        var discardedBytes = 0;
        var invalidLengths = 0;
        var offset = 0;

        while (_count - offset >= 3)
        {
            if (_buffer[offset] != 0x55)
            {
                offset++;
                discardedBytes++;
                continue;
            }

            var totalLength = _buffer[offset + 1] | ((_buffer[offset + 2] & 0x03) << 8);
            if (totalLength < DumlFrame.MinimumLength)
            {
                offset++;
                discardedBytes++;
                invalidLengths++;
                continue;
            }

            if (_count - offset < totalLength)
            {
                break;
            }

            frames.Add(DumlFrame.Parse(_buffer.AsSpan(offset, totalLength).ToArray()));
            offset += totalLength;
        }

        if (offset > 0)
        {
            Buffer.BlockCopy(_buffer, offset, _buffer, 0, _count - offset);
            _count -= offset;
        }

        return new DumlParseBatch(frames, discardedBytes, invalidLengths);
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _buffer.Length)
        {
            return;
        }

        Array.Resize(ref _buffer, Math.Max(required, _buffer.Length * 2));
    }
}

internal sealed record DumlParseBatch(
    IReadOnlyList<DumlFrame> Frames,
    int DiscardedBytes,
    int InvalidLengths);
