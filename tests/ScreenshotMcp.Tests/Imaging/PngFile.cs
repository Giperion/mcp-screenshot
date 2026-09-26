using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ScreenshotMcp.Imaging;
using StbImageSharp;

namespace ScreenshotMcp.Tests.Imaging;

internal sealed record PngChunk(string Type, byte[] Data, uint Crc);

/// <summary>Reads PNGs with code that shares nothing with the encoder: StbImageSharp decodes the pixels.</summary>
internal static class PngFile
{
    public static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Decodes <paramref name="png"/> and checks that its RGB pixels equal <paramref name="expected"/>.</summary>
    public static void AssertDecodesTo(BgraImage expected, byte[] png)
    {
        var decoded = ImageResult.FromMemory(png, ColorComponents.RedGreenBlue);
        Assert.Equal(ColorComponents.RedGreenBlue, decoded.SourceComp);
        Assert.Equal(expected.Width, decoded.Width);
        Assert.Equal(expected.Height, decoded.Height);

        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var i = (y * expected.Width + x) * 3;
                var actual = new Rgb(decoded.Data[i], decoded.Data[i + 1], decoded.Data[i + 2]);
                var wanted = expected.GetPixel(x, y);
                if (actual != wanted)
                {
                    Assert.Fail($"Pixel ({x}, {y}) decoded as {actual}, expected {wanted}.");
                }
            }
        }
    }

    public static List<PngChunk> ReadChunks(byte[] png)
    {
        Assert.True(png.AsSpan().StartsWith(Signature), "PNG signature is missing.");
        var chunks = new List<PngChunk>();
        var offset = Signature.Length;
        while (offset < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length).ToArray();
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length));
            chunks.Add(new PngChunk(type, data, crc));
            offset += 12 + length;
        }

        Assert.Equal(png.Length, offset);
        return chunks;
    }

    /// <summary>The filter type byte at the start of every scanline.</summary>
    public static byte[] ReadFilterTypes(byte[] png)
    {
        var chunks = ReadChunks(png);
        var ihdr = chunks[0].Data;
        var width = BinaryPrimitives.ReadInt32BigEndian(ihdr);
        var height = BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(4));

        using var compressed = new MemoryStream(chunks.Where(c => c.Type == "IDAT").SelectMany(c => c.Data).ToArray());
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var scanlines = new MemoryStream();
        zlib.CopyTo(scanlines);

        var stride = 1 + width * 3;
        Assert.Equal(stride * height, scanlines.Length);
        var bytes = scanlines.ToArray();
        return Enumerable.Range(0, height).Select(y => bytes[y * stride]).ToArray();
    }
}
