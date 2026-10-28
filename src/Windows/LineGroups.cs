using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Clothesline.Core;
using Clothesline.UI;

namespace Clothesline.Windows;

/// <summary>
/// Groups on the line: where grouped cards hang (side by side, or folded into a stack), their name labels,
/// joining, leaving and reordering by dragging a card, and moving a group by its label or its stack.
/// </summary>
internal sealed partial class LineWindow
{
    private const int JoinMs = 200, FoldMs = 250;
    private const double LabelGap = 4;

    private readonly record struct Spot(double X, double Tilt, int Z);

    private readonly Dictionary<string, GroupLabel> _labels = new();

    // the group just moved, grown or unfolded: it keeps its place and overlapping groups step aside
    private string? _anchorGroup;
    private bool _keepingApart;

    // a free or grouped card hovering over a group: the group and the slot it would take
    private (string GroupId, int Index)? _preview;

    // a group held by its label or its folded stack
    private string? _moveGroup;
    private UIElement? _moveHandle;
    private Point _movePoint;
    private double _moveStart, _moveLastX;
    private bool _moveDragging;

    private IEnumerable<CardGroup> RopeGroups => _app.Library.Groups.Where(g => g.RopeId == _ropeId);

    private CardGroup? GroupById(string? id) => id == null ? null : _app.Library.Groups.FirstOrDefault(g => g.Id == id);

    private bool IsFloating(CardView c) => c == _pressed && _dragging;

    /// <summary>A group's cards on the line, left to right, without the one being dragged.</summary>
    private List<CardView> Members(CardGroup g) =>
        g.ItemIds.Select(id => _cards.FirstOrDefault(c => c.Item.Id == id)).OfType<CardView>().Where(c => !IsFloating(c)).ToList();

    private double FreeX(CardView c) => Math.Clamp(c.Item.Position, 0, 1) * LineWidth;

    private double XOf(CardView c) => SpotOf(c).X;

    private Spot SpotOf(CardView c)
    {
        if (!IsFloating(c) && _app.Library.GroupOf(c.Item) is { } g && g.RopeId == _ropeId)
            foreach (var (card, spot) in Arrange(g).Cards)
                if (card == c) return spot;
        return new Spot(FreeX(c), 0, 0);
    }

    private double LabelWidth(CardGroup g) => _labels.TryGetValue(g.Id, out var l) ? l.MeasuredWidth : 0;

    /// <summary>Where a group's first card starts: its own position, kept clear of the rope tag (label included) and on screen.</summary>
    private double StartOf(CardGroup g, double width)
    {
        double tag = _tag.Visibility == Visibility.Visible ? (_tag.ActualWidth > 0 ? _tag.ActualWidth : 90) : 0;
        double min = TagX + tag + 8 + LabelWidth(g) + LabelGap;
        double start = g.Position < 0 ? min : g.Position * LineWidth;
        return Math.Max(min, Math.Min(start, LineWidth - width - 6));
    }

    /// <summary>
    /// Where a group's cards hang: side by side from its start (with a gap where a dragged card would go), or folded
    /// into a stack there, the top card first.
    /// </summary>
    private (double Start, double End, List<(CardView Card, Spot Spot)> Cards) Arrange(CardGroup g, bool? folded = null)
    {
        var cards = Members(g);
        var widths = cards.Select(c => c.Width).ToList();
        var placed = new List<(CardView, Spot)>();
        if (folded ?? g.Folded)
        {
            double top = widths.Count == 0 ? 0 : widths.Max();
            double width = top + LineLayout.StackStep * Math.Max(0, cards.Count - 1);
            double s = StartOf(g, width);
            for (int i = 0; i < cards.Count; i++)
            {
                var (dx, tilt) = LineLayout.StackSlot(i);
                placed.Add((cards[i], new Spot(s + top / 2 + dx, tilt, Math.Clamp(9 - i, 1, 9))));
            }
            return (s, s + width, placed);
        }
        int gap = _preview is { } p && p.GroupId == g.Id && _pressed != null ? Math.Min(p.Index, cards.Count) : -1;
        if (gap >= 0) widths.Insert(gap, _pressed!.Width);
        double start = StartOf(g, LineLayout.SpanEnd(0, widths, LineLayout.GroupGap));
        var slots = LineLayout.GroupSlots(start, widths, LineLayout.GroupGap);
        for (int i = 0, k = 0; i < cards.Count; i++, k++)
        {
            if (k == gap) k++;
            placed.Add((cards[i], new Spot(slots[k], 0, 0)));
        }
        return (start, LineLayout.SpanEnd(start, widths, LineLayout.GroupGap), placed);
    }

    /// <summary>What each group covers on the rope, label included. Free cards keep out of these.</summary>
    private List<(double Start, double End)> GroupSpans() =>
        RopeGroups.Select(g => { var a = Arrange(g); return (a.Start - LabelWidth(g) - LabelGap, a.End); }).ToList();

    // ───────────── layout ─────────────

    private void LayoutCard(CardView c) => Place(c, SpotOf(c), 0);

    /// <summary>
    /// Puts every card and label where it belongs, eased over <paramref name="ms"/>. Once nothing is held,
    /// free cards a group has grown over move just past its edge (the right one, unless they sit on its left half).
    /// </summary>
    private void LayoutCards(int ms = 0)
    {
        var spots = new Dictionary<CardView, (Spot Spot, int Ms)>();
        var spans = new List<(double Start, double End)>();
        bool settle = _pressed == null && _moveGroup == null;
        var stepped = settle ? KeepGroupsApart() : new HashSet<string>();
        foreach (var g in RopeGroups)
        {
            int gms = stepped.Contains(g.Id) ? Math.Max(ms, JoinMs) : ms;
            var (start, end, cards) = Arrange(g);
            foreach (var (c, s) in cards) spots[c] = (s, gms);
            if (_labels.TryGetValue(g.Id, out var l))
            {
                double left = Math.Round(start - l.MeasuredWidth - LabelGap);
                EaseTo(l, Canvas.LeftProperty, left, gms);
                Canvas.SetTop(l, Math.Round(RopeAt(left + l.MeasuredWidth / 2) - 1));
            }
            spans.Add((start - LabelWidth(g) - LabelGap, end));
        }

        bool pushed = false;
        foreach (var c in _cards)
        {
            if (spots.TryGetValue(c, out var s)) { Place(c, s.Spot, s.Ms); continue; }
            double x = FreeX(c);
            if (settle && !IsFloating(c) && LineLayout.PushPast(x, c.Width / 2, spans, LineLayout.GroupGap) is var px && px != x)
            {
                double to = Math.Clamp(px / LineWidth, 0, 1);
                if (to != Math.Clamp(c.Item.Position, 0, 1)) // already against the right end: nowhere further to go
                {
                    c.Item.Position = to;
                    pushed = true;
                    Place(c, new Spot(FreeX(c), 0, 0), JoinMs);
                    continue;
                }
            }
            Place(c, new Spot(x, 0, 0), ms);
        }
        if (pushed) _app.Library.ScheduleSave();

        Canvas.SetLeft(_tag, TagX);
        Canvas.SetTop(_tag, Math.Round(RopeAt(TagX + 20) - 1));
    }

    /// <summary>
    /// Groups never overlap: the anchor (just moved, grown or unfolded) keeps its place, then every other group, from
    /// left to right, steps past whatever it overlaps (left or right by the same half rule as free cards). Returns the
    /// groups that moved; their new positions are committed.
    /// </summary>
    private HashSet<string> KeepGroupsApart()
    {
        var moved = new HashSet<string>();
        var anchor = _anchorGroup;
        if (_keepingApart) return moved;
        _anchorGroup = null;
        var groups = RopeGroups.ToList();
        if (groups.Count < 2) return moved;
        var placed = new List<(double Start, double End)>();
        foreach (var g in groups.OrderBy(g => g.Id == anchor ? 0 : 1).ThenBy(g => Arrange(g).Start).ToList())
        {
            var (start, end, _) = Arrange(g);
            double label = LabelWidth(g) + LabelGap;
            double to = LineLayout.PushSpanPast(start - label, end, placed, LineLayout.GroupGap) + label;
            if (Math.Abs(to - start) >= 0.5)
            {
                g.Position = to / LineWidth; // in memory now, so this layout already uses it; committed below
                (start, end, _) = Arrange(g);
                moved.Add(g.Id);
            }
            placed.Add((start - label, end));
        }
        _keepingApart = true; // each commit raises Changed, which lays out again: nothing is left to keep apart by then
        try { foreach (var id in moved) _app.Library.MoveGroup(id, GroupById(id)!.Position); }
        finally { _keepingApart = false; }
        return moved;
    }

    private void Place(CardView c, Spot s, int ms)
    {
        Canvas.SetTop(c, Math.Round(RopeAt(s.X) - 9));
        EaseTo(c, Canvas.LeftProperty, Math.Round(s.X - c.Width / 2), ms);
        EaseTo(c.Tilt, RotateTransform.AngleProperty, s.Tilt, ms);
        int z = Panel.GetZIndex(c);
        if (s.Z > 0) Panel.SetZIndex(c, s.Z);
        else if (z is > 0 and < 10) Panel.SetZIndex(c, 0); // was in a stack; 10 marks the last card pressed
    }

    private static readonly ConditionalWeakTable<DependencyObject, AnimationTimeline> Easing = new();

    /// <summary>Moves a value at once, or eases it from where it shows now; the held value is let go when the ease ends.</summary>
    private static void EaseTo<T>(T target, DependencyProperty dp, double to, int ms) where T : DependencyObject, IAnimatable
    {
        if (ms > 0 && Easing.TryGetValue(target, out var running) && running is DoubleAnimation { To: var heading } && heading == to) return; // already on its way
        double from = (double)target.GetValue(dp);
        if (ms <= 0 || !Motion.Enabled || double.IsNaN(from) || Math.Abs(from - to) < 0.5)
        {
            // Only a held ease of ours is cut short; another animation (a rope's slide-in) finishes on its own.
            if (Easing.TryGetValue(target, out _)) { Easing.Remove(target); target.BeginAnimation(dp, null); }
            target.SetValue(dp, to);
            return;
        }
        target.SetValue(dp, to); // where it rests once the animation lets go
        var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        a.Completed += (_, _) =>
        {
            if (!Easing.TryGetValue(target, out var current) || current != a) return; // a newer move took over
            Easing.Remove(target);
            target.BeginAnimation(dp, null);
        };
        Easing.AddOrUpdate(target, a);
        target.BeginAnimation(dp, a);
    }

    // ───────────── labels and stacks ─────────────

    /// <summary>Brings labels and badges in line with the library's groups, then moves cards to their slots.</summary>
    private void SyncGroups(bool animate)
    {
        var groups = RopeGroups.ToList();
        foreach (var id in _labels.Keys.Where(id => groups.All(g => g.Id != id)).ToList())
        {
            _stage.Children.Remove(_labels[id]);
            _labels.Remove(id);
        }
        bool folding = false;
        var fresh = new List<GroupLabel>();
        foreach (var g in groups)
        {
            if (_labels.TryGetValue(g.Id, out var l))
            {
                folding |= l.Folded != g.Folded;
                if (g.ItemIds.Count > l.Count || (l.Folded && !g.Folded)) _anchorGroup = g.Id; // it grew: the others make room
            }
            else fresh.Add(l = NewLabel(g.Id));
            l.Set(g.Name, g.ItemIds.Count, g.Folded);
        }
        foreach (var c in _cards)
        {
            var g = _app.Library.GroupOf(c.Item);
            if (g is { Folded: true } && Members(g).FirstOrDefault() == c)
            {
                var id = g.Id;
                c.SetBadge(g.ItemIds.Count, L.F("group.name", g.Name, L.N("group.cards", g.ItemIds.Count)), () => _app.Library.SetFolded(id, false));
            }
            else c.SetBadge(0);
        }
        LayoutCards(animate ? (folding ? FoldMs : JoinMs) : 0);
        foreach (var l in fresh)
        {
            if (_lifted) l.Opacity = 0;
            else if (!_expanded) l.Slide.Y = LabelCollapsedOffset(l);
            else if (animate && IsVisible) l.DropIn(() => _expanded && !_lifted);
        }
    }

    private GroupLabel NewLabel(string id)
    {
        var l = new GroupLabel(id);
        l.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (GroupById(id) is not { } g) return;
            GroupPress(g, l, e.GetPosition(this));
            e.Handled = true;
        };
        l.PreviewMouseMove += (_, e) => { if (_moveHandle == l && e.LeftButton == MouseButtonState.Pressed) { GroupMove(e.GetPosition(this)); e.Handled = true; } };
        l.PreviewMouseLeftButtonUp += (_, e) => { if (_moveHandle == l) { GroupRelease(); e.Handled = true; } };
        l.MouseRightButtonUp += (_, e) => { if (GroupById(id) is { } g) _app.ShowGroupMenu(g, l); e.Handled = true; };
        l.Invoked += () => { if (GroupById(id) is { } g) _app.Library.SetFolded(id, !g.Folded); };
        _labels[id] = l;
        _stage.Children.Add(l);
        return l;
    }

    private static double LabelCollapsedOffset(GroupLabel l) =>
        -(Canvas.GetTop(l) + (l.ActualHeight > 0 ? l.ActualHeight : 26) - Peek);

    private bool OverLabel(Point p) =>
        _labels.Values.Any(l => p.X >= Canvas.GetLeft(l) - 8 && p.X <= Canvas.GetLeft(l) + l.MeasuredWidth + 8);

    // ───────────── moving a group ─────────────

    private void GroupPress(CardGroup g, UIElement handle, Point p)
    {
        _moveGroup = g.Id;
        _moveHandle = handle;
        _movePoint = p;
        _moveLastX = p.X;
        _moveDragging = false;
        _moveStart = Arrange(g).Start;
        handle.LostMouseCapture += GroupCaptureLost;
        handle.CaptureMouse();
    }

    /// <summary>Capture taken away mid-press (another window, a menu): end it like a release, but a click never folds.</summary>
    private void GroupCaptureLost(object sender, MouseEventArgs e) => GroupRelease(click: false);

    /// <summary>Dragging the label (or a folded stack) moves the whole group; its cards follow and swing.</summary>
    private void GroupMove(Point p)
    {
        if (GroupById(_moveGroup) is not { } g) return;
        double dx = p.X - _movePoint.X;
        if (!_moveDragging && Math.Abs(dx) <= DragThreshold && Math.Abs(p.Y - _movePoint.Y) <= DragThreshold) return;
        _moveDragging = true;
        g.Position = (_moveStart + dx) / LineWidth; // committed on release; StartOf keeps it on screen meanwhile
        foreach (var c in Members(g)) Kick(c, -(p.X - _moveLastX) * 7);
        _moveLastX = p.X;
        LayoutCards();
    }

    /// <summary>A click folds or unfolds (a stack only unfolds); a drag saves where the group went.</summary>
    private void GroupRelease(bool click = true)
    {
        var handle = _moveHandle;
        var id = _moveGroup;
        bool dragged = _moveDragging;
        _moveGroup = null;
        _moveHandle = null;
        _moveDragging = false;
        if (handle != null)
        {
            handle.LostMouseCapture -= GroupCaptureLost;
            handle.ReleaseMouseCapture();
        }
        if (GroupById(id) is not { } g) return;
        if (dragged)
        {
            _anchorGroup = g.Id; // where it was put down wins; groups it landed on step aside
            _app.Library.MoveGroup(g.Id, Arrange(g).Start / LineWidth);
        }
        else if (click) _app.Library.SetFolded(g.Id, !g.Folded);
    }

    // ───────────── dragging a card ─────────────

    /// <summary>While a card is dragged along the rope, the group under its centre opens a gap where it would join.</summary>
    private void UpdatePreview(CardView c)
    {
        double x = XOf(c);
        var own = _app.Library.GroupOf(c.Item);
        (string GroupId, int Index)? next = null;
        foreach (var g in RopeGroups.Where(g => !g.Folded).OrderBy(g => g == own)) // another group's span wins over leaving one's own
        {
            var widths = Members(g).Select(m => m.Width).ToList();
            double start = StartOf(g, LineLayout.SpanEnd(0, widths, LineLayout.GroupGap));
            bool hit = g == own
                ? !LineLayout.IsOutsideGroup(start, widths, LineLayout.GroupGap, x, c.Width)
                : widths.Count > 0 && x >= start && x <= LineLayout.SpanEnd(start, widths, LineLayout.GroupGap);
            if (!hit) continue;
            next = (g.Id, LineLayout.SlotIndexAt(start, widths, LineLayout.GroupGap, x));
            break;
        }
        if (next == _preview) return;
        _preview = next;
        LayoutCards(JoinMs);
    }

    /// <summary>A dragged card is let go on the rope: it joins (or reorders in) the group it hovers, or leaves its own.</summary>
    private void DropCard(CardView c)
    {
        var target = _preview;
        _preview = null;
        if (target is { } t) _app.Library.JoinGroup(c.Item, t.GroupId, t.Index);
        else if (_app.Library.GroupOf(c.Item) != null) _app.Library.LeaveGroup(c.Item); // Position already holds where it was dropped
        else _app.Library.Touch(c.Item);
    }

    private CardGroup? FoldedGroupOf(CardView c) => _app.Library.GroupOf(c.Item) is { Folded: true } g ? g : null;

    // ───────────── for the menus ─────────────

    /// <summary>A new group starts where the card hangs now (its own position is stale while grouped).</summary>
    public void SettleForGroup(HangItem item)
    {
        if (CardOf(item) is { } c) item.Position = (XOf(c) - c.Width / 2) / LineWidth;
    }

    /// <summary>Takes a card out of its group, leaving it where it hangs.</summary>
    public void LeaveGroup(HangItem item)
    {
        if (CardOf(item) is { } c) item.Position = XOf(c) / LineWidth;
        _app.Library.LeaveGroup(item);
    }

    /// <summary>Where each card of a group goes when it is dissolved: its unfolded slot.</summary>
    public Dictionary<string, double> UngroupPositions(CardGroup g) =>
        Arrange(g, folded: false).Cards.ToDictionary(p => p.Card.Item.Id, p => p.Spot.X / LineWidth);
}
