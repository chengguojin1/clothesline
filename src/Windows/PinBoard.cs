using System.Windows;
using Jemur.App;
using Jemur.Core;
using Jemur.Interop;
using Jemur.UI;

namespace Jemur.Windows;

/// <summary>In desktop mode, owns a <see cref="PinWindow"/> for every card of the active rope and follows the library, lift, rope switches and captures.</summary>
internal sealed class PinBoard
{
    private readonly JemurApp _app;
    private readonly List<PinWindow> _windows = new();
    private readonly List<PinWindow> _hiddenForCapture = new();
    private string _ropeId;
    private bool _lifted;

    public PinBoard(JemurApp app)
    {
        _app = app;
        _ropeId = app.ActiveRope;
        _lifted = app.Lifted;
        AddAll(animate: false);
        app.Library.Added += Follow;
        app.Library.Relocated += Follow;
        app.Library.Removed += item => { var w = WindowOf(item); if (w != null) { _windows.Remove(w); w.Close(); } };
        app.Library.Updated += item => WindowOf(item)?.Card.RefreshNote();
    }

    public PinWindow? WindowOf(HangItem item) => _windows.FirstOrDefault(w => w.Item == item);

    public IReadOnlyList<PinWindow> Windows => _windows;

    private bool Belongs(HangItem item) => _app.InDesktopMode && item.RopeId == _ropeId && _app.Library.Items.Contains(item);

    private void Follow(HangItem item)
    {
        var w = WindowOf(item);
        bool on = Belongs(item);
        if (w != null && !on) { _windows.Remove(w); w.Close(); }
        else if (w == null && on)
        {
            w = Add(item);
            if (!_lifted) w.DropIn(Motion.Enabled);
        }
    }

    /// <summary>
    /// Makes a window for a card. One that was never on the desktop first gets a free spot on the monitor under the cursor.
    /// A saved spot that is off every screen shows clamped onto one, but stays saved, so replugging a monitor brings it back.
    /// Not shown yet.
    /// </summary>
    private PinWindow Add(HangItem item)
    {
        var w = new PinWindow(_app, item);
        if (!item.Pinned)
        {
            Native.GetCursorPos(out var cursor);
            var r = Native.MonitorAt(cursor, workArea: true);
            var area = new Rect(r.Left, r.Top, r.Width, r.Height);
            var taken = _windows.Select(o => new Rect(o.CardPx, o.CardSizePx(o.Scale))).ToList();
            var free = LineLayout.FreeDesktopSpot(area, w.CardSizePx(Native.ScaleAt(area.TopLeft)), taken);
            _app.Library.Place(item, free.X, free.Y);
        }
        var spot = new Point(item.PinLeft, item.PinTop);
        w.PlaceAt(LineLayout.ClampPin(new Rect(spot, w.CardSizePx(Native.ScaleAt(spot))), Native.WorkAreas()));
        _windows.Add(w);
        return w;
    }

    /// <summary>Makes the windows of the active rope; with <paramref name="hold"/> they wait unseen for <see cref="Land"/>.</summary>
    private void AddAll(bool animate, bool hold = false)
    {
        foreach (var item in _app.Library.Items.Where(Belongs).ToList())
        {
            var w = Add(item);
            if (!_lifted && !hold) w.DropIn(animate);
        }
    }

    /// <summary>Old pins fade out, the new rope's pins drop in. While lifted the new ones wait out of sight.</summary>
    public void ShowRope(string ropeId, bool animate)
    {
        if (ropeId == _ropeId) return;
        _ropeId = ropeId;
        bool play = animate && Motion.Enabled;
        foreach (var w in _windows) w.FadeAway(0, play);
        _windows.Clear();
        AddAll(play);
    }

    /// <summary>
    /// Recreates every window, e.g. after the card size, language or mode changed (in line mode that leaves none).
    /// With <paramref name="hold"/> the new windows wait unseen until their card lands (<see cref="Land"/>).
    /// </summary>
    public void Rebuild(bool hold = false)
    {
        foreach (var w in _windows) w.Close();
        _windows.Clear();
        AddAll(animate: false, hold);
    }

    public void SetLifted(bool lifted, bool animate)
    {
        if (lifted == _lifted) return;
        _lifted = lifted;
        bool play = animate && Motion.Enabled;
        foreach (var w in _windows)
        {
            if (lifted) w.LiftAway(play);
            else w.DropIn(play);
        }
    }

    public void HideForCapture()
    {
        foreach (var w in _windows.Where(w => w.IsVisible))
        {
            _hiddenForCapture.Add(w);
            w.Hide();
        }
    }

    /// <summary>Safe to call more than once: only what <see cref="HideForCapture"/> hid comes back, and not while lifted.</summary>
    public void RestoreAfterCapture()
    {
        foreach (var w in _hiddenForCapture)
            if (_windows.Contains(w) && !_lifted) w.Show();
        _hiddenForCapture.Clear();
    }

    // ───────────── flights and leaving ─────────────

    /// <summary>A card flew in from the line: its waiting window appears and swings.</summary>
    public void Land(HangItem item)
    {
        var w = WindowOf(item);
        if (w == null || _lifted) return;
        w.DropIn(animate: false);
        w.Kick((Random.Shared.NextDouble() < .5 ? -1 : 1) * 60);
    }

    /// <summary>Discarded from a pin: the card falls off the desktop.</summary>
    public void LetGo(HangItem item)
    {
        var w = WindowOf(item);
        if (w is not { IsVisible: true }) return;
        var ghost = new GhostWindow(w.Card);
        ghost.ShowAtPx(w.CardPx, w.Card.Angle);
        w.Hide();
        ghost.Fall();
    }

    /// <summary>Moved to a rope that isn't showing: the pin shows a toast, then fades away.</summary>
    public void SeeOff(HangItem item, string toast)
    {
        var w = WindowOf(item);
        if (w == null) return;
        _windows.Remove(w);
        w.Card.Toast(toast);
        w.FadeAway(1100, Motion.Enabled);
    }
}
