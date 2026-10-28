using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Clothesline.Interop;
using Microsoft.Win32;

namespace Clothesline.UI;

/// <summary>
/// Windows 11 design tokens for light and dark mode, plus the user's accent colour.
/// Values follow the WinUI 3 theme resources so Clothesline sits naturally next to built-in apps.
/// </summary>
internal static class Theme
{
    public static bool Dark { get; private set; }
    public static event Action? Changed;

    // surfaces
    public static Brush Window { get; private set; } = Brushes.White;
    public static Brush Layer { get; private set; } = Brushes.White;
    public static Brush Card { get; private set; } = Brushes.White;
    public static Brush CardHover { get; private set; } = Brushes.White;
    public static Brush CardStroke { get; private set; } = Brushes.Gray;
    public static Brush Flyout { get; private set; } = Brushes.White;
    public static Brush FlyoutStroke { get; private set; } = Brushes.Gray;
    public static Brush Divider { get; private set; } = Brushes.Gray;
    public static Brush Smoke { get; private set; } = Brushes.Black;
    // controls
    public static Brush Control { get; private set; } = Brushes.White;
    public static Brush ControlHover { get; private set; } = Brushes.White;
    public static Brush ControlPressed { get; private set; } = Brushes.White;
    public static Brush ControlStroke { get; private set; } = Brushes.Gray;
    public static Brush ControlStrokeBottom { get; private set; } = Brushes.Gray;
    public static Brush Subtle { get; private set; } = Brushes.Transparent;
    public static Brush SubtleHover { get; private set; } = Brushes.Transparent;
    public static Brush SubtlePressed { get; private set; } = Brushes.Transparent;
    public static Brush InputFocused { get; private set; } = Brushes.White;
    // text
    public static Brush Text { get; private set; } = Brushes.Black;
    public static Brush TextSecondary { get; private set; } = Brushes.Gray;
    public static Brush TextTertiary { get; private set; } = Brushes.Gray;
    public static Brush TextDisabled { get; private set; } = Brushes.Gray;
    // accent
    public static Color AccentColor { get; private set; }
    public static Brush Accent { get; private set; } = Brushes.Blue;
    public static Brush AccentHover { get; private set; } = Brushes.Blue;
    public static Brush OnAccent { get; private set; } = Brushes.White;
    public static Brush AccentText { get; private set; } = Brushes.Blue;
    public static Brush Critical { get; private set; } = Brushes.Red;
    public static Brush CriticalBg { get; private set; } = Brushes.MistyRose;
    // switch "off" state
    public static Brush SwitchOffStroke { get; private set; } = Brushes.Gray;

    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    /// <summary>Handwriting for notes — part of the paper metaphor, not the chrome.</summary>
    public static readonly FontFamily Hand = new("Segoe Print, Ink Free, Segoe UI");
    public static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x2C, 0x1E)));

    private static string _mode = "system";
    private static HwndSource? _watcher;

    public static bool SupportsMica => Environment.OSVersion.Version.Build >= 22621;

    public static void Init(string mode)
    {
        _mode = mode;
        Apply();
        // Follow system theme / accent changes live.
        _watcher = new HwndSource(new HwndSourceParameters("ClotheslineTheme") { Width = 0, Height = 0, WindowStyle = 0 });
        _watcher.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == Native.WM_SETTINGCHANGE || msg == 0x0320 /* WM_DWMCOLORIZATIONCOLORCHANGED */)
                Application.Current?.Dispatcher.BeginInvoke(Apply);
            return IntPtr.Zero;
        });
    }

    public static void SetMode(string mode)
    {
        _mode = mode;
        Apply();
    }

    private static bool _applied, _lastDark;
    private static Color _lastAccent;

    private static void Apply()
    {
        bool dark = _mode switch { "dark" => true, "light" => false, _ => !IsAppsLight() };
        var accent = ReadAccent(dark);
        if (_applied && dark == _lastDark && accent == _lastAccent) return; // nothing changed
        _applied = true; _lastDark = dark; _lastAccent = accent;
        Dark = dark;

        if (dark)
        {
            Window = B(0x20, 0x20, 0x20);
            Layer = B(0x1C, 0x1C, 0x1C);
            Card = A(0x0D, 0xFF, 0xFF, 0xFF); CardHover = A(0x14, 0xFF, 0xFF, 0xFF); CardStroke = A(0x19, 0x00, 0x00, 0x00);
            Flyout = B(0x2C, 0x2C, 0x2C); FlyoutStroke = A(0x33, 0x00, 0x00, 0x00);
            Divider = A(0x15, 0xFF, 0xFF, 0xFF); Smoke = A(0x4D, 0x00, 0x00, 0x00);
            Control = A(0x0F, 0xFF, 0xFF, 0xFF); ControlHover = A(0x15, 0xFF, 0xFF, 0xFF); ControlPressed = A(0x08, 0xFF, 0xFF, 0xFF);
            ControlStroke = A(0x12, 0xFF, 0xFF, 0xFF); ControlStrokeBottom = A(0x18, 0xFF, 0xFF, 0xFF);
            Subtle = Brushes.Transparent; SubtleHover = A(0x0F, 0xFF, 0xFF, 0xFF); SubtlePressed = A(0x0A, 0xFF, 0xFF, 0xFF);
            InputFocused = A(0xB3, 0x1E, 0x1E, 0x1E);
            Text = B(0xFF, 0xFF, 0xFF); TextSecondary = A(0xC5, 0xFF, 0xFF, 0xFF); TextTertiary = A(0x87, 0xFF, 0xFF, 0xFF); TextDisabled = A(0x5D, 0xFF, 0xFF, 0xFF);
            OnAccent = B(0x00, 0x00, 0x00);
            Critical = B(0xFF, 0x99, 0xA4); CriticalBg = B(0x44, 0x27, 0x26);
            SwitchOffStroke = A(0x9A, 0xFF, 0xFF, 0xFF);
        }
        else
        {
            Window = B(0xF3, 0xF3, 0xF3);
            Layer = B(0xF9, 0xF9, 0xF9);
            Card = A(0xB3, 0xFF, 0xFF, 0xFF); CardHover = A(0x80, 0xF9, 0xF9, 0xF9); CardStroke = A(0x0F, 0x00, 0x00, 0x00);
            Flyout = B(0xF9, 0xF9, 0xF9); FlyoutStroke = A(0x0F, 0x00, 0x00, 0x00);
            Divider = A(0x0F, 0x00, 0x00, 0x00); Smoke = A(0x33, 0x00, 0x00, 0x00);
            Control = A(0xB3, 0xFF, 0xFF, 0xFF); ControlHover = A(0x80, 0xF9, 0xF9, 0xF9); ControlPressed = A(0x4D, 0xF9, 0xF9, 0xF9);
            ControlStroke = A(0x0F, 0x00, 0x00, 0x00); ControlStrokeBottom = A(0x29, 0x00, 0x00, 0x00);
            Subtle = Brushes.Transparent; SubtleHover = A(0x09, 0x00, 0x00, 0x00); SubtlePressed = A(0x06, 0x00, 0x00, 0x00);
            InputFocused = B(0xFF, 0xFF, 0xFF);
            Text = A(0xE4, 0x00, 0x00, 0x00); TextSecondary = A(0x9E, 0x00, 0x00, 0x00); TextTertiary = A(0x72, 0x00, 0x00, 0x00); TextDisabled = A(0x5C, 0x00, 0x00, 0x00);
            OnAccent = B(0xFF, 0xFF, 0xFF);
            Critical = B(0xC4, 0x2B, 0x1C); CriticalBg = B(0xFD, 0xE7, 0xE9);
            SwitchOffStroke = A(0x9E, 0x00, 0x00, 0x00);
        }

        AccentColor = accent;
        Accent = Freeze(new SolidColorBrush(accent));
        AccentHover = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, accent.R, accent.G, accent.B)));
        AccentText = Accent;
        Publish();
        Changed?.Invoke();
    }

    /// <summary>Exposes the tokens as application resources ("J.Text", "J.Accent", …) for DynamicResource lookups in XAML styles.</summary>
    private static void Publish()
    {
        var r = Application.Current?.Resources;
        if (r == null) return;
        r["J.Window"] = Window; r["J.Layer"] = Layer; r["J.Card"] = Card; r["J.CardHover"] = CardHover; r["J.CardStroke"] = CardStroke;
        r["J.Flyout"] = Flyout; r["J.FlyoutStroke"] = FlyoutStroke; r["J.Divider"] = Divider; r["J.Smoke"] = Smoke;
        r["J.Control"] = Control; r["J.ControlHover"] = ControlHover; r["J.ControlPressed"] = ControlPressed;
        r["J.ControlStroke"] = ControlStroke; r["J.ControlStrokeBottom"] = ControlStrokeBottom;
        r["J.SubtleHover"] = SubtleHover; r["J.SubtlePressed"] = SubtlePressed; r["J.InputFocused"] = InputFocused;
        r["J.Text"] = Text; r["J.TextSecondary"] = TextSecondary; r["J.TextTertiary"] = TextTertiary; r["J.TextDisabled"] = TextDisabled;
        r["J.Accent"] = Accent; r["J.AccentHover"] = AccentHover; r["J.OnAccent"] = OnAccent;
        r["J.Critical"] = Critical; r["J.CriticalBg"] = CriticalBg; r["J.SwitchOffStroke"] = SwitchOffStroke;
    }

    /// <summary>Mica backdrop + dark caption on Windows 11; solid fallback elsewhere.</summary>
    public static void StyleWindow(Window w)
    {
        void apply()
        {
            var h = Native.Handle(w);
            if (h == IntPtr.Zero) return;
            int dark = Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
            if (SupportsMica)
            {
                var src = HwndSource.FromHwnd(h);
                if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                Native.DwmExtendFrameIntoClientArea(h, ref m);
                int mica = 2; // DWMSBT_MAINWINDOW
                Native.DwmSetWindowAttribute(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref mica, 4);
                w.Background = Brushes.Transparent;
            }
            else w.Background = Window;
        }
        w.SourceInitialized += (_, _) => apply();
        void onChange() => w.Dispatcher.BeginInvoke(apply);
        Changed += onChange;
        w.Closed += (_, _) => Changed -= onChange;
    }

    // ───────────── system reads ─────────────

    private static bool IsAppsLight() => ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) != 0;
    public static bool IsTaskbarLight() => ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) != 0;

    /// <summary>Windows derives button colours from the accent palette: a darker shade in light mode, a lighter one in dark mode.</summary>
    private static Color ReadAccent(bool dark)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (k?.GetValue("AccentPalette") is byte[] p && p.Length >= 32)
            {
                int i = dark ? 1 : 4; // Light2 for dark mode, Dark1 for light mode
                return Color.FromRgb(p[i * 4], p[i * 4 + 1], p[i * 4 + 2]);
            }
        }
        catch { }
        return dark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8);
    }

    private static int ReadDword(string path, string name, int fallback)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(path);
            return k?.GetValue(name) is int v ? v : fallback;
        }
        catch { return fallback; }
    }

    private static Brush B(byte r, byte g, byte b) => Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));
    private static Brush A(byte a, byte r, byte g, byte b) => Freeze(new SolidColorBrush(Color.FromArgb(a, r, g, b)));
    public static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
