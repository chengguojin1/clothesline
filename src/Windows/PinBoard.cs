using System.Windows;
using Jemur.App;
using Jemur.Core;
using Jemur.Interop;
using Jemur.UI;

namespace Jemur.Windows;

/// <summary>Owns the <see cref="PinWindow"/>s of the active rope and follows the library, lift, rope switches and captures.</summary>
internal sealed class PinBoard
{
    private readonly JemurApp _app;
    private readonly List<PinWindow> _windows = new();
    private readonly HashSet<HangItem> _held = new();
    private readonly List<PinWindow> _hiddenForCapture = new();
    private string _ropeId;
    private bool _lifted;

    public PinBoard(JemurApp app)
    {
        _app = app;
        _ropeId = app.ActiveRope;
        _lifted = app.Lifted;
        AddAll(animate: false);
        app.Library.Relocated += OnRelocated;
        app.Library.Removed += item => { var w = WindowOf(item); if (w != null) { _windows.Remove(w); w.Close(); } };
        app.Library.Updated += item => WindowOf(item)?.Card.RefreshNote();
    }

    public PinWindow? WindowOf(HangItem item) => _windows.FirstOrDefault(w => w.Item == item);

    private bool Belongs(HangItem item) => item.Pinned && item.RopeId == _ropeId && _app.Library.Items.Contains(item);

    private void OnRelocated(HangItem item)
    {
        var w = WindowOf(item);
        bool on = Belongs(item);
        if (w != null && !on) { _windows.Remove(w); w.Close(); }
        else if (w == null && on && !_held.Contains(item))
        {
            w = Add(item);
            if (!_lifted) w.DropIn(Motion.Enabled);
        }
    }

    /// <summary>Makes a window for a pin, clamped onto a work area (and saved back if that moved it). Not shown yet.</summary>
    private PinWindow Add(HangItem item)
    {
        var w = new PinWindow(_app, item);
        var spot = new Point(item.PinLeft, item.PinTop);
        var size = w.CardSizePx(Native.ScaleAt(spot));
        var fit = LineLayout.ClampPin(new Rect(spot, size), Native.WorkAreas());
        if (Math.Abs(fit.X - spot.X) > 0.5 || Math.Abs(fit.Y - spot.Y) > 0.5) _app.Library.MovePin(item, fit.X, fit.Y, item.PinWidth, item.PinHeight);
        w.PlaceAt(fit);
        _windows.Add(w);
        return w;
    }

    private void AddAll(bool animate)
    {
        foreach (var item in _app.Library.Items.Where(Belongs).ToList())
        {
            var w = Add(item);
            if (!_lifted) w.DropIn(animate);
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
        _held.Clear();
        AddAll(play);
    }

    /// <summary>Recreates every pin, e.g. after the card size or language changed.</summary>
    public void Rebuild()
    {
        foreach (var w in _windows) w.Close();
        _windows.Clear();
        AddAll(animate: false);
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

    // ───────────── pinning / unpinning ─────────────

    /// <summary>Call before <see cref="Library.Pin"/> when the card will fly in: its window waits for <see cref="Land"/>.</summary>
    public void Hold(HangItem item) => _held.Add(item);

    /// <summary>The flight arrived: the pin window appears and swings.</summary>
    public void Land(HangItem item)
    {
        _held.Remove(item);
        if (!Belongs(item) || WindowOf(item) != null) return;
        var w = Add(item);
        if (_lifted) return;
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
