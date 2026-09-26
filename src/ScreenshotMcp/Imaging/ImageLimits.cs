namespace ScreenshotMcp.Imaging;

/// <summary>
/// Size limits for images returned to a model. Defaults follow Claude's vision limits: larger
/// images are downsized by the model anyway, so sending them only costs bytes.
/// </summary>
public sealed record ImageLimits
{
    public static ImageLimits Default { get; } = new();

    /// <summary>
    /// Longest side in pixels. 0 keeps the native resolution and also disables
    /// <see cref="MaxVisualTokens"/>; <see cref="MaxDimension"/> and the byte budget still apply.
    /// </summary>
    public int MaxLongEdge
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 2576;

    /// <summary>Visual tokens, <c>ceil(w / 28) * ceil(h / 28)</c>. 0 means no limit.</summary>
    public int MaxVisualTokens
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 4784;

    /// <summary>
    /// Hard per-side limit: the Claude API rejects images larger than 8000 px on either side,
    /// so this applies even when <see cref="MaxLongEdge"/> is 0. 0 means no limit.
    /// </summary>
    public int MaxDimension
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 8000;

    /// <summary>Maximum length of the base64-encoded PNG.</summary>
    public int MaxBase64Bytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 4_500_000;
}
