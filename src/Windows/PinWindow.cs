using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Jemur.App;
using Jemur.Core;
using Jemur.Interop;
using Jemur.UI;

namespace Jemur.Windows;

/// <summary>
/// One card pinned to the desktop: a small transparent, topmost, no-activate window that holds a <see cref="CardView"/>.
/// Click opens the spotlight, hold marks up, drag moves it with the pendulum swinging from the clothespin.
/// Positions are physical pixels (<see cref="HangItem.PinLeft"/>/<see cref="HangItem.PinTop"/> are the card's top-left),
/// so a pin stays put across monitors with different scaling. Idle, it costs nothing: no timers tick and no frames render.
/// </summary>
internal sealed class PinWindow : Window
{
    private const double PadTop = 28, PadBottom = 14, DragThreshold = 5, LiftBy = 24, MaxSwing = 38;
    private const int HoldMs = 500, LiftMs = 220;

    private readonly JemurApp _app;
    private readonly Grid _root = new();
    private double _padX;
    private readonly ResizeGrip _grip = new();
    private readonly DispatcherTimer _hold;
    private Point _spot;
    private int _fadeSeq;

    // press / drag (physical pixels)
    private bool _pressed, _dragging, _holdFired;
    private Native.POINT _pressCursor, _lastCursor;
    private Point _pressCard;

    // physics
    private bool _animating;

    // resize (physical pixels for the cursor, DIPs for sizes)
    private bool _resizing, _resized;
    private Native.POINT _resizeCursor;
    private Size _resizeFrom, _resizeStored;
    private TimeSpan _lastFrame;

    public CardView Card { get; }
    public HangItem Item => Card.Item;

    public PinWindow(JemurApp app, HangItem item)
    {
        _app = app;
        Card = new CardView(item, desktop: true) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        // Room on each side for the card's corner at the widest swing, so the pendulum never clips.
        SetPad();
        Card.Children.Add(_grip);
        _root.Children.Add(Card);
        Content = _root;

        Title = L.F("pin.name", ItemName(item));
        Ui.AutomationName(Card, Title);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; Top = -32000; // off screen until placed in pixels
        SourceInitialized += (_, _) => Native.MakeToolWindow(this, noActivate: true);
        Loaded += (_, _) => PlaceAt(_spot); // in case showing re-applied Left/Top

        _hold = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(HoldMs) };
        _hold.Tick += (_, _) => OnHold();
        Card.PreviewMouseLeftButtonDown += Down;
        Card.PreviewMouseMove += Move;
        Card.PreviewMouseLeftButtonUp += Up;
        Card.LostMouseCapture += (_, _) => { if (_pressed) EndPress(); };
        Card.MouseEnter += (_, _) => _grip.Opacity = 1;
        Card.MouseLeave += (_, _) => { if (!_resizing) _grip.Opacity = 0; };
        _grip.PreviewMouseLeftButtonDown += GripDown;
        _grip.DragStarted += (_, _) => ResizeStart();
        _grip.DragDelta += (_, _) => ResizeMove();
        _grip.DragCompleted += (_, _) => ResizeEnd();
        SizeChanged += (_, _) => { if (_resizing) PlaceAt(_spot); }; // keep the card's top-left still while the window grows
        Card.MouseEnter += (_, _) => { if (!_pressed) Kick((Random.Shared.NextDouble() < .5 ? -1 : 1) * 26); };
        Card.MouseRightButtonUp += (_, e) => { _app.ShowItemMenu(Item, Card); e.Handled = true; };
    }

    /// <summary>Room on each side for the card's corner at the widest swing, so the pendulum never clips.</summary>
    private void SetPad()
    {
        _padX = Math.Ceiling(Math.Sin(MaxSwing * Math.PI / 180) * Card.Height) + 8;
        Card.Margin = new Thickness(_padX, PadTop, _padX, PadBottom);
    }

    private static string ItemName(HangItem item) =>
        item.Kind == ItemKind.Image ? L.T("kind.screenshot") : ControlPanel.FirstLine(item.Text) ?? L.T("kind.note");

    // ───────────── placement ─────────────

    private IntPtr Hwnd => new WindowInteropHelper(this).EnsureHandle();
    private Vector Pad => new(_padX, PadTop);

    /// <summary>DPI scale of the monitor the window is on.</summary>
    public double Scale => Native.GetDpiForWindow(Hwnd) / 96.0;

    /// <summary>The card's top-left in physical pixels of the virtual screen.</summary>
    public Point CardPx
    {
        get
        {
            Native.GetWindowRect(Hwnd, out var r);
            return LineLayout.CardInWindow(new Point(r.Left, r.Top), Pad, Scale);
        }
    }

    /// <summary>The card's size in physical pixels at a given scale.</summary>
    public Size CardSizePx(double scale) => new(Card.Width * scale, Card.Height * scale);

    /// <summary>Puts the card's top-left at a point in physical pixels.</summary>
    public void PlaceAt(Point cardPx)
    {
        _spot = cardPx;
        var h = Hwnd;
        // The first move may land on a monitor with another DPI (WPF then rescales the window); the second uses that DPI.
        var w = LineLayout.WindowForCard(cardPx, Pad, Native.ScaleAt(cardPx));
        Native.MoveTo(h, w.X, w.Y);
        w = LineLayout.WindowForCard(cardPx, Pad, Scale);
        Native.MoveTo(h, w.X, w.Y);
    }

    // ───────────── motion ─────────────

    public void Kick(double v)
    {
        Card.Impulse(v);
        if (_animating) return;
        _animating = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.05, (t - _lastFrame).TotalSeconds);
        _lastFrame = t;
        if (dt <= 0 || Card.Step(dt)) return;
        StopPhysics();
    }

    private void StopPhysics()
    {
        if (!_animating) return;
        CompositionTarget.Rendering -= OnFrame; // stop rendering entirely when the card hangs still
        _animating = false;
    }

    /// <summary>Lift: rises 24 DIP and fades, then hides.</summary>
    public void LiftAway(bool animate)
    {
        int seq = ++_fadeSeq;
        if (!animate || !IsVisible) { Settle(); Hide(); return; }
        var dur = TimeSpan.FromMilliseconds(LiftMs);
        Card.Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -LiftBy, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        var fade = new DoubleAnimation(1, 0, dur);
        fade.Completed += (_, _) => { if (seq == _fadeSeq) { Settle(); Hide(); } };
        _root.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Hang back out: shows, drops 24 DIP into place and swings.</summary>
    public void DropIn(bool animate)
    {
        ++_fadeSeq;
        Show();
        if (!animate) { Settle(); return; }
        var dur = TimeSpan.FromMilliseconds(LiftMs);
        Card.Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-LiftBy, 0, dur) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } });
        _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
        Kick((Random.Shared.NextDouble() < .5 ? -1 : 1) * 30);
    }

    /// <summary>Fades out after <paramref name="delayMs"/>, then closes (switching ropes, or moved to a rope that isn't showing).</summary>
    public void FadeAway(int delayMs, bool animate)
    {
        ++_fadeSeq;
        IsHitTestVisible = false;
        if (!IsVisible) { Close(); return; }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(animate ? 260 : 0)) { BeginTime = TimeSpan.FromMilliseconds(delayMs) };
        fade.Completed += (_, _) => Close();
        _root.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Drops every running lift or fade and shows the card at rest.</summary>
    private void Settle()
    {
        Card.Slide.BeginAnimation(TranslateTransform.YProperty, null);
        Card.Slide.Y = 0;
        _root.BeginAnimation(OpacityProperty, null);
        _root.Opacity = 1;
    }

    protected override void OnClosed(EventArgs e)
    {
        _hold.Stop();
        StopPhysics();
        base.OnClosed(e);
    }

    // ───────────── click / hold / drag ─────────────

    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, _grip)) return; // the grip resizes; it must not drag, hold or click the card
        Native.GetCursorPos(out _pressCursor);
        _lastCursor = _pressCursor;
        _pressCard = CardPx;
        _pressed = true;
        _dragging = _holdFired = false;
        Card.CaptureMouse();
        _hold.Stop();
        _hold.Start();
        e.Handled = true;
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (!_pressed || _holdFired || e.LeftButton != MouseButtonState.Pressed) return;
        Native.GetCursorPos(out var p);
        double s = Scale;
        int dx = p.X - _pressCursor.X, dy = p.Y - _pressCursor.Y;
        if (!_dragging && (Math.Abs(dx) > DragThreshold * s || Math.Abs(dy) > DragThreshold * s)) { _dragging = true; _hold.Stop(); }
        if (!_dragging) return;
        // Anchor the card, not the window: the pad inside the window is DIPs, so its size in pixels changes with the monitor.
        var card = LineLayout.DraggedCard(_pressCard, new Point(_pressCursor.X, _pressCursor.Y), new Point(p.X, p.Y));
        var w = LineLayout.WindowForCard(card, Pad, s);
        Native.MoveTo(Hwnd, w.X, w.Y);
        double now = Scale; // crossing a seam: WPF has just applied WM_DPICHANGED's suggested rect; put the card back under the cursor
        if (now != s) { w = LineLayout.WindowForCard(card, Pad, now); Native.MoveTo(Hwnd, w.X, w.Y); }
        Kick(-(p.X - _lastCursor.X) / s * 7);
        _lastCursor = p;
        e.Handled = true;
    }

    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        bool click = !_dragging && !_holdFired;
        EndPress();
        e.Handled = true;
        if (click) _app.Spotlight(Item);
    }

    /// <summary>Ends a press; a drag is saved here, on mouse up (or when capture is lost), never while moving.</summary>
    private void EndPress()
    {
        _hold.Stop();
        _pressed = false;
        if (_dragging)
        {
            _dragging = false;
            _spot = CardPx;
            _app.Library.MovePin(Item, _spot.X, _spot.Y, Item.PinWidth, Item.PinHeight);
        }
        Card.ReleaseMouseCapture();
    }

    // ───────────── resize ─────────────

    /// <summary>Double-click resets to the default size.</summary>
    private void GripDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        e.Handled = true;
        _app.Library.MovePin(Item, _spot.X, _spot.Y, 0, 0);
        Card.ApplySavedSize();
        SettleSize();
    }

    /// <summary>After the card's size changed: room for its swing, and its top-left back where it was.</summary>
    private void SettleSize()
    {
        var spot = _spot;
        SetPad();
        PlaceAt(spot);
    }

    private void ResizeStart()
    {
        Native.GetCursorPos(out _resizeCursor);
        _spot = CardPx;
        _resizeFrom = Card.PaperSize;
        _resizeStored = LineLayout.StoredSize(_resizeFrom, CardView.Scale);
        _resizing = true;
        _resized = false;
        _grip.Opacity = 1;
    }

    /// <summary>Live: only the window and paper change, nothing is decoded.</summary>
    private void ResizeMove()
    {
        if (!_resizing) return;
        Native.GetCursorPos(out var p);
        double s = Scale;
        var wanted = new Size(_resizeFrom.Width + (p.X - _resizeCursor.X) / s, _resizeFrom.Height + (p.Y - _resizeCursor.Y) / s);
        _resizeStored = LineLayout.ClampCardSize(Item.Kind == ItemKind.Image, LineLayout.StoredSize(wanted, CardView.Scale), Card.Aspect);
        Card.SetPaperSize(LineLayout.ShownSize(_resizeStored, CardView.Scale));
        _resized = true;
    }

    private void ResizeEnd()
    {
        if (!_resizing) return;
        _resizing = false;
        if (!Card.IsMouseOver) _grip.Opacity = 0;
        if (!_resized) return;
        _app.Library.MovePin(Item, _spot.X, _spot.Y, _resizeStored.Width, _resizeStored.Height);
        if (Item.Kind == ItemKind.Image) Card.ReloadImage((int)(Card.PaperSize.Width * 2));
        SettleSize();
    }

    private void OnHold()
    {
        _hold.Stop();
        if (!_pressed || _dragging) return;
        _holdFired = true;
        EndPress();
        Card.Pulse();
        if (Item.Kind == ItemKind.Image) _app.Markup(Item);
        else _app.Spotlight(Item);
    }
}
