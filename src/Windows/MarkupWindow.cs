using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Interop;
using Clothesline.UI;

namespace Clothesline.Windows;

/// <summary>Annotate a screenshot: pen, highlighter, box, arrow, eraser. Saves back into the same PNG.</summary>
internal sealed class MarkupWindow : Window
{
    private enum Tool { Pen, Highlighter, Box, Arrow, Eraser }

    private readonly string _path;
    private readonly BitmapImage _bmp;
    private readonly InkCanvas _ink;
    private readonly Stack<(StrokeCollection Added, StrokeCollection Removed)> _undo = new(), _redo = new();
    private readonly Dictionary<Tool, ToggleButton> _toolButtons = new();
    private readonly List<(Color Color, ToggleButton Button)> _colorButtons = new();
    private readonly double _unit;
    private bool _replaying, _dirty;
    private Tool _tool = Tool.Pen;
    private Color _color = Color.FromRgb(0xE8, 0x3B, 0x2E);
    private double _size = 1;
    private Point? _shapeStart;
    private Stroke? _shapePreview;

    private static readonly Color[] Palette =
    {
        Color.FromRgb(0xE8, 0x3B, 0x2E), Color.FromRgb(0xF7, 0xB5, 0x00), Color.FromRgb(0x2E, 0xA0, 0x43),
        Color.FromRgb(0x1F, 0x6F, 0xEB), Color.FromRgb(0x1B, 0x1B, 0x1B), Colors.White,
    };

    public event Action? Saved;

    public MarkupWindow(string path)
    {
        _path = path;
        _bmp = Images.Load(path);
        _unit = Math.Max(2, Math.Max(_bmp.PixelWidth, _bmp.PixelHeight) / 300.0);

        Title = L.T("markup.title");
        FontFamily = Theme.Font;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var wa = SystemParameters.WorkArea;
        Width = Math.Min(wa.Width * 0.86, Math.Max(760, _bmp.PixelWidth + 96));
        Height = Math.Min(wa.Height * 0.9, Math.Max(540, _bmp.PixelHeight + 180));
        MinWidth = 720; MinHeight = 420;
        SetResourceReference(ForegroundProperty, "J.Text");
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/jemur.ico")); } catch { }
        Theme.StyleWindow(this);

        var surface = new Grid { Width = _bmp.PixelWidth, Height = _bmp.PixelHeight };
        surface.Children.Add(new Image { Source = _bmp, Stretch = Stretch.Fill });
        _ink = new InkCanvas { Background = Brushes.Transparent };
        _ink.Strokes.StrokesChanged += (_, e) =>
        {
            if (_replaying) return;
            _undo.Push((new StrokeCollection(e.Added), new StrokeCollection(e.Removed)));
            _redo.Clear();
            _dirty = true;
        };
        _ink.PreviewMouseLeftButtonDown += Shape_Down;
        _ink.PreviewMouseMove += Shape_Move;
        _ink.PreviewMouseLeftButtonUp += Shape_Up;
        surface.Children.Add(_ink);

        var frame = new Border { Child = surface, BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "J.CardStroke");
        var view = new Viewbox { Child = frame, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(24, 8, 24, 24) };

        var dock = new DockPanel();
        var bar = Toolbar();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(view);
        Content = dock;

        PreviewKeyDown += OnKey;
        Closing += (_, e) =>
        {
            if (!_dirty) return;
            var r = MessageBox.Show(this, L.T("markup.unsaved"), L.T("markup.title"), MessageBoxButton.YesNoCancel, MessageBoxImage.None);
            if (r == MessageBoxResult.Cancel) e.Cancel = true;
            else if (r == MessageBoxResult.Yes) Save(close: false);
        };
        Closed += (_, _) => Memory.TrimSoon();
        ApplyTool();
    }

    private UIElement Toolbar()
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(ToolButton(Tool.Pen, "", L.T("markup.pen")));
        left.Children.Add(ToolButton(Tool.Highlighter, "", L.T("markup.highlighter")));
        left.Children.Add(ToolButton(Tool.Box, "", L.T("markup.box")));
        left.Children.Add(ToolButton(Tool.Arrow, "", L.T("markup.arrow")));
        left.Children.Add(ToolButton(Tool.Eraser, "", L.T("markup.eraser")));
        left.Children.Add(Separator());
        foreach (var c in Palette)
        {
            var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(c), BorderThickness = new Thickness(1) };
            dot.SetResourceReference(Border.BorderBrushProperty, "J.ControlStrokeBottom");
            var b = Toggle(dot, null);
            b.Click += (_, _) => { _color = c; if (_tool == Tool.Eraser) _tool = Tool.Pen; ApplyTool(); };
            _colorButtons.Add((c, b));
            left.Children.Add(b);
        }
        left.Children.Add(Separator());
        left.Children.Add(Ui.DropDown(new[] { (0.6, L.T("markup.thin")), (1.0, L.T("markup.medium")), (1.9, L.T("markup.thick")) }, _size, v => { _size = v; ApplyTool(); }, 110));
        left.Children.Add(Separator());
        left.Children.Add(Ui.IconButton("", L.T("markup.undo") + " (Ctrl+Z)", Undo));
        left.Children.Add(Ui.IconButton("", L.T("markup.redo") + " (Ctrl+Y)", Redo));

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var cancel = Ui.Button(L.T("common.cancel"), () => { _dirty = false; Close(); });
        cancel.Margin = new Thickness(0, 0, 8, 0);
        right.Children.Add(cancel);
        right.Children.Add(Ui.Button(L.T("common.save"), () => Save(close: true), Ui.Kind.Accent, null, "Ctrl+S"));

        var dp = new DockPanel { Margin = new Thickness(16, 10, 16, 10) };
        DockPanel.SetDock(right, Dock.Right);
        dp.Children.Add(right);
        dp.Children.Add(left);
        return dp;
    }

    private static Border Separator()
    {
        var b = new Border { Width = 1, Height = 20, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, "J.Divider");
        return b;
    }

    private ToggleButton ToolButton(Tool t, string glyph, string tip)
    {
        var b = Toggle(Ui.Glyph(glyph, 16), tip);
        b.Click += (_, _) => { _tool = t; ApplyTool(); };
        _toolButtons[t] = b;
        return b;
    }

    private static ToggleButton Toggle(UIElement content, string? tip)
    {
        var bd = new Border { Width = 34, Height = 32, CornerRadius = new CornerRadius(4), Child = content };
        var b = new ToggleButton { Content = bd, ToolTip = tip, Cursor = Cursors.Hand, Margin = new Thickness(1, 0, 1, 0), Focusable = false };
        b.Template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        void paint()
        {
            if (b.IsChecked == true) bd.SetResourceReference(Border.BackgroundProperty, "J.ControlHover");
            else if (b.IsMouseOver) bd.SetResourceReference(Border.BackgroundProperty, "J.SubtleHover");
            else bd.Background = Brushes.Transparent;
        }
        b.Checked += (_, _) => paint(); b.Unchecked += (_, _) => paint();
        b.MouseEnter += (_, _) => paint(); b.MouseLeave += (_, _) => paint();
        b.Loaded += (_, _) => paint();
        if (tip != null) Ui.AutomationName(b, tip);
        return b;
    }

    private void ApplyTool()
    {
        foreach (var (t, b) in _toolButtons) b.IsChecked = t == _tool;
        foreach (var (c, b) in _colorButtons) b.IsChecked = c == _color && _tool != Tool.Eraser;
        _ink.EditingMode = _tool switch
        {
            Tool.Eraser => InkCanvasEditingMode.EraseByStroke,
            Tool.Box or Tool.Arrow => InkCanvasEditingMode.None,
            _ => InkCanvasEditingMode.Ink,
        };
        _ink.Cursor = _tool is Tool.Box or Tool.Arrow ? Cursors.Cross : null;
        _ink.DefaultDrawingAttributes = Attributes(_tool == Tool.Highlighter);
    }

    private DrawingAttributes Attributes(bool highlighter)
    {
        double w = _unit * _size * (highlighter ? 5 : 1.5);
        return new DrawingAttributes
        {
            Color = highlighter ? Color.FromArgb(0x66, _color.R, _color.G, _color.B) : _color,
            Width = w, Height = w, FitToCurve = !highlighter, IsHighlighter = highlighter, IgnorePressure = true,
            StylusTip = highlighter ? StylusTip.Rectangle : StylusTip.Ellipse,
        };
    }

    // ───────────── box & arrow ─────────────

    private void Shape_Down(object sender, MouseButtonEventArgs e)
    {
        if (_tool is not (Tool.Box or Tool.Arrow)) return;
        _shapeStart = e.GetPosition(_ink);
        _ink.CaptureMouse();
        e.Handled = true;
    }

    private void Shape_Move(object sender, MouseEventArgs e)
    {
        if (_shapeStart is not { } s) return;
        _replaying = true;
        if (_shapePreview != null) _ink.Strokes.Remove(_shapePreview);
        _shapePreview = BuildShape(s, e.GetPosition(_ink));
        if (_shapePreview != null) _ink.Strokes.Add(_shapePreview);
        _replaying = false;
    }

    private void Shape_Up(object sender, MouseButtonEventArgs e)
    {
        if (_shapeStart is not { } s) return;
        _ink.ReleaseMouseCapture();
        _replaying = true;
        if (_shapePreview != null) _ink.Strokes.Remove(_shapePreview);
        _replaying = false;
        _shapePreview = null;
        _shapeStart = null;
        var final = BuildShape(s, e.GetPosition(_ink));
        if (final != null) _ink.Strokes.Add(final);
    }

    private Stroke? BuildShape(Point a, Point b)
    {
        if ((b - a).Length < 4) return null;
        var pts = new StylusPointCollection();
        if (_tool == Tool.Box)
        {
            foreach (var p in new[] { a, new Point(b.X, a.Y), b, new Point(a.X, b.Y), a }) pts.Add(new StylusPoint(p.X, p.Y));
        }
        else
        {
            var dir = b - a; dir.Normalize();
            double head = Math.Max(_unit * _size * 7, 10);
            var n = new Vector(-dir.Y, dir.X);
            var left = b - dir * head + n * head * 0.55;
            var right = b - dir * head - n * head * 0.55;
            foreach (var p in new[] { a, b, left, b, right }) pts.Add(new StylusPoint(p.X, p.Y));
        }
        var attr = Attributes(false);
        attr.FitToCurve = false;
        return new Stroke(pts, attr);
    }

    // ───────────── history ─────────────

    private void Undo()
    {
        if (_undo.Count == 0) return;
        var step = _undo.Pop();
        Replay(step.Added, step.Removed);
        _redo.Push(step);
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        var step = _redo.Pop();
        Replay(step.Removed, step.Added);
        _undo.Push(step);
    }

    private void Replay(StrokeCollection remove, StrokeCollection add)
    {
        _replaying = true;
        try
        {
            foreach (var s in remove) if (_ink.Strokes.Contains(s)) _ink.Strokes.Remove(s);
            if (add.Count > 0) _ink.Strokes.Add(add);
            _dirty = true;
        }
        finally { _replaying = false; }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { Save(close: true); e.Handled = true; }
        else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (!ctrl && e.Key is Key.P) { _tool = Tool.Pen; ApplyTool(); }
        else if (!ctrl && e.Key is Key.H) { _tool = Tool.Highlighter; ApplyTool(); }
        else if (!ctrl && e.Key is Key.R) { _tool = Tool.Box; ApplyTool(); }
        else if (!ctrl && e.Key is Key.A) { _tool = Tool.Arrow; ApplyTool(); }
        else if (!ctrl && e.Key is Key.E) { _tool = Tool.Eraser; ApplyTool(); }
    }

    private void Save(bool close)
    {
        try
        {
            int w = _bmp.PixelWidth, h = _bmp.PixelHeight;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(_bmp, new Rect(0, 0, w, h));
                _ink.Strokes.Draw(dc);
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            Images.SavePng(rtb, _path);
            _dirty = false;
            Saved?.Invoke();
            if (close) Close();
        }
        catch (Exception ex)
        {
            Log.Error("Saving markup failed", ex);
            MessageBox.Show(this, L.F("markup.saveFailed", ex.Message), L.T("markup.title"));
        }
    }
}
