using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Clothesline.Interop;

/// <summary>Win32 entry points. Kept in one file so it's obvious what Clothesline asks of the OS.</summary>
internal static partial class Native
{
    // ───────────── structs ─────────────
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public readonly int Width => Right - Left; public readonly int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    // ───────────── user32 ─────────────
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT p);
    [LibraryImport("user32.dll")] public static partial uint GetDoubleClickTime();
    [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLastInputInfo(ref LASTINPUTINFO info);

    /// <summary>How long since the last mouse or keyboard input in this session; zero when Windows can't say.</summary>
    public static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime)) : TimeSpan.Zero;
    }
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static partial IntPtr GetWindowLongPtr(IntPtr h, int i);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static partial IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr h);
    [LibraryImport("user32.dll")] public static partial IntPtr GetForegroundWindow();
    [LibraryImport("user32.dll")] public static partial uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [LibraryImport("user32.dll")] public static partial uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [LibraryImport("kernel32.dll")] public static partial uint GetCurrentThreadId();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint a, uint b, [MarshalAs(UnmanagedType.Bool)] bool attach);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(IntPtr h);
    [LibraryImport("user32.dll")] public static partial void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [LibraryImport("user32.dll")] public static partial IntPtr MonitorFromPoint(POINT pt, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO info);
    [LibraryImport("user32.dll")] public static partial IntPtr GetDC(IntPtr h);
    [LibraryImport("user32.dll")] public static partial int ReleaseDC(IntPtr h, IntPtr dc);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr h);
    [LibraryImport("user32.dll")] public static partial IntPtr CreateIconFromResourceEx(IntPtr data, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint ver, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] public static partial int GetSystemMetricsForDpi(int index, uint dpi);
    [LibraryImport("user32.dll")] public static partial uint GetDpiForSystem();
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string name);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr h);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr h);
    [LibraryImport("user32.dll")] public static partial IntPtr GetWindow(IntPtr h, uint cmd);
    [LibraryImport("user32.dll")] public static partial IntPtr GetTopWindow(IntPtr h);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr h, out RECT r);
    [LibraryImport("user32.dll")] public static partial uint GetDpiForWindow(IntPtr h);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, delegate* unmanaged<IntPtr, IntPtr, RECT*, IntPtr, int> proc, IntPtr data);
    [LibraryImport("shcore.dll")] private static partial int GetDpiForMonitor(IntPtr hMon, int type, out uint dpiX, out uint dpiY);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312, WM_DISPLAYCHANGE = 0x007E, WM_SETTINGCHANGE = 0x001A, WM_DPICHANGED = 0x02E0;
    public const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    public const uint GW_HWNDNEXT = 2;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    // ───────────── gdi32 ─────────────
    [LibraryImport("gdi32.dll")] public static partial IntPtr CreateCompatibleDC(IntPtr dc);
    [LibraryImport("gdi32.dll")] public static partial IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [LibraryImport("gdi32.dll")] public static partial IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(IntPtr dc);
    public const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

    // ───────────── dwmapi ─────────────
    [LibraryImport("dwmapi.dll")] public static partial int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [LibraryImport("dwmapi.dll")] public static partial int DwmGetWindowAttribute(IntPtr h, int attr, out RECT value, int size);
    [LibraryImport("dwmapi.dll")] public static partial int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [LibraryImport("dwmapi.dll")] public static partial int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
    public const int DWMWA_CLOAKED = 14, DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
        DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_SYSTEMBACKDROP_TYPE = 38;

    // ───────────── DWM live thumbnails (Alt-Tab preview) ─────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }
    public const uint DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_RECTSOURCE = 0x2, DWM_TNP_OPACITY = 0x4,
        DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [LibraryImport("dwmapi.dll")] public static partial int DwmRegisterThumbnail(IntPtr hwndDestination, IntPtr hwndSource, out IntPtr phThumbnailId);
    [LibraryImport("dwmapi.dll")] public static partial int DwmUnregisterThumbnail(IntPtr hThumbnailId);
    [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnailId, ref DWM_THUMBNAIL_PROPERTIES ptnProperties);

    [LibraryImport("user32.dll")] public static partial IntPtr WindowFromPoint(POINT p);
    [LibraryImport("user32.dll")] public static partial IntPtr GetAncestor(IntPtr h, uint flags);
    public const uint GA_ROOT = 2;
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static partial IntPtr GetWindowLongPtrW(IntPtr h, int i);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] public static partial int GetWindowTextLength(IntPtr h);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetWindowText(IntPtr h, [Out] char[] buf, int max);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool IsWindow(IntPtr h);
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr param);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool UnhookWinEvent(IntPtr hWinEventHook);
    public const uint EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_HIDE = 0x8003, WINEVENT_OUTOFCONTEXT = 0x0;
    public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    // ───────────── kernel32 / psapi ─────────────
    [LibraryImport("kernel32.dll")] public static partial IntPtr GetCurrentProcess();
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessWorkingSetSizeEx(IntPtr proc, nint min, nint max, uint flags);

    // ───────────── process / window launching ─────────────
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    [LibraryImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)] public static partial IntPtr OpenProcessHandle(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessPath(IntPtr h, [MarshalAs(UnmanagedType.Bool)] bool nameEx, System.Text.StringBuilder buf, ref uint size);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr h);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr h, int cmd);
    public const int SW_RESTORE = 9, SW_SHOW = 5, SW_SHOWNORMAL = 1, SW_MINIMIZE = 6;

    // ───────────── helpers ─────────────

    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).Handle;

    /// <summary>Hide from Alt+Tab / taskbar; optionally let every mouse event fall through.</summary>
    public static void MakeToolWindow(Window w, bool clickThrough = false, bool noActivate = false)
    {
        var h = Handle(w);
        long s = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        s = (s | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        if (clickThrough) s |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        if (noActivate || clickThrough) s |= WS_EX_NOACTIVATE;
        SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(s));
    }

    /// <summary>Cursor position in the device-independent units of <paramref name="reference"/>.</summary>
    public static Point CursorDip(Visual reference)
    {
        GetCursorPos(out var p);
        var src = PresentationSource.FromVisual(reference);
        var pt = new Point(p.X, p.Y);
        return src?.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.Transform(pt) : pt;
    }

    /// <summary>
    /// Brings a window to the front even when Clothesline isn't the active app (e.g. after a global shortcut).
    /// Windows normally refuses this; attaching to the foreground thread's input queue is the documented workaround.
    /// </summary>
    public static void ForceForeground(Window w)
    {
        var h = Handle(w);
        if (h == IntPtr.Zero) return;
        var fg = GetForegroundWindow();
        if (fg == h) { w.Activate(); return; }
        uint fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero), me = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        try
        {
            if (!attached)
            {
                // Fallback: a synthetic Alt tap lifts the foreground lock.
                keybd_event(0x12, 0, 0, UIntPtr.Zero);
                keybd_event(0x12, 0, 2, UIntPtr.Zero);
            }
            BringWindowToTop(h);
            SetForegroundWindow(h);
            w.Activate();
        }
        finally { if (attached) AttachThreadInput(me, fgThread, false); }
    }

    // ───────────── shell32 — file/EXE icons ─────────────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }
    public const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10;
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, int cbFileInfo, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint QueryDosDevice(string? dev, System.Text.StringBuilder buf, uint size);

    /// <summary>The process executable path of the process that owns a window, or null when it can't be read.</summary>
    public static string? GetProcessPath(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;
        IntPtr h = OpenProcessHandle(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var buf = new System.Text.StringBuilder(1024);
            uint size = (uint)buf.Capacity;
            if (!QueryFullProcessPath(h, true, buf, ref size)) return null;
            string nt = buf.ToString();
            return NtPathToWin32(nt);
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Turns an NT device path (\Device\HarddiskVolume3\...) into a Win32 drive path (C:\...).</summary>
    private static string NtPathToWin32(string nt)
    {
        if (string.IsNullOrEmpty(nt) || nt.Length >= 2 && nt[1] == ':') return nt; // already C:\...
        for (char d = 'A'; d <= 'Z'; d++)
        {
            var dev = new System.Text.StringBuilder(512);
            uint n = QueryDosDevice(d + ":", dev, (uint)dev.Capacity);
            if (n == 0) continue;
            string name = dev.ToString();
            if (name.Length > 0 && nt.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                return d + ":\\" + nt.Substring(name.Length).TrimStart('\\');
        }
        return nt; // can't resolve — return as-is
    }

    /// <summary>Brings any window HWND to the foreground, restoring it from the taskbar if minimised.</summary>
    public static void ForceForegroundHwnd(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        var fg = GetForegroundWindow();
        if (fg == hwnd) { SetForegroundWindow(hwnd); return; }
        uint fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero), me = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        try
        {
            if (!attached) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); }
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally { if (attached) AttachThreadInput(me, fgThread, false); }
    }

    public static RECT MonitorAt(POINT pt, bool workArea = false)
    {
        var mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(mon, ref info);
        return workArea ? info.rcWork : info.rcMonitor;
    }

    /// <summary>DPI scale (1.0 = 96 dpi) of the monitor nearest a point in physical pixels.</summary>
    public static double ScaleAt(Point px)
    {
        var mon = MonitorFromPoint(new POINT { X = (int)Math.Round(px.X), Y = (int)Math.Round(px.Y) }, MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(mon, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : GetDpiForSystem() / 96.0;
    }

    /// <summary>Moves a window's top-left to a point in physical pixels, keeping its size and z-order.</summary>
    public static void MoveTo(IntPtr h, double x, double y) =>
        SetWindowPos(h, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    /// <summary>The work area (screen minus taskbar) of every monitor, in physical pixels.</summary>
    public static unsafe List<Rect> WorkAreas()
    {
        var list = new List<Rect>();
        var gc = GCHandle.Alloc(list);
        try { EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, &CollectWorkArea, GCHandle.ToIntPtr(gc)); }
        finally { gc.Free(); }
        return list;
    }

    [UnmanagedCallersOnly]
    private static unsafe int CollectWorkArea(IntPtr mon, IntPtr dc, RECT* bounds, IntPtr data)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(mon, ref info))
            ((List<Rect>)GCHandle.FromIntPtr(data).Target!).Add(new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Width, info.rcWork.Height));
        return 1;
    }
}
