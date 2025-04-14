using System.Diagnostics.CodeAnalysis;

using PgdGeImageConverter.Core;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// CompressTester.Main([]);
// Environment.Exit(0);

if (args.Length == 0)
    args = ["D:\\UncensoredTest\\Playground\\未命名.png"];

if (args.Length != 1)
    ErrorQuit("把文件拖到我身上，而不是直接点开，知道了吗");

var filePath = args[0];
var file = new FileInfo(filePath);
if (!file.Exists)
    ErrorQuit("文件不存在");

Console.WriteLine($"正在读取文件{file.FullName}...");
var image = await Image.LoadAsync(file.FullName);
var bgr24 = image as Image<Bgr24> ?? image.CloneAs<Bgr24>();
Console.WriteLine($"图片分辨率：{bgr24.Width}x{bgr24.Height}");
Console.WriteLine("正在编码...");
var encoder = new Encoder(bgr24);
var encoded = encoder.Encode();
Console.WriteLine($"编码后长度：{encoded.Length}字节");
Console.WriteLine("正在压缩...");
var compressor = new Compressor();
var compressed = compressor.Compress(encoded);
var outputFile = new FileInfo(Path.GetFileNameWithoutExtension(file.Name) + ".pgd");
Console.WriteLine($"正在写入文件{outputFile.FullName}...");
await using (var fs = new FileStream(outputFile.FullName, FileMode.Create, FileAccess.Write))
await using (var bw = new BinaryWriter(fs))
{
    bw.Write(0x00204547);  // magic number
    bw.Write(0x00);  // offsetX int32
    bw.Write(0x00);  // offsetY int32
    bw.Write(bgr24.Width);  // width int32
    bw.Write(bgr24.Height);  // height int32
    bw.Write(bgr24.Width);  // width int32
    bw.Write(bgr24.Height);  // height int32
    bw.Write(3);  // encode method

    bw.Write(encoded.Length);  // unpacked size
    bw.Write(compressed.Length);  // packed size
    bw.Write(compressed);  // packed data();
}

Console.WriteLine($"文件写入完成，文件大小：{outputFile.Length}字节");
Console.WriteLine("可以关闭程序了...");
Console.ReadLine();
return;

[DoesNotReturn]
void ErrorQuit(string msg)
{
    Console.WriteLine($"错误：{msg}");
    Console.ReadLine();
    Environment.Exit(1);
}