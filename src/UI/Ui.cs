using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Clothesline.Core;

namespace Clothesline.UI;

/// <summary>Small factory for building Fluent UI in code. Brushes are bound by resource key so they follow theme changes.</summary>
internal static class Ui
{
    public enum Type { Caption, Body, BodyStrong, Subtitle, Title }

    public static TextBlock Text(string text, Type type = Type.Body, string brush = "J.Text")
    {
        var t = new TextBlock { Text = text, FontFamily = type >= Type.Subtitle ? Theme.Display : Theme.Font, TextTrimming = TextTrimming.CharacterEllipsis };
        (t.FontSize, t.FontWeight) = type switch
        {
            Type.Caption => (12.0, FontWeights.Normal),
            Type.BodyStrong => (14.0, FontWeights.SemiBold),
            Type.Subtitle => (20.0, FontWeights.SemiBold),
            Type.Title => (28.0, FontWeights.SemiBold),
            _ => (14.0, FontWeights.Normal),
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock Wrap(string text, Type type = Type.Body, string brush = "J.TextSecondary")
    {
        var t = Text(text, type, brush);
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        return t;
    }

    public static TextBlock Glyph(string glyph, double size = 16, string brush = "J.Text")
    {
        var t = new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public enum Kind { Standard, Accent, Subtle }

    public static Button Button(string text, Action onClick, Kind kind = Kind.Standard, string? glyph = null, string? tip = null)
    {
        var b = new Button { Focusable = true, ToolTip = tip };
        b.SetResourceReference(FrameworkElement.StyleProperty, kind switch { Kind.Accent => "J.AccentButton", Kind.Subtle => "J.SubtleButton", _ => "J.Button" });
        if (glyph == null) b.Content = text;
        else
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var g = new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
            g.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Control.Foreground)) { Source = b });
            sp.Children.Add(g);
            if (text.Length > 0) sp.Children.Add(new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            b.Content = sp;
        }
        b.Click += (_, _) => onClick();
        if (text.Length > 0) AutomationName(b, text);
        return b;
    }

    public static Button IconButton(string glyph, string tip, Action onClick, double size = 32)
    {
        var b = Button("", onClick, Kind.Subtle, glyph, tip);
        b.Width = size; b.Height = size; b.MinHeight = size;
        b.Padding = new Thickness(0);
        AutomationName(b, tip);
        return b;
    }

    public static void AutomationName(DependencyObject d, string name) => System.Windows.Automation.AutomationProperties.SetName(d, name);

    public static ToggleButton Switch(bool on, Action<bool> changed)
    {
        var t = new ToggleButton { IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(FrameworkElement.StyleProperty, "J.Switch");
        void label() => t.Content = L.T(t.IsChecked == true ? "common.on" : "common.off");
        label();
        t.Click += (_, _) => { label(); changed(t.IsChecked == true); };
        return t;
    }

    public static Border Divider(double vMargin = 0)
    {
        var d = new Border { Height = 1, Margin = new Thickness(0, vMargin, 0, vMargin) };
        d.SetResourceReference(Border.BackgroundProperty, "J.Divider");
        return d;
    }

    /// <summary>Win11-style keyboard key chips, e.g. [Alt] [Shift] [S].</summary>
    public static StackPanel KeyCaps(Hotkey key)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var part in key.Parts())
        {
            var cap = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 3), Margin = new Thickness(0, 0, 4, 0), BorderThickness = new Thickness(1, 1, 1, 2), MinWidth = 26 };
            cap.SetResourceReference(Border.BackgroundProperty, "J.Control");
            cap.SetResourceReference(Border.BorderBrushProperty, "J.ControlStroke");
            var t = Text(part, Type.Caption);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            cap.Child = t;
            sp.Children.Add(cap);
        }
        return sp;
    }

    // ───────────── menus ─────────────

    public static MenuItem MenuItem(string header, Action onClick, string? glyph = null, string? gesture = null, bool critical = false, bool isChecked = false)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsChecked = isChecked };
        if (glyph != null) mi.Icon = Glyph(glyph, 16, critical ? "J.Critical" : "J.Text");
        if (critical) mi.SetResourceReference(Control.ForegroundProperty, "J.Critical");
        mi.Click += (_, _) => onClick();
        return mi;
    }

    /// <summary>A dropdown: a button showing the current choice that opens a themed menu of options.</summary>
    public static Button DropDown<T>(IReadOnlyList<(T Value, string Label)> options, T selected, Action<T> changed, double minWidth = 160)
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var chevron = new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 12, Margin = new Thickness(12, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "J.TextSecondary");
        var dp = new DockPanel();
        DockPanel.SetDock(chevron, Dock.Right);
        dp.Children.Add(chevron);
        dp.Children.Add(label);
        var b = new Button { Content = dp, MinWidth = minWidth, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        b.SetResourceReference(FrameworkElement.StyleProperty, "J.Button");
        var current = selected;
        void refresh()
        {
            label.Text = options.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, current)).Label ?? "";
            var title = (System.Windows.Automation.AutomationProperties.GetLabeledBy(b) as TextBlock)?.Text;
            AutomationName(b, title == null ? label.Text : $"{title}: {label.Text}");
        }
        refresh();
        b.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom, VerticalOffset = 4, MinWidth = b.ActualWidth };
            foreach (var (value, text) in options)
            {
                var v = value;
                menu.Items.Add(MenuItem(text, () => { current = v; refresh(); changed(v); }, isChecked: EqualityComparer<T>.Default.Equals(v, current)));
            }
            menu.IsOpen = true;
        };
        return b;
    }

    // ───────────── settings layout ─────────────

    public static TextBlock GroupHeader(string text)
    {
        var t = Text(text, Type.BodyStrong);
        t.Margin = new Thickness(1, 24, 0, 8);
        return t;
    }

    /// <summary>The standard Windows 11 Settings row: icon · title + description · control.</summary>
    public static Border SettingsCard(string glyph, string title, string? description, UIElement? control)
    {
        var row = new DockPanel { MinHeight = 36 };
        var icon = Glyph(glyph, 20);
        icon.Margin = new Thickness(2, 0, 18, 0);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        if (control != null)
        {
            if (control is FrameworkElement fe) { fe.VerticalAlignment = VerticalAlignment.Center; fe.Margin = new Thickness(16, 0, 0, 0); }
            DockPanel.SetDock(control, Dock.Right);
            row.Children.Add(control);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = Text(title);
        text.Children.Add(titleBlock);
        var input = control is HotkeyRecorder recorder ? recorder.InputButton : control;
        if (input != null)
        {
            System.Windows.Automation.AutomationProperties.SetLabeledBy(input, titleBlock);
            System.Windows.Automation.AutomationProperties.SetHelpText(input, description ?? "");
            if (control is HotkeyRecorder hotkey) hotkey.RefreshLabel();
            else
            {
                var value = System.Windows.Automation.AutomationProperties.GetName(input);
                AutomationName(input, input is ToggleButton || string.IsNullOrEmpty(value) ? title : $"{title}: {value}");
            }
        }
        if (!string.IsNullOrEmpty(description))
        {
            var d = Wrap(description, Type.Caption);
            d.Margin = new Thickness(0, 1, 0, 0);
            text.Children.Add(d);
        }
        row.Children.Add(text);
        return Card(row);
    }

    public static Border Card(UIElement child, Thickness? padding = null)
    {
        var c = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(16, 13, 16, 13), Margin = new Thickness(0, 0, 0, 4), Child = child };
        c.SetResourceReference(Border.BackgroundProperty, "J.Card");
        c.SetResourceReference(Border.BorderBrushProperty, "J.CardStroke");
        return c;
    }

    public static TextBox TextBox(string placeholder, string text = "")
    {
        var t = new TextBox { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, "J.TextBox");
        System.Windows.Automation.AutomationProperties.SetHelpText(t, placeholder);
        AutomationName(t, placeholder);
        return t;
    }

    /// <summary>A text box with a placeholder and a leading search glyph.</summary>
    public static (Grid Root, TextBox Box) SearchBox(string placeholder, string text)
    {
        var box = TextBox(placeholder, text);
        box.Padding = new Thickness(10, 5, 34, 6);
        var hint = Text(placeholder, Type.Body, "J.TextTertiary");
        hint.Margin = new Thickness(11, 0, 34, 1);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.IsHitTestVisible = false;
        var icon = Glyph("", 14, "J.TextSecondary");
        icon.HorizontalAlignment = HorizontalAlignment.Right;
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.IsHitTestVisible = false;
        void sync() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => sync();
        sync();
        var g = new Grid();
        g.Children.Add(box);
        g.Children.Add(hint);
        g.Children.Add(icon);
        return (g, box);
    }

    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return L.T("time.now");
        if (d.TotalHours < 1) return L.N("time.minutes", (int)d.TotalMinutes);
        if (d.TotalDays < 1) return L.N("time.hours", (int)d.TotalHours);
        if (d.TotalDays < 7) return L.N("time.days", (int)d.TotalDays);
        return t.ToString("d MMM yyyy", L.Culture);
    }
}
