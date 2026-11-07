using System;
using System.Windows;
using System.Windows.Media;

namespace TokenWatch;

internal sealed class CountdownDonut : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(CountdownDonut),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var track = new Pen(new SolidColorBrush(Color.FromRgb(202, 212, 227)), 5);
        var blue = new Pen(new SolidColorBrush(Color.FromRgb(50, 109, 250)), 5);
        drawing.DrawEllipse(null, track, new Point(16, 16), 11.5, 11.5);
        var fraction = Math.Clamp(Fraction, 0, 1);
        if (fraction <= 0) return;
        if (fraction >= 0.999999)
        {
            drawing.DrawEllipse(null, blue, new Point(16, 16), 11.5, 11.5);
            return;
        }
        var angle = (1 - fraction) * 2 * Math.PI;
        var arc = new StreamGeometry();
        using (var context = arc.Open())
        {
            context.BeginFigure(new Point(16 + 11.5 * Math.Sin(angle), 16 - 11.5 * Math.Cos(angle)), false, false);
            context.ArcTo(new Point(16, 4.5), new Size(11.5, 11.5), 0, fraction > .5, SweepDirection.Clockwise, true, false);
        }
        arc.Freeze();
        drawing.DrawGeometry(null, blue, arc);
    }
}
