using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Interop;

namespace Clothesline.Windows;

/// <summary>
/// Extracts an application's icon (from its executable path) and turns it into a frozen WPF
/// ImageSource ready to drop into a card. The Win32 HICON is destroyed after conversion.
/// </summary>
internal static class AppIcon
{
    /// <summary>The large (typically 32×32) icon of the executable at <paramref name="exePath"/>, or null.</summary>
    public static ImageSource? For(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        var info = new Native.SHFILEINFO();
        IntPtr h = Native.SHGetFileInfo(exePath, 0, ref info, Marshal.SizeOf<Native.SHFILEINFO>(),
            Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
        if (h == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            // HICON → cross-thread-safe frozen BitmapSource
            var bmp = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var frozen = new WriteableBitmap((BitmapSource)bmp);
            frozen.Freeze();
            return frozen;
        }
        catch { return null; }
        finally { Native.DestroyIcon(info.hIcon); }
    }
}
