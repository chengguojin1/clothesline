using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Clothesline.Core;
using Clothesline.Interop;
using Clothesline.UI;

namespace Clothesline.Windows;

/// <summary>
/// Frozen image of one monitor. Drag to select an area, click a highlighted window to take just that
/// window, press Enter for the whole screen, Esc to cancel.
/// </summary>
internal sealed class CaptureWindow : Window
{
    private readonly BitmapSource _shot;
    private readonly Native.RECT _bounds;
    private readonly List<Native.RECT> _windows;
    private readonly Path _dim;
    private readonly Rectangle _sel, _selOuter;
    private readonly Border _sizeTag;
    private readonly TextBlock _sizeText;
    private Point? _start;
    private Point _last;
    private Rect? _hoverWindow;

    public BitmapSource? Result { get; private set; }

    public CaptureWindow(BitmapSource shot, Native.RECT bounds, List<Native.RECT> windows)
    {
        _shot = shot;
        _bounds = bounds;
        _windows = windows;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; Top = -32000; Width = 200; Height = 200;
        UseLayoutRounding = true;

        var root = new Grid();
        root.Children.Add(new Image { Source = shot, Stretch = Stretch.Fill });
        var canvas = new Canvas();
        _dim = new Path { Fill = Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0))) };
        _selOuter = new Rectangle { Stroke = Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0))), StrokeThickness = 3, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        _sel = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        _sizeText = Ui.Text("", Ui.Type.Caption);
        _sizeTag = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 3), Child = _sizeText, Visibility = Visibility.Collapsed, BorderThickness = new Thickness(1) };
        _sizeTag.SetResourceReference(Border.BackgroundProperty, "J.Flyout");
        _sizeTag.SetResourceReference(Border.BorderBrushProperty, "J.FlyoutStroke");
        canvas.Children.Add(_dim);
        canvas.Children.Add(_selOuter);
        canvas.Children.Add(_sel);
        canvas.Children.Add(_sizeTag);
        root.Children.Add(canvas);
        root.Children.Add(Hint());
        Content = root;

        SourceInitialized += (_, _) =>
        {
            // Put the window on the captured monitor first so WPF picks up that monitor's DPI, then fill it.
            Native.SetWindowPos(Native.Handle(this), IntPtr.Zero, _bounds.Left + 40, _bounds.Top + 40, 300, 300, Native.SWP_NOZORDER);
            WindowState = WindowState.Maximized;
        };
        Loaded += (_, _) => { Update(); Native.ForceForeground(this); Focus(); };
        SizeChanged += (_, _) => Update();
        KeyDown += OnKey;
        MouseRightButtonUp += (_, _) => Close();
        MouseLeftButtonDown += (_, e) => { _start = e.GetPosition(this); _last = _start.Value; CaptureMouse(); };
        MouseMove += (_, e) => { _last = e.GetPosition(this); if (_start == null) HitTestWindows(); Update(); };
        MouseLeftButtonUp += (_, _) => Finish();
    }

    private static UIElement Hint()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        void part(string key, string text)
        {
            if (sp.Children.Count > 0) sp.Children.Add(new Border { Width = 1, Height = 14, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Background = Theme.Divider });
            var keyText = Ui.Text(key, Ui.Type.Caption, "J.Text");
            keyText.FontWeight = FontWeights.SemiBold;
            keyText.Margin = new Thickness(0, 0, 6, 0);
            sp.Children.Add(keyText);
            sp.Children.Add(Ui.Text(text, Ui.Type.Caption, "J.TextSecondary"));
        }
        part(L.T("capture.drag"), L.T("capture.dragText"));
        part(L.T("capture.click"), L.T("capture.clickText"));
        part("Enter", L.T("capture.fullText"));
        part("Esc", L.T("capture.cancelText"));
        var b = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 9, 16, 10), BorderThickness = new Thickness(1), Child = sp,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 0, 0), IsHitTestVisible = false,
        };
        b.SetResourceReference(Border.BackgroundProperty, "J.Flyout");
        b.SetResourceReference(Border.BorderBrushProperty, "J.FlyoutStroke");
        return b;
    }

    private double PxPerDip => _shot.PixelWidth / Math.Max(1, ActualWidth);

    /// <summary>Highlights the top-most window under the cursor so a single click can capture it.</summary>
    private void HitTestWindows()
    {
        double s = PxPerDip;
        double px = _bounds.Left + _last.X * s, py = _bounds.Top + _last.Y * s;
        _hoverWindow = null;
        foreach (var r in _windows)
        {
            if (px < r.Left || px >= r.Right || py < r.Top || py >= r.Bottom) continue;
            var dip = new Rect((r.Left - _bounds.Left) / s, (r.Top - _bounds.Top) / s, r.Width / s, r.Height / s);
            dip.Intersect(new Rect(0, 0, ActualWidth, ActualHeight));
            if (!dip.IsEmpty) _hoverWindow = dip;
            break;
        }
    }

    private Rect? Selection()
    {
        if (_start is { } s)
        {
            var r = new Rect(s, _last);
            if (r.Width >= 4 && r.Height >= 4) return r;
            return _hoverWindow; // a click (no drag) takes the highlighted window
        }
        return _hoverWindow;
    }

    private void Update()
    {
        var full = new RectangleGeometry(new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)));
        var r = Selection();
        if (r is { } sel)
        {
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(sel));
            foreach (var rect in new[] { _sel, _selOuter })
            {
                rect.Visibility = Visibility.Visible;
                Canvas.SetLeft(rect, sel.X); Canvas.SetTop(rect, sel.Y);
                rect.Width = sel.Width; rect.Height = sel.Height;
            }
            var px = ToPixels(sel);
            _sizeText.Text = $"{px.Width} × {px.Height}";
            _sizeTag.Visibility = Visibility.Visible;
            Canvas.SetLeft(_sizeTag, sel.X);
            Canvas.SetTop(_sizeTag, sel.Y > 30 ? sel.Y - 28 : sel.Bottom + 6);
        }
        else
        {
            _dim.Data = full;
            _sel.Visibility = _selOuter.Visibility = _sizeTag.Visibility = Visibility.Collapsed;
        }
    }

    private Int32Rect ToPixels(Rect r)
    {
        double s = PxPerDip;
        int x = (int)Math.Round(r.X * s), y = (int)Math.Round(r.Y * s);
        x = Math.Clamp(x, 0, _shot.PixelWidth - 1); y = Math.Clamp(y, 0, _shot.PixelHeight - 1);
        int w = Math.Clamp((int)Math.Round(r.Width * s), 1, _shot.PixelWidth - x), h = Math.Clamp((int)Math.Round(r.Height * s), 1, _shot.PixelHeight - y);
        return new Int32Rect(x, y, w, h);
    }

    private void Finish()
    {
        if (_start == null) return;
        ReleaseMouseCapture();
        var r = Selection();
        _start = null;
        if (r is { } sel)
        {
            var crop = new CroppedBitmap(_shot, ToPixels(sel));
            crop.Freeze();
            Result = crop;
            Close();
        }
        else Update();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.Enter) { Result = _shot; Close(); }
    }
}
