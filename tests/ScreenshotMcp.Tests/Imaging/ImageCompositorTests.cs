using ScreenshotMcp.Imaging;

namespace ScreenshotMcp.Tests.Imaging;

public sealed class ImageCompositorTests
{
    private static readonly Rgb Black = new(0, 0, 0);

    [Fact]
    public void Compose_MonitorLeftOfPrimary_HasNegativeOrigin()
    {
        var left = TestImages.Noise(4, 3, seed: 1);
        var primary = TestImages.Noise(2, 2, seed: 2);

        var composite = ImageCompositor.Compose([new(left, -4, 0), new(primary, 0, 1)]);

        Assert.Equal((-4, 0), (composite.OriginX, composite.OriginY));
        Assert.Equal((6, 3), (composite.Image.Width, composite.Image.Height));
        AssertTileAt(composite.Image, left, 0, 0);
        AssertTileAt(composite.Image, primary, 4, 1);
        AssertBlack(composite.Image, 4, 0, 2, 1);
    }

    [Fact]
    public void Compose_MonitorAbovePrimary_HasNegativeOriginY()
    {
        var primary = TestImages.Noise(3, 2, seed: 3);
        var above = TestImages.Noise(3, 2, seed: 4);

        var composite = ImageCompositor.Compose([new(primary, 0, 0), new(above, 1, -2)]);

        Assert.Equal((0, -2), (composite.OriginX, composite.OriginY));
        Assert.Equal((4, 4), (composite.Image.Width, composite.Image.Height));
        AssertTileAt(composite.Image, primary, 0, 2);
        AssertTileAt(composite.Image, above, 1, 0);
        AssertBlack(composite.Image, 0, 0, 1, 2);
        AssertBlack(composite.Image, 3, 2, 1, 2);
    }

    [Fact]
    public void Compose_GapBetweenMonitors_IsOpaqueBlack()
    {
        var first = TestImages.Solid(2, 2, new Rgb(200, 10, 10));
        var second = TestImages.Solid(2, 2, new Rgb(10, 200, 10));

        var composite = ImageCompositor.Compose([new(first, 0, 0), new(second, 5, 1)]);

        Assert.Equal((7, 3), (composite.Image.Width, composite.Image.Height));
        AssertTileAt(composite.Image, first, 0, 0);
        AssertTileAt(composite.Image, second, 5, 1);
        AssertBlack(composite.Image, 2, 0, 3, 3);
        AssertBlack(composite.Image, 0, 2, 2, 1);
        AssertBlack(composite.Image, 5, 0, 2, 1);
    }

    [Fact]
    public void Compose_DifferentSizesAndOffsets_CoversTheBoundingBox()
    {
        var landscape = TestImages.Noise(8, 6, seed: 5);
        var portrait = TestImages.Noise(4, 8, seed: 6);
        var small = TestImages.Noise(3, 2, seed: 7);

        var composite = ImageCompositor.Compose(
        [
            new(landscape, 0, 0),
            new(portrait, 8, -2),
            new(small, -3, 4),
        ]);

        Assert.Equal((-3, -2), (composite.OriginX, composite.OriginY));
        Assert.Equal((15, 8), (composite.Image.Width, composite.Image.Height));
        AssertTileAt(composite.Image, landscape, 3, 2);
        AssertTileAt(composite.Image, portrait, 11, 0);
        AssertTileAt(composite.Image, small, 0, 6);
        AssertBlack(composite.Image, 0, 0, 11, 2);
        AssertBlack(composite.Image, 0, 2, 3, 4);
    }

    [Fact]
    public void Compose_SingleTile_ReturnsACopyAtItsPosition()
    {
        var tile = TestImages.Noise(5, 4, seed: 8);

        var composite = ImageCompositor.Compose([new(tile, 100, -200)]);

        Assert.Equal((100, -200), (composite.OriginX, composite.OriginY));
        Assert.NotSame(tile.Pixels, composite.Image.Pixels);
        Assert.Equal(tile.Pixels, composite.Image.Pixels);
    }

    [Fact]
    public void Compose_OverlappingTiles_LaterTileWins()
    {
        var bottom = TestImages.Solid(3, 3, new Rgb(255, 0, 0));
        var top = TestImages.Solid(2, 2, new Rgb(0, 0, 255));

        var composite = ImageCompositor.Compose([new(bottom, 0, 0), new(top, 1, 1)]);

        Assert.Equal(new Rgb(255, 0, 0), composite.Image.GetPixel(0, 0));
        Assert.Equal(new Rgb(0, 0, 255), composite.Image.GetPixel(1, 1));
        Assert.Equal(new Rgb(0, 0, 255), composite.Image.GetPixel(2, 2));
    }

    [Fact]
    public void Compose_NoTiles_Throws()
    {
        Assert.Throws<ArgumentException>(() => ImageCompositor.Compose([]));
    }

    [Fact]
    public void Compose_TileWithoutImage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ImageCompositor.Compose([default]));
    }

    private static void AssertTileAt(BgraImage composite, BgraImage tile, int x, int y)
    {
        Assert.Equal(tile.Pixels, composite.Crop(x, y, tile.Width, tile.Height).Pixels);
    }

    private static void AssertBlack(BgraImage image, int x, int y, int width, int height)
    {
        var region = image.Crop(x, y, width, height);
        Assert.Equal(TestImages.Solid(width, height, Black).Pixels, region.Pixels);
    }
}
