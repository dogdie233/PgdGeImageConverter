namespace PgdGeImageConverter.Core;

public class Decompressor
{
    private readonly Stream _input;
    private readonly byte[] _output;

    // Constructor for using a Stream
    public Decompressor(Stream input, int outputLength)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = new byte[outputLength];
    }

    // Constructor for using a byte array (easier for testing)
    public Decompressor(byte[] inputData, int outputLength)
    {
        // Wrap byte[] in MemoryStream for consistent ReadByte/ReadUInt16
        _input = new MemoryStream(inputData ?? throw new ArgumentNullException(nameof(inputData)));
        _output = new byte[outputLength];
    }


    // Helper to read single byte from stream or array
    private int ReadByte()
    {
        var byteRead = _input.ReadByte();
        if (byteRead == -1)
        {
            throw new EndOfStreamException("Unexpected end of input stream during decompression.");
        }
        return byteRead;
    }

    // Helper to read UInt16 (little-endian assumed)
    private ushort ReadUInt16()
    {
        var b1 = ReadByte();
        var b2 = ReadByte();
        return (ushort)(b1 | (b2 << 8));
    }

    // Helper for overlapping copy (critical for LZ variants)
    private static void CopyOverlapped(byte[] buffer, int srcOffset, int dstOffset, int count)
    {
        if (count <= 0) return;

        // Handle overlapping case: copy byte by byte if overlapping backwards
        if (dstOffset > srcOffset && dstOffset < srcOffset + count)
        {
            while (count-- > 0)
            {
                 if (dstOffset >= buffer.Length || srcOffset >= buffer.Length || dstOffset < 0 || srcOffset < 0)
                 {
                    throw new IndexOutOfRangeException($"CopyOverlapped out of bounds: dst={dstOffset}, src={srcOffset}, count={count+1}, buffer={buffer.Length}");
                 }
                 buffer[dstOffset++] = buffer[srcOffset++];
            }
        }
        else
        {
            // Non-overlapping or forward overlap: Buffer.BlockCopy is efficient
             if (dstOffset + count > buffer.Length || srcOffset + count > buffer.Length || dstOffset < 0 || srcOffset < 0)
             {
                 throw new IndexOutOfRangeException($"CopyOverlapped (BlockCopy) out of bounds: dst={dstOffset}, src={srcOffset}, count={count}, buffer={buffer.Length}");
             }
            Buffer.BlockCopy(buffer, srcOffset, buffer, dstOffset, count);
        }
    }

    // The original decompression logic
    public byte[] UnpackGePre ()
    {
        var dst = 0;
        var ctl = 2;
        while (dst < _output.Length)
        {
            ctl >>= 1;
            if (1 == ctl)
                ctl = _input.ReadByte() | 0x100;
            int count;
            if (0 != (ctl & 1))
            {
                int offset = ReadUInt16();
                count = offset & 7;
                if (0 == (offset & 8))
                {
                    count = count << 8 | _input.ReadByte();
                }
                count += 4;
                offset >>= 4;
                CopyOverlapped (_output, dst - offset, dst, count);
            }
            else
            {
                count = _input.ReadByte();
                _input.ReadExactly(_output, dst, count);
            }
            dst += count;
        }
        return _output;
    }
}