using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Jemur.App;
using Jemur.Core;
using Jemur.Interop;
using Jemur.UI;

namespace Jemur.Windows;

/// <summary>
/// Takes an item off the line into the middle of the screen: a large sheet of paper to write on, or a
/// big print of a screenshot with a command bar. Flies back to its spot on the line when dismissed.
/// </summary>
internal sealed class SpotlightWindow : Window
{
    private readonly HangItem _item;
    private readonly JemurApp _app;
    private readonly Point _origin;
    private readonly Border _dim;
    private readonly Grid _stage;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _move = new();
    private readonly RotateTransform _rot = new();
    private bool _closing, _ready;
    private bool _suspendDeactivate;

    private TextBox? _box;
    private TextBlock? _placeholder, _count;
    private Border? _paper;
    private StackPanel? _swatches;
    private Border? _flash;
    private FrameworkElement? _card;

    public event Action<HangItem>? Finished;

    public SpotlightWindow(HangItem item, JemurApp app, Point origin)
    {
        _item = item;
        _app = app;
        _origin = origin;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0; Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        FontFamily = Theme.Font;

        var root = new Grid();
        _dim = new Border { Background = Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x73, 0, 0, 0))), Opacity = 0 };
        _dim.MouseLeftButtonDown += (_, _) => Dismiss();
        root.Children.Add(_dim);

        _stage = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
        var tg = new TransformGroup();
        tg.Children.Add(_scale);
        tg.Children.Add(_rot);
        tg.Children.Add(_move);
        _stage.RenderTransform = tg;
        _stage.Children.Add(item.Kind == ItemKind.Note ? BuildNote() : BuildImage());
        root.Children.Add(_stage);

        var hint = new TextBlock
        {
            Text = L.T(item.Kind == ItemKind.Note ? "spotlight.noteHint" : "spotlight.imageHint"),
            Foreground = Theme.Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF))), FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 36), IsHitTestVisible = false,
        };
        root.Children.Add(hint);
        Content = root;

        SourceInitialized += (_, _) => Native.MakeToolWindow(this);
        Loaded += (_, _) => AnimateIn();
        Deactivated += (_, _) => { if (_ready && !_suspendDeactivate) Dismiss(); };
        PreviewKeyDown += OnKey;
    }

    // ───────────── note ─────────────

    private UIElement BuildNote()
    {
        _paper = new Border
        {
            Width = 500, Height = 420, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 22, 0, 0),
            Background = new SolidColorBrush(NoteColors.Paper(_item.Color)), Padding = new Thickness(36, 34, 32, 18),
            Effect = new DropShadowEffect { BlurRadius = 48, ShadowDepth = 18, Direction = 270, Opacity = 0.42 },
        };
        var dock = new DockPanel();

        // footer: word count · colour swatches · copy · delete
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(PaperButton("", L.T("action.copy"), () => { if (_app.Copy(_item)) Flash(L.T("toast.copied")); }));
        actions.Children.Add(PaperButton("", L.T("action.discard"), () => Dismiss(() => _app.Discard(_item))));
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        _swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        for (int i = 0; i < NoteColors.Count; i++)
        {
            int idx = i;
            var sw = new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 7, 0), Cursor = Cursors.Hand, BorderThickness = new Thickness(1.5),
                Background = new SolidColorBrush(NoteColors.Paper(i)), ToolTip = L.T(NoteColors.Keys[i]),
            };
            Ui.AutomationName(sw, L.T(NoteColors.Keys[i]));
            sw.MouseLeftButtonUp += (_, e) => { SetColor(idx); e.Handled = true; };
            _swatches.Children.Add(sw);
        }
        DockPanel.SetDock(_swatches, Dock.Right);
        footer.Children.Add(_swatches);
        _count = new TextBlock { FontSize = 12, FontFamily = Theme.Font, Foreground = InkFaint, VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(_count);
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        RefreshSwatches();

        var area = new Grid();
        _box = new TextBox
        {
            Text = _item.Text, FontFamily = Theme.Hand, FontSize = 19, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Theme.Ink, CaretBrush = Theme.Ink,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0), FocusVisualStyle = null,
            SelectionBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x6D, 0x2B)), SelectionOpacity = 0.3,
        };
        _box.SpellCheck.IsEnabled = false;
        TextBlock.SetLineHeight(_box, 31);
        _box.Template = PlainTextBoxTemplate();
        _placeholder = new TextBlock { Text = L.T("note.placeholder"), FontFamily = Theme.Hand, FontSize = 19, Foreground = Theme.Ink, Opacity = 0.32, IsHitTestVisible = false, Margin = new Thickness(2, 1, 0, 0) };
        _box.TextChanged += (_, _) =>
        {
            _item.Text = _box.Text;
            UpdateCount();
            _app.Library.Touch(_item);
        };
        area.Children.Add(_placeholder);
        area.Children.Add(_box);
        dock.Children.Add(area);
        UpdateCount();
        _paper.Child = dock;

        var holder = new Grid();
        holder.Children.Add(_paper);
        holder.Children.Add(BigPin());
        _card = holder;
        return holder;
    }

    private static readonly Brush InkFaint = Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x8C, 0x33, 0x2C, 0x1E)));
    private static readonly Brush InkHover = Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x14, 0x33, 0x2C, 0x1E)));

    /// <summary>A bare template: the default TextBox template draws a border and focus chrome we don't want on paper.</summary>
    private static ControlTemplate PlainTextBoxTemplate()
    {
        var t = new ControlTemplate(typeof(TextBox));
        var sv = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        sv.SetValue(ScrollViewer.FocusableProperty, false);
        t.VisualTree = sv;
        return t;
    }

    private Button PaperButton(string glyph, string tip, Action a)
    {
        var g = new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 16, Foreground = Theme.Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var bd = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Child = g };
        var b = new Button { Content = bd, ToolTip = tip, Cursor = Cursors.Hand, Margin = new Thickness(2, 0, 0, 0), Focusable = false };
        var tpl = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        b.Template = tpl;
        b.MouseEnter += (_, _) => bd.Background = InkHover;
        b.MouseLeave += (_, _) => bd.Background = Brushes.Transparent;
        Ui.AutomationName(b, tip);
        b.Click += (_, _) => a();
        return b;
    }

    private static UIElement BigPin()
    {
        var pin = new Grid { Width = 18, Height = 48, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        pin.Children.Add(new Border { CornerRadius = new CornerRadius(4, 4, 3, 3), Background = new LinearGradientBrush(Color.FromRgb(0xE6, 0xB7, 0x83), Color.FromRgb(0xAF, 0x74, 0x3D), 0), Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 2, Direction = 270, Opacity = 0.4 } });
        pin.Children.Add(new Border { Width = 1.4, Background = new SolidColorBrush(Color.FromArgb(0x66, 0x4A, 0x2C, 0x10)), Margin = new Thickness(0, 4, 0, 4) });
        pin.Children.Add(new Border { Height = 4, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-2, 17, -2, 0), CornerRadius = new CornerRadius(2), Background = new LinearGradientBrush(Color.FromRgb(0xDD, 0xE0, 0xE5), Color.FromRgb(0x84, 0x8B, 0x94), 90) });
        return pin;
    }

    private void SetColor(int idx)
    {
        _item.Color = idx;
        if (_paper?.Background is SolidColorBrush b)
        {
            var nb = new SolidColorBrush(b.Color);
            _paper.Background = nb;
            nb.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(NoteColors.Paper(idx), TimeSpan.FromMilliseconds(200)));
        }
        RefreshSwatches();
        _app.Library.Touch(_item);
    }

    private void RefreshSwatches()
    {
        if (_swatches == null) return;
        for (int i = 0; i < _swatches.Children.Count; i++)
            ((Border)_swatches.Children[i]).BorderBrush = i == _item.Color ? Theme.Ink : InkHover;
    }

    private void UpdateCount()
    {
        if (_box == null || _count == null || _placeholder == null) return;
        _placeholder.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        int words = _box.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        _count.Text = words == 0 ? _item.Created.ToString("d MMM, HH.mm", L.Culture) : L.N("note.words", words);
    }

    // ───────────── image ─────────────

    private UIElement BuildImage()
    {
        var panel = new StackPanel();
        BitmapImage? bmp = null;
        var (pw, ph) = _item.File != null ? Images.Size(_item.File) : (0, 0);
        // Show at the screenshot's natural size (pixels → DIPs), but never so big that it crowds the line or the command bar.
        double dpi = Native.GetDpiForSystem() / 96.0;
        double maxW = SystemParameters.PrimaryScreenWidth * 0.62, maxH = SystemParameters.PrimaryScreenHeight * 0.58;
        double w = pw > 0 ? pw / dpi : 640, h = ph > 0 ? ph / dpi : 400;
        double f = Math.Min(1, Math.Min(maxW / w, maxH / h));
        w = Math.Round(w * f); h = Math.Round(h * f);
        // Decode at the displayed size × DPI, not the full capture.
        try { if (_item.File != null) bmp = Images.Load(_item.File, (int)Math.Min(pw, w * dpi)); } catch (Exception ex) { Log.Warn("Preview failed", ex); }

        var img = new Image { Source = bmp, Width = w, Height = h, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var print = new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(3), Padding = new Thickness(12, 12, 12, 36),
            Effect = new DropShadowEffect { BlurRadius = 48, ShadowDepth = 18, Direction = 270, Opacity = 0.42 },
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0),
        };
        var inner = new Grid();
        inner.Children.Add(new Border { Child = img, Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC)) });
        var caption = new TextBlock
        {
            Text = _item.Created.ToString("d MMM yyyy · HH.mm", L.Culture) + (pw > 0 ? $"  ·  {pw} × {ph}" : ""),
            FontFamily = Theme.Font, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 0, -26),
        };
        inner.Children.Add(caption);
        print.Child = inner;
        print.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { _app.OpenFile(_item); Dismiss(); } };

        var holder = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        holder.Children.Add(print);
        holder.Children.Add(BigPin());
        _card = holder;
        panel.Children.Add(holder);
        panel.Children.Add(CommandBar());
        return panel;
    }

    private UIElement CommandBar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        void add(string glyph, string text, Action a, Ui.Kind kind = Ui.Kind.Subtle, string? gesture = null)
        {
            var b = Ui.Button(text, a, kind, glyph, gesture == null ? null : $"{text} ({gesture})");
            b.Margin = new Thickness(2, 0, 2, 0);
            bar.Children.Add(b);
        }
        add("", L.T("action.copy"), () => { if (_app.Copy(_item)) Flash(L.T("toast.copied")); }, Ui.Kind.Accent, "Ctrl+C");
        add("", L.T("action.markup"), () => Dismiss(() => _app.Markup(_item)));
        add("", L.T("action.open"), () => { _app.OpenFile(_item); Dismiss(); });
        add("", L.T("action.saveAs"), () => { _suspendDeactivate = true; _app.SaveAs(_item, this); _suspendDeactivate = false; Native.ForceForeground(this); }, gesture: "Ctrl+S");
        add("", L.T("action.showInFolder"), () => { _app.ShowInFolder(_item); Dismiss(); });
        var sep = new Border { Width = 1, Height = 20, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        sep.SetResourceReference(Border.BackgroundProperty, "J.Divider");
        bar.Children.Add(sep);
        add("", L.T("action.discard"), () => Dismiss(() => _app.Discard(_item)), gesture: "Del");

        var shell = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(4), BorderThickness = new Thickness(1), Child = bar,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Direction = 270, Opacity = 0.28 },
        };
        shell.SetResourceReference(Border.BackgroundProperty, "J.Flyout");
        shell.SetResourceReference(Border.BorderBrushProperty, "J.FlyoutStroke");
        return shell;
    }

    // ───────────── feedback ─────────────

    private void Flash(string text)
    {
        if (_flash == null)
        {
            _flash = new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 6, 12, 7), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 72, 0, 0), IsHitTestVisible = false,
                Child = Ui.Text("", Ui.Type.Body), Opacity = 0,
            };
            _flash.SetResourceReference(Border.BackgroundProperty, "J.Flyout");
            _flash.SetResourceReference(Border.BorderBrushProperty, "J.FlyoutStroke");
            ((Grid)Content).Children.Add(_flash);
        }
        ((TextBlock)_flash.Child).Text = text;
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1450))));
        _flash.BeginAnimation(OpacityProperty, a);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (e.Key == Key.Escape || (e.Key == Key.Enter && ctrl)) { Dismiss(); e.Handled = true; }
        else if (_item.Kind == ItemKind.Image && ctrl && e.Key == Key.C) { if (_app.Copy(_item)) Flash(L.T("toast.copied")); e.Handled = true; }
        else if (_item.Kind == ItemKind.Image && ctrl && e.Key == Key.S) { _suspendDeactivate = true; _app.SaveAs(_item, this); _suspendDeactivate = false; e.Handled = true; }
        else if (_item.Kind == ItemKind.Image && e.Key == Key.Delete) { Dismiss(() => _app.Discard(_item)); e.Handled = true; }
    }

    // ───────────── motion ─────────────

    private Vector OriginOffset() => _origin - new Point(ActualWidth / 2, ActualHeight / 2);

    private void AnimateIn()
    {
        var off = OriginOffset();
        var dur = TimeSpan.FromMilliseconds(340);
        var ease = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut };
        _move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(off.X, 0, dur) { EasingFunction = ease });
        _move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(off.Y, 0, dur) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.24, 1, dur) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.24, 1, dur) { EasingFunction = ease });
        _rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-7, _item.Kind == ItemKind.Note ? -1 : 0, TimeSpan.FromMilliseconds(480)) { EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 6, EasingMode = EasingMode.EaseOut } });
        _stage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        _dim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));

        Native.ForceForeground(this);
        if (_box != null) { _box.Focus(); Keyboard.Focus(_box); _box.CaretIndex = _box.Text.Length; }
        else Focus();
        Dispatcher.BeginInvoke(() => _ready = true, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Fly back to the line, close, then optionally run a follow-up action.</summary>
    public void Dismiss(Action? then = null)
    {
        if (_closing) return;
        _closing = true;
        _ready = false;
        var off = OriginOffset();
        var dur = TimeSpan.FromMilliseconds(240);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var last = new DoubleAnimation(1, 0.2, dur) { EasingFunction = ease };
        last.Completed += (_, _) =>
        {
            Close();
            Finished?.Invoke(_item);
            then?.Invoke();
        };
        _move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, off.X, dur) { EasingFunction = ease });
        _move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, off.Y, dur) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.2, dur) { EasingFunction = ease });
        _rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(_rot.Angle, 5, dur) { EasingFunction = ease });
        _stage.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, dur) { BeginTime = TimeSpan.FromMilliseconds(110) });
        _dim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, dur));
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, last);
    }
}
