using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace TokenWatch.WinUI;

internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private readonly IntPtr _window, _icon;
    private readonly WindowProc _procedure;
    private readonly string _className = "TokenWatch.TrayHost." + Environment.ProcessId;
    private readonly IntPtr _instance = GetModuleHandle(null);
    private readonly DispatcherQueue _queue;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private bool _disposed;
    internal bool IsRegistered { get; private set; }
    internal bool UsesVersion4 { get; private set; }
    internal IntPtr Handle => _window;
    internal bool HasShellRectangle
    {
        get { var identity = new IconIdentity { Size = (uint)Marshal.SizeOf<IconIdentity>(), Window = _window, Id = 1 }; return ShellNotifyIconGetRect(ref identity, out _) == 0; }
    }
    internal event Action? Activated;
    internal event Action<int, int>? ContextRequested;
    internal TrayIcon()
    {
        _queue = DispatcherQueue.GetForCurrentThread(); _procedure = WindowMessage;
        var windowClass = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = Marshal.GetFunctionPointerForDelegate(_procedure), Instance = _instance, ClassName = _className };
        if (RegisterClassEx(ref windowClass) == 0) throw new Win32Exception();
        _window = CreateWindowEx(0x80, _className, "TokenWatch tray", 0x80000000, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
        if (_window == IntPtr.Zero) { UnregisterClass(_className, _instance); throw new Win32Exception(); }
        _icon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "TokenWatch.ico"), 1, GetSystemMetrics(49), GetSystemMetrics(50), 0x10);
        if (_icon == IntPtr.Zero) { DestroyWindow(_window); UnregisterClass(_className, _instance); throw new Win32Exception(); }
        RetryRegistration();
    }
    private NotifyIconData Data() => new() { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = 1, Flags = 1 | 2 | 4 | 0x80,
        Callback = CallbackMessage, Icon = _icon, Tip = $"TokenWatch {TokenWatch.AppVersion.Display} · {(App.Session.IsDemo ? "Sample data" : "AI usage")}", Info = "", InfoTitle = "" };
    internal void RetryRegistration()
    {
        if (_disposed || IsRegistered) return;
        var data = Data(); IsRegistered = ShellNotifyIcon(0, ref data);
        if (IsRegistered) { data.Version = 4; UsesVersion4 = ShellNotifyIcon(4, ref data); }
    }
    private IntPtr WindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (!_disposed && message == _taskbarCreated) { IsRegistered = false; RetryRegistration(); }
        if (!_disposed && message == CallbackMessage)
        {
            var action = UsesVersion4 ? (uint)(lParam.ToInt64() & 0xFFFF) : (uint)lParam.ToInt64();
            if ((UsesVersion4 && action is 0x400 or 0x401) || (!UsesVersion4 && action == 0x202))
                _queue.TryEnqueue(() => { if (!_disposed) Activated?.Invoke(); });
            else if ((UsesVersion4 && action == 0x7B) || (!UsesVersion4 && action == 0x205))
            {
                var x = 0; var y = 0;
                var identity = new IconIdentity { Size = (uint)Marshal.SizeOf<IconIdentity>(), Window = _window, Id = 1 };
                if (ShellNotifyIconGetRect(ref identity, out var rect) == 0) { x = (rect.Left + rect.Right) / 2; y = rect.Top; }
                else if (GetCursorPos(out var pointer)) { x = pointer.X; y = pointer.Y; }
                var menuX = x; var menuY = y;
                _queue.TryEnqueue(() => { if (!_disposed) ContextRequested?.Invoke(menuX, menuY); });
            }
            return IntPtr.Zero;
        }
        return DefWindowProc(window, message, wParam, lParam);
    }
    internal void RestoreFocus() { if (IsRegistered && !_disposed) { var data = Data(); ShellNotifyIcon(3, ref data); } }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (IsRegistered) { var data = Data(); ShellNotifyIcon(2, ref data); }
        IsRegistered = false; DestroyWindow(_window); UnregisterClass(_className, _instance); DestroyIcon(_icon);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
    {
        public uint Size, Style; public IntPtr Procedure; public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background; public string? MenuName, ClassName; public IntPtr SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyIconData
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
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")] private static extern int ShellNotifyIconGetRect(ref IconIdentity identity, out Rect rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW", SetLastError = true)] private static extern IntPtr LoadImage(IntPtr instance, string path, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW", SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW")] private static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)] private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameters);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
