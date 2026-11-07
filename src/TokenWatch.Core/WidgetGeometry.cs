namespace TokenWatch.Core;

[Flags]
public enum ResizeEdges
{
    Left = 1, Top = 2, Right = 4, Bottom = 8,
    TopLeft = Top | Left, TopRight = Top | Right, BottomLeft = Bottom | Left, BottomRight = Bottom | Right
}

public readonly record struct WidgetRectangle(double Left, double Top, double Width, double Height);

public static class WidgetGeometry
{
    public const double MinimumWidth = 220;
    public const double MinimumHeight = 1; // Content determines height at each responsive width.
    public const double DesignWidth = 410;
    public const double DefaultWidth = 410;
    public const double MaximumWidth = 560;
    public const double MaximumHeight = 1000;

    public static double ClampWidth(double value) => double.IsFinite(value) ? Math.Clamp(value, MinimumWidth, MaximumWidth) : DefaultWidth;
    // The actual minimum height depends on the measured content, not the old fixed window height.
    public static double ClampHeight(double value) => double.IsFinite(value) ? Math.Clamp(value, 1, MaximumHeight) : MaximumHeight;

    public static WidgetRectangle Resize(WidgetRectangle bounds, ResizeEdges edge, double dx, double dy,
        double maximumWidth = MaximumWidth, double maximumHeight = MaximumHeight)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return bounds;
        var horizontal = edge.HasFlag(ResizeEdges.Left) || edge.HasFlag(ResizeEdges.Right);
        var vertical = edge.HasFlag(ResizeEdges.Top) || edge.HasFlag(ResizeEdges.Bottom);
        var xChange = (edge.HasFlag(ResizeEdges.Left) ? -dx : dx) / bounds.Width;
        var yChange = (edge.HasFlag(ResizeEdges.Top) ? -dy : dy) / bounds.Height;
        // A corner follows the stronger drag axis; every edge preserves the same aspect ratio.
        var change = horizontal && (!vertical || Math.Abs(xChange) >= Math.Abs(yChange)) ? xChange : vertical ? yChange : 0;
        var maximumScale = Math.Min(maximumWidth / bounds.Width, maximumHeight / bounds.Height);
        var minimumScale = Math.Min(MinimumWidth / bounds.Width, maximumScale);
        var scale = Math.Clamp(1 + change, minimumScale, maximumScale);
        var width = bounds.Width * scale;
        var height = bounds.Height * scale;
        var left = edge.HasFlag(ResizeEdges.Left) ? bounds.Left + bounds.Width - width : bounds.Left;
        var top = edge.HasFlag(ResizeEdges.Top) ? bounds.Top + bounds.Height - height : bounds.Top;
        return new(left, top, width, height);
    }
}
