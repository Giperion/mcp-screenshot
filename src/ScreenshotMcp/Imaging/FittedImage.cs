namespace ScreenshotMcp.Imaging;

/// <summary>A PNG sized for a model, with what is needed to map image points back to the source.</summary>
public sealed record FittedImage(byte[] Png, int Width, int Height, int SourceWidth, int SourceHeight)
{
    /// <summary>Output width / source width. A source coordinate is an image coordinate divided by it.</summary>
    public double Scale => (double)Width / SourceWidth;

    public bool IsResized => Width != SourceWidth || Height != SourceHeight;
}
