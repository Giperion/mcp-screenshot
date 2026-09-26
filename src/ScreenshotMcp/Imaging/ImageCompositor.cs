namespace ScreenshotMcp.Imaging;

/// <summary>Stitches per-monitor captures into one picture of the virtual screen.</summary>
public static class ImageCompositor
{
    /// <summary>
    /// Covers the bounding box of all tiles. Areas no tile covers (gaps between monitors of
    /// different sizes or offsets) are opaque black; where tiles overlap, later tiles win.
    /// </summary>
    public static CompositeImage Compose(IReadOnlyList<ImageTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (tiles.Count == 0)
        {
            throw new ArgumentException("At least one tile is required.", nameof(tiles));
        }

        long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
        foreach (var tile in tiles)
        {
            ArgumentNullException.ThrowIfNull(tile.Image, nameof(tiles));
            minX = Math.Min(minX, tile.X);
            minY = Math.Min(minY, tile.Y);
            maxX = Math.Max(maxX, (long)tile.X + tile.Image.Width);
            maxY = Math.Max(maxY, (long)tile.Y + tile.Image.Height);
        }

        if (maxX - minX > int.MaxValue || maxY - minY > int.MaxValue)
        {
            throw new ArgumentException("The tiles span too large an area.", nameof(tiles));
        }

        var result = new BgraImage((int)(maxX - minX), (int)(maxY - minY));
        foreach (var tile in tiles)
        {
            var left = (int)(tile.X - minX) * BgraImage.BytesPerPixel;
            var top = (int)(tile.Y - minY);
            for (var row = 0; row < tile.Image.Height; row++)
            {
                tile.Image.GetRow(row).CopyTo(result.GetRow(top + row)[left..]);
            }
        }

        return new CompositeImage(result, (int)minX, (int)minY);
    }
}
