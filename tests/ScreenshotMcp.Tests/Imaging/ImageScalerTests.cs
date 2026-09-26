using ScreenshotMcp.Imaging;

namespace ScreenshotMcp.Tests.Imaging;

public sealed class ImageScalerTests
{
    [Fact]
    public void Downscale_ByTwo_AveragesTwoByTwoBlocks()
    {
        var image = new BgraImage(4, 2);
        image.SetPixel(0, 0, new Rgb(10, 0, 100));
        image.SetPixel(1, 0, new Rgb(20, 0, 100));
        image.SetPixel(0, 1, new Rgb(30, 0, 104));
        image.SetPixel(1, 1, new Rgb(40, 4, 104));
        image.SetPixel(2, 0, new Rgb(255, 1, 7));
        image.SetPixel(3, 0, new Rgb(255, 2, 8));
        image.SetPixel(2, 1, new Rgb(255, 3, 9));
        image.SetPixel(3, 1, new Rgb(255, 6, 8));

        var result = ImageScaler.Downscale(image, 2, 1);

        Assert.Equal(new Rgb(25, 1, 102), result.GetPixel(0, 0));
        Assert.Equal(new Rgb(255, 3, 8), result.GetPixel(1, 0));
    }

    [Fact]
    public void Downscale_ByThree_AveragesThreeByThreeBlocks()
    {
        var image = new BgraImage(6, 3);
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                image.SetPixel(x, y, new Rgb((byte)(10 * (y * 6 + x)), (byte)(x < 3 ? 90 : 9), 0));
            }
        }

        var result = ImageScaler.Downscale(image, 2, 1);

        // Left block: 10 * (0+1+2+6+7+8+12+13+14) / 9 = 70; right block: 10 * (3+4+5+9+10+11+15+16+17) / 9 = 100.
        Assert.Equal(new Rgb(70, 90, 0), result.GetPixel(0, 0));
        Assert.Equal(new Rgb(100, 9, 0), result.GetPixel(1, 0));
    }

    [Fact]
    public void Downscale_NonIntegerRatio_WeightsPartiallyCoveredPixels()
    {
        // 3 -> 2: each output pixel covers 1.5 source pixels, so out0 = (2*p0 + p1) / 3, out1 = (p1 + 2*p2) / 3.
        var row = new BgraImage(3, 1);
        row.SetPixel(0, 0, new Rgb(30, 0, 255));
        row.SetPixel(1, 0, new Rgb(90, 255, 0));
        row.SetPixel(2, 0, new Rgb(150, 0, 0));
        var column = new BgraImage(1, 3);
        column.SetPixel(0, 0, new Rgb(30, 0, 255));
        column.SetPixel(0, 1, new Rgb(90, 255, 0));
        column.SetPixel(0, 2, new Rgb(150, 0, 0));

        var horizontal = ImageScaler.Downscale(row, 2, 1);
        var vertical = ImageScaler.Downscale(column, 1, 2);

        Assert.Equal(new Rgb(50, 85, 170), horizontal.GetPixel(0, 0));
        Assert.Equal(new Rgb(130, 85, 0), horizontal.GetPixel(1, 0));
        Assert.Equal(new Rgb(50, 85, 170), vertical.GetPixel(0, 0));
        Assert.Equal(new Rgb(130, 85, 0), vertical.GetPixel(0, 1));
    }

    [Fact]
    public void Downscale_FiveToTwo_SpreadsTheCenterPixelOverAllOutputs()
    {
        // Each output pixel covers 2.5 x 2.5 source pixels and a quarter of the center one:
        // 255 * (0.5 * 0.5) / (2.5 * 2.5) = 10.2.
        var image = new BgraImage(5, 5);
        image.SetPixel(2, 2, new Rgb(255, 255, 255));

        var result = ImageScaler.Downscale(image, 2, 2);

        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                Assert.Equal(new Rgb(10, 10, 10), result.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Downscale_OnePixelLine_IsKeptAsAFainterLine()
    {
        // Point sampling at 9 -> 6 would either drop the line or keep it at full strength.
        var image = TestImages.Solid(9, 1, new Rgb(255, 255, 255));
        image.SetPixel(4, 0, new Rgb(0, 0, 0));

        var result = ImageScaler.Downscale(image, 6, 1);

        var values = Enumerable.Range(0, 6).Select(x => result.GetPixel(x, 0).R).ToArray();
        Assert.Equal([255, 255, 170, 170, 255, 255], values);
    }

    [Theory]
    [InlineData(97, 61, 13, 7)]
    [InlineData(3840, 2160, 2576, 1449)]
    [InlineData(10, 10, 1, 1)]
    public void Downscale_UniformColor_StaysExact(int width, int height, int targetWidth, int targetHeight)
    {
        var color = new Rgb(12, 34, 56);

        var result = ImageScaler.Downscale(TestImages.Solid(width, height, color), targetWidth, targetHeight);

        Assert.Equal(targetWidth, result.Width);
        Assert.Equal(targetHeight, result.Height);
        Assert.Equal(TestImages.Solid(targetWidth, targetHeight, color).Pixels, result.Pixels);
    }

    [Fact]
    public void Downscale_NonIntegerRatio_PreservesMeanBrightness()
    {
        var image = TestImages.Noise(257, 131, seed: 3);

        var result = ImageScaler.Downscale(image, 100, 50);

        Assert.Equal(MeanRed(image), MeanRed(result), tolerance: 0.5);

        static double MeanRed(BgraImage image)
        {
            double sum = 0;
            for (var i = 2; i < image.Pixels.Length; i += 4)
            {
                sum += image.Pixels[i];
            }

            return sum / (image.Width * image.Height);
        }
    }

    [Fact]
    public void Downscale_OneAxisOnly_LeavesTheOtherAxisUntouched()
    {
        var image = TestImages.Gradient(8, 5);

        var result = ImageScaler.Downscale(image, 8, 1);

        for (var x = 0; x < 8; x++)
        {
            var expectedRed = image.GetPixel(x, 0).R;
            Assert.Equal(expectedRed, result.GetPixel(x, 0).R);
        }
    }

    [Fact]
    public void Downscale_ProducesOpaquePixels()
    {
        var result = ImageScaler.Downscale(TestImages.Noise(30, 20, seed: 5), 7, 3);

        for (var i = 3; i < result.Pixels.Length; i += 4)
        {
            Assert.Equal(255, result.Pixels[i]);
        }
    }

    [Fact]
    public void Downscale_SameSize_ReturnsAnEqualCopy()
    {
        var image = TestImages.Noise(9, 4, seed: 11);

        var result = ImageScaler.Downscale(image, 9, 4);

        Assert.NotSame(image.Pixels, result.Pixels);
        Assert.Equal(image.Pixels, result.Pixels);
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(4, 4)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void Downscale_InvalidTarget_Throws(int width, int height)
    {
        var image = new BgraImage(4, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => ImageScaler.Downscale(image, width, height));
    }
}
