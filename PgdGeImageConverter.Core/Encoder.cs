using System.Buffers.Binary;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PgdGeImageConverter.Core;

public class Encoder(Image<Bgr24> image)
{
    private readonly int _width = image.Width;
    private readonly int _height = image.Height;
    private readonly int _pixelSize = 3;
    private readonly int _stride = image.Width * 3;

    public byte[] Encode()
    {
        var rawPixels = new byte[_stride * _height];
        image.CopyPixelDataTo(rawPixels);
        var output = new byte[8 + _height + rawPixels.Length];  // 头 + 控制字节 + 差分数据
        var outputSpan = output.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(outputSpan[2..], (ushort)(_pixelSize * 8));  // bpp
        BinaryPrimitives.WriteUInt16LittleEndian(outputSpan[4..], (ushort)_width);  // width
        BinaryPrimitives.WriteUInt16LittleEndian(outputSpan[6..], (ushort)_height);  // height
        EncodePal(rawPixels, outputSpan[8..]);
        return output;
    }

    private void EncodePal(ReadOnlySpan<byte> rawPixels, Span<byte> output)
    {
        var stride = _width * _pixelSize;
        
        for (var row = 0; row < _height; row++)
        {
            var rowOffset = row * stride;

            // 尝试三种预测模式，选择最优（最小差分绝对值之和）
            // 模式1：行内差分（前一个像素作为预测）
            var diffs1 = ComputeDiffsMode1(rawPixels, rowOffset);
            var sum1 = SumAbsoluteDiffs(diffs1.AsSpan()[1..]);
            var control = (byte)1;
            var bestDiffSum = sum1;
            var bestDiffs = diffs1;

            // 模式2：跨行参考（上一行同位置像素作为预测）
            if (row > 0)
            {
                var diffs2 = ComputeDiffsMode2(rawPixels, rowOffset);
                var sum2 = SumAbsoluteDiffs(diffs2);
                if (sum2 < bestDiffSum)
                {
                    control = 2;
                    bestDiffSum = sum2;
                    bestDiffs = diffs2;
                }
            }
            
            // 模式3：平均值混合（左+上像素的平均值作为预测）
            if (row > 0)
            {
                var diffs3 = ComputeDiffsMode3(rawPixels, rowOffset);
                var sum3 = SumAbsoluteDiffs(diffs3.AsSpan()[1..]);
                if (sum3 < bestDiffSum)
                {
                    control = 0; // 默认分支
                    bestDiffSum = sum3;
                    bestDiffs = diffs3;
                }
            }

            // 设置控制位
            output[row] = control;

            // 存储差分数据
            bestDiffs.CopyTo(output[(rowOffset + _height)..]);
        }
    }

    private byte[] ComputeDiffsMode1(ReadOnlySpan<byte> pixels, int rowOffset)
    {
        var diffs = new byte[_stride];
        var thisLine = pixels.Slice(rowOffset, _stride);
        pixels.Slice(rowOffset, _pixelSize).CopyTo(diffs);
        for (var i = _stride - 1; i >= _pixelSize; i--)
            diffs[i] = (byte)(thisLine[i - _pixelSize] - thisLine[i]);
        return diffs;
    }

    private byte[] ComputeDiffsMode2(ReadOnlySpan<byte> pixels, int rowOffset)
    {
        var diffs = new byte[_stride];
        var thisLine = pixels.Slice(rowOffset, _stride);
        var prevLine = pixels.Slice(rowOffset - _stride, _stride);
        for (var i = 0; i < _stride; i++)
            diffs[i] = (byte)(prevLine[i] - thisLine[i]);
        return diffs.ToArray();
    }

    private byte[] ComputeDiffsMode3(ReadOnlySpan<byte> pixels, int rowOffset)
    {
        var diffs = new byte[_stride];
        var prevLine = pixels.Slice(rowOffset - _stride, _stride);
        var thisLine = pixels.Slice(rowOffset, _stride);
        thisLine[.._pixelSize].CopyTo(diffs);

        // 第一个像素直接存储原始值（由调用者处理）
        for (var i = _stride - 1; i >= _pixelSize; i--)
        {
            int left = thisLine[i - _pixelSize];
            int top = prevLine[i];
            var predicted = (left + top) / 2; // 左和上像素的平均值
            diffs[i] = (byte)(pixels[i] - predicted);
        }

        return diffs;
    }
    
    private static int SumAbsoluteDiffs(ReadOnlySpan<byte> diffs)
    {
        // 计算差分绝对值之和
        // 注意：这里的差分值是有符号的，所以需要转换为 sbyte
        var sum = 0;
        foreach (var diff in diffs)
            sum += (sbyte)diff == sbyte.MinValue ? sbyte.MaxValue : Math.Abs((sbyte)diff);
        return sum;
    }
}