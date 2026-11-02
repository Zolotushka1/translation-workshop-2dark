namespace Workshop.Core;

/// <summary>The game's 16:9 canvas is centered inside differently sized native windows.</summary>
public static class ViewportPointer
{
    private static (double Width, double Height, double X, double Y) Canvas(int width, int height)
    {
        double w = Math.Max(1, width), h = Math.Max(1, height);
        double contentWidth = Math.Min(w, h * 16 / 9), contentHeight = Math.Min(h, w * 9 / 16);
        return (contentWidth, contentHeight, (w - contentWidth) / 2, (h - contentHeight) / 2);
    }
    public static (double X, double Y) Normalize(double x, double y, int width, int height)
    {
        var area = Canvas(width, height);
        return ((x - area.X) / area.Width, (y - area.Y) / area.Height);
    }
    public static (float X, float Y) Pixels(double x, double y, int width, int height)
    {
        var area = Canvas(width, height);
        return ((float)(area.X + x * area.Width), (float)(area.Y + y * area.Height));
    }
}
