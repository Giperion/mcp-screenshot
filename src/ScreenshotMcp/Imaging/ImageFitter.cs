namespace ScreenshotMcp.Imaging;

/// <summary>
/// Picks the largest aspect-preserving size within <see cref="ImageLimits"/>, downscales and encodes
/// the image as PNG, and shrinks it (always from the original) until the base64 form fits the byte budget.
/// </summary>
public static class ImageFitter
{
    public const int TokenPatchSize = 28;
    public const double ShrinkFactor = 0.85;

    public static FittedImage Fit(BgraImage source, ImageLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= ImageLimits.Default;

        var (width, height) = GetTargetSize(source.Width, source.Height, limits);
        while (true)
        {
            var image = width == source.Width && height == source.Height
                ? source
                : ImageScaler.Downscale(source, width, height);
            var png = PngEncoder.Encode(image);
            var base64Length = Base64Length(png.Length);
            if (base64Length <= limits.MaxBase64Bytes)
            {
                return new FittedImage(png, width, height, source.Width, source.Height);
            }

            var longEdge = Math.Max(width, height);
            if (longEdge == 1)
            {
                throw new InvalidOperationException(
                    $"Even a 1x1 PNG ({base64Length} base64 bytes) exceeds the budget of {limits.MaxBase64Bytes} bytes.");
            }

            // PNG size grows roughly with the pixel count, so jump to the size that should fit
            // instead of re-encoding at every step; still shrink by at least ShrinkFactor.
            var factor = Math.Min(ShrinkFactor, Math.Sqrt((double)limits.MaxBase64Bytes / base64Length));
            var shrunk = Math.Min(longEdge - 1, (int)(longEdge * factor));
            (width, height) = SizeForLongEdge(source.Width, source.Height, Math.Max(1, shrunk));
        }
    }

    /// <summary>
    /// The largest size with the source aspect ratio (each side at least 1) that satisfies the
    /// long-edge, per-side and token limits. The byte budget is not considered here.
    /// </summary>
    public static (int Width, int Height) GetTargetSize(int sourceWidth, int sourceHeight, ImageLimits limits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentNullException.ThrowIfNull(limits);

        var maxLongEdge = Math.Max(sourceWidth, sourceHeight);
        if (limits.MaxLongEdge > 0)
        {
            maxLongEdge = Math.Min(maxLongEdge, limits.MaxLongEdge);
        }

        if (limits.MaxDimension > 0)
        {
            maxLongEdge = Math.Min(maxLongEdge, limits.MaxDimension);
        }

        var maxTokens = limits.MaxLongEdge > 0 ? limits.MaxVisualTokens : 0;
        if (maxTokens == 0 || FitsTokens(maxLongEdge))
        {
            return SizeForLongEdge(sourceWidth, sourceHeight, maxLongEdge);
        }

        // Token count grows monotonically with the long edge; a 1x1 image is a single token.
        int low = 1, high = maxLongEdge - 1;
        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (FitsTokens(mid))
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return SizeForLongEdge(sourceWidth, sourceHeight, low);

        bool FitsTokens(int longEdge)
        {
            var (w, h) = SizeForLongEdge(sourceWidth, sourceHeight, longEdge);
            return VisualTokens(w, h) <= maxTokens;
        }
    }

    /// <summary>Claude's image token count: one token per started 28x28 patch.</summary>
    public static long VisualTokens(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        return CeilDiv(width, TokenPatchSize) * CeilDiv(height, TokenPatchSize);
    }

    public static long Base64Length(long byteCount) => (byteCount + 2) / 3 * 4;

    private static (int Width, int Height) SizeForLongEdge(int sourceWidth, int sourceHeight, int longEdge)
    {
        if (sourceWidth >= sourceHeight)
        {
            return (longEdge, ScaleSide(sourceHeight, longEdge, sourceWidth));
        }

        return (ScaleSide(sourceWidth, longEdge, sourceHeight), longEdge);
    }

    /// <summary><c>round(side * numerator / denominator)</c>, at least 1.</summary>
    private static int ScaleSide(int side, int numerator, int denominator) =>
        (int)Math.Max(1, ((long)side * numerator * 2 + denominator) / (2L * denominator));

    private static long CeilDiv(int value, int divisor) => ((long)value + divisor - 1) / divisor;
}
