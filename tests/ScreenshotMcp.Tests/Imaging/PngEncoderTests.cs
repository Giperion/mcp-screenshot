using System.Buffers.Binary;
using ScreenshotMcp.Imaging;

namespace ScreenshotMcp.Tests.Imaging;

public sealed class PngEncoderTests
{
    [Fact]
    public void Encode_WritesSignatureAndWellFormedChunks()
    {
        var png = PngEncoder.Encode(TestImages.Gradient(7, 5));

        Assert.True(png.AsSpan().StartsWith(PngFile.Signature));
        var chunks = PngFile.ReadChunks(png);

        var ihdr = chunks[0];
        Assert.Equal("IHDR", ihdr.Type);
        Assert.Equal(13, ihdr.Data.Length);
        Assert.Equal(7, BinaryPrimitives.ReadInt32BigEndian(ihdr.Data));
        Assert.Equal(5, BinaryPrimitives.ReadInt32BigEndian(ihdr.Data.AsSpan(4)));
        Assert.Equal([8, 2, 0, 0, 0], ihdr.Data[8..]); // 8-bit, truecolor, deflate, adaptive, not interlaced

        Assert.All(chunks[1..^1], chunk => Assert.Equal("IDAT", chunk.Type));
        Assert.NotEmpty(chunks[1..^1]);
        Assert.Equal("IEND", chunks[^1].Type);
        Assert.Empty(chunks[^1].Data);

        foreach (var chunk in chunks)
        {
            var typeAndData = System.Text.Encoding.ASCII.GetBytes(chunk.Type).Concat(chunk.Data).ToArray();
            Assert.Equal(System.IO.Hashing.Crc32.HashToUInt32(typeAndData), chunk.Crc);
        }
    }

    [Fact]
    public void Encode_SinglePixel_RoundTrips()
    {
        var image = new BgraImage(1, 1);
        image.SetPixel(0, 0, new Rgb(10, 200, 30));

        PngFile.AssertDecodesTo(image, PngEncoder.Encode(image));
    }

    // Widths cover rows shorter than one 16-byte vector, exact multiples of it, and a scalar tail.
    [Theory]
    [InlineData(1, 7)]
    [InlineData(5, 1)]
    [InlineData(6, 3)]
    [InlineData(7, 5)]
    [InlineData(16, 4)]
    [InlineData(33, 9)]
    [InlineData(257, 31)]
    public void Encode_Noise_RoundTrips(int width, int height)
    {
        var image = TestImages.Noise(width, height, seed: width * 1000 + height);

        PngFile.AssertDecodesTo(image, PngEncoder.Encode(image));
    }

    [Fact]
    public void Encode_Gradient_RoundTrips()
    {
        var image = TestImages.Gradient(301, 203);

        PngFile.AssertDecodesTo(image, PngEncoder.Encode(image));
    }

    [Fact]
    public void Encode_UiLikeImage_RoundTrips()
    {
        var image = TestImages.UiLike(640, 480);

        PngFile.AssertDecodesTo(image, PngEncoder.Encode(image));
    }

    [Fact]
    public void Encode_PicksTheCheapestFilterForEachRow()
    {
        // Gray rows designed so that one filter clearly wins under the minimum-sum-of-absolute-
        // residuals heuristic. 33 pixels = 99 bytes: six 16-byte vectors plus a scalar tail.
        const int Width = 33;
        var rows = new int[5][];
        rows[0] = Row(x => 60 + 5 * x); // ramp: Sub leaves a constant 5
        rows[1] = Row(x => 60 + 5 * x); // same as above: Up leaves zeros
        rows[2] = Row(x => 55 + 5 * x); // shifted ramp: Paeth predicts the up-left pixel exactly
        rows[3] = new int[Width]; // Average of left and up, exactly
        for (var x = 0; x < Width; x++)
        {
            rows[3][x] = ((x > 0 ? rows[3][x - 1] : 0) + rows[2][x]) >> 1;
        }

        rows[4] = Row(x => (x % 3) switch { 0 => 0, 1 => 1, _ => 255 }); // tiny signed values: None

        var image = new BgraImage(Width, rows.Length);
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var v = (byte)rows[y][x];
                image.SetPixel(x, y, new Rgb(v, v, v));
            }
        }

        var png = PngEncoder.Encode(image);

        Assert.Equal([1, 2, 4, 3, 0], PngFile.ReadFilterTypes(png)); // Sub, Up, Paeth, Average, None
        PngFile.AssertDecodesTo(image, png);

        static int[] Row(Func<int, int> value) => Enumerable.Range(0, Width).Select(value).ToArray();
    }

    [Fact]
    public void Encode_UiLikeImage_IsMuchSmallerThanRawPixels()
    {
        var image = TestImages.UiLike(1280, 720);

        var png = PngEncoder.Encode(image);

        var raw = image.Width * image.Height * 3;
        Assert.True(png.Length < raw / 20, $"PNG is {png.Length} bytes, raw RGB is {raw} bytes.");
        Assert.True(PngFile.ReadFilterTypes(png).Distinct().Count() >= 2);
    }

    [Fact]
    public void Encode_IgnoresAlpha()
    {
        var opaque = TestImages.Noise(20, 10, seed: 7);
        var translucent = opaque.Clone();
        for (var i = 3; i < opaque.Pixels.Length; i += 4)
        {
            opaque.Pixels[i] = 255;
            translucent.Pixels[i] = (byte)(i % 256);
        }

        Assert.Equal(PngEncoder.Encode(opaque), PngEncoder.Encode(translucent));
    }

    [Fact]
    public void Encode_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PngEncoder.Encode(null!));
    }
}
