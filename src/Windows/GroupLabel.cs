using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Clothesline.Core;
using Clothesline.UI;

namespace Clothesline.Windows;

/// <summary>
/// A group's small name tag on the rope, just left of its first card: the name and a fold chevron, in the
/// rope tag's linen. Clicking folds or unfolds; the line window handles the mouse (click, drag, menu).
/// </summary>
internal sealed class GroupLabel : Border
{
    private const string ChevronDown = "", ChevronRight = "";

    public readonly TranslateTransform Slide = new();
    public readonly RotateTransform Swing = new();
    public string GroupId { get; }
    public bool Folded { get; private set; }
    public int Count { get; private set; }
    private readonly TextBlock _text, _chevron;

    /// <summary>Invoked from Narrator / UI Automation (fold or unfold).</summary>
    public event Action? Invoked;

    public GroupLabel(string groupId)
    {
        GroupId = groupId;
        Cursor = Cursors.Hand;
        UseLayoutRounding = true;
        RenderTransformOrigin = new Point(0.5, 0); // swings from its cord
        var tg = new TransformGroup();
        tg.Children.Add(Swing);
        tg.Children.Add(Slide);
        RenderTransform = tg;

        _text = new TextBlock { FontFamily = Theme.Font, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.Ink, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        _chevron = new TextBlock { FontFamily = Theme.Icons, FontSize = 8, Foreground = Theme.Ink, Opacity = 0.7, Margin = new Thickness(5, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_text);
        row.Children.Add(_chevron);

        var cloth = new Grid();
        cloth.Children.Add(new Border { Background = RopeTag.Shadow[0], CornerRadius = new CornerRadius(4), Margin = new Thickness(-2, 1, -2, -4) });
        cloth.Children.Add(new Border { Background = RopeTag.Shadow[1], CornerRadius = new CornerRadius(3), Margin = new Thickness(-1, 0, -1, -1.5) });
        cloth.Children.Add(new Border { Background = RopeTag.Linen, BorderBrush = RopeTag.Edge, BorderThickness = new Thickness(0.75), CornerRadius = new CornerRadius(2, 2, 3, 3) });
        cloth.Children.Add(new Rectangle { Stroke = RopeTag.Stitch, StrokeThickness = 0.8, StrokeDashArray = new DoubleCollection { 2.5, 2 }, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(2.5), IsHitTestVisible = false });
        cloth.Children.Add(new Border { Child = row, Padding = new Thickness(8, 3, 8, 4) });

        var stack = new StackPanel();
        stack.Children.Add(new Border { Width = 1.4, Height = 5, Background = RopeTag.Cord, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(cloth);
        Child = stack;
    }

    public void Set(string name, int count, bool folded)
    {
        _text.Text = name;
        Folded = folded;
        Count = count;
        _chevron.Text = folded ? ChevronRight : ChevronDown;
        Ui.AutomationName(this, L.F("group.name", name, L.N("group.cards", count)));
    }

    public double MeasuredWidth
    {
        get { if (!IsMeasureValid) Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return DesiredSize.Width; }
    }

    /// <summary>"Group created": the tag drops onto the rope and swings, then lets go of every animated value.</summary>
    public void DropIn(Func<bool> settle)
    {
        if (!Motion.Enabled) return;
        var drop = new DoubleAnimation(-28, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } };
        drop.Completed += (_, _) =>
        {
            if (!settle()) return; // the line tucked away meanwhile: its own animation holds Slide now
            Slide.BeginAnimation(TranslateTransform.YProperty, null);
            Slide.Y = 0;
        };
        Slide.BeginAnimation(TranslateTransform.YProperty, drop);
        var swing = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 3, EasingMode = EasingMode.EaseOut } };
        swing.Completed += (_, _) => { Swing.BeginAnimation(RotateTransform.AngleProperty, null); Swing.Angle = 0; };
        Swing.BeginAnimation(RotateTransform.AngleProperty, swing);
    }

    // Border has no automation peer of its own, so the name above would never reach Narrator.
    protected override AutomationPeer OnCreateAutomationPeer() => new InvokePeer(this, () => Invoked?.Invoke());
}

/// <summary>The count on a folded group's top card. Narrator reads it as the group's button; invoking it unfolds.</summary>
internal sealed class StackBadge : Border
{
    private static readonly Brush Fill = Theme.Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)));
    private readonly TextBlock _count;
    private readonly ScaleTransform _pop = new(1, 1);

    public Action? Unfold { get; set; }

    public StackBadge()
    {
        _count = new TextBlock { Foreground = Brushes.White, FontFamily = Theme.Font, FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        Child = _count;
        Background = Fill;
        MinWidth = 20;
        Height = 20;
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(6, 1, 6, 0);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _pop;
    }

    public void Set(int count, string name)
    {
        _count.Text = count.ToString();
        Ui.AutomationName(this, name);
    }

    /// <summary>"Badge pops in": scale 0 → 1 with a little overshoot, then the scale is let go.</summary>
    public void Pop()
    {
        if (!Motion.Enabled) return;
        var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
        a.Completed += (_, _) =>
        {
            _pop.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _pop.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        };
        _pop.ScaleX = _pop.ScaleY = 1;
        _pop.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        _pop.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new InvokePeer(this, () => Unfold?.Invoke());
}

/// <summary>Reads as a button with the element's automation name; the inner text isn't announced again.</summary>
internal sealed class InvokePeer : FrameworkElementAutomationPeer, IInvokeProvider
{
    private readonly Action _invoke;
    public InvokePeer(FrameworkElement owner, Action invoke) : base(owner) => _invoke = invoke;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    protected override string GetClassNameCore() => Owner.GetType().Name;
    protected override List<AutomationPeer>? GetChildrenCore() => null;
    public override object? GetPattern(PatternInterface p) => p == PatternInterface.Invoke ? this : base.GetPattern(p);
    public void Invoke() => _invoke();
}
