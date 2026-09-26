using System.Runtime.InteropServices;

namespace ScreenshotMcp.Imaging;

/// <summary>
/// 32-bit BGRA bitmap with top-down rows and no row padding (stride = width * 4).
/// Alpha is ignored everywhere: screenshots are opaque.
/// </summary>
public sealed class BgraImage
{
    public const int BytesPerPixel = 4;

    /// <summary>Allocates an opaque black image.</summary>
    public BgraImage(int width, int height)
    {
        Pixels = new byte[ByteLength(width, height)];
        MemoryMarshal.Cast<byte, uint>(Pixels.AsSpan()).Fill(BitConverter.IsLittleEndian ? 0xFF000000u : 0x000000FFu);
        Width = width;
        Height = height;
    }

    /// <summary>Wraps <paramref name="pixels"/> without copying.</summary>
    public BgraImage(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var length = ByteLength(width, height);
        if (pixels.Length != length)
        {
            throw new ArgumentException(
                $"A {width}x{height} image needs {length} bytes, got {pixels.Length}.", nameof(pixels));
        }

        Pixels = pixels;
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride => Width * BytesPerPixel;

    public byte[] Pixels { get; }

    public Span<byte> GetRow(int y)
    {
        if ((uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, $"Row must be in [0, {Height}).");
        }

        return Pixels.AsSpan(y * Stride, Stride);
    }

    public BgraImage Crop(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width > Width - x || height > Height - y)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), $"Region {width}x{height} at ({x}, {y}) exceeds the {Width}x{Height} image.");
        }

        var result = new BgraImage(width, height, new byte[width * height * BytesPerPixel]);
        var rowBytes = width * BytesPerPixel;
        for (var row = 0; row < height; row++)
        {
            Pixels.AsSpan((y + row) * Stride + x * BytesPerPixel, rowBytes).CopyTo(result.GetRow(row));
        }

        return result;
    }

    public BgraImage Clone() => new(Width, Height, (byte[])Pixels.Clone());

    private static int ByteLength(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var length = (long)width * height * BytesPerPixel;
        if (length > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"A {width}x{height} image is too large.");
        }

        return (int)length;
    }
}
