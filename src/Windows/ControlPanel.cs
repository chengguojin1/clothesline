using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Jemur.App;
using Jemur.Core;
using Jemur.Interop;
using Jemur.UI;

namespace Jemur.Windows;

/// <summary>The control panel: what's on the line, what was discarded, settings and about.</summary>
internal sealed class ControlPanel : Window
{
    private enum Page { Line, Discarded, Settings, About }

    private readonly JemurApp _app;
    private readonly ContentControl _host = new();
    private readonly Dictionary<Page, RadioButton> _nav = new();
    private readonly StackPanel _navPanel = new();
    private Page _page = Page.Line;
    private string _filter = "all";
    private string _query = "";
    private UpdateCheckResult? _updateResult;
    private DateTime? _lastUpdateCheck;
    private bool _updateChecking;
    private CancellationTokenSource? _updateRequest;
    private System.Windows.Threading.DispatcherTimer? _searchDelay;

    public ControlPanel(JemurApp app)
    {
        _app = app;
        Title = "Jemur";
        Width = 1040; Height = 700; MinWidth = 720; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.Font;
        UseLayoutRounding = true;
        SetResourceReference(ForegroundProperty, "J.Text");
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/jemur.ico")); } catch { }
        Theme.StyleWindow(this);

        var root = new DockPanel();
        var pane = BuildPane();
        DockPanel.SetDock(pane, Dock.Left);
        root.Children.Add(pane);
        root.Children.Add(_host);
        Content = root;

        _app.Library.Changed += OnLibraryChanged;
        _app.RopeSwitched += OnLibraryChanged;
        L.Changed += OnLanguageChanged;
        Closed += (_, _) =>
        {
            _updateRequest?.Cancel();
            _app.Library.Changed -= OnLibraryChanged;
            _app.RopeSwitched -= OnLibraryChanged;
            L.Changed -= OnLanguageChanged;
            Memory.TrimSoon();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !IsRecordingShortcut(e.OriginalSource as DependencyObject)) Close();
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && _page == Page.Line) { _focusSearch = true; Navigate(Page.Line); e.Handled = true; }
        };
        Navigate(Page.Line);
        ContentRendered += (_, _) => Memory.Report("panel shown");
    }

    private bool _focusSearch;

    private static bool IsRecordingShortcut(DependencyObject? source)
    {
        for (var d = source; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is HotkeyRecorder recorder) return recorder.IsRecording;
        return false;
    }

    private System.Windows.Threading.DispatcherTimer? _refresh;

    /// <summary>Coalesces bursts (typing in a note fires a change per keystroke) into one redraw.</summary>
    private void OnLibraryChanged()
    {
        if (_page is not (Page.Line or Page.Discarded)) return;
        _refresh ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(350), System.Windows.Threading.DispatcherPriority.Background, (_, _) =>
        {
            _refresh!.Stop();
            if (IsVisible) Navigate(_page, animate: false);
        }, Dispatcher);
        _refresh.Stop();
        _refresh.Start();
    }

    private void OnLanguageChanged()
    {
        BuildNav();
        Navigate(_page, animate: false);
    }

    public void ShowSettings() => Navigate(Page.Settings);

    public void ShowLine() => Navigate(Page.Line);

    /// <summary>Renders each page to a PNG (used by <c>--render-docs</c> to produce README screenshots).</summary>
    internal void RenderPages(string dir, string suffix)
    {
        foreach (var (page, name) in new[] { (Page.Line, "panel"), (Page.Settings, "settings"), (Page.Discarded, "discarded"), (Page.About, "about") })
        {
            Navigate(page, animate: false);
            var root = (FrameworkElement)Content;
            var oldBg = root is Panel pnl ? pnl.Background : null;
            if (root is Panel p) p.Background = Theme.Window;
            root.UpdateLayout();
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            const double scale = 1.5;
            var rtb = new RenderTargetBitmap((int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(root);
            Images.SavePng(rtb, System.IO.Path.Combine(dir, $"{name}-{suffix}.png"));
            if (root is Panel p2) p2.Background = oldBg;
        }
    }

    // ───────────── navigation pane ─────────────

    private UIElement BuildPane()
    {
        var pane = new DockPanel { Width = 240, Margin = new Thickness(8, 8, 0, 8) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 6, 0, 18) };
        brand.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo-256.png")) { DecodePixelWidth = 48 }, Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0) });
        brand.Children.Add(Ui.Text("Jemur", Ui.Type.BodyStrong));
        DockPanel.SetDock(brand, Dock.Top);
        pane.Children.Add(brand);
        DockPanel.SetDock(_navPanel, Dock.Top);
        pane.Children.Add(_navPanel);
        BuildNav();
        return pane;
    }

    private void BuildNav()
    {
        bool restoreFocus = _nav.Values.Any(button => button.IsKeyboardFocusWithin);
        _navPanel.Children.Clear();
        _nav.Clear();
        void add(Page p, string glyph, string text, int? badge = null)
        {
            var dp = new DockPanel();
            var g = Ui.Glyph(glyph, 16);
            g.Margin = new Thickness(0, 0, 14, 0);
            dp.Children.Add(g);
            if (badge is > 0)
            {
                var b = Ui.Text(badge.Value.ToString(L.Culture), Ui.Type.Caption, "J.TextSecondary");
                DockPanel.SetDock(b, Dock.Right);
                dp.Children.Add(b);
            }
            dp.Children.Add(Ui.Text(text));
            var r = new RadioButton { GroupName = "nav", Content = dp, IsChecked = p == _page };
            r.SetResourceReference(StyleProperty, "J.NavItem");
            r.Checked += (_, _) => { if (_page != p) Navigate(p); };
            Ui.AutomationName(r, text);
            _nav[p] = r;
            _navPanel.Children.Add(r);
        }
        add(Page.Line, "", L.T("nav.line"));
        add(Page.Discarded, "", L.T("nav.discarded"), _app.Library.Discarded.Count);
        add(Page.Settings, "", L.T("nav.settings"));
        add(Page.About, "", L.T("nav.about"));
        if (restoreFocus) _nav[_page].Focus();
    }

    private void Navigate(Page p, bool animate = true)
    {
        bool changed = p != _page;
        _page = p;
        if (_nav.TryGetValue(p, out var r) && r.IsChecked != true) r.IsChecked = true;
        if (changed || p == Page.Discarded) BuildNav();
        FrameworkElement content = p switch
        {
            Page.Discarded => DiscardedPage(),
            Page.Settings => SettingsPage(),
            Page.About => AboutPage(),
            _ => LinePage(),
        };
        if (animate && changed)
        {
            content.Opacity = 0;
            var tt = new TranslateTransform(0, 12);
            content.RenderTransform = tt;
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 } });
        }
        _host.Content = content;
    }

    /// <summary>Page frame: a title, an optional command row, then scrolling content on a layer.</summary>
    private static DockPanel Shell(string title, UIElement? commands, UIElement body, bool scroll = true)
    {
        var dp = new DockPanel();
        var head = new DockPanel { Margin = new Thickness(36, 28, 36, 20) };
        if (commands != null) { DockPanel.SetDock(commands, Dock.Right); head.Children.Add(commands); }
        var t = Ui.Text(title, Ui.Type.Title);
        t.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(t);
        DockPanel.SetDock(head, Dock.Top);
        dp.Children.Add(head);
        UIElement inner = scroll
            ? new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(36, 0, 24, 24), Focusable = false }
            : new Border { Child = body, Padding = new Thickness(36, 0, 36, 24) };
        dp.Children.Add(inner);
        return dp;
    }

    // ───────────── Line ─────────────

    private FrameworkElement LinePage()
    {
        var items = _app.Library.ItemsOn(_app.ActiveRope).ToList();
        var commands = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var paste = Ui.Button(L.T("action.paste"), _app.HangClipboard, Ui.Kind.Subtle, "", HotkeyTip(Actions.Paste));
        var note = Ui.Button(L.T("action.newNote"), _app.NewNote, Ui.Kind.Standard, "", HotkeyTip(Actions.Note));
        var shot = Ui.Button(L.T("action.screenshot"), () => _ = _app.TakeScreenshot(), Ui.Kind.Accent, "", HotkeyTip(Actions.Screenshot));
        paste.Margin = note.Margin = new Thickness(0, 0, 8, 0);
        commands.Children.Add(paste);
        commands.Children.Add(note);
        commands.Children.Add(shot);

        var body = new StackPanel();
        body.Children.Add(RopeBar());
        if (items.Count == 0)
        {
            string title = _app.Library.Ropes.Count > 1 ? L.F("rope.emptyTitle", _app.Library.RopeById(_app.ActiveRope)?.Name ?? "") : L.T("line.emptyTitle");
            body.Children.Add(EmptyState(title, L.F("line.emptyText", HotkeyText(Actions.Screenshot), HotkeyText(Actions.Note)), rope: true));
            return Shell(L.T("nav.line"), commands, body);
        }

        // filter + search
        var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var (search, box) = Ui.SearchBox(L.T("line.search"), _query);
        search.Width = 260;
        box.TextChanged += (_, _) =>
        {
            _query = box.Text;
            _searchDelay ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _searchDelay.Stop();
            _searchDelay.Tick -= SearchTick;
            _searchDelay.Tick += SearchTick;
            _searchDelay.Start();
        };
        if (_query.Length > 0 || _focusSearch) Dispatcher.BeginInvoke(() => { box.Focus(); box.CaretIndex = box.Text.Length; }, System.Windows.Threading.DispatcherPriority.Input);
        _focusSearch = false;
        DockPanel.SetDock(search, Dock.Right);
        tools.Children.Add(search);
        int shots = items.Count(i => i.Kind == ItemKind.Image), notes = items.Count - shots;
        var filter = Ui.DropDown(new[] { ("all", L.F("filter.all", items.Count)), ("img", L.F("filter.screenshots", shots)), ("note", L.F("filter.notes", notes)) }, _filter,
            v => { _filter = v; Navigate(Page.Line, animate: false); }, 170);
        filter.HorizontalAlignment = HorizontalAlignment.Left;
        tools.Children.Add(filter);
        body.Children.Add(tools);

        var shown = items
            .Where(i => _filter == "all" || (_filter == "img") == (i.Kind == ItemKind.Image))
            .Where(i => _query.Length == 0 || (i.Kind == ItemKind.Note && i.Text.Contains(_query, StringComparison.CurrentCultureIgnoreCase)))
            .OrderByDescending(i => i.Created).ToList();

        if (shown.Count == 0)
        {
            body.Children.Add(EmptyState(L.T("line.noMatchTitle"), L.T("line.noMatchText"), rope: false));
            return Shell(L.T("nav.line"), commands, body);
        }
        var wrap = new WrapPanel { Margin = new Thickness(-6, 0, 0, 0) };
        foreach (var it in shown) wrap.Children.Add(Tile(it, discarded: false));
        body.Children.Add(wrap);
        return Shell(L.T("nav.line"), commands, body);
    }

    /// <summary>Which line the page (and the screen) shows, plus creating, renaming and deleting lines.</summary>
    private UIElement RopeBar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var ropes = _app.Library.Ropes.Select(r => (r.Id, r.Name)).ToList();
        var pick = Ui.DropDown(ropes, _app.ActiveRope, _app.SwitchRope, 200);
        Ui.AutomationName(pick, $"{L.T("rope.lines")}: {_app.Library.RopeById(_app.ActiveRope)?.Name}");
        bar.Children.Add(pick);
        var rename = Ui.Button(L.T("rope.rename"), () => _app.RenameRope(_app.ActiveRope, this), Ui.Kind.Subtle, "");
        var delete = Ui.Button(L.T("rope.delete"), () => _app.DeleteRope(_app.ActiveRope, this), Ui.Kind.Subtle, "");
        delete.IsEnabled = _app.ActiveRope != Rope.DefaultId;
        var add = Ui.Button(L.T("rope.new"), () => _app.NewRope(this), Ui.Kind.Subtle, "");
        rename.Margin = new Thickness(8, 0, 0, 0);
        bar.Children.Add(rename);
        bar.Children.Add(delete);
        bar.Children.Add(add);
        return bar;
    }

    private void SearchTick(object? sender, EventArgs e)
    {
        _searchDelay?.Stop();
        Navigate(Page.Line, animate: false);
    }

    private string HotkeyText(string action) =>
        _app.Settings.Hotkeys.TryGetValue(action, out var s) && Hotkey.TryParse(s, out var hk) ? hk.ToString() : "—";

    private string HotkeyTip(string action) => HotkeyText(action) is var t && t != "—" ? t : "";

    /// <summary>On-brand empty state: a bare line with two empty pins, drawn in the theme's tertiary ink.</summary>
    private static UIElement EmptyState(string title, string text, bool rope)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 56, 0, 40), MaxWidth = 420 };
        if (rope)
        {
            var c = new Canvas { Width = 240, Height = 64, Margin = new Thickness(0, 0, 0, 20) };
            var line = new Path { Data = Geometry.Parse("M 4,14 Q 120,40 236,14"), StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            line.SetResourceReference(Shape.StrokeProperty, "J.TextTertiary");
            c.Children.Add(line);
            foreach (var x in new[] { 84.0, 150.0 })
            {
                double y = 14 + 4 * 13 * (x - 4) / 232 * (1 - (x - 4) / 232);
                var pin = new Rectangle { Width = 7, Height = 20, RadiusX = 2, RadiusY = 2, StrokeThickness = 1.5 };
                pin.SetResourceReference(Shape.StrokeProperty, "J.TextTertiary");
                Canvas.SetLeft(pin, x - 3.5); Canvas.SetTop(pin, y - 7);
                c.Children.Add(pin);
            }
            sp.Children.Add(c);
        }
        var t = Ui.Text(title, Ui.Type.Subtitle);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(t);
        var d = Ui.Wrap(text);
        d.TextAlignment = TextAlignment.Center;
        d.Margin = new Thickness(0, 8, 0, 0);
        d.LineHeight = 20;
        sp.Children.Add(d);
        return sp;
    }

    private FrameworkElement Tile(HangItem it, bool discarded)
    {
        const double W = 224, H = 148;
        var root = new StackPanel { Width = W, Margin = new Thickness(6, 0, 6, 18), Cursor = discarded ? Cursors.Arrow : Cursors.Hand, Background = Brushes.Transparent };

        var thumb = new Grid { Height = H };
        var frame = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), ClipToBounds = true };
        frame.SetResourceReference(Border.BorderBrushProperty, "J.CardStroke");
        if (it.Kind == ItemKind.Image && it.File != null)
        {
            frame.SetResourceReference(Border.BackgroundProperty, "J.Card");
            try
            {
                var img = new Image { Source = Images.Thumbnail(it.File, (int)(W * 2)), Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                frame.Child = img;
                thumb.Clip = new RectangleGeometry(new Rect(0, 0, W, H), 4, 4);
            }
            catch { frame.Child = Ui.Glyph("", 24, "J.TextTertiary"); }
        }
        else
        {
            frame.Background = new SolidColorBrush(NoteColors.Paper(it.Color));
            frame.Padding = new Thickness(14, 12, 14, 10);
            frame.Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(it.Text) ? L.T("note.empty") : it.Text, FontFamily = Theme.Hand, FontSize = 13, Foreground = Theme.Ink,
                TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 20, Opacity = string.IsNullOrWhiteSpace(it.Text) ? 0.45 : 1,
            };
        }
        string title = it.Kind == ItemKind.Image ? L.T("kind.screenshot") : FirstLine(it.Text) ?? L.T("kind.note");
        if (discarded) thumb.Children.Add(frame);
        else
        {
            var open = Ui.Button(title, () => _app.Spotlight(it), Ui.Kind.Subtle);
            open.Content = frame;
            open.Padding = new Thickness(0);
            open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            open.VerticalContentAlignment = VerticalAlignment.Stretch;
            thumb.Children.Add(open);
        }

        // hover actions, top-right
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        void act(string glyph, string tip, Action a)
        {
            var b = Ui.IconButton(glyph, tip, a, 30);
            actions.Children.Add(b);
        }
        if (discarded)
        {
            act("", L.T("action.restore"), () => _app.Restore(it));
            act("", L.T("action.deleteForever"), () => _app.Library.DeleteForever(it));
        }
        else
        {
            act("", L.T("action.copy"), () => { if (_app.Copy(it)) _app.Kick(it, 40); });
            if (it.Kind == ItemKind.Image) act("", L.T("action.markup"), () => _app.Markup(it));
            act("", L.T("action.discard"), () => _app.Discard(it));
        }
        var bar = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(2), BorderThickness = new Thickness(1), Child = actions, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0), Opacity = 0 };
        bar.SetResourceReference(Border.BackgroundProperty, "J.Flyout");
        bar.SetResourceReference(Border.BorderBrushProperty, "J.FlyoutStroke");
        thumb.Children.Add(bar);
        root.Children.Add(thumb);

        // caption
        var t = Ui.Text(title);
        t.Margin = new Thickness(2, 9, 2, 0);
        root.Children.Add(t);
        if (discarded && _app.Library.RopeById(it.RopeId ?? Rope.DefaultId) is { } rope)
        {
            var r = Ui.Text(rope.Name, Ui.Type.Caption, "J.TextSecondary"); // Discarded covers every line, so say which one it goes back to
            r.Margin = new Thickness(2, 1, 2, 0);
            root.Children.Add(r);
        }
        string sub = discarded && it.DiscardedAt is { } at ? L.F("discarded.when", Ui.Ago(at)) : Ui.Ago(it.Created);
        if (it.Kind == ItemKind.Image && it.File != null && Images.Size(it.File) is var (pw, ph) && pw > 0) sub += $"  ·  {pw} × {ph}";
        var s = Ui.Text(sub, Ui.Type.Caption, "J.TextSecondary");
        s.Margin = new Thickness(2, 1, 2, 0);
        root.Children.Add(s);

        void revealActions()
        {
            bool visible = root.IsMouseOver || root.IsKeyboardFocusWithin;
            bar.BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(100)));
            bar.IsHitTestVisible = visible;
            frame.SetResourceReference(Border.BorderBrushProperty, visible ? "J.ControlStrokeBottom" : "J.CardStroke");
        }
        bar.IsHitTestVisible = false;
        root.MouseEnter += (_, _) => revealActions();
        root.MouseLeave += (_, _) => revealActions();
        root.IsKeyboardFocusWithinChanged += (_, _) => revealActions();
        if (!discarded)
        {
            root.MouseLeftButtonUp += (_, e) => { if (!IsInsideButton(e.OriginalSource as DependencyObject)) _app.Spotlight(it); };
            root.MouseRightButtonUp += (_, e) => { _app.ShowItemMenu(it, root); e.Handled = true; };
            root.PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.Apps || (key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift))
                {
                    _app.ShowItemMenu(it, Keyboard.FocusedElement as FrameworkElement ?? root, keyboard: true);
                    e.Handled = true;
                }
            };
        }
        Ui.AutomationName(root, title);
        return root;
    }

    private static bool IsInsideButton(DependencyObject? d)
    {
        for (; d != null; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    internal static string? FirstLine(string text)
    {
        var l = text.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0);
        return l == null ? null : l.Length > 40 ? l[..40] + "…" : l;
    }

    // ───────────── Discarded ─────────────

    private FrameworkElement DiscardedPage()
    {
        var items = _app.Library.Discarded;
        var empty = Ui.Button(L.T("discarded.empty"), () =>
        {
            if (MessageBox.Show(this, L.N("discarded.confirm", items.Count), L.T("discarded.empty"), MessageBoxButton.OKCancel, MessageBoxImage.None) == MessageBoxResult.OK)
                _app.Library.EmptyDiscarded();
        }, Ui.Kind.Standard, "");
        empty.IsEnabled = items.Count > 0;

        var body = new StackPanel();
        var info = Ui.Wrap(L.N("discarded.info", _app.Settings.KeepDiscardedDays), Ui.Type.Body);
        info.Margin = new Thickness(0, 0, 0, 20);
        body.Children.Add(info);
        if (items.Count == 0)
        {
            body.Children.Add(EmptyState(L.T("discarded.noneTitle"), L.T("discarded.noneText"), rope: false));
            return Shell(L.T("nav.discarded"), empty, body);
        }
        var wrap = new WrapPanel { Margin = new Thickness(-6, 0, 0, 0) };
        foreach (var it in items) wrap.Children.Add(Tile(it, discarded: true));
        body.Children.Add(wrap);
        return Shell(L.T("nav.discarded"), empty, body);
    }

    // ───────────── Settings ─────────────

    private FrameworkElement SettingsPage()
    {
        var s = _app.Settings;
        var list = new StackPanel { MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Stretch };

        list.Children.Add(Ui.GroupHeader(L.T("settings.appearance")));
        list.Children.Add(Ui.SettingsCard("", L.T("settings.theme"), L.T("settings.themeDesc"),
            Ui.DropDown(new[] { ("system", L.T("settings.themeSystem")), ("light", L.T("settings.themeLight")), ("dark", L.T("settings.themeDark")) }, s.Theme, _app.SetTheme, 210)));
        var langs = new List<(string, string)> { ("auto", L.T("settings.languageAuto")) };
        langs.AddRange(L.Available.Select(l => (l.Code, l.Name)));
        string language = s.Language.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "auto" : L.Code.ToLowerInvariant();
        list.Children.Add(Ui.SettingsCard("", L.T("settings.language"), L.T("settings.languageDesc"), Ui.DropDown(langs, language, _app.SetLanguage, 210)));
        list.Children.Add(Ui.SettingsCard("", L.T("settings.cardSize"), L.T("settings.cardSizeDesc"),
            Ui.DropDown(new[] { (0, L.T("settings.small")), (1, L.T("settings.medium")), (2, L.T("settings.large")) }, s.CardSize, _app.SetCardSize, 210)));
        list.Children.Add(Ui.SettingsCard("\uE718", L.T("settings.mode"), null,
            Ui.DropDown(new[] { (Settings.LineMode, L.T("mode.line")), (Settings.DesktopMode, L.T("mode.desktop")) }, s.Mode, _app.SetMode, 210)));

        list.Children.Add(Ui.GroupHeader(L.T("settings.behavior")));
        list.Children.Add(Ui.SettingsCard("", L.T("settings.autoHide"), L.T("settings.autoHideDesc"), Ui.Switch(s.AutoHide, _app.SetAutoHide)));
        list.Children.Add(Ui.SettingsCard("", L.T("settings.keep"), L.T("settings.keepDesc"),
            Ui.DropDown(new[] { (1, L.N("settings.days", 1)), (7, L.N("settings.days", 7)), (30, L.N("settings.days", 30)) }, s.KeepDiscardedDays, _app.SetKeepDays, 210)));
        list.Children.Add(Ui.SettingsCard("", L.T("settings.startup"), L.T("settings.startupDesc"), Ui.Switch(_app.IsStartupEnabled(), _app.SetStartup)));

        list.Children.Add(Ui.GroupHeader(L.T("settings.shortcuts")));
        var shortcuts = new StackPanel();
        foreach (var (action, glyph) in new[] { (Actions.Screenshot, ""), (Actions.Note, ""), (Actions.Paste, ""), (Actions.Toggle, ""), (Actions.NextRope, ""), (Actions.Mode, "\uE718"), (Actions.Panel, "") })
        {
            Hotkey? current = s.Hotkeys.TryGetValue(action, out var str) && Hotkey.TryParse(str, out var hk) ? hk : null;
            var rec = new HotkeyRecorder(current, h => _app.SetHotkey(action, h), recording => _app.SuspendHotkeys(recording));
            shortcuts.Children.Add(Ui.SettingsCard(glyph, L.T("shortcut." + action), null, rec));
        }
        list.Children.Add(shortcuts);
        var reset = Ui.Button(L.T("settings.resetShortcuts"), () => { _app.ResetHotkeys(); Navigate(Page.Settings, animate: false); }, Ui.Kind.Subtle);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 4, 0, 0);
        list.Children.Add(reset);

        list.Children.Add(Ui.GroupHeader(L.T("settings.storage")));
        list.Children.Add(Ui.SettingsCard("", L.T("settings.folder"), AppPaths.Shots, Ui.Button(L.T("action.openFolder"), () => Open(AppPaths.Shots))));
        list.Children.Add(Ui.SettingsCard("", L.T("settings.logs"), L.T("settings.logsDesc"), Ui.Button(L.T("action.openFolder"), () => Open(AppPaths.Logs))));
        return Shell(L.T("nav.settings"), null, list);
    }

    // ───────────── About ─────────────

    private FrameworkElement AboutPage()
    {
        var list = new StackPanel { MaxWidth = 1000 };
        var head = new DockPanel();
        var logo = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo-256.png")) { DecodePixelWidth = 128 }, Width = 64, Height = 64, Margin = new Thickness(0, 0, 18, 0) };
        head.Children.Add(logo);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text("Jemur", Ui.Type.Subtitle));
        text.Children.Add(Ui.Text(L.F("about.version", JemurApp.Version), Ui.Type.Body, "J.TextSecondary"));
        head.Children.Add(text);
        list.Children.Add(Ui.Card(head, new Thickness(20)));

        var updateActions = new StackPanel { Orientation = Orientation.Horizontal };
        var check = Ui.Button(_updateChecking ? L.T("about.checkingUpdates") : L.T("about.checkUpdates"), () => _ = CheckForUpdates(), Ui.Kind.Subtle);
        check.IsEnabled = !_updateChecking;
        updateActions.Children.Add(check);
        if (_updateResult?.Kind == UpdateCheckKind.UpdateAvailable && _updateResult.Manifest is { } update)
        {
            var download = Ui.Button(L.T("about.downloadInstaller"), () => Open(update.ReleaseUrl.ToString()), Ui.Kind.Accent);
            download.Margin = new Thickness(8, 0, 0, 0);
            updateActions.Children.Add(download);
        }
        var updateStatus = _updateResult?.Kind switch
        {
            UpdateCheckKind.UpToDate => L.F("about.upToDate", JemurApp.Version),
            UpdateCheckKind.UpdateAvailable when _updateResult.Manifest is { } available => L.F("about.updateAvailable", available.Version),
            UpdateCheckKind.Failed => L.F("about.updateFailed", _updateResult.Error ?? ""),
            _ => L.T("about.updatesDesc"),
        };
        if (_lastUpdateCheck is { } checkedAt) updateStatus += " " + L.F("about.lastChecked", checkedAt.ToString("g", L.Culture));
        list.Children.Add(Ui.SettingsCard("", L.T("about.updates"), updateStatus, updateActions));

        var tagline = Ui.Wrap(L.T("about.tagline"), Ui.Type.Body, "J.Text");
        tagline.Margin = new Thickness(2, 16, 0, 4);
        list.Children.Add(tagline);

        list.Children.Add(Ui.GroupHeader(L.T("about.links")));
        list.Children.Add(Ui.SettingsCard("", L.T("about.source"), JemurApp.RepoUrl, Ui.Button(L.T("action.open"), () => Open(JemurApp.RepoUrl), Ui.Kind.Standard, "")));
        list.Children.Add(Ui.SettingsCard("", L.T("about.issue"), L.T("about.issueDesc"), Ui.Button(L.T("action.open"), () => Open(JemurApp.RepoUrl + "/issues/new/choose"), Ui.Kind.Standard, "")));
        list.Children.Add(Ui.SettingsCard("", L.T("about.translate"), L.T("about.translateDesc"), Ui.Button(L.T("action.openFolder"), () => { System.IO.Directory.CreateDirectory(AppPaths.UserLocales); Open(AppPaths.UserLocales); })));

        list.Children.Add(Ui.GroupHeader(L.T("about.credits")));
        list.Children.Add(Ui.Card(Ui.Wrap(L.T("about.creditsText"), Ui.Type.Body)));
        list.Children.Add(Ui.Card(Ui.Wrap(L.T("about.license"), Ui.Type.Body)));
        return Shell(L.T("nav.about"), null, list);
    }

    private async Task CheckForUpdates()
    {
        if (_updateChecking) return;
        _updateChecking = true;
        _updateRequest?.Cancel();
        _updateRequest = new CancellationTokenSource();
        Navigate(Page.About, animate: false);
        try
        {
            var result = await _app.CheckForUpdatesAsync(_updateRequest.Token);
            if (!IsVisible) return;
            _updateResult = result;
            _lastUpdateCheck = DateTime.Now;
        }
        finally
        {
            _updateChecking = false;
            if (IsVisible) Navigate(Page.About, animate: false);
        }
    }

    private static void Open(string target)
    {
        try
        {
            if (!target.StartsWith("http", StringComparison.OrdinalIgnoreCase)) System.IO.Directory.CreateDirectory(target);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn($"Could not open {target}", ex); }
    }
}
