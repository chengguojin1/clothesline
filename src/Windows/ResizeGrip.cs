using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Jemur.Core;
using Jemur.UI;

namespace Jemur.Windows;

/// <summary>
/// The 14 x 14 DIP corner handle of a desktop card. It is a <see cref="Thumb"/> so Narrator announces it as a draggable control;
/// the owner reads the cursor itself (DragDelta's offsets shift as the window it sits in resizes).
/// </summary>
internal sealed class ResizeGrip : Thumb
{
    public const double Extent = 14;

    private static readonly Pen Stroke = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0x33, 0x2C, 0x1E)), 1.25) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

    private static Pen Frozen(Pen p) { p.Freeze(); return p; }

    public ResizeGrip()
    {
        Width = Height = Extent;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        OverridesDefaultStyle = true; // draw it ourselves, no themed template
        Cursor = Cursors.SizeNWSE;
        Opacity = 0; // shown on hover by the card
        Ui.AutomationName(this, L.T("card.resize"));
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Extent, Extent)); // the whole square takes the mouse
        dc.DrawLine(Stroke, new Point(Extent - 2, 4), new Point(4, Extent - 2));
        dc.DrawLine(Stroke, new Point(Extent - 2, 8), new Point(8, Extent - 2));
        dc.DrawLine(Stroke, new Point(Extent - 2, 12), new Point(12, Extent - 2));
    }
}
