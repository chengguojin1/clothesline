using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Clothesline.Core;
using Clothesline.UI;

namespace Clothesline.Interop;

/// <summary>
/// Notification-area icon talking to Shell_NotifyIcon directly. Using WinForms' NotifyIcon would pull
/// System.Windows.Forms and System.Drawing into memory for this one feature (~15 MB).
/// </summary>
internal sealed partial class TrayIcon : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);

    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const int NIIF_INFO = 1, NIIF_WARNING = 2, NIIF_ERROR = 3;
    private const int WM_TRAY = 0x8001;
    private const int WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B, NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;

    private readonly HwndSource _src;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private string _tip = "Clothesline";
    private bool _added;
    private long _lastClick = long.MinValue / 2;

    public event Action? Click;
    public event Action? RightClick;

    public TrayIcon()
    {
        _src = new HwndSource(new HwndSourceParameters("ClotheslineTray") { Width = 0, Height = 0, WindowStyle = 0 });
        _src.AddHook(Hook);
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    }

    public IntPtr Hwnd => _src.Handle;

    public void Show(bool lightTaskbar, string tip)
    {
        _tip = tip;
        SetIcon(lightTaskbar);
        var d = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIcon(NIM_ADD, ref d);
        d.uVersion = 4;
        Shell_NotifyIcon(NIM_SETVERSION, ref d);
    }

    /// <summary>Swap between the black and white glyph when the taskbar theme changes.</summary>
    public void SetIcon(bool lightTaskbar)
    {
        var old = _icon;
        _icon = LoadIcon(lightTaskbar ? "tray-light.ico" : "tray-dark.ico");
        if (_added)
        {
            var d = Data(NIF_ICON);
            Shell_NotifyIcon(NIM_MODIFY, ref d);
        }
        if (old != IntPtr.Zero) Native.DestroyIcon(old);
    }

    public void SetTip(string tip)
    {
        _tip = tip;
        if (!_added) return;
        var d = Data(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_MODIFY, ref d);
    }

    public void Notify(string title, string text, bool warning = false)
    {
        if (!_added) return;
        var d = Data(NIF_INFO);
        d.szInfoTitle = Trim(title, 63);
        d.szInfo = Trim(text, 255);
        d.dwInfoFlags = warning ? NIIF_WARNING : NIIF_INFO;
        Shell_NotifyIcon(NIM_MODIFY, ref d);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    private NOTIFYICONDATA Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _src.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_TRAY,
        hIcon = _icon,
        szTip = Trim(_tip, 127),
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_TRAY)
        {
            int ev = (int)(l.ToInt64() & 0xFFFF);
            // With NOTIFYICON_VERSION_4 the shell sends NIN_SELECT for a mouse click (on top of the raw
            // WM_LBUTTONUP) and NIN_KEYSELECT for Enter/Space, and a double-click sends two NIN_SELECTs.
            // So listen to the selection events only, and treat a repeat inside the double-click time
            // as part of the same click, or a double-click would lift and drop again.
            if (ev is NIN_SELECT or NIN_KEYSELECT)
            {
                long now = Environment.TickCount64;
                if (now - _lastClick >= Native.GetDoubleClickTime())
                {
                    _lastClick = now;
                    Click?.Invoke();
                }
            }
            else if (ev is WM_RBUTTONUP or WM_CONTEXTMENU) RightClick?.Invoke();
            handled = true;
        }
        else if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            // Explorer restarted: our icon is gone, add it again.
            _added = false;
            Show(Theme.IsTaskbarLight(), _tip);
        }
        return IntPtr.Zero;
    }

    /// <summary>Loads the best-fitting frame of an embedded multi-size .ico for the current DPI.</summary>
    private static IntPtr LoadIcon(string resource)
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (s == null) return IntPtr.Zero;
            var bytes = new byte[s.Length];
            s.ReadExactly(bytes);
            uint dpi = Native.GetDpiForSystem();
            int want = Native.GetSystemMetricsForDpi(49 /* SM_CXSMICON */, dpi);
            int count = BitConverter.ToUInt16(bytes, 4);
            int best = -1, bestSize = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                int e = 6 + i * 16;
                int size = bytes[e] == 0 ? 256 : bytes[e];
                int score = size >= want ? size - want : 1000 + (want - size); // prefer the smallest frame ≥ wanted
                if (score < bestSize) { bestSize = score; best = i; }
            }
            if (best < 0) return IntPtr.Zero;
            int entry = 6 + best * 16;
            int len = BitConverter.ToInt32(bytes, entry + 8), off = BitConverter.ToInt32(bytes, entry + 12);
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { return Native.CreateIconFromResourceEx(handle.AddrOfPinnedObject() + off, (uint)len, true, 0x00030000, want, want, 0); }
            finally { handle.Free(); }
        }
        catch (Exception ex)
        {
            Log.Warn("Tray icon could not be loaded", ex);
            return IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var d = Data(0);
            Shell_NotifyIcon(NIM_DELETE, ref d);
            _added = false;
        }
        if (_icon != IntPtr.Zero) Native.DestroyIcon(_icon);
        _src.Dispose();
    }
}
