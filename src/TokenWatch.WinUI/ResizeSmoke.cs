using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace TokenWatch.WinUI;

internal static class ResizeSmoke
{
    internal static async Task ResizeHeightAsync(WidgetWindow widget, double height)
    {
        var window = widget.AppWindow;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(widget);
        var scale = widget.Surface.XamlRoot.RasterizationScale;
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Rect>());
        try
        {
            GetWindowRect(handle, out var rect);
            rect.Bottom = rect.Top + (int)Math.Round(height * scale) + window.Size.Height - window.ClientSize.Height;
            Marshal.StructureToPtr(rect, memory, false);
            SendMessage(handle, 0x231, IntPtr.Zero, IntPtr.Zero);
            SendMessage(handle, 0x214, new IntPtr(6), memory);
            rect = Marshal.PtrToStructure<Rect>(memory);
            window.MoveAndResize(new RectInt32(rect.Left, rect.Top, rect.Width, rect.Height));
            SendMessage(handle, 0x232, IntPtr.Zero, IntPtr.Zero);
            await Task.Delay(200);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    internal static async Task RunAsync(WidgetWindow widget, Action<string, bool> check)
    {
        var window = widget.AppWindow;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(widget);
        var position = window.Position; var size = window.Size;
        var scale = widget.Surface.XamlRoot.RasterizationScale;
        var margin = size.Width - window.ClientSize.Width;
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Rect>());
        try
        {
            var changes = 0;
            Windows.Foundation.TypedEventHandler<AppWindow, AppWindowChangedEventArgs> changed = (_, args) => { if (args.DidSizeChange) changes++; };
            window.Changed += changed;
            var frames = 0; var anchored = true; var bounds = true; var stable = true;
            try
            {
                SendMessage(handle, 0x231, IntPtr.Zero, IntPtr.Zero);
                foreach (var width in new[] { 380, 420, 480, 540, 800, 560, 510, 460, 400, 350, 300, 260, 240, 100, 220, 260, 300, 380 })
                {
                    GetWindowRect(handle, out var before);
                    var proposed = before; proposed.Right = proposed.Left + (int)Math.Round(width * scale) + margin;
                    proposed.Bottom = proposed.Top + size.Height + 80; // Drag diagonally to a chosen height.
                    Marshal.StructureToPtr(proposed, memory, false);
                    SendMessage(handle, 0x214, new IntPtr(8), memory);
                    var constrained = Marshal.PtrToStructure<Rect>(memory);
                    anchored &= constrained.Left == before.Left && constrained.Top == before.Top;
                    bounds &= constrained.Width >= Math.Ceiling(220 * scale) + margin && constrained.Width <= Math.Floor(560 * scale) + margin;
                    window.MoveAndResize(new RectInt32(constrained.Left, constrained.Top, constrained.Width, constrained.Height)); frames++;
                    await Task.Delay(34);
                    GetWindowRect(handle, out var actual);
                    stable &= actual.Equals(constrained);
                    App.Session.NotifyUsage(); // A provider observation during a drag must not resize the window.
                    await Task.Delay(16);
                    GetWindowRect(handle, out actual);
                    stable &= actual.Equals(constrained);
                }
                check("Diagonal native resizing keeps the opposite corner anchored", anchored);
                check("Native resizing enforces width limits before committing a frame", bounds);
                check("Diagonal resizing preserves a height taller than the quota contents", window.Size.Height >= size.Height + 80);
                check("Provider observations do not fight an active native resize", stable);
                check("Native resize frames do not trigger corrective resize storms", changes <= frames + 1);
                var finalSize = window.Size;
                SendMessage(handle, 0x232, IntPtr.Zero, IntPtr.Zero);
                await Task.Delay(250);
                check("Releasing the resize border does not snap to a different size", window.Size == finalSize);
                var settledChanges = changes;
                await Task.Delay(300);
                check("Resized layout settles without a continuing feedback loop", changes == settledChanges);
            }
            finally
            {
                SendMessage(handle, 0x232, IntPtr.Zero, IntPtr.Zero);
                window.Changed -= changed;
            }
            GetWindowRect(handle, out var start);
            SendMessage(handle, 0x231, IntPtr.Zero, IntPtr.Zero);
            var topLeft = start; topLeft.Left = topLeft.Right - (int)(240 * scale) - margin; topLeft.Top -= 120;
            Marshal.StructureToPtr(topLeft, memory, false);
            SendMessage(handle, 0x214, new IntPtr(4), memory);
            var result = Marshal.PtrToStructure<Rect>(memory);
            check("Top-left resizing preserves the bottom-right corner", result.Right == start.Right && result.Bottom == start.Bottom);
            check("Top-left resizing can increase height independently", result.Height >= start.Height + 120);
            window.MoveAndResize(new RectInt32(result.Left, result.Top, result.Width, result.Height));
            SendMessage(handle, 0x232, IntPtr.Zero, IntPtr.Zero);
            await Task.Delay(150);
            GetWindowRect(handle, out start);
            SendMessage(handle, 0x231, IntPtr.Zero, IntPtr.Zero);
            var vertical = start; vertical.Bottom = start.Bottom - 60;
            Marshal.StructureToPtr(vertical, memory, false);
            SendMessage(handle, 0x214, new IntPtr(6), memory);
            result = Marshal.PtrToStructure<Rect>(memory);
            check("Bottom resizing reduces height without changing width", result.Width == start.Width && result.Height == start.Height - 60);
            window.MoveAndResize(new RectInt32(result.Left, result.Top, result.Width, result.Height));
            SendMessage(handle, 0x232, IntPtr.Zero, IntPtr.Zero);
            await Task.Delay(200);
            var chosenSize = window.Size;
            App.Session.NotifyUsage(); await Task.Delay(180);
            check("Usage updates retain the chosen window height", window.Size == chosenSize);
            widget.SaveGeometry();
            var saved = PreviewSession.Load(App.Session.SettingsPath);
            check("User-selected height survives settings reload", saved.Backend.WidgetPreferredHeight == App.Session.Backend.WidgetPreferredHeight && saved.Backend.WidgetPreferredHeight is > 0);
            var caption = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(window.Id).GetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Caption);
            check("Title has a native caption region excluding header buttons", caption.Length == 1 && caption[0].Width > 20 && caption[0].X > 30 * scale && caption[0].X + caption[0].Width < window.ClientSize.Width - 80 * scale);
            SendMessage(handle, 0x231, IntPtr.Zero, IntPtr.Zero);
            window.Move(new PointInt32(window.Position.X + 20, window.Position.Y + 10));
            var moved = window.Position;
            SendMessage(handle, 0x232, IntPtr.Zero, IntPtr.Zero); await Task.Delay(150);
            check("Finishing a move retains the window position and dimensions", window.Position == moved && window.Size == chosenSize);
            var limitsMemory = Marshal.AllocHGlobal(40);
            try
            {
                for (var offset = 0; offset < 40; offset += 4) Marshal.WriteInt32(limitsMemory, offset, 0);
                SendMessage(handle, 0x24, IntPtr.Zero, limitsMemory);
                check("Windows receives native minimum and maximum track widths", Marshal.ReadInt32(limitsMemory, 24) == Math.Ceiling(220 * scale) + margin && Marshal.ReadInt32(limitsMemory, 32) == Math.Floor(560 * scale) + margin);
            }
            finally { Marshal.FreeHGlobal(limitsMemory); }
        }
        finally
        {
            SendMessage(handle, 0x232, IntPtr.Zero, IntPtr.Zero);
            Marshal.FreeHGlobal(memory);
            window.MoveAndResize(new RectInt32(position.X, position.Y, size.Width, size.Height));
            await Task.Delay(200);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
}
