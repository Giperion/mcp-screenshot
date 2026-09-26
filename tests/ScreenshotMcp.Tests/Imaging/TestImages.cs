using ScreenshotMcp.Imaging;

namespace ScreenshotMcp.Tests.Imaging;

internal readonly record struct Rgb(byte R, byte G, byte B);

internal static class TestImages
{
    public static Rgb GetPixel(this BgraImage image, int x, int y)
    {
        var row = image.GetRow(y);
        return new Rgb(row[x * 4 + 2], row[x * 4 + 1], row[x * 4]);
    }

    public static void SetPixel(this BgraImage image, int x, int y, Rgb color, byte alpha = 255)
    {
        var row = image.GetRow(y);
        row[x * 4] = color.B;
        row[x * 4 + 1] = color.G;
        row[x * 4 + 2] = color.R;
        row[x * 4 + 3] = alpha;
    }

    public static void Fill(this BgraImage image, int x, int y, int width, int height, Rgb color)
    {
        for (var row = Math.Max(0, y); row < Math.Min(image.Height, y + height); row++)
        {
            for (var column = Math.Max(0, x); column < Math.Min(image.Width, x + width); column++)
            {
                image.SetPixel(column, row, color);
            }
        }
    }

    public static BgraImage Solid(int width, int height, Rgb color)
    {
        var image = new BgraImage(width, height);
        image.Fill(0, 0, width, height, color);
        return image;
    }

    /// <summary>Random bytes, including random alpha.</summary>
    public static BgraImage Noise(int width, int height, int seed)
    {
        var pixels = new byte[width * height * BgraImage.BytesPerPixel];
        new Random(seed).NextBytes(pixels);
        return new BgraImage(width, height, pixels);
    }

    public static BgraImage Gradient(int width, int height)
    {
        var image = new BgraImage(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.SetPixel(x, y, new Rgb(
                    (byte)(x * 255 / Math.Max(1, width - 1)),
                    (byte)(y * 255 / Math.Max(1, height - 1)),
                    (byte)(x + y)));
            }
        }

        return image;
    }

    /// <summary>
    /// A desktop-like picture: gradient wallpaper, windows with title bars and sidebars,
    /// lines of anti-aliased "text" from a fixed glyph set, buttons and a taskbar.
    /// </summary>
    public static BgraImage UiLike(int width, int height, int seed = 1)
    {
        var random = new Random(seed);
        var image = new BgraImage(width, height);
        for (var y = 0; y < height; y++)
        {
            image.Fill(0, y, width, 1, new Rgb(
                (byte)(20 + 40 * y / height), (byte)(70 + 60 * y / height), (byte)(120 + 80 * y / height)));
        }

        var glyphs = new byte[64][];
        foreach (ref var glyph in glyphs.AsSpan())
        {
            glyph = new byte[6 * 9];
            for (var i = 0; i < glyph.Length; i++)
            {
                glyph[i] = random.NextDouble() < 0.3 ? (byte)(30 + 80 * random.Next(3)) : (byte)255;
            }
        }

        for (var window = 0; window < 5; window++)
        {
            var w = random.Next(width / 3, width * 2 / 3);
            var h = random.Next(height / 3, height * 2 / 3);
            var left = random.Next(0, width - w);
            var top = random.Next(0, height - h - 40);
            image.Fill(left - 1, top - 1, w + 2, h + 2, new Rgb(160, 160, 160));
            image.Fill(left, top, w, h, new Rgb(255, 255, 255));
            image.Fill(left, top, w, 30, new Rgb(240, 240, 240));
            image.Fill(left, top + 30, 200, h - 30, new Rgb(245, 246, 248));
            for (var line = top + 45; line < top + h - 15; line += 18)
            {
                var x = left + (line % 36 == 0 ? 10 : 215);
                var end = left + w - 60;
                if (random.NextDouble() < 0.1)
                {
                    image.Fill(x, line - 2, 90, 16, new Rgb(0, 120, 215));
                    x += 100;
                }

                while (x < end)
                {
                    var wordLength = random.Next(2, 10);
                    for (var c = 0; c < wordLength && x + 7 < end; c++, x += 7)
                    {
                        var glyph = glyphs[random.Next(glyphs.Length)];
                        for (var gy = 0; gy < 9; gy++)
                        {
                            for (var gx = 0; gx < 6; gx++)
                            {
                                var value = glyph[gy * 6 + gx];
                                if (value != 255)
                                {
                                    image.SetPixel(x + gx, line + gy, new Rgb(value, value, value));
                                }
                            }
                        }
                    }

                    x += 6;
                }
            }
        }

        image.Fill(0, height - 40, width, 40, new Rgb(32, 32, 32));
        for (var i = 0; i < 12; i++)
        {
            image.Fill(10 + i * 48, height - 34, 28, 28,
                new Rgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        }

        return image;
    }
}
