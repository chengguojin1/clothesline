using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Jemur.Core;
using Jemur.Interop;
using Jemur.UI;

namespace Jemur.Windows;

/// <summary>
/// Full-screen picker: mouse moves over a top-level window, it highlights with a white border;
/// click hangs that window on the line. Esc cancels.
/// </summary>
internal sealed class WindowPicker : Window
{
    private readonly Rectangle _hl;
    private readonly Border _tag;
    private readonly TextBlock _tagText;
    private IntPtr _hoverHwnd;

    public IntPtr Result { get; private set; } = IntPtr.Zero;

    public WindowPicker()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0; Top = 0;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        var canvas = new Canvas();
        _hl = new Rectangle
        {
            Stroke = Brushes.White, StrokeThickness = 2,
            Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        };
        canvas.Children.Add(_hl);

        _tagText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontFamily = Theme.Font };
        _tag = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 4),
            Child = _tagText, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        };
        canvas.Children.Add(_tag);
        Content = canvas;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseMove += (_, e) => { UpdateHover(e); };
        MouseLeftButtonUp += (_, e) =>
        {
            UpdateHover(e);
            if (_hoverHwnd != IntPtr.Zero) { Result = _hoverHwnd; Close(); }
        };
        MouseRightButtonUp += (_, _) => Close();
        Loaded += (_, _) => { Native.ForceForeground(this); Focus(); };
    }

    private void UpdateHover(MouseEventArgs e)
    {
        var pt = e.GetPosition(this);
        var src = PresentationSource.FromVisual(this);
        var transform = src?.CompositionTarget?.TransformToDevice;
        int px = (int)Math.Round(pt.X * (transform?.M11 ?? 1.0));
        int py = (int)Math.Round(pt.Y * (transform?.M22 ?? 1.0));

        var hwnd = Native.WindowFromPoint(new Native.POINT { X = px, Y = py });
        hwnd = Native.GetAncestor(hwnd, Native.GA_ROOT);
        if (hwnd == IntPtr.Zero || hwnd == Native.Handle(this)) { HideHover(); return; }
        if (!Native.IsWindowVisible(hwnd)) { HideHover(); return; }

        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT r,
                System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) != 0)
        { HideHover(); return; }

        double s = transform?.M11 ?? 1.0;
        double left = (r.Left - Left) / s, top = (r.Top - Top) / s;
        Canvas.SetLeft(_hl, left); Canvas.SetTop(_hl, top);
        _hl.Width = r.Width / s; _hl.Height = r.Height / s;
        _hl.Visibility = Visibility.Visible;

        int len = Native.GetWindowTextLength(hwnd);
        var buf = new char[Math.Max(1, len)];
        Native.GetWindowText(hwnd, buf, buf.Length);
        string title = new string(buf).Trim();
        if (string.IsNullOrEmpty(title)) title = "(untitled)";
        _tagText.Text = title;
        Canvas.SetLeft(_tag, left);
        Canvas.SetTop(_tag, top - _tag.ActualHeight - 6 > 0 ? top - _tag.ActualHeight - 6 : top + 6);
        _tag.Visibility = Visibility.Visible;

        _hoverHwnd = hwnd;
    }

    private void HideHover()
    {
        _hoverHwnd = IntPtr.Zero;
        _hl.Visibility = Visibility.Collapsed;
        _tag.Visibility = Visibility.Collapsed;
    }
}
