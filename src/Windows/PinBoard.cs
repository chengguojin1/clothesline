using System.Windows;
using Clothesline.App;
using Clothesline.Core;
using Clothesline.Interop;
using Clothesline.UI;

namespace Clothesline.Windows;

/// <summary>In desktop mode, owns a <see cref="PinWindow"/> for every card of the active rope and follows the library, lift, rope switches and captures.</summary>
internal sealed class PinBoard
{
    private readonly ClotheslineApp _app;
    private readonly List<PinWindow> _windows = new();
    private readonly List<PinWindow> _hiddenForCapture = new();
    private string _ropeId;
    private bool _lifted;

    // DIPs each card of a pile sits right of and below the one under it; ms between cards gliding into place
    private const double PileDx = 22, PileDy = 16, TidyGap = 24;
    private const int Stagger = 45;

    // a card being dragged: where its pile mates were when it started, and the card it groups with if let go now
    private readonly Dictionary<PinWindow, Point> _carried = new();
    private Point _carrierFrom;
    private PinWindow? _target;

    public PinBoard(ClotheslineApp app)
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
        w.DragBegan += BeginCarry;
        w.Dragged += Carry;
        w.Dropped += Drop;
        w.Shaken += OnShaken;
        if (!item.Pinned)
        {
            Native.GetCursorPos(out var cursor);
            var r = Native.MonitorAt(cursor, workArea: true);
            var area = new Rect(r.Left, r.Top, r.Width, r.Height);
            var taken = _windows.Select(o => new Rect(o.CardPx, o.CardSizePx(o.Scale))).ToList();
            var scale = Native.ScaleAt(area.TopLeft);
            var free = LineLayout.FreeDesktopSpot(area, w.CardSizePx(scale), taken, scale);
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
        foreach (var item in _app.Library.Items.Where(Belongs).OrderBy(i => !i.Pinned).ToList()) // placed cards first, so new ones go around them
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

    // ───────────── piles (groups on the desktop) ─────────────

    private PinWindow? Window(string id) => _windows.FirstOrDefault(w => w.Item.Id == id);

    /// <summary>The windows of a group, bottom of the pile first.</summary>
    private List<PinWindow> Pile(CardGroup g) => g.ItemIds.Select(Window).OfType<PinWindow>().ToList();

    private List<PinWindow> Mates(PinWindow w) =>
        _app.Library.GroupOf(w.Item) is { } g ? Pile(g).Where(o => o != w).ToList() : new();

    private static Rect RectOf(PinWindow w) => new(w.CardPx, w.CardSizePx(w.Scale));

    /// <summary>Dragging any card of a pile carries the whole pile.</summary>
    private void BeginCarry(PinWindow w)
    {
        _carried.Clear();
        _carrierFrom = w.CardPx;
        foreach (var m in Mates(w)) _carried[m] = m.CardPx;
    }

    private void Carry(PinWindow w)
    {
        var delta = w.CardPx - _carrierFrom;
        foreach (var (m, from) in _carried)
        {
            double was = m.CardPx.X;
            m.PlaceAt(from + delta);
            m.Kick(-(from.X + delta.X - was) / m.Scale * 7);
        }
        var r = RectOf(w);
        var centre = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        SetTarget(_windows.LastOrDefault(o => o != w && o.IsVisible && !_carried.ContainsKey(o) && RectOf(o).Contains(centre)));
    }

    /// <summary>The card under a dragged one grows a little: letting go there puts them in one pile.</summary>
    private void SetTarget(PinWindow? t)
    {
        if (t == _target) return;
        _target?.Card.Beckon(false);
        _target = t;
        if (t == null) return;
        t.Card.Beckon(true);
        t.Kick((Random.Shared.NextDouble() < .5 ? -1 : 1) * 24);
    }

    private void Drop(PinWindow w)
    {
        var target = _target;
        SetTarget(null);
        foreach (var m in _carried.Keys) m.SaveSpot();
        _carried.Clear();
        if (target != null && _windows.Contains(target)) Merge(w, target);
    }

    /// <summary>A card (with its pile, if it has one) joins the pile of the card it was dropped on, or starts one with it.</summary>
    private void Merge(PinWindow w, PinWindow target)
    {
        var moving = _app.Library.GroupOf(w.Item) is { } own ? Pile(own) : new List<PinWindow> { w };
        var g = _app.Library.GroupOf(target.Item) ?? _app.Library.CreateGroup(target.Item, _app.NextGroupName(target.Item.RopeId ?? Rope.DefaultId));
        foreach (var m in moving) _app.Library.JoinGroup(m.Item, g.Id, int.MaxValue);
        Arrange(g);
    }

    /// <summary>A pile fans out neatly from its bottom card: each card glides to its place a little after the one before.</summary>
    private void Arrange(CardGroup g, int delayMs = 0)
    {
        var pile = Pile(g);
        if (pile.Count == 0) return;
        var at = pile[0].CardPx;
        double s = pile[0].Scale;
        for (int i = 0; i < pile.Count; i++)
        {
            pile[i].Front();
            if (i > 0) pile[i].GlideTo(at + new Vector(i * PileDx * s, i * PileDy * s), delayMs + Stagger * (i - 1));
        }
    }

    /// <summary>
    /// Shaking a card while dragging it: a card in a pile shakes loose (and drags on alone); a loose card tidies the
    /// desktop, lining every card and pile on its monitor up in rows.
    /// </summary>
    private void OnShaken(PinWindow w)
    {
        SetTarget(null);
        if (_app.Library.GroupOf(w.Item) != null) { Loosen(w.Item); return; }
        w.EndPress();
        Tidy(w);
    }

    /// <summary>Takes a card out of its pile; a pile of one is no pile, so the last card is let go too. The rest close up.</summary>
    public void Loosen(HangItem item)
    {
        var w = WindowOf(item);
        if (_app.Library.GroupOf(item) is not { } g) return;
        foreach (var m in _carried.Keys) m.SaveSpot();
        _carried.Clear(); // the pile stays where it got to; the shaken card goes on alone
        if (g.ItemIds.Count <= 2) _app.Library.Ungroup(g.Id);
        else
        {
            _app.Library.LeaveGroup(item);
            Arrange(g);
        }
        if (w == null) return;
        w.Card.Pulse();
        w.Card.Toast(L.T("toast.ungrouped"));
        w.Kick((Random.Shared.NextDouble() < .5 ? -1 : 1) * 90);
    }

    private void Tidy(PinWindow shaken)
    {
        var c = shaken.CardPx;
        var r = Native.MonitorAt(new Native.POINT { X = (int)c.X, Y = (int)c.Y }, workArea: true);
        var area = new Rect(r.Left, r.Top, r.Width, r.Height);
        double s = Native.ScaleAt(area.TopLeft);
        var units = _windows
            .Where(w => w.IsVisible && (_app.Library.GroupOf(w.Item) is not { } g || Pile(g).FirstOrDefault() == w))
            .Select(w => (Lead: w, Group: _app.Library.GroupOf(w.Item), Rect: RectOf(w)))
            .Where(u => area.Contains(new Point(u.Rect.X + u.Rect.Width / 2, u.Rect.Y + u.Rect.Height / 2)))
            .OrderBy(u => Math.Round((u.Rect.Y - area.Top) / (160 * s))).ThenBy(u => u.Rect.X) // reading order, rows about a card tall
            .ToList();
        var sizes = units.Select(u =>
        {
            int n = u.Group == null ? 1 : Pile(u.Group).Count;
            return new Size(u.Rect.Width + (n - 1) * PileDx * s, u.Rect.Height + (n - 1) * PileDy * s);
        }).ToList();
        var spots = LineLayout.Flow(area, sizes, LineLayout.SpotMargin * s, TidyGap * s);
        for (int i = 0; i < units.Count; i++)
        {
            int delay = Stagger * i;
            units[i].Lead.GlideTo(spots[i], delay);
            if (units[i].Group is { } g)
            {
                var pile = Pile(g);
                for (int k = 1; k < pile.Count; k++) pile[k].GlideTo(spots[i] + new Vector(k * PileDx * s, k * PileDy * s), delay + 30 * k);
            }
        }
        shaken.Card.Toast(L.T("toast.tidied"));
    }
}
