using System;
using System.Windows;
using System.Windows.Media;

namespace TokenWatch;

public sealed class QuotaPaceMarker : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(nameof(Percent), typeof(double?), typeof(QuotaPaceMarker), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public double? Percent { get => (double?)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    protected override void OnRender(DrawingContext drawing)
    {
        if (Percent is not { } percent || !double.IsFinite(percent) || ActualWidth < 7) return;
        var x = Math.Clamp(ActualWidth * percent / 100, 3.5, ActualWidth - 3.5);
        drawing.DrawRoundedRectangle(Brushes.White, null, new Rect(x - 1, 2, 2, 8), 1, 1);
        var caret = new StreamGeometry();
        using (var context = caret.Open()) { context.BeginFigure(new Point(x, 12), true, true); context.LineTo(new Point(x - 3.5, 16), true, false); context.LineTo(new Point(x + 3.5, 16), true, false); }
        caret.Freeze(); drawing.DrawGeometry(Brushes.White, null, caret);
    }
}
