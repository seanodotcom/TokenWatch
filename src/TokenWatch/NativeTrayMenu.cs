using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace TokenWatch;

internal enum TrayCommand : uint { None, Show = 1001, Pin, Topmost, Refresh, Settings, Exit }

// A real HMENU: Windows supplies drawing, accessibility, keyboard navigation and checkmarks.
internal sealed class NativeTrayMenu : IDisposable
{
    internal IntPtr Handle { get; private set; }
    internal NativeTrayMenu(bool pinned, bool topmost, bool canRefresh)
    {
        Handle = CreatePopupMenu();
        if (Handle == IntPtr.Zero) throw new Win32Exception();
        try
        {
            Add(0, TrayCommand.Show, "&Show widget");
            Add(1, TrayCommand.Pin, "&Keep widget open", pinned ? 8u : 0);
            Add(2, TrayCommand.Topmost, "Always on &top", topmost ? 8u : 0);
            Add(3, TrayCommand.None, null);
            Add(4, TrayCommand.Refresh, "&Refresh usage", canRefresh ? 0u : 3);
            Add(5, TrayCommand.Settings, "S&ettings…");
            Add(6, TrayCommand.None, null);
            Add(7, TrayCommand.Exit, "E&xit TokenWatch");
            if (!SetMenuDefaultItem(Handle, (uint)TrayCommand.Show, false)) throw new Win32Exception();
        }
        catch { Dispose(); throw; }
    }

    private void Add(uint position, TrayCommand command, string? text, uint state = 0)
    {
        var item = new MenuItemInfo
        {
            Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = 0x143,
            Type = text is null ? 0x800u : 0, State = state, Id = (uint)command, Text = text
        };
        if (!InsertMenuItem(Handle, position, true, ref item)) throw new Win32Exception();
    }

    internal TrayCommand Show(IntPtr owner, int x, int y)
    {
        // Notification menus require foreground ownership and a benign post afterwards to reopen cleanly.
        SetForegroundWindow(owner);
        try
        {
            const uint returnCommand = 0x100, noNotify = 0x80, rightButton = 2, bottomAlign = 0x20, workArea = 0x10000;
            var alignment = GetSystemMetrics(40) == 0 ? 0u : 8u;
            return (TrayCommand)TrackPopupMenuEx(Handle, returnCommand | noNotify | rightButton | bottomAlign | workArea | alignment, x, y, owner, IntPtr.Zero);
        }
        finally { PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero); }
    }

    internal uint State(TrayCommand command) => GetMenuState(Handle, (uint)command, 0);
    internal int Count => GetMenuItemCount(Handle);
    internal TrayCommand Default => (TrayCommand)GetMenuDefaultItem(Handle, false, 0);
    internal string Label(TrayCommand command)
    {
        var text = new StringBuilder(128);
        GetMenuString(Handle, (uint)command, text, text.Capacity, 0);
        return text.ToString();
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        DestroyMenu(Handle); Handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MenuItemInfo
    {
        public uint Size, Mask, Type, State, Id;
        public IntPtr SubMenu, CheckedBitmap, UncheckedBitmap;
        public UIntPtr ItemData;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Text;
        public uint TextLength;
        public IntPtr ItemBitmap;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "InsertMenuItemW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InsertMenuItem(IntPtr menu, uint position, [MarshalAs(UnmanagedType.Bool)] bool byPosition, ref MenuItemInfo item);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint GetMenuDefaultItem(IntPtr menu, [MarshalAs(UnmanagedType.Bool)] bool byPosition, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuStringW")] private static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int count, uint flags);
}
