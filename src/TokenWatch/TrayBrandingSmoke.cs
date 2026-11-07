using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;

namespace TokenWatch;

internal static class TrayBrandingSmoke
{
    internal static async Task RunAsync(MainWindow window, Action<string, bool> check)
    {
        var app = (App)Application.Current;
        check("Tray has a native Shell host", app.HasNativeTrayHost);
        check("Shell accepted the notification icon", app.HasRegisteredTrayIcon);
        check("Tray uses Windows version-4 keyboard notifications", app.HasKeyboardTraySupport);
        using (var menu = app.BuildTrayMenu())
        {
            check("Tray menu is a real Windows HMENU", IsMenu(menu.Handle) && menu.Count == 8);
            check("Native default command opens the widget", menu.Default == TrayCommand.Show);
            check("Native menu contains Settings and Exit", menu.Label(TrayCommand.Settings).Contains("ettings") && menu.Label(TrayCommand.Exit).Contains("TokenWatch"));
            check("Native checkmarks reflect current preferences", ((menu.State(TrayCommand.Pin) & 8) != 0) == window.Preferences.Pinned && ((menu.State(TrayCommand.Topmost) & 8) != 0) == window.Preferences.AlwaysOnTop);
            check("Sample-mode refresh is disabled by Windows", (menu.State(TrayCommand.Refresh) & 3) == 3);
            check("Menu uses system rendering rather than owner drawing", (menu.State(TrayCommand.Show) & 0x100) == 0);
            var handle = menu.Handle;
            menu.Dispose();
            check("Native menu handle is released", !IsMenu(handle));
        }
        using (var enabled = new NativeTrayMenu(false, false, true))
            check("Live-mode native refresh is enabled", (enabled.State(TrayCommand.Refresh) & 3) == 0);
        var pinned = window.Preferences.Pinned;
        app.ExecuteTrayCommand(TrayCommand.Pin);
        using (var updated = app.BuildTrayMenu())
            check("Pin menu command applies and updates the next native menu", window.Preferences.Pinned != pinned && ((updated.State(TrayCommand.Pin) & 8) != 0) == window.Preferences.Pinned);
        app.ExecuteTrayCommand(TrayCommand.Pin);
        var topmost = window.Topmost;
        app.ExecuteTrayCommand(TrayCommand.Topmost);
        using (var updated = app.BuildTrayMenu())
            check("Topmost menu command updates the native window and checkmark", window.Topmost != topmost && ((updated.State(TrayCommand.Topmost) & 8) != 0) == window.Topmost);
        app.ExecuteTrayCommand(TrayCommand.Topmost);
        window.HideWidget();
        // Deliver a Shell callback to this app's own hidden host; no actual keyboard/mouse input is sent.
        SendMessage(app.NativeTrayHandle, 0x8001, IntPtr.Zero, new IntPtr((1 << 16) | 0x401));
        await Dispatcher.Yield(DispatcherPriority.Background);
        check("Shell keyboard activation opens the widget", window.IsVisible);
        window.HideWidget(); app.ExecuteTrayCommand(TrayCommand.Show);
        check("Native Show command restores the widget", window.IsVisible);

        var logo = (Grid)window.FindName("HeaderLogo");
        var countdown = (CountdownDonut)window.FindName("CountdownArc");
        check("Header has a square countdown donut", logo.Width == logo.Height && countdown.Fraction is >= 0 and <= 1 && logo.ToolTip is string);
        check("Widget and settings use the branded window icon", window.Icon is not null && new SettingsWindow(window.Preferences).Icon is not null);
        using (var png = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/tokenwatch.png")).Stream)
        using (var bitmap = new Drawing.Bitmap(png))
            check("Header artwork has a transparent donut center and bright remainder", IsDonut(bitmap));
        var frames = new List<(int Size, uint Length, uint Offset)>();
        using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/TokenWatch.ico")).Stream)
        using (var reader = new BinaryReader(stream))
        {
            check("Application ICO contains all nine DPI sizes", reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1 && reader.ReadUInt16() == 9);
            for (var i = 0; i < 9; i++)
            {
                var width = reader.ReadByte(); reader.ReadByte(); reader.ReadByte(); reader.ReadByte();
                reader.ReadUInt16(); reader.ReadUInt16();
                frames.Add((width == 0 ? 256 : width, reader.ReadUInt32(), reader.ReadUInt32()));
            }
            foreach (var frame in frames)
            {
                stream.Position = frame.Offset;
                using var png = new MemoryStream(reader.ReadBytes(checked((int)frame.Length)));
                using var bitmap = new Drawing.Bitmap(png);
                check($"Donut remains legible with transparent edges at {frame.Size}px", bitmap.Width == frame.Size && bitmap.Height == frame.Size && IsDonut(bitmap));
            }
        }
        foreach (var size in new[] { 16, 24, 32 })
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/TokenWatch.ico")).Stream;
            using var icon = new Drawing.Icon(stream, size, size);
            using var bitmap = icon.ToBitmap();
            check($"Windows HICON decoding selects the correct {size}px tray frame", bitmap.Width == size && IsDonut(bitmap));
        }
    }

    private static bool IsDonut(Drawing.Bitmap bitmap)
    {
        var size = bitmap.Width;
        var blue = bitmap.GetPixel((int)(size * .22), size / 4);
        var gray = bitmap.GetPixel(size * 3 / 4, size / 4);
        return bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(size / 2, size / 2).A == 0 &&
            blue.A > 180 && gray.A > 180 && blue.B > blue.R + 100 && gray.R > blue.R + 100 && gray.G > blue.G + 50;
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
