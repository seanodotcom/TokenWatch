using System;
using System.Windows;
using System.Windows.Controls;

namespace TokenWatch;

// Spread spare vertical space across providers rather than leaving a band above the footer.
internal sealed class ProviderFlowPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0d;
        var width = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            height += child.DesiredSize.Height;
            width = Math.Max(width, child.DesiredSize.Width);
        }
        return new(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var naturalHeight = 0d;
        foreach (UIElement child in InternalChildren) naturalHeight += child.DesiredSize.Height;
        var extra = InternalChildren.Count == 0 ? 0 : Math.Max(0, finalSize.Height - naturalHeight) / InternalChildren.Count;
        var top = 0d;
        foreach (UIElement child in InternalChildren)
        {
            var height = child.DesiredSize.Height + extra;
            child.Arrange(new Rect(0, top, finalSize.Width, height));
            top += height;
        }
        return finalSize;
    }
}
