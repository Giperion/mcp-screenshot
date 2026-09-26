using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.Intrinsics;

namespace ScreenshotMcp.Imaging;

/// <summary>
/// Encodes a <see cref="BgraImage"/> as a non-interlaced 8-bit RGB PNG. Each row gets the filter
/// (None/Sub/Up/Average/Paeth) with the smallest sum of absolute signed residuals, as libpng does.
/// </summary>
public static class PngEncoder
{
    private const int Channels = 3;
    private const int ChunkOverhead = 12;
    private const int IhdrLength = 13;

    // Measured on 2576x1449 synthetic screenshots, zlib level 3 deflates about twice as fast as
    // level 6. Output is smaller on downscaled UI, ~2% larger on photo-like content, and ~20% larger
    // only on crisp native UI, which is far below any byte budget anyway.
    private const int CompressionLevel = 3;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Encode(BgraImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var compressed = Compress(image);
        var idat = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);

        var png = new byte[Signature.Length + 3 * ChunkOverhead + IhdrLength + idat.Length];
        Signature.CopyTo(png);
        var offset = Signature.Length;

        Span<byte> ihdr = stackalloc byte[IhdrLength];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], image.Height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // color type: truecolor
        ihdr[10] = 0; // compression: deflate
        ihdr[11] = 0; // filter method: adaptive
        ihdr[12] = 0; // interlace: none

        offset += WriteChunk(png.AsSpan(offset), "IHDR"u8, ihdr);
        offset += WriteChunk(png.AsSpan(offset), "IDAT"u8, idat);
        WriteChunk(png.AsSpan(offset), "IEND"u8, []);
        return png;
    }

    private static int WriteChunk(Span<byte> destination, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        BinaryPrimitives.WriteInt32BigEndian(destination, data.Length);
        type.CopyTo(destination[4..]);
        data.CopyTo(destination[8..]);
        var crc = Crc32.Compute(destination.Slice(4, type.Length + data.Length));
        BinaryPrimitives.WriteUInt32BigEndian(destination[(8 + data.Length)..], crc);
        return ChunkOverhead + data.Length;
    }

    private static MemoryStream Compress(BgraImage image)
    {
        var rowLength = image.Width * Channels;

        // Rows start with Channels zero bytes, so row[i] is the left neighbour of row[i + Channels]
        // and the first pixel's left neighbour reads as 0 without a special case.
        var previous = new byte[Channels + rowLength];
        var current = new byte[Channels + rowLength];
        var filtered = new byte[1 + rowLength];

        var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, new ZLibCompressionOptions { CompressionLevel = CompressionLevel }, leaveOpen: true))
        {
            for (var y = 0; y < image.Height; y++)
            {
                ToRgb(image.GetRow(y), current.AsSpan(Channels));
                var filter = ChooseFilter(current, previous);
                filtered[0] = (byte)filter;
                WriteResiduals(filter, current, previous, filtered.AsSpan(1));
                zlib.Write(filtered);
                (previous, current) = (current, previous);
            }
        }

        return output;
    }

    private static void ToRgb(ReadOnlySpan<byte> bgra, Span<byte> rgb)
    {
        for (int s = 0, d = 0; d < rgb.Length; s += 4, d += 3)
        {
            rgb[d] = bgra[s + 2];
            rgb[d + 1] = bgra[s + 1];
            rgb[d + 2] = bgra[s];
        }
    }

    private static Filter ChooseFilter(ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous)
    {
        // A lane gains at most 2 * 128 per step, so ushort lane sums are flushed before 256 steps.
        const int StepsPerFlush = 255;
        var width = Vector128<byte>.Count;
        var length = row.Length - Channels;
        int none = 0, sub = 0, up = 0, average = 0, paeth = 0;
        var i = 0;
        while (length - i >= width)
        {
            var steps = Math.Min((length - i) / width, StepsPerFlush);
            Vector128<ushort> vNone = default, vSub = default, vUp = default, vAverage = default, vPaeth = default;
            for (var step = 0; step < steps; step++, i += width)
            {
                var x = Vector128.Create(row.Slice(i + Channels, width));
                var a = Vector128.Create(row.Slice(i, width));
                var b = Vector128.Create(previous.Slice(i + Channels, width));
                var c = Vector128.Create(previous.Slice(i, width));
                vNone += Cost(x);
                vSub += Cost(x - a);
                vUp += Cost(x - b);
                vAverage += Cost(x - Average(a, b));
                vPaeth += Cost(x - Paeth(a, b, c));
            }

            none += Total(vNone);
            sub += Total(vSub);
            up += Total(vUp);
            average += Total(vAverage);
            paeth += Total(vPaeth);
        }

        for (; i < length; i++)
        {
            int x = row[i + Channels], a = row[i], b = previous[i + Channels], c = previous[i];
            none += Cost(x);
            sub += Cost(x - a);
            up += Cost(x - b);
            average += Cost(x - ((a + b) >> 1));
            paeth += Cost(x - Paeth(a, b, c));
        }

        var best = Filter.None;
        var bestCost = none;
        if (sub < bestCost) (best, bestCost) = (Filter.Sub, sub);
        if (up < bestCost) (best, bestCost) = (Filter.Up, up);
        if (average < bestCost) (best, bestCost) = (Filter.Average, average);
        if (paeth < bestCost) best = Filter.Paeth;
        return best;
    }

    private static void WriteResiduals(Filter filter, ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, Span<byte> output)
    {
        var width = Vector128<byte>.Count;
        var i = 0;
        for (; output.Length - i >= width; i += width)
        {
            var x = Vector128.Create(row.Slice(i + Channels, width));
            var a = Vector128.Create(row.Slice(i, width));
            var b = Vector128.Create(previous.Slice(i + Channels, width));
            var c = Vector128.Create(previous.Slice(i, width));
            var predicted = filter switch
            {
                Filter.None => Vector128<byte>.Zero,
                Filter.Sub => a,
                Filter.Up => b,
                Filter.Average => Average(a, b),
                _ => Paeth(a, b, c),
            };
            (x - predicted).CopyTo(output[i..]);
        }

        for (; i < output.Length; i++)
        {
            int a = row[i], b = previous[i + Channels], c = previous[i];
            var predicted = filter switch
            {
                Filter.None => 0,
                Filter.Sub => a,
                Filter.Up => b,
                Filter.Average => (a + b) >> 1,
                _ => Paeth(a, b, c),
            };
            output[i] = (byte)(row[i + Channels] - predicted);
        }
    }

    /// <summary>|residual| with the residual read as a signed byte.</summary>
    private static int Cost(int residual) => Math.Abs((int)(sbyte)residual);

    private static Vector128<ushort> Cost(Vector128<byte> residuals)
    {
        var magnitudes = Vector128.Abs(residuals.AsSByte()).AsByte(); // -128 wraps to 128, as wanted
        return Vector128.WidenLower(magnitudes) + Vector128.WidenUpper(magnitudes);
    }

    private static int Total(Vector128<ushort> sums) =>
        (int)Vector128.Sum(Vector128.WidenLower(sums) + Vector128.WidenUpper(sums));

    /// <summary><c>floor((a + b) / 2)</c> without overflowing a byte.</summary>
    private static Vector128<byte> Average(Vector128<byte> a, Vector128<byte> b) =>
        (a & b) + Vector128.ShiftRightLogical(a ^ b, 1);

    private static int Paeth(int a, int b, int c)
    {
        var pa = Math.Abs(b - c);
        var pb = Math.Abs(a - c);
        var pc = Math.Abs(a + b - 2 * c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static Vector128<byte> Paeth(Vector128<byte> a, Vector128<byte> b, Vector128<byte> c)
    {
        var pa = AbsoluteDifference(b, c);
        var pb = AbsoluteDifference(a, c);

        // pc = |(a - c) + (b - c)|. When both terms have the same sign it is pa + pb, which is never
        // below pa or pb, so 255 gives the same comparisons; otherwise it is |pa - pb|.
        var sameSign = ~(Vector128.GreaterThanOrEqual(b, c) ^ Vector128.GreaterThanOrEqual(a, c));
        var pc = AbsoluteDifference(pa, pb) | sameSign;

        var useA = Vector128.LessThanOrEqual(pa, pb) & Vector128.LessThanOrEqual(pa, pc);
        var useB = Vector128.LessThanOrEqual(pb, pc);
        return Vector128.ConditionalSelect(useA, a, Vector128.ConditionalSelect(useB, b, c));
    }

    private static Vector128<byte> AbsoluteDifference(Vector128<byte> x, Vector128<byte> y) =>
        Vector128.Max(x, y) - Vector128.Min(x, y);

    private enum Filter : byte
    {
        None = 0,
        Sub = 1,
        Up = 2,
        Average = 3,
        Paeth = 4,
    }
}
