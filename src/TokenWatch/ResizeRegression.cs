using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using TokenWatch.Core;

namespace TokenWatch;

internal static class ResizeRegression
{
    internal static async Task RunAsync(MainWindow window, string reportPath, Action<string, bool> check)
    {
        var originalReader = window.ReadResizePointer;
        var corner = (Thumb)window.FindName("ResizeCorner");
        window.FitPreferredHeight(null);
        window.ApplyResize(ResizeEdges.Right, 410 - window.Width, 0); window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var pointer = corner.PointToScreen(new Point(7, 7));
        window.ReadResizePointer = () => pointer;
        corner.RaiseEvent(new DragStartedEventArgs(7, 7) { RoutedEvent = Thumb.DragStartedEvent });
        var previous = window.Width; var stable = true;
        for (var i = 0; i < 95; i++)
        {
            pointer.X -= 2 * dpi.DpiScaleX;
            corner.RaiseEvent(new DragDeltaEventArgs(-2, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout();
            stable &= window.Width <= previous + .01 && Math.Abs(window.Width - previous) < 3;
            previous = window.Width;
        }
        var stopped = window.Width;
        for (var i = 0; i < 8; i++) corner.RaiseEvent(new DragDeltaEventArgs(999, 999) { RoutedEvent = Thumb.DragDeltaEvent });
        await Dispatcher.Yield(DispatcherPriority.Background);
        check("Stationary screen pointer cannot amplify a resize delta", window.Width == stopped);
        corner.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        check("Continuous corner resizing reaches minimum width monotonically", stable && Math.Abs(window.Width - 220) < 1);
        await Task.Delay(230);
        check("Provider identity regains full contrast after resize", (double)window.Resources["ProviderHeaderOpacity"] == 1 || (double)window.Resources["ProviderIconOpacity"] == 1);
        check("Resting title is one complete word", ((Viewbox)window.FindName("FullTitleView")).Opacity == 1 || ((Viewbox)window.FindName("CompactTitleView")).Opacity == 1);
        window.FitPreferredHeight(null); window.ApplyResize(ResizeEdges.Right, 410 - window.Width, 0); window.UpdateLayout();
        var compactHeight = window.Height;
        var compactFont = (double)window.Resources["QuotaFontSize"];
        window.ApplyResize(ResizeEdges.Bottom, 0, 150); window.UpdateLayout();
        check("Bottom edge changes height independently of width", Math.Abs(window.Width - 410) < 1 && window.Height > compactHeight + 20);
        var tallHeight = window.Height;
        check("Extra height enlarges text within thirty percent", (double)window.Resources["QuotaFontSize"] > compactFont && (double)window.Resources["QuotaFontSize"] <= compactFont * 1.3 + .01);
        check("Extra height increases quota bar thickness", (double)window.Resources["QuotaBarHeight"] > 8);
        window.HideWidget(); window.ShowWidget(); window.UpdateLayout();
        check("Showing the widget retains its chosen height", Math.Abs(window.Height - tallHeight) < 1);
        check("Saved preferred height matches the independent height", Math.Abs(Settings.Load().WidgetPreferredHeight.GetValueOrDefault() - tallHeight) < 1);
        window.Preferences.ShowRemaining = !window.Preferences.ShowRemaining; window.SavePreferences(); window.UpdateLayout();
        check("Usage display updates preserve the chosen height", Math.Abs(window.Height - tallHeight) < 1);
        window.Preferences.ShowRemaining = !window.Preferences.ShowRemaining; window.SavePreferences();
        var before = new WidgetRectangle(window.Left, window.Top, window.Width, window.Height);
        window.ApplyResize(ResizeEdges.TopLeft, 20, 10); window.UpdateLayout();
        check("Top-left resize anchors the opposite corner", Math.Abs(window.Left + window.Width - before.Left - before.Width) < 1 && Math.Abs(window.Top + window.Height - before.Top - before.Height) < 1);
        pointer = corner.PointToScreen(new Point(7, 7));
        corner.RaiseEvent(new DragStartedEventArgs(7, 7) { RoutedEvent = Thumb.DragStartedEvent });
        var cancelOrigin = new WidgetRectangle(window.Left, window.Top, window.Width, window.Height);
        pointer.Offset(-20 * dpi.DpiScaleX, 20 * dpi.DpiScaleY);
        corner.RaiseEvent(new DragDeltaEventArgs(-20, 20) { RoutedEvent = Thumb.DragDeltaEvent });
        await Dispatcher.Yield(DispatcherPriority.Background);
        corner.RaiseEvent(new DragCompletedEventArgs(0, 0, true) { RoutedEvent = Thumb.DragCompletedEvent });
        check("Cancelling a resize restores width height and position", Math.Abs(window.Left - cancelOrigin.Left) <= 1 && Math.Abs(window.Top - cancelOrigin.Top) <= 1 && Math.Abs(window.Width - cancelOrigin.Width) <= 1 && Math.Abs(window.Height - cancelOrigin.Height) <= 1);
        window.FitPreferredHeight(null); window.ApplyResize(ResizeEdges.Right, 410 - window.Width, 0); window.UpdateLayout();
        check("Returning to content height restores compact text and bars", Math.Abs((double)window.Resources["QuotaFontSize"] - compactFont) < .01 && (double)window.Resources["QuotaBarHeight"] == 8);
        foreach (var width in new[] { 220, 240, 300, 340, 370, 410, 560 })
        {
            window.ApplyResize(ResizeEdges.Right, width - window.Width, 0); window.UpdateLayout();
            var bars = Children<ProgressBar>(window).ToArray();
            check($"Quota bars stay within window at {width} DIPs", bars.Length > 0 && bars.All(b => b.TransformToAncestor(window).TransformBounds(new Rect(0, 0, b.ActualWidth, b.ActualHeight)).Bottom <= window.ActualHeight + 1));
        }
        window.Left = -30000; window.Top = -30000; window.ShowWidget(); window.UpdateLayout();
        check("Off-screen saved positions recover onto an available monitor", window.Left > -30000 && window.Top > -30000);
        window.ReadResizePointer = originalReader;
        window.FitPreferredHeight(null); window.ApplyResize(ResizeEdges.Right, 410 - window.Width, 0); window.UpdateLayout();
    }
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
}
