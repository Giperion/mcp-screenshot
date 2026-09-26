namespace ScreenshotMcp.Imaging;

/// <summary>A stitched image whose top-left pixel is (<paramref name="OriginX"/>, <paramref name="OriginY"/>) on the virtual screen.</summary>
public sealed record CompositeImage(BgraImage Image, int OriginX, int OriginY);
