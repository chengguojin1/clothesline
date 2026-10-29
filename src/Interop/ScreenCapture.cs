using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Clothesline.Interop;

/// <summary>Screen grabbing with plain GDI (no System.Drawing).</summary>
internal static partial class ScreenCapture
{
    /// <summary>Captures the monitor under the mouse. Returns the bitmap and the monitor rectangle in physical pixels.</summary>
    public static (BitmapSource Shot, Native.RECT Bounds) MonitorUnderCursor()
    {
        Native.GetCursorPos(out var pt);
        var b = Native.MonitorAt(pt);
        IntPtr screen = Native.GetDC(IntPtr.Zero), mem = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            mem = Native.CreateCompatibleDC(screen);
            bmp = Native.CreateCompatibleBitmap(screen, b.Width, b.Height);
            old = Native.SelectObject(mem, bmp);
            Native.BitBlt(mem, 0, 0, b.Width, b.Height, screen, b.Left, b.Top, Native.SRCCOPY | Native.CAPTUREBLT);
            Native.SelectObject(mem, old);
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // Re-wrap as an opaque format: CreateBitmapSourceFromHBitmap can report a zero alpha channel.
            var opaque = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgr32, null, 0);
            var frozen = new WriteableBitmap(opaque);
            frozen.Freeze();
            return (frozen, b);
        }
        finally
        {
            if (bmp != IntPtr.Zero) Native.DeleteObject(bmp);
            if (mem != IntPtr.Zero) Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetClassName(IntPtr h, [Out] char[] buf, int max);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(IntPtr h);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, int flags);
    private const int PW_RENDERFULLCONTENT = 0x00000002;

    /// <summary>
    /// Captures a single window (its full frame, title bar included) by asking it to render itself into
    /// a bitmap. PW_RENDERFULLCONTENT makes this work for modern (DirectComposition) windows. Returns null
    /// when the window is gone, has no size, or cannot render.
    /// </summary>
    public static BitmapSource? CaptureWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return null;
        if (!Native.GetWindowRect(hwnd, out var r)) return null;
        int w = r.Width, h = r.Height;
        if (w < 8 || h < 8) return null;
        IntPtr screen = Native.GetDC(IntPtr.Zero), mem = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            mem = Native.CreateCompatibleDC(screen);
            bmp = Native.CreateCompatibleBitmap(screen, w, h);
            old = Native.SelectObject(mem, bmp);
            if (!PrintWindow(hwnd, mem, PW_RENDERFULLCONTENT))
                Native.BitBlt(mem, 0, 0, w, h, screen, r.Left, r.Top, Native.SRCCOPY | Native.CAPTUREBLT);
            Native.SelectObject(mem, old);
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var opaque = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgr32, null, 0);
            var frozen = new WriteableBitmap(opaque);
            frozen.Freeze();
            return frozen;
        }
        catch { return null; }
        finally
        {
            if (bmp != IntPtr.Zero) Native.DeleteObject(bmp);
            if (mem != IntPtr.Zero) Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>
    /// Visible top-level window rectangles (physical pixels) in z-order, front to back — used to
    /// highlight and capture a whole window with a single click.
    /// </summary>
    public static List<Native.RECT> VisibleWindows(IntPtr exclude)
    {
        var list = new List<Native.RECT>();
        var buf = new char[64];
        for (var h = Native.GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = Native.GetWindow(h, Native.GW_HWNDNEXT))
        {
            if (h == exclude || !Native.IsWindowVisible(h) || Native.IsIconic(h)) continue;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) continue;
            long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && GetWindowTextLength(h) == 0) continue;
            int n = GetClassName(h, buf, buf.Length);
            var cls = new string(buf, 0, Math.Max(0, n));
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") continue;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT r, Marshal.SizeOf<Native.RECT>()) != 0) continue;
            if (r.Width < 40 || r.Height < 40) continue;
            list.Add(r);
        }
        return list;
    }
}
