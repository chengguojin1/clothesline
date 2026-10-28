using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Clothesline.Interop;

namespace Clothesline.Interop;

/// <summary>
/// A live DWM thumbnail of one top-level window, painted into our overlay window.
/// The thumbnail is a read-only mirror: it shows the target window's current content
/// (video, scrolling, animations) with no per-frame screenshots on our side.
/// </summary>
internal sealed class LiveThumbnail : IDisposable
{
    private IntPtr _destHwnd;
    private IntPtr _sourceHwnd;
    private IntPtr _thumb;
    private bool _disposed;

    public IntPtr SourceHwnd => _sourceHwnd;

    /// <summary>Registers a thumbnail of <paramref name="source"/> painted into <paramref name="dest"/>.</summary>
    public LiveThumbnail(IntPtr destHwnd, IntPtr sourceHwnd)
    {
        _destHwnd = destHwnd;
        _sourceHwnd = sourceHwnd;
        int hr = Native.DwmRegisterThumbnail(destHwnd, sourceHwnd, out _thumb);
        if (hr != 0) { _thumb = IntPtr.Zero; throw new InvalidOperationException($"DwmRegisterThumbnail failed: 0x{hr:X8}"); }
    }

    /// <summary>True while the source window still exists and is a real top-level window.</summary>
    public bool SourceAlive => _sourceHwnd != IntPtr.Zero && Native.IsWindow(_sourceHwnd);

    /// <summary>
    /// Moves/resizes the thumbnail within the destination window.
    /// Coordinates are physical pixels relative to the destination window's client area.
    /// </summary>
    public void Update(Rect destRectPx, double opacity = 1.0)
    {
        if (_thumb == IntPtr.Zero || _disposed) return;
        var props = new Native.DWM_THUMBNAIL_PROPERTIES();
        props.dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_OPACITY;
        props.rcDestination = new Native.RECT
        {
            Left = (int)Math.Round(destRectPx.X),
            Top = (int)Math.Round(destRectPx.Y),
            Right = (int)Math.Round(destRectPx.Right),
            Bottom = (int)Math.Round(destRectPx.Bottom),
        };
        props.opacity = (byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255);
        props.fVisible = true;
        props.fSourceClientAreaOnly = false;
        Native.DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    /// <summary>Hides the thumbnail without unregistering it (e.g. while the line is tucked away).</summary>
    public void Hide()
    {
        if (_thumb == IntPtr.Zero || _disposed) return;
        var props = new Native.DWM_THUMBNAIL_PROPERTIES { dwFlags = Native.DWM_TNP_VISIBLE, fVisible = false };
        Native.DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_thumb != IntPtr.Zero) { Native.DwmUnregisterThumbnail(_thumb); _thumb = IntPtr.Zero; }
    }
}
