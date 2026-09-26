namespace ScreenshotMcp.Imaging;

/// <summary>
/// Area-averaging (box filter) downscaler. Every output pixel is the average of the source area
/// it covers, with partially covered source pixels weighted by their coverage, so thin lines and
/// small text fade proportionally instead of disappearing as they do with point sampling.
/// </summary>
public static class ImageScaler
{
    public static BgraImage Downscale(BgraImage source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, source.Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, source.Height);

        if (width == source.Width && height == source.Height)
        {
            return source.Clone();
        }

        var columns = Taps.Create(source.Width, width);
        var rows = Taps.Create(source.Height, height);
        var divisor = (long)columns.Total * rows.Total;
        var result = new BgraImage(width, height);

        // Weighted sums of source rows for the current output row, per source byte (BGRA).
        var accumulator = new long[source.Stride];
        for (var y = 0; y < height; y++)
        {
            Array.Clear(accumulator);
            for (var t = rows.Offsets[y]; t < rows.Offsets[y + 1]; t++)
            {
                var sourceRow = source.GetRow(rows.Indices[t]);
                var weight = rows.Weights[t];
                for (var i = 0; i < accumulator.Length; i++)
                {
                    accumulator[i] += sourceRow[i] * (long)weight;
                }
            }

            var destination = result.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                long blue = 0, green = 0, red = 0;
                for (var t = columns.Offsets[x]; t < columns.Offsets[x + 1]; t++)
                {
                    var i = columns.Indices[t] * BgraImage.BytesPerPixel;
                    var weight = columns.Weights[t];
                    blue += accumulator[i] * weight;
                    green += accumulator[i + 1] * weight;
                    red += accumulator[i + 2] * weight;
                }

                var d = x * BgraImage.BytesPerPixel;
                destination[d] = Round(blue, divisor);
                destination[d + 1] = Round(green, divisor);
                destination[d + 2] = Round(red, divisor);
            }
        }

        return result;
    }

    private static byte Round(long sum, long divisor) => (byte)((sum + divisor / 2) / divisor);

    /// <summary>
    /// Source pixels covering each output pixel along one axis, with integer weights. Both pixel grids
    /// are measured in units of 1/(sourceSize * targetSize / gcd), where every overlap is a whole number:
    /// a source pixel is <c>targetSize / gcd</c> units long and an output pixel <see cref="Total"/> units.
    /// </summary>
    private sealed class Taps
    {
        private Taps(int[] offsets, int[] indices, int[] weights, int total)
        {
            Offsets = offsets;
            Indices = indices;
            Weights = weights;
            Total = total;
        }

        /// <summary>Taps of output pixel <c>o</c> are <c>[Offsets[o], Offsets[o + 1])</c>.</summary>
        public int[] Offsets { get; }

        public int[] Indices { get; }

        public int[] Weights { get; }

        /// <summary>Sum of the weights of every output pixel.</summary>
        public int Total { get; }

        public static Taps Create(int sourceSize, int targetSize)
        {
            var gcd = Gcd(sourceSize, targetSize);
            var sourceUnit = targetSize / gcd;
            var targetUnit = sourceSize / gcd;
            var maxTaps = targetUnit / sourceUnit + 2;

            var offsets = new int[targetSize + 1];
            var indices = new int[targetSize * maxTaps];
            var weights = new int[targetSize * maxTaps];
            var count = 0;
            for (var o = 0; o < targetSize; o++)
            {
                var start = (long)o * targetUnit;
                var end = start + targetUnit;
                var first = (int)(start / sourceUnit);
                var last = (int)((end - 1) / sourceUnit);
                for (var i = first; i <= last; i++)
                {
                    var overlap = Math.Min((long)(i + 1) * sourceUnit, end) - Math.Max((long)i * sourceUnit, start);
                    indices[count] = i;
                    weights[count] = (int)overlap;
                    count++;
                }

                offsets[o + 1] = count;
            }

            return new Taps(offsets, indices, weights, targetUnit);
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }

            return a;
        }
    }
}
