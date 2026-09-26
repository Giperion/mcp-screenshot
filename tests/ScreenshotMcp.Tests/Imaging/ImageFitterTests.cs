using ScreenshotMcp.Imaging;

namespace ScreenshotMcp.Tests.Imaging;

public sealed class ImageFitterTests
{
    private static readonly Rgb Gray = new(90, 100, 110);

    // Anthropic vision docs, high-resolution tier: 2576 px long edge and 4784 visual tokens.
    [Theory]
    [InlineData(1920, 1080, 1920, 1080, 2691)]
    [InlineData(2000, 1500, 2000, 1500, 3888)]
    [InlineData(3840, 2160, 2576, 1449, 4784)]
    [InlineData(1000, 1000, 1000, 1000, 1296)]
    public void Fit_DocumentedSizes(int width, int height, int expectedWidth, int expectedHeight, long expectedTokens)
    {
        var fitted = ImageFitter.Fit(TestImages.Solid(width, height, Gray));

        Assert.Equal((expectedWidth, expectedHeight), (fitted.Width, fitted.Height));
        Assert.Equal((width, height), (fitted.SourceWidth, fitted.SourceHeight));
        Assert.Equal(expectedTokens, ImageFitter.VisualTokens(fitted.Width, fitted.Height));
        Assert.Equal((double)expectedWidth / width, fitted.Scale);
        Assert.Equal(width != expectedWidth, fitted.IsResized);
        PngFile.AssertDecodesTo(TestImages.Solid(expectedWidth, expectedHeight, Gray), fitted.Png);
    }

    [Fact]
    public void Fit_PortraitImage_IsLimitedByItsHeight()
    {
        var fitted = ImageFitter.Fit(TestImages.Solid(2160, 3840, Gray));

        Assert.Equal((1449, 2576), (fitted.Width, fitted.Height));
    }

    [Fact]
    public void GetTargetSize_SquareImage_IsLimitedByTokens()
    {
        // 2576x2576 is within the long edge but needs 92 * 92 tokens; 69 * 69 = 4761 fits, 70 * 70 does not.
        Assert.Equal((1932, 1932), ImageFitter.GetTargetSize(2576, 2576, ImageLimits.Default));
    }

    [Theory]
    [InlineData(3840, 2160)]
    [InlineData(2160, 3840)]
    [InlineData(5120, 1440)]
    [InlineData(7680, 2160)]
    [InlineData(2560, 2560)]
    [InlineData(3000, 2999)]
    [InlineData(2577, 10)]
    [InlineData(1, 5000)]
    [InlineData(5000, 1)]
    public void GetTargetSize_KeepsAspectRatioWithinLimits(int width, int height)
    {
        var (w, h) = ImageFitter.GetTargetSize(width, height, ImageLimits.Default);

        Assert.InRange(w, 1, width);
        Assert.InRange(h, 1, height);
        Assert.True(Math.Max(w, h) <= 2576);
        Assert.True(ImageFitter.VisualTokens(w, h) <= 4784);
        if (width >= height)
        {
            Assert.InRange(h, Math.Max(1, (double)height * w / width - 1), (double)height * w / width + 1);
        }
        else
        {
            Assert.InRange(w, Math.Max(1, (double)width * h / height - 1), (double)width * h / height + 1);
        }
    }

    [Fact]
    public void Fit_MaxLongEdgeZero_KeepsNativeSize()
    {
        var fitted = ImageFitter.Fit(TestImages.Solid(3840, 2160, Gray), new ImageLimits { MaxLongEdge = 0 });

        Assert.Equal((3840, 2160), (fitted.Width, fitted.Height));
        Assert.False(fitted.IsResized);
        Assert.Equal(1.0, fitted.Scale);
    }

    [Fact]
    public void GetTargetSize_MaxLongEdgeZero_StillRespectsMaxDimension()
    {
        var limits = new ImageLimits { MaxLongEdge = 0 };

        Assert.Equal((8000, 2000), ImageFitter.GetTargetSize(12000, 3000, limits));
        Assert.Equal((5000, 5000), ImageFitter.GetTargetSize(5000, 5000, limits));
    }

    [Fact]
    public void Fit_IncompressibleImageOverBudget_ShrinksUntilItFits()
    {
        var source = TestImages.Noise(400, 300, seed: 1);
        var limits = new ImageLimits { MaxBase64Bytes = 100_000 };

        var fitted = ImageFitter.Fit(source, limits);

        Assert.True(ImageFitter.Base64Length(fitted.Png.Length) <= limits.MaxBase64Bytes);
        Assert.Equal(Convert.ToBase64String(fitted.Png).Length, ImageFitter.Base64Length(fitted.Png.Length));
        Assert.True(fitted.Width < 400);
        Assert.InRange(fitted.Height, fitted.Width * 3 / 4.0 - 1, fitted.Width * 3 / 4.0 + 1);
        Assert.Equal((400, 300), (fitted.SourceWidth, fitted.SourceHeight));
        Assert.Equal(fitted.Width / 400.0, fitted.Scale);

        // Scaled once from the original, never from an intermediate result.
        var expected = ImageScaler.Downscale(source, fitted.Width, fitted.Height);
        PngFile.AssertDecodesTo(expected, fitted.Png);
    }

    [Fact]
    public void Fit_ImageWithinAllLimits_IsEncodedAsIs()
    {
        var source = TestImages.UiLike(800, 600);

        var fitted = ImageFitter.Fit(source);

        Assert.False(fitted.IsResized);
        Assert.Equal(PngEncoder.Encode(source), fitted.Png);
    }

    [Fact]
    public void Fit_BudgetBelowAnyPng_Throws()
    {
        var limits = new ImageLimits { MaxBase64Bytes = 10 };

        Assert.Throws<InvalidOperationException>(() => ImageFitter.Fit(TestImages.Noise(50, 40, seed: 2), limits));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(28, 28, 1)]
    [InlineData(29, 28, 2)]
    [InlineData(56, 57, 6)]
    public void VisualTokens_CountsStarted28PixelPatches(int width, int height, long expected)
    {
        Assert.Equal(expected, ImageFitter.VisualTokens(width, height));
    }

    [Fact]
    public void Base64Length_MatchesConvert()
    {
        for (var n = 0; n < 10; n++)
        {
            Assert.Equal(Convert.ToBase64String(new byte[n]).Length, ImageFitter.Base64Length(n));
        }
    }

    [Fact]
    public void ImageLimits_Defaults()
    {
        var limits = ImageLimits.Default;

        Assert.Equal(2576, limits.MaxLongEdge);
        Assert.Equal(4784, limits.MaxVisualTokens);
        Assert.Equal(8000, limits.MaxDimension);
        Assert.Equal(4_500_000, limits.MaxBase64Bytes);
    }

    [Fact]
    public void ImageLimits_RejectsInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageLimits { MaxLongEdge = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageLimits { MaxVisualTokens = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageLimits { MaxDimension = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageLimits { MaxBase64Bytes = 0 });
    }
}
