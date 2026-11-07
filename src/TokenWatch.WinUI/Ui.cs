using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace TokenWatch.WinUI;

internal static class Ui
{
    internal static SolidColorBrush Brush(string hex) => new(Color.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16)));
    internal static TextBlock Text(string text, double size = 12, string color = "#EEF0F5") => new() { Text = text, FontSize = size, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
    internal static FrameworkElement Logo(ProviderItem provider, double size = 24)
    {
        return new Image { Source = provider.LogoImage, Width = size, Height = size };
    }
    internal static void Place(Window window, int width, int height, bool right = false)
    {
        var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(window.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        var scale = (window.Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1;
        var pixelWidth = (int)Math.Round(width * scale); var pixelHeight = (int)Math.Round(height * scale);
        var area = display.WorkArea;
        window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(right ? area.X + area.Width - pixelWidth - 24 : area.X + Math.Max(0, (area.Width - pixelWidth) / 2), area.Y + Math.Max(12, (area.Height - pixelHeight) / 2), pixelWidth, Math.Min(pixelHeight, area.Height - 24)));
    }
}
