namespace PgdGeImageConverter.Core;

public class Compressor : IDisposable, IAsyncDisposable
{
    private const int MinMatchLength = 4;
    private const int MaxMatchLength = 2051; // (7 << 8 | 255) + 4
    private const int MaxOffset = 4095; // (1 << 12) - 1
    private byte[] _inputData = null!;
    private MemoryStream _outputStream = null!;
    private BinaryWriter _writer = null!; // For easy writing of bytes and ushorts

    // Buffering for control bits and corresponding data blocks
    private byte _controlByte;
    private int _controlBitIndex;
    private List<Action> _pendingWriteActions = null!; // Stores actions to write data blocks

    public byte[] Compress(byte[] inputData)
    {
        _inputData = inputData;
        if (_inputData.Length == 0)
        {
            return [];
        }

        // Use BinaryWriter for convenient Little-Endian writing by default
        _controlByte = 0;
        _controlBitIndex = 0;
        _outputStream = new MemoryStream();
        _writer = new BinaryWriter(_outputStream);
        _pendingWriteActions = [];
        var src = 0; // Current position in inputData
        
        // No compression
        // int wrote = 0, ctlR = 7;
        // while (wrote < inputData.Length)
        // {
        //     ctlR = (ctlR + 1) % 8;
        //     if (ctlR == 0)
        //         _outputStream.WriteByte(0);
        //     var thisWrite = Math.Min(255, inputData.Length - wrote);
        //     _outputStream.WriteByte((byte)thisWrite);
        //     _outputStream.Write(_inputData, wrote, thisWrite);
        //     wrote += thisWrite;
        // }
        //
        // return _outputStream.ToArray();
        
        while (src < _inputData.Length)
        {
            // Find the longest match in the preceding window
            FindBestMatch(src, out var bestMatchLength, out var bestMatchOffset);

            // Decide whether to encode as a copy or literal(s)
            // Encode as copy if match is long enough (>= MinMatchLength)
            if (bestMatchLength >= MinMatchLength)
            {
                // Encode as copy (back-reference)
                EncodeCopy(bestMatchLength, bestMatchOffset);
                src += bestMatchLength;
            }
            else
            {
                // Encode as literal run
                // Find how many literals we need to output (at least 1)
                // For simplicity here, we'll just output one literal.
                // A better implementation would group consecutive literals.
                // Let's improve this: Group literals until the next potential match >= MinMatchLength
                // or until we hit the max literal run length (255) or end of input.
                var literalRunLength = 0;
                var searchPos = src;
                while (literalRunLength < 255 && searchPos < _inputData.Length)
                {
                    // Check if a good match starts at the *next* position
                    FindBestMatch(searchPos, out var nextMatchLen, out var _);
                    if (nextMatchLen >= MinMatchLength &&
                        literalRunLength >
                        0) // Don't start a literal run if a match is immediately available unless we already started one
                    {
                        // A good match starts right after the current literal run. Stop the run here.
                        break;
                    }

                    literalRunLength++;
                    searchPos++;

                    // Optimization: If the next potential match is good, break early to encode it.
                    // This check ensures we don't create suboptimal runs like Lit(A) Lit(B) Lit(C) when Copy(ABC) was possible starting at B.
                    // We re-evaluate the match at each step. This is slightly less optimal than a full dynamic programming approach
                    // but much better than just encoding one literal at a time.
                }

                // Ensure we don't create a zero-length literal run by mistake if the loop condition caused an early exit.
                if (literalRunLength == 0)
                {
                    // This should only happen if src is already at the end, but check anyway.
                    if (src < _inputData.Length)
                    {
                        literalRunLength = 1; // Encode at least one if possible
                    }
                    else
                    {
                        break; // Reached end
                    }
                }

                EncodeLiterals(src, literalRunLength);
                src += literalRunLength;
            }
        }

        // Flush any remaining control bits and data
        FlushPendingWrites();
        return _outputStream.ToArray();
    }

    // Finds the longest match for data starting at 'src' within the allowed window
    private void FindBestMatch(int src, out int bestMatchLength, out int bestMatchOffset)
    {
        bestMatchLength = 0;
        bestMatchOffset = 0;

        // Define the search window [searchStart, src - 1]
        var searchStart = Math.Max(0, src - MaxOffset);
        var maxPossibleLength = Math.Min(MaxMatchLength, _inputData.Length - src);
        if (maxPossibleLength < MinMatchLength)
        {
            // Not enough remaining data for a valid match
            return;
        }

        // Iterate backwards through possible starting positions in the window
        for (var pos = src - 1; pos >= searchStart; pos--)
        {
            // Check if the first few bytes match (quick check)
            if (_inputData[pos] == _inputData[src] && (bestMatchLength < 1 ||
                                                       _inputData[pos + bestMatchLength] ==
                                                       _inputData
                                                           [src + bestMatchLength])) // Check if this pos can beat current best
            {
                var currentLength = 0;
                // Calculate match length
                while (currentLength < maxPossibleLength &&
                       _inputData[pos + currentLength] == _inputData[src + currentLength])
                {
                    currentLength++;
                }

                // If this match is longer than the best one found so far
                if (currentLength > bestMatchLength)
                {
                    bestMatchLength = currentLength;
                    bestMatchOffset = src - pos; // Calculate offset

                    // Optimization: If we found the maximum possible length, no need to search further back
                    if (bestMatchLength == maxPossibleLength)
                    {
                        break;
                    }
                }
            }
        }

        // Ensure match length doesn't exceed maximum encodable length
        if (bestMatchLength > MaxMatchLength)
        {
            bestMatchLength = MaxMatchLength;
            // Re-calculate offset if needed? No, offset is determined by position 'pos'.
        }

        // Final check: Only return matches meeting the minimum length requirement
        if (bestMatchLength < MinMatchLength)
        {
            bestMatchLength = 0;
            bestMatchOffset = 0;
        }
    }

    // Adds a control bit and schedules the write action
    private void AddBlock(bool isCopy, Action writeAction)
    {
        if (isCopy)
        {
            _controlByte |= (byte)(1 << _controlBitIndex); // Set bit for copy
        }
        // Else: Bit is already 0 for literal

        _controlBitIndex++;
        _pendingWriteActions.Add(writeAction);
        if (_controlBitIndex == 8)
        {
            FlushPendingWrites();
        }
    }

    // Writes the buffered control byte and executes pending data writes
    private void FlushPendingWrites()
    {
        if (_controlBitIndex > 0)
        {
            _writer.Write(_controlByte);
            foreach (var action in _pendingWriteActions)
            {
                action(); // Execute the write (e.g., _writer.Write(...) )
            }

            // Reset for the next batch
            _controlByte = 0;
            _controlBitIndex = 0;
            _pendingWriteActions.Clear();
        }

        // Ensure writer buffer is flushed to the underlying stream
        _writer.Flush();
    }

    // Encode a literal run block
    private void EncodeLiterals(int startPos, int length)
    {
        // Ensure length is valid (decompressor reads count as byte)
        if (length is <= 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Literal run length must be between 1 and 255.");
        }

        // Control bit is 0 (default)
        AddBlock(false, () =>
        {
            _writer.Write((byte)length); // Write count
            _writer.Write(_inputData, startPos, length); // Write literal bytes
        });
    }

    // Encode a copy block (back-reference)
    private void EncodeCopy(int length, int offset)
    {
        if (length < MinMatchLength || length > MaxMatchLength) throw new ArgumentOutOfRangeException(nameof(length));
        if (offset <= 0 || offset > MaxOffset) throw new ArgumentOutOfRangeException(nameof(offset));

        // Control bit is 1
        var count = length - 4; // Decompressor adds 4 back
        if (count <= 7) // Short copy (length 4-11)
        {
            // Format: (Offset << 4) | 8 | Count
            var encoded = (ushort)((offset << 4) | 0x08 | count);
            AddBlock(true, () => _writer.Write(encoded)); // Write 2 bytes
        }
        else // Long copy (length >= 12)
        {
            // Format: ushort = (Offset << 4) | (Count >> 8)
            //         byte   = Count & 0xFF
            var encodedHigh =
                (ushort)((offset << 4) | (count >> 8)); // Top 12 bits offset, 0 flag, high 3 bits count
            var encodedLow = (byte)(count & 0xFF); // Low 8 bits count
            AddBlock(true, () =>
            {
                _writer.Write(encodedHigh); // Write 2 bytes
                _writer.Write(encodedLow); // Write 1 byte
            });
        }
    }

    public void Dispose()
    {
        _outputStream.Dispose();
        _writer.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await _outputStream.DisposeAsync();
        await _writer.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}