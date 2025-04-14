namespace PgdGeImageConverter.Core;

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;

public static class CompressTester
{
    public static void Main(string[] args)
    {
        // --- Test Cases ---
        TestCompression("Short string with no repeats.");
        TestCompression("ababababababababababababababab");
        TestCompression("abcabcabcabcabcabcabcabcabcabc");
        TestCompression("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        TestCompression(GenerateTestData(1024, 5)); // Some repeating data
        TestCompression(GenerateTestData(2048, 1)); // Highly repetitive
        TestCompression(GenerateTestData(5000, 50)); // Less repetitive
        TestCompression(""); // Empty Input
        TestCompression("a"); // Single byte
        TestCompression("abc"); // Less than min match length

        // Test case matching max length possibilities
        var prefix = "0123456789ABCDEF"; // 16 bytes
        var longRepeat = string.Concat(Enumerable.Repeat(prefix, MaxLength / prefix.Length + 2));
        TestCompression("START" + longRepeat + "END");

        // Test case with offset near max
        var block = "XYZ";
        var dataNearMaxOffset = string.Concat(Enumerable.Repeat("A", 4090)) + block + "FILLER" + block;
        TestCompression(dataNearMaxOffset);

         // Test case requiring the long length encoding (> 3 bits for length)
        var baseStr = "1234567890"; // 10 chars
        var longMatchData = baseStr + string.Concat(Enumerable.Repeat(" ", 20)) + baseStr; // Match length 10
        TestCompression(longMatchData);

        Console.WriteLine("\nAll tests completed.");
    }

    // Helper to generate test data
    private static string GenerateTestData(int length, int variety)
    {
        var rand = new Random(123); // Seeded for predictable tests
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append((char)('A' + rand.Next(variety)));
        }
        return sb.ToString();
    }

    // Actual compression/decompression test function
    private static void TestCompression(string originalString)
    {
        Console.WriteLine($"--- Testing: \"{originalString.Substring(0, Math.Min(originalString.Length, 50))}...\" (Length: {originalString.Length}) ---");
        var originalData = Encoding.UTF8.GetBytes(originalString);

        var compressor = new Compressor();
        byte[]? compressedData;
        var swCompress = Stopwatch.StartNew();
        try
        {
            compressedData = compressor.Compress(originalData);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Compression FAILED: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine(ex.StackTrace);
            return; // Skip decompression if compression failed
        }
        swCompress.Stop();

        var ratio = originalData.Length == 0 ? 1 : (double)compressedData.Length / originalData.Length;
        Console.WriteLine($"  Original Size: {originalData.Length} bytes");
        Console.WriteLine($"  Compressed Size: {compressedData.Length} bytes (Ratio: {ratio:P2})");
        Console.WriteLine($"  Compression Time: {swCompress.ElapsedMilliseconds} ms");
        // Optional: Print compressed bytes (can be long)
        // Console.WriteLine($"  Compressed Hex: {BitConverter.ToString(compressedData).Replace("-", "")}");


        var decompressor = new Decompressor(compressedData, originalData.Length);
        byte[]? decompressedData;
        var swDecompress = Stopwatch.StartNew();
        try
        {
            decompressedData = decompressor.UnpackGePre();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Decompression FAILED: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine(ex.StackTrace);
            Console.WriteLine($"  Original Size: {originalData.Length}, Compressed Size: {compressedData.Length}");
            Console.WriteLine($"  Compressed Hex: {BitConverter.ToString(compressedData).Replace("-", "")}");
            return;
        }
        swDecompress.Stop();
        Console.WriteLine($"  Decompression Time: {swDecompress.ElapsedMilliseconds} ms");

        // Verification
        var success = originalData.SequenceEqual(decompressedData);
        if (success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Result: SUCCESS - Decompressed data matches original.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Result: FAILED - Decompressed data DOES NOT match original.");
            Console.ResetColor();
            // Optional: Print details on failure
             Console.WriteLine($"  Original (UTF8): '{Encoding.UTF8.GetString(originalData)}'");
             Console.WriteLine($"  Decompressed (UTF8): '{Encoding.UTF8.GetString(decompressedData)}'");
             Console.WriteLine($"  Original Hex: {BitConverter.ToString(originalData).Replace("-", "")}");
             Console.WriteLine($"  Decompressed Hex: {BitConverter.ToString(decompressedData).Replace("-", "")}");
        }
        Console.WriteLine("---------------------------------------\n");
    }

    // Constants needed by the compressor within the test setup
    private const int MaxLength = 2051;
}