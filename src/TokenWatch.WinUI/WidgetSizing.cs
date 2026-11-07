using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TokenWatch.WinUI;

// Constrain Windows' proposed drag rectangle instead of issuing a competing resize.
// WM_SIZING: https://learn.microsoft.com/windows/win32/winmsg/wm-sizing
internal sealed class WidgetSizing : IDisposable
{
    internal const double MinimumWidth = 220, MaximumWidth = 560;
    private readonly IntPtr _window;
    private readonly SubclassProc _callback;
    private readonly Func<double> _scale;
    private readonly Func<double, double> _contentHeight;
    private readonly Action _finished;
    private bool _disposed;
    internal bool IsSizing { get; private set; }
    internal double? PreferredHeight { get; private set; }
    internal double HeightForWidth(double width) => Math.Max(_contentHeight(width), PreferredHeight ?? 0);

    internal WidgetSizing(IntPtr window, Func<double> scale, Func<double, double> contentHeight, Action finished, double? preferredHeight = null)
    {
        _window = window; _scale = scale; _contentHeight = contentHeight; _finished = finished;
        _callback = WindowMessage;
        PreferredHeight = preferredHeight is > 0 && double.IsFinite(preferredHeight.Value) ? preferredHeight : null;
        if (!SetWindowSubclass(window, _callback, 1, 0)) throw new Win32Exception("Could not install widget sizing handler.");
    }

    private IntPtr WindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (message == 0x82) { Dispose(); return DefSubclassProc(window, message, wParam, lParam); } // WM_NCDESTROY
        if (_disposed) return DefSubclassProc(window, message, wParam, lParam);
        if (message == 0x231) IsSizing = true; // WM_ENTERSIZEMOVE
        if (message == 0x232) { IsSizing = false; _finished(); } // WM_EXITSIZEMOVE
        if (message == 0x24 && lParam != IntPtr.Zero) // WM_GETMINMAXINFO
        {
            var result = DefSubclassProc(window, message, wParam, lParam);
            var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var (borderWidth, _) = Borders(); var scale = _scale();
            limits.MinTrack.X = (int)Math.Ceiling(MinimumWidth * scale) + borderWidth;
            limits.MaxTrack.X = (int)Math.Floor(MaximumWidth * scale) + borderWidth;
            Marshal.StructureToPtr(limits, lParam, false);
            return result;
        }
        if (message == 0x214 && lParam != IntPtr.Zero) // WM_SIZING
        {
            var rect = Marshal.PtrToStructure<Rect>(lParam);
            var (borderWidth, borderHeight) = Borders(); var scale = _scale();
            var clientWidth = Math.Clamp(rect.Right - rect.Left - borderWidth,
                (int)Math.Ceiling(MinimumWidth * scale), (int)Math.Floor(MaximumWidth * scale));
            var width = clientWidth + borderWidth;
            var edge = wParam.ToInt32();
            // Side drags change width; top/bottom/corner drags also select a height.
            if (edge is >= 3 and <= 8) PreferredHeight = Math.Max(1, (rect.Bottom - rect.Top - borderHeight) / scale);
            var height = (int)Math.Ceiling(HeightForWidth(clientWidth / scale) * scale) + borderHeight;
            // The edge opposite the pointer remains anchored, including top/left corners.
            if (edge is 1 or 4 or 7) rect.Left = rect.Right - width; else rect.Right = rect.Left + width;
            if (edge is 3 or 4 or 5) rect.Top = rect.Bottom - height; else rect.Bottom = rect.Top + height;
            Marshal.StructureToPtr(rect, lParam, false);
            return new IntPtr(1);
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }
    private (int Width, int Height) Borders()
    {
        GetWindowRect(_window, out var outer); GetClientRect(_window, out var client);
        return (outer.Right - outer.Left - client.Right, outer.Bottom - outer.Top - client.Bottom);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; IsSizing = false;
        RemoveWindowSubclass(_window, _callback, 1);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public Point Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr window, out Rect rect);
}
