using ScreenshotMcp.Imaging;

namespace ScreenshotMcp.Tests.Imaging;

public sealed class BgraImageTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 5)]
    [InlineData(5, -1)]
    [InlineData(65536, 65536)] // 16 GiB
    public void Constructor_InvalidSize_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BgraImage(width, height));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BgraImage(width, height, new byte[4]));
    }

    [Fact]
    public void Constructor_AllocatesOpaqueBlack()
    {
        var image = new BgraImage(3, 2);

        Assert.Equal((3, 2, 12), (image.Width, image.Height, image.Stride));
        Assert.Equal(Enumerable.Repeat<byte[]>([0, 0, 0, 255], 6).SelectMany(p => p), image.Pixels);
    }

    [Fact]
    public void Constructor_WrapsBufferWithoutCopying()
    {
        var pixels = new byte[3 * 2 * 4];

        var image = new BgraImage(3, 2, pixels);

        Assert.Same(pixels, image.Pixels);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(25)]
    [InlineData(0)]
    public void Constructor_WrongBufferLength_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => new BgraImage(3, 2, new byte[length]));
    }

    [Fact]
    public void Constructor_NullBuffer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new BgraImage(3, 2, null!));
    }

    [Fact]
    public void GetRow_ReturnsThatRow()
    {
        var image = TestImages.Noise(3, 4, seed: 1);

        Assert.Equal(image.Pixels.AsSpan(24, 12).ToArray(), image.GetRow(2).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetRow(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetRow(-1));
    }

    [Fact]
    public void Crop_CopiesTheRegion()
    {
        var image = TestImages.Noise(5, 4, seed: 2);

        var crop = image.Crop(1, 2, 3, 2);

        Assert.Equal((3, 2), (crop.Width, crop.Height));
        for (var y = 0; y < 2; y++)
        {
            Assert.Equal(image.GetRow(y + 2).Slice(4, 12).ToArray(), crop.GetRow(y).ToArray());
        }

        crop.Pixels[0] ^= 0xFF;
        Assert.NotEqual(crop.Pixels[0], image.GetRow(2)[4]);
    }

    [Fact]
    public void Crop_WholeImage_IsAnEqualCopy()
    {
        var image = TestImages.Noise(5, 4, seed: 3);

        var crop = image.Crop(0, 0, 5, 4);

        Assert.NotSame(image.Pixels, crop.Pixels);
        Assert.Equal(image.Pixels, crop.Pixels);
    }

    [Theory]
    [InlineData(-1, 0, 1, 1)]
    [InlineData(0, -1, 1, 1)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(4, 0, 2, 1)]
    [InlineData(0, 3, 1, 2)]
    [InlineData(5, 0, 1, 1)]
    [InlineData(1, 0, int.MaxValue, 1)]
    [InlineData(int.MaxValue, 0, 1, 1)]
    public void Crop_OutOfBounds_Throws(int x, int y, int width, int height)
    {
        var image = new BgraImage(5, 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => image.Crop(x, y, width, height));
    }

    [Fact]
    public void Clone_CopiesPixels()
    {
        var image = TestImages.Noise(4, 4, seed: 4);

        var clone = image.Clone();

        Assert.NotSame(image.Pixels, clone.Pixels);
        Assert.Equal(image.Pixels, clone.Pixels);
    }
}
