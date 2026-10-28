using System.Windows;

namespace Clothesline.Core;

/// <summary>Pure geometry for card groups and desktop pins, kept free of windows so it can be tested.</summary>
internal static class LineLayout
{
    public const double GroupGap = 10, StackStep = 4, StackTilt = 3;

    /// <summary>Centre x of each card when laid out left to right from <paramref name="startX"/>.</summary>
    public static double[] GroupSlots(double startX, IReadOnlyList<double> widths, double gap)
    {
        var centres = new double[widths.Count];
        var x = startX;
        for (var i = 0; i < widths.Count; i++)
        {
            centres[i] = x + widths[i] / 2;
            x += widths[i] + gap;
        }
        return centres;
    }

    /// <summary>The index a card dragged to centre <paramref name="x"/> would be inserted at.</summary>
    public static int SlotIndexAt(double startX, IReadOnlyList<double> widths, double gap, double x) =>
        GroupSlots(startX, widths, gap).Count(c => c < x);

    /// <summary>Right edge of cards laid out from <paramref name="startX"/>.</summary>
    public static double SpanEnd(double startX, IReadOnlyList<double> widths, double gap) =>
        startX + widths.Sum() + gap * Math.Max(0, widths.Count - 1);

    /// <summary>True when the dragged card centre is more than half a card beyond either end of the group.</summary>
    public static bool IsOutsideGroup(double startX, IReadOnlyList<double> widths, double gap, double x, double cardWidth)
    {
        var end = SpanEnd(startX, widths, gap);
        return x < startX - cardWidth / 2 || x > end + cardWidth / 2;
    }

    /// <summary>Card <paramref name="index"/> of a folded stack (0 is the top card): its x offset and resting tilt, alternating ±3°.</summary>
    public static (double Dx, double Tilt) StackSlot(int index) =>
        (index * StackStep, index % 2 == 0 ? -StackTilt : StackTilt);

    /// <summary>Where a group's span <paramref name="start"/>..<paramref name="end"/> starts once it is clear of the other groups' spans (same rule as <see cref="PushPast"/>).</summary>
    public static double PushSpanPast(double start, double end, IReadOnlyList<(double Start, double End)> spans, double gap)
    {
        double half = (end - start) / 2;
        return PushPast(start + half, half, spans, gap) - half;
    }

    /// <summary>
    /// Where a free card centred at <paramref name="x"/> goes so it overlaps no span (a group with its label): unchanged
    /// if clear, else just past the edge of the span it hits — the right edge (where a growing group pushes it) unless
    /// its centre is on the span's left half — and on in that direction past any span that lands it in.
    /// </summary>
    public static double PushPast(double x, double halfWidth, IReadOnlyList<(double Start, double End)> spans, double gap)
    {
        // ponytail: no room check; a card pushed off either end is clamped there by the caller and may still overlap.
        int dir = 0;
        for (int step = 0; step < spans.Count; step++)
        {
            int hit = -1;
            for (int i = 0; i < spans.Count && hit < 0; i++)
                if (x + halfWidth > spans[i].Start && x - halfWidth < spans[i].End) hit = i;
            if (hit < 0) break;
            var (start, end) = spans[hit];
            if (dir == 0) dir = x < (start + end) / 2 ? -1 : 1;
            x = dir < 0 ? start - gap - halfWidth : end + gap + halfWidth;
        }
        return x;
    }

    public const double SpotMargin = 32, SpotGap = 16, SpotWrapOffset = 24;

    /// <summary>
    /// Where a new desktop card goes: cascade from the top-left, row by row, to the first spot that no occupied rectangle
    /// covers by more than half. When every spot is taken, restart offset by <see cref="SpotWrapOffset"/> until one is free.
    /// </summary>
    public static Point FreeDesktopSpot(Rect workArea, Size card, IReadOnlyList<Rect> occupied, double scale = 1)
    {
        double margin = SpotMargin * scale, gap = SpotGap * scale, wrap = SpotWrapOffset * scale; // the area and card are in pixels, the constants in DIPs
        double half = card.Width * card.Height / 2;
        // ponytail: once the plain grid is full, a cascade step only avoids landing exactly on another card (overlap is the point).
        bool Free(Rect r, bool stacked) => !occupied.Any(o =>
        {
            if (stacked) return Math.Abs(o.X - r.X) < 1 && Math.Abs(o.Y - r.Y) < 1;
            var i = Rect.Intersect(o, r);
            return !i.IsEmpty && i.Width * i.Height > half;
        });

        Point? first = null;
        for (double off = 0; off == 0 || off < Math.Min(card.Width, card.Height); off += wrap)
            for (double y = workArea.Top + margin + off; y + card.Height <= workArea.Bottom - margin; y += card.Height + gap)
                for (double x = workArea.Left + margin + off; x + card.Width <= workArea.Right - margin; x += card.Width + gap)
                {
                    first ??= new Point(x, y);
                    if (Free(new Rect(x, y, card.Width, card.Height), off > 0)) return new Point(x, y);
                }
        return first ?? new Point(workArea.Left + margin, workArea.Top + margin);
    }

    /// <summary>Top-lefts for sizes laid out left to right in rows from an area's top-left, wrapping at its right edge (a tidied desktop).</summary>
    public static Point[] Flow(Rect area, IReadOnlyList<Size> sizes, double margin, double gap)
    {
        // ponytail: no check for running off the bottom; a desktop that full is not tidied by any layout.
        var spots = new Point[sizes.Count];
        double x = area.Left + margin, y = area.Top + margin, row = 0;
        for (int i = 0; i < sizes.Count; i++)
        {
            if (x > area.Left + margin && x + sizes[i].Width > area.Right - margin) { x = area.Left + margin; y += row + gap; row = 0; }
            spots[i] = new Point(x, y);
            x += sizes[i].Width + gap;
            row = Math.Max(row, sizes[i].Height);
        }
        return spots;
    }

    /// <summary>A desktop card's size within its limits: notes 120..640 x 100..640; images 120..960 wide at the given aspect (width / height).
    /// Images are also at most 960 tall, which wins over the 120 minimum width for extremely tall ones (aspect under 0.125).</summary>
    public static Size ClampCardSize(bool image, Size wanted, double aspect)
    {
        if (!image) return new Size(Math.Clamp(wanted.Width, 120, 640), Math.Clamp(wanted.Height, 100, 640));
        if (!(aspect > 0)) aspect = 16 / 9.0; // unknown or broken image size
        var w = Math.Min(Math.Clamp(wanted.Width, 120, 960), 960 * aspect); // a portrait image stops growing when it is 960 tall
        return new Size(w, w / aspect);
    }

    /// <summary>A stored (unscaled) desktop size as shown at a card-size scale, in whole DIPs.</summary>
    public static Size ShownSize(Size stored, double scale) => new(Math.Round(stored.Width * scale), Math.Round(stored.Height * scale));

    /// <summary>A shown size back to the unscaled size that is stored.</summary>
    public static Size StoredSize(Size shown, double scale) => new(shown.Width / scale, shown.Height / scale);

    /// <summary>Keeps a pin reachable after a monitor change: unchanged if at least half is on one work area.</summary>
    public static Point ClampPin(Rect pin, IReadOnlyList<Rect> workAreas)
    {
        if (workAreas.Count == 0) return pin.TopLeft;
        var area = pin.Width * pin.Height;
        if (workAreas.Any(w => { var o = Rect.Intersect(w, pin); return !o.IsEmpty && o.Width * o.Height >= area / 2; })) return pin.TopLeft;

        var cx = pin.X + pin.Width / 2;
        var cy = pin.Y + pin.Height / 2;
        var near = workAreas.OrderBy(w => Math.Pow(w.X + w.Width / 2 - cx, 2) + Math.Pow(w.Y + w.Height / 2 - cy, 2)).First();
        return new Point(Math.Max(near.Left, Math.Min(pin.X, near.Right - pin.Width)),
                         Math.Max(near.Top, Math.Min(pin.Y, near.Bottom - pin.Height)));
    }

    /// <summary>A window's top-left in physical pixels, for a card held <paramref name="padDip"/> inside it.</summary>
    public static Point WindowForCard(Point cardPx, Vector padDip, double scale) =>
        new(cardPx.X - padDip.X * scale, cardPx.Y - padDip.Y * scale);

    /// <summary>Where a dragged card's top-left goes: it keeps its offset from the cursor, in physical pixels, on any monitor.</summary>
    public static Point DraggedCard(Point cardAtPress, Point cursorAtPress, Point cursor) => cardAtPress + (cursor - cursorAtPress);

    /// <summary>The inverse of <see cref="WindowForCard"/>.</summary>
    public static Point CardInWindow(Point windowPx, Vector padDip, double scale) =>
        new(windowPx.X + padDip.X * scale, windowPx.Y + padDip.Y * scale);
}
