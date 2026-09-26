namespace ScreenshotMcp.Imaging;

/// <summary>An image placed at (<paramref name="X"/>, <paramref name="Y"/>) in virtual-screen coordinates.</summary>
public readonly record struct ImageTile(BgraImage Image, int X, int Y);
