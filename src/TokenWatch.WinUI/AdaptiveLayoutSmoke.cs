using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TokenWatch.WinUI;

internal static class AdaptiveLayoutSmoke
{
    internal static async Task RunAsync(WidgetWindow widget, string reportPath, Action<string, bool> check)
    {
        var root = widget.Surface; var session = App.Session;
        await ResizeSmoke.ResizeHeightAsync(widget, 1);
        var denseHeight = root.ActualHeight;
        var bars = Descendants<ProgressBar>(root).ToArray();
        var values = bars.Select(bar => bar.Value).ToArray();
        var changes = 0; var reloads = 0;
        RoutedEventHandler loaded = (_, _) => reloads++;
        var callbacks = bars.Select(bar =>
        {
            bar.Loaded += loaded;
            return bar.RegisterPropertyChangedCallback(ProgressBar.ValueProperty, (_, _) => changes++);
        }).ToArray();
        try
        {
            await PreviewSmoke.CaptureAsync(root, Path.ChangeExtension(reportPath, ".dense.png"));
            await ResizeSmoke.ResizeHeightAsync(widget, denseHeight + 180);
            check("Extra height enlarges quota bars and percentage text", bars.All(bar => bar.Height > 6) && Percentages(root).All(text => text.FontSize > 15));
            check("The visible quota indicators thicken with vertical expansion", bars.All(bar => Descendants<Microsoft.UI.Xaml.Shapes.Rectangle>(bar).Any(rectangle => rectangle.Name == "DeterminateProgressBarIndicator" && rectangle.ActualHeight > 6)));
            check("Expanded typography remains bounded and readable", Percentages(root).All(text => text.FontSize <= 19.5 && text.FontSize >= 15));
            check("Expanded quotas use the window height with the footer below them", FillsHeight(root));
            check("Vertical expansion retains quota controls and readings", bars.SequenceEqual(Descendants<ProgressBar>(root)) && bars.Select(bar => bar.Value).SequenceEqual(values) && changes == 0 && reloads == 0);
            var expanded = widget.AppWindow.Size;
            session.NotifyUsage(); session.NotifyUsage(); await Task.Delay(220);
            check("Usage notifications preserve expanded size and layout", widget.AppWindow.Size == expanded && FillsHeight(root) && changes == 0 && reloads == 0);
            await PreviewSmoke.CaptureAsync(root, Path.ChangeExtension(reportPath, ".expanded.png"));
        }
        finally
        {
            for (var index = 0; index < bars.Length; index++)
            { bars[index].UnregisterPropertyChangedCallback(ProgressBar.ValueProperty, callbacks[index]); bars[index].Loaded -= loaded; }
        }
        var hidden = session.Providers.Where(provider => provider.Id is "copilot" or "cursor").ToArray();
        foreach (var provider in hidden) provider.Enabled = false;
        await Task.Delay(220);
        check("Vertical layout adapts to three enabled providers", Descendants<ProgressBar>(root).Count() == 7 && FillsHeight(root) && Percentages(root).All(text => text.FontSize > 15));
        await PreviewSmoke.CaptureAsync(root, Path.ChangeExtension(reportPath, ".expanded-three-providers.png"));
        await ResizeSmoke.ResizeHeightAsync(widget, 1);
        check("Shrinking returns controls to compact sizes without a minimum-height ratchet", Descendants<ProgressBar>(root).All(bar => Math.Abs(bar.Height - 6) < .01) && Percentages(root).All(text => Math.Abs(text.FontSize - 15) < .01) && root.ActualHeight < denseHeight);
        // Native controls round their geometry to physical pixels at fractional DPI.
        check("Shrinking restores visible quota indicator thickness", Descendants<ProgressBar>(root).All(bar => Descendants<Microsoft.UI.Xaml.Shapes.Rectangle>(bar).Any(rectangle => rectangle.Name == "DeterminateProgressBarIndicator" && Math.Abs(rectangle.ActualHeight - 6) <= 1 / root.XamlRoot.RasterizationScale)));
        foreach (var provider in hidden) provider.Enabled = true;
        await Task.Delay(180);
        await ResizeSmoke.ResizeHeightAsync(widget, 1);
        check("Restored providers fit at compact density", Descendants<ProgressBar>(root).Count() == 10 && FillsHeight(root) && root.ActualHeight <= denseHeight + 2);
    }
    private static IEnumerable<TextBlock> Percentages(DependencyObject root) => Descendants<TextBlock>(root).Where(text => text.Text.EndsWith('%'));
    private static bool FillsHeight(FrameworkElement root)
    {
        var bars = Descendants<ProgressBar>(root).ToArray();
        var footer = Descendants<TextBlock>(root).First(text => text.Name == "BuildCaption");
        var footerTop = footer.TransformToVisual(root).TransformPoint(new(0, 0)).Y;
        var bottoms = bars.Select(bar => bar.TransformToVisual(root).TransformPoint(new(0, 0)).Y + bar.ActualHeight).ToArray();
        return bars.Length > 0 && bottoms.All(bottom => bottom < footerTop) && bottoms.Max() > root.ActualHeight - 100;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
