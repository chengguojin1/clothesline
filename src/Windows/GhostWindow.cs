using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Clothesline.Core;
using Clothesline.Interop;

namespace Clothesline.Windows;

/// <summary>A click-through copy of a card that follows the cursor while dragging, falls when let go, or flies between the line and the desktop.</summary>
internal sealed class GhostWindow : Window
{
    private const double Pad = 20;
    private readonly RotateTransform _tilt = new(-5);

    public GhostWindow(CardView source)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;

        var view = new CardView(source.Item, desktop: source.Desktop) { IsHitTestVisible = false, Opacity = 0.94 };
        var host = new Grid { Margin = new Thickness(Pad), RenderTransformOrigin = new Point(0.5, 0), RenderTransform = _tilt };
        host.Children.Add(view);
        Content = host;
        Width = source.Width + Pad * 2;
        Height = source.Height + Pad * 2;
        Left = -10000; Top = -10000;
        SourceInitialized += (_, _) => Native.MakeToolWindow(this, clickThrough: true);
    }

    public void FollowCursor()
    {
        var p = Native.CursorDip(this);
        Left = p.X - Width / 2;
        Top = p.Y - Pad - 4;
    }

    public void ShowAt(double left, double top, double angle)
    {
        Left = left - Pad;
        Top = top - Pad;
        _tilt.Angle = angle;
        Show();
    }

    private Point _cardPx;

    /// <summary>Shows the ghost with its card's top-left at a point in physical pixels (any monitor, any DPI).</summary>
    public void ShowAtPx(Point cardPx, double angle)
    {
        _tilt.Angle = angle;
        new WindowInteropHelper(this).EnsureHandle();
        MoveCard(cardPx);
        MoveCard(cardPx); // the first move can change the window's DPI; the second one uses it
        Show();
    }

    private void MoveCard(Point cardPx)
    {
        _cardPx = cardPx;
        var h = Native.Handle(this);
        var w = LineLayout.WindowForCard(cardPx, new Vector(Pad, Pad), Native.GetDpiForWindow(h) / 96.0);
        Native.MoveTo(h, w.X, w.Y);
    }

    /// <summary>The card's top-left in physical pixels: where it was shown, or where its flight has got to.</summary>
    public Point CardPx => _cardPx;

    private EventHandler? _frame;
    private Action? _landed;

    /// <summary>
    /// Carries the card on a short arc to <paramref name="toPx"/> with a small tilt, then closes and calls <paramref name="landed"/>.
    /// It waits <paramref name="delayMs"/> where it is first (staggered flights).
    /// Moves in physical pixels frame by frame, so it stays right when it crosses monitors with different scaling.
    /// </summary>
    public void FlyTo(Point toPx, int ms, Action landed, int delayMs = 0)
    {
        var from = _cardPx;
        double tilt0 = _tilt.Angle, lift = Math.Min(90, (toPx - from).Length * 0.3);
        var clock = Stopwatch.StartNew();
        _landed = landed;
        _frame = (_, _) =>
        {
            double t = Math.Clamp((clock.Elapsed.TotalMilliseconds - delayMs) / ms, 0, 1);
            double e = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; // cubic in-out
            MoveCard(new Point(from.X + (toPx.X - from.X) * e, from.Y + (toPx.Y - from.Y) * e - lift * Math.Sin(Math.PI * t)));
            _tilt.Angle = tilt0 * (1 - e) - 8 * Math.Sin(Math.PI * t);
            if (t >= 1) Finish();
        };
        CompositionTarget.Rendering += _frame;
    }

    /// <summary>Ends a flight at once: closes, and calls its landing unless <paramref name="land"/> is false.</summary>
    public void Finish(bool land = true)
    {
        if (_frame == null) return;
        CompositionTarget.Rendering -= _frame;
        _frame = null;
        var landed = _landed;
        _landed = null;
        Close();
        if (land) landed?.Invoke();
    }

    public void Fall()
    {
        var dur = TimeSpan.FromMilliseconds(560);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var drop = new DoubleAnimation(Top, Top + 480, dur) { EasingFunction = ease };
        drop.Completed += (_, _) => Close();
        BeginAnimation(TopProperty, drop);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, dur) { EasingFunction = ease });
        _tilt.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(_tilt.Angle, _tilt.Angle + (Random.Shared.NextDouble() < .5 ? -1 : 1) * 60, dur) { EasingFunction = ease });
    }
}
