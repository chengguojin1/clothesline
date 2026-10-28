using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Clothesline.Core;
using Clothesline.UI;

namespace Clothesline.Windows;

/// <summary>
/// The fabric name tag tied to the left end of the rope. Drawn like the clothespin and paper:
/// flat fills, a stitched edge and layered translucent borders for the shadow (no effects).
/// </summary>
internal sealed class RopeTag : Border
{
    public readonly ScaleTransform Flip = new();
    public readonly TranslateTransform Slide = new();
    private readonly TextBlock _text;

    /// <summary>Clicked, or invoked from Narrator / UI Automation.</summary>
    public event Action? Invoked;

    internal static readonly Brush Linen = Theme.Freeze(new LinearGradientBrush(Color.FromRgb(0xF4, 0xEC, 0xDC), Color.FromRgb(0xE6, 0xD9, 0xC0), 90));
    internal static readonly Brush Edge = Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x50, 0x6B, 0x55, 0x38)));
    internal static readonly Brush Stitch = Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0x9A, 0x7B, 0x55)));
    internal static readonly Brush Cord = Theme.Freeze(new SolidColorBrush(Color.FromRgb(0xE9, 0xDF, 0xCF)));
    internal static readonly Brush[] Shadow =
    {
        Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x0A, 0, 0, 0))),
        Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x16, 0, 0, 0))),
    };

    public RopeTag()
    {
        Cursor = Cursors.Hand;
        UseLayoutRounding = true;
        RenderTransformOrigin = new Point(0.5, 0); // flips around the cord, like a tag turning on its loop
        var tg = new TransformGroup();
        tg.Children.Add(Flip);
        tg.Children.Add(Slide);
        RenderTransform = tg;

        _text = new TextBlock { FontFamily = Theme.Font, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Ink, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis };
        var cloth = new Grid();
        cloth.Children.Add(new Border { Background = Shadow[0], CornerRadius = new CornerRadius(4), Margin = new Thickness(-2, 1, -2, -4) });
        cloth.Children.Add(new Border { Background = Shadow[1], CornerRadius = new CornerRadius(3), Margin = new Thickness(-1, 0, -1, -1.5) });
        cloth.Children.Add(new Border { Background = Linen, BorderBrush = Edge, BorderThickness = new Thickness(0.75), CornerRadius = new CornerRadius(2, 2, 3, 3) });
        cloth.Children.Add(new Rectangle { Stroke = Stitch, StrokeThickness = 0.8, StrokeDashArray = new DoubleCollection { 2.5, 2 }, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(2.5), IsHitTestVisible = false });
        cloth.Children.Add(new Border { Child = _text, Padding = new Thickness(9, 4, 9, 5) });

        var stack = new StackPanel();
        stack.Children.Add(new Border { Width = 1.4, Height = 6, Background = Cord, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(cloth);
        Child = stack;
        MouseLeftButtonUp += (_, e) => { Invoked?.Invoke(); e.Handled = true; };
    }

    public string Text
    {
        get => _text.Text;
        set { _text.Text = value; Ui.AutomationName(this, L.F("rope.tagName", value)); }
    }

    // Border has no automation peer of its own, so the name above would never reach Narrator.
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>Reads as a button named "Line: …" that opens the rope menu; the inner text isn't announced again.</summary>
    private sealed class Peer : FrameworkElementAutomationPeer, IInvokeProvider
    {
        public Peer(RopeTag owner) : base(owner) { }
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => nameof(RopeTag);
        protected override List<AutomationPeer>? GetChildrenCore() => null;
        public override object? GetPattern(PatternInterface p) => p == PatternInterface.Invoke ? this : base.GetPattern(p);
        public void Invoke() => ((RopeTag)Owner).Invoked?.Invoke();
    }
}
