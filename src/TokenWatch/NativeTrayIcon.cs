using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Drawing = System.Drawing;

namespace TokenWatch;

// Shell version 4 provides the same mouse and keyboard activation contract as other Windows tray icons.
internal sealed class NativeTrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private readonly HwndSource _source;
    private readonly Drawing.Icon _icon;
    private readonly string _tooltip;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private bool _added, _version4, _disposed;
    internal IntPtr Handle => _source.Handle;
    internal bool IsRegistered => _added;
    internal bool UsesVersion4 => _version4;
    internal event Action? Activated;
    internal event Action<int, int>? ContextRequested;

    internal NativeTrayIcon(string tooltip)
    {
        _tooltip = tooltip;
        using var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/TokenWatch.ico")).Stream;
        _icon = new Drawing.Icon(resource, System.Windows.Forms.SystemInformation.SmallIconSize);
        _source = new HwndSource(new HwndSourceParameters("TokenWatch tray")
        { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x80 });
        _source.AddHook(WindowMessage);
        AddIcon();
    }

    private NotifyIconData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = Handle, Id = 1,
        Flags = 1 | 2 | 4 | 0x80, Callback = CallbackMessage, Icon = _icon.Handle,
        Tip = _tooltip, Info = "", InfoTitle = ""
    };

    private void AddIcon()
    {
        var data = Data();
        _added = ShellNotifyIcon(0, ref data);
        if (!_added) return; // Explorer will send TaskbarCreated when its notification area is available again.
        data.Version = 4;
        _version4 = ShellNotifyIcon(4, ref data);
    }
    internal void RetryRegistration() { if (!_disposed && !_added) AddIcon(); }

    private IntPtr WindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed) return IntPtr.Zero;
        if ((uint)message == _taskbarCreated) { _added = false; AddIcon(); }
        if ((uint)message != CallbackMessage) return IntPtr.Zero;
        handled = true;
        var action = _version4 ? (uint)(lParam.ToInt64() & 0xFFFF) : (uint)lParam.ToInt64();
        if ((_version4 && action is 0x400 or 0x401) || (!_version4 && action == 0x202))
            _source.Dispatcher.BeginInvoke(new Action(() => { if (!_disposed) Activated?.Invoke(); }));
        else if ((_version4 && action == 0x7B) || (!_version4 && action == 0x205))
        {
            // WM_CONTEXTMENU's wParam is not a reliable screen anchor for every Shell version/input mode.
            var x = 0; var y = 0;
            var identity = new IconIdentity { Size = (uint)Marshal.SizeOf<IconIdentity>(), Window = Handle, Id = 1 };
            if (ShellNotifyIconGetRect(ref identity, out var bounds) == 0) { x = (bounds.Left + bounds.Right) / 2; y = bounds.Top; }
            else if (GetCursorPos(out var pointer)) { x = pointer.X; y = pointer.Y; }
            var menuX = x; var menuY = y;
            _source.Dispatcher.BeginInvoke(new Action(() => { if (!_disposed) ContextRequested?.Invoke(menuX, menuY); }));
        }
        return IntPtr.Zero;
    }

    internal void RestoreFocus()
    {
        if (!_added || _disposed) return;
        var data = Data(); ShellNotifyIcon(3, ref data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_added) { var data = Data(); ShellNotifyIcon(2, ref data); }
        _added = false; _version4 = false;
        _disposed = true;
        _source.RemoveHook(WindowMessage); _source.Dispose(); _icon.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconIdentity { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")] private static extern int ShellNotifyIconGetRect(ref IconIdentity identity, out Rect rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
}
