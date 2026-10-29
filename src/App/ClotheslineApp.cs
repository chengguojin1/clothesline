using System.IO;
using System.Net.Http;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Interop;
using Clothesline.UI;
using Clothesline.Windows;
using Microsoft.Win32;

namespace Clothesline.App;

/// <summary>Owns the long-lived pieces (library, line, tray, shortcuts) and every user-facing action.</summary>
internal sealed class ClotheslineApp
{
    public const string RepoUrl = "https://github.com/chengguojin1/clothesline";
    public const string ManifestUrl = "https://github.com/chengguojin1/clothesline/releases/latest/download/manifest.json";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly double[] CardScales = { 0.85, 1.0, 1.22 };

    public static string Version { get; } =
        typeof(ClotheslineApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    private readonly Application _wpf;
    private readonly bool _background;
    private HotkeyManager _hotkeys = null!;
    private TrayIcon _tray = null!;
    private DragAttachWatcher? _dragAttach;
    private HttpClient? _updateHttp;
    private UpdateCoordinator? _updates;
    private HttpClient? _downloadHttp;
    private HttpClient? _linkHttp;
    private readonly Dictionary<string, CancellationTokenSource> _linkWaits = new();
    private UpdateInstaller? _installer;
    private bool _trayMenuOpen;
    private ControlPanel? _panel;
    private SpotlightWindow? _spotlight;
    private MarkupWindow? _markup;
    private bool _capturing;

    public Settings Settings { get; private set; } = new();
    public Library Library { get; } = new();
    public LineWindow Line { get; private set; } = null!;
    public PinBoard Pins { get; private set; } = null!;
    public bool HasOverlay => _spotlight != null || _markup != null || _capturing;
    internal IUpdateChecker UpdateChecker { get; private set; } = null!;

    public ClotheslineApp(Application wpf, bool background, string? renderDocs = null, string? updated = null)
    {
        _wpf = wpf;
        _background = background;
        _renderDocs = renderDocs;
        _updated = updated;
    }

    private readonly string? _renderDocs;
    /// <summary>The version a self-update restarted us for (<c>--updated &lt;v&gt;</c>).</summary>
    private readonly string? _updated;

    /// <summary>
    /// Developer tool: <c>Clothesline.exe --render-docs &lt;folder&gt;</c> renders the control panel in dark and light
    /// theme straight to PNG, without touching the screen. Combine with JEMUR_DATA to use demo content.
    /// </summary>
    private void RenderDocs(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var mode in new[] { "dark", "light" })
        {
            Theme.SetMode(mode);
            var panel = new ControlPanel(this) { Left = -32000, Top = -32000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            panel.Show();
            panel.RenderPages(dir, mode);
            panel.Close();

            var shot = Library.Items.FirstOrDefault(i => i.Kind == ItemKind.Image)?.File;
            if (shot != null)
            {
                var markup = new MarkupWindow(shot) { Left = -32000, Top = -32000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Width = 1100, Height = 760 };
                markup.Show();
                var root = (System.Windows.Controls.Panel)markup.Content;
                root.Background = Theme.Window;
                root.UpdateLayout();
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(root.ActualWidth * 1.5), (int)(root.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
                rtb.Render(root);
                Images.SavePng(rtb, Path.Combine(dir, $"markup-{mode}.png"));
                markup.Close();
            }
        }
        Log.Info($"Rendered docs to {dir}");
        _wpf.Shutdown();
    }

    // ───────────── lifecycle ─────────────

    public void Start()
    {
        AppPaths.Ensure();
        Log.Info($"Clothesline {Version} starting on Windows {Environment.OSVersion.Version}");
        Settings = SettingsStore.Load();
        L.Init(Settings.Language);
        if (_renderDocs != null)
        {
            _wpf.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/UI/Controls.xaml") });
            Theme.Init(Settings.Theme);
            LoadLibrary();
            Line = new LineWindow(this);
            _hotkeys = new HotkeyManager();
            _tray = new TrayIcon();
            RenderDocs(_renderDocs);
            return;
        }
        _wpf.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/UI/Controls.xaml") });
        Theme.Init(Settings.Theme);
        CardView.Scale = CardScales[Settings.CardSize];

        LoadLibrary();
        Line = new LineWindow(this) { AutoHide = Settings.AutoHide };
        if (Settings.Lifted) Line.SetLifted(true, animate: false);
        else if (Line.StaysOut) Line.Show();
        Pins = new PinBoard(this);
        Library.Changed += () => _tray?.SetTip(TrayTip());
        Library.Added += WatchLink;
        Library.Updated += WatchLink;

        _tray = new TrayIcon();
        _tray.Click += ToggleLift;
        _tray.RightClick += ShowTrayMenu;
        _tray.Show(Theme.IsTaskbarLight(), TrayTip());
        _updateHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        UpdateChecker = new UpdateChecker(_updateHttp, Version, new Uri(ManifestUrl), RepoUrl);
        _updates = new UpdateCoordinator(UpdateChecker, _tray.Notify, () => ShowPanel(), TimeProvider.System, TimeSpan.FromSeconds(30), TimeSpan.FromHours(24));
        StartInstaller();
        _updates.Start();
        Theme.Changed += () => _tray.SetIcon(Theme.IsTaskbarLight());
        L.Changed += () => _tray.SetTip(TrayTip());

        _hotkeys = new HotkeyManager();
        var failed = BindHotkeys();
        if (failed.Count > 0) _tray.Notify("Clothesline", L.F("tray.shortcutsTaken", string.Join(", ", failed)), warning: true);

        _dragAttach = new DragAttachWatcher(HangLiveWindow, OnDragEnterHotZone, OnDragLeaveHotZone);
        _dragAttach.Start();
        Log.Info("DragAttach watcher started");

        ListenForSecondInstance();

        if (!Settings.Welcomed)
        {
            Settings.Welcomed = true;
            SettingsStore.Save(Settings);
            ShowPanel();
        }
        else if (!_background && Library.Items.Count == 0) _tray.Notify("Clothesline", L.F("tray.running", HotkeyText(Actions.Screenshot)));

        Memory.TrimSoon(4000);
        Memory.Report("started");
    }

    private void LoadLibrary()
    {
        Library.Load(Settings.KeepDiscardedDays, L.T("rope.default"));
        if (Library.RopeById(Settings.ActiveRope) == null) Settings.ActiveRope = Rope.DefaultId; // its rope was deleted, or the file was replaced
    }

    public void Stop()
    {
        if (_renderDocs == null) Library.SaveNow();
        _installer?.Dispose();
        _updates?.Dispose();
        _updateHttp?.Dispose();
        _downloadHttp?.Dispose();
        _linkHttp?.Dispose();
        _hotkeys?.Dispose();
        _dragAttach?.Dispose();
        _tray?.Dispose();
        Log.Info("Clothesline stopped");
    }

    /// <summary>Quitting with an update staged installs it on the way out.</summary>
    public void Quit()
    {
        if (_installer?.ApplyNow() != true) _wpf.Shutdown();
    }

    // ───────────── self-update ─────────────

    private bool AutoUpdateAllowed(string version) => Settings.AutoUpdate && version != Settings.SkipAutoUpdateVersion;

    private void StartInstaller()
    {
        var outcome = UpdateInstaller.UpdatedOutcome(_updated, Version);
        if (outcome == UpdateOutcome.Done) _tray.Notify("Clothesline", L.F("update.done", Version));
        else if (outcome == UpdateOutcome.Failed)
        {
            // The installer ran but this is still the old Clothesline: never try that version again by itself.
            Log.Warn($"Restarted for {_updated} but still running {Version}");
            Settings.SkipAutoUpdateVersion = _updated!;
            SettingsStore.Save(Settings);
            _tray.Notify("Clothesline", L.T("update.failed"), warning: true);
        }

        bool installed = UpdateInstaller.IsInstalled();
        if (!installed && Environment.ProcessPath is { } exe) UpdatePackage.CleanupOld(exe);
        _downloadHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // the installer applies its own 10-minute limit
        var host = new UpdateHost(_wpf.Dispatcher, AutoUpdateAllowed,
            Busy: () => HasOverlay || Line.MenuOpen || _trayMenuOpen || System.Windows.Interop.ComponentDispatcher.IsThreadModal,
            Save: () => { var ok = Library.SaveNow(); SettingsStore.Save(Settings); return ok; },
            Exit: _wpf.Shutdown,
            Fallback: (m, skip) =>
            {
                if (skip) { Settings.SkipAutoUpdateVersion = m.Version; SettingsStore.Save(Settings); }
                _updates?.NotifyAvailable(m);
            });
        _installer = new UpdateInstaller(_downloadHttp, UpdateInstaller.DefaultFolder, new Uri(ManifestUrl), installed, host);
        _installer.CleanUp(keepStaged: outcome == UpdateOutcome.None && Settings.AutoUpdate);
        _updates!.Stage = m =>
        {
            if (!AutoUpdateAllowed(m.Version)) return false;
            _ = _installer.StageAsync(m, CancellationToken.None); // reports its own failure through Fallback
            return true;
        };
    }

    private string TrayTip() => Settings.Lifted ? L.T("tray.lifted") : $"{RopeName(ActiveRope)} · {L.N("tray.items", Library.ItemsOn(ActiveRope).Count())}";

    public bool Lifted => Settings.Lifted;

    public void SetLifted(bool lifted, bool animate = true)
    {
        if (Settings.Lifted == lifted) return;
        LandFlights();
        Settings.Lifted = lifted;
        SettingsStore.Save(Settings);
        Line.SetLifted(lifted, animate);
        Pins.SetLifted(lifted, animate);
        _tray?.SetTip(TrayTip());
    }

    public void ToggleLift() => SetLifted(!Lifted);

    /// <summary>Anything that hangs a new card first brings the line back out if it was taken in.</summary>
    public void EnsureVisible(bool animate = true) { if (Lifted) SetLifted(false, animate); }

    // ───────────── modes ─────────────

    private const int FlightMs = 380;

    /// <summary>Cards in the air between the line and the desktop during a mode switch.</summary>
    private readonly Dictionary<HangItem, GhostWindow> _flights = new();

    /// <summary><see cref="Settings.LineMode"/> (every card on the rope) or <see cref="Settings.DesktopMode"/> (every card free on the desktop).</summary>
    public string Mode => Settings.Mode;

    /// <summary>The mode changed, or a request to change it was ignored, so a control showing it should redraw.</summary>
    public event Action? ModeChanged;

    public bool InDesktopMode => Settings.Mode == Settings.DesktopMode;

    public void ToggleMode() => SetMode(InDesktopMode ? Settings.LineMode : Settings.DesktopMode);

    /// <summary>
    /// Saves the mode, un-lifts, then flies every card of the active rope between its rope slot and its desktop spot.
    /// Switching again mid-flight turns the cards round from where they are; nothing is shown twice.
    /// </summary>
    public void SetMode(string mode)
    {
        if (mode is not (Settings.LineMode or Settings.DesktopMode) || mode == Settings.Mode || _capturing || _markup != null) { ModeChanged?.Invoke(); return; }
        if (_spotlight != null) { _spotlight.Dismiss(() => SetMode(mode)); return; } // its card must be back before the cards fly
        var midAir = _flights.ToDictionary(f => f.Key, f => f.Value.CardPx);
        foreach (var g in _flights.Values.ToList()) g.Finish(land: false);
        _flights.Clear();

        Settings.Mode = mode;
        if (Lifted)
        {
            Settings.Lifted = false;
            Line.SetLifted(false, animate: false);
            Pins.SetLifted(false, animate: false);
        }
        SettingsStore.Save(Settings);
        _tray?.SetTip(TrayTip());
        ModeChanged?.Invoke();
        bool play = Motion.Enabled;

        if (InDesktopMode)
        {
            // where each card is now: on the rope, or wherever its flight back had got to
            var from = Library.ItemsOn(ActiveRope).Where(i => Line.CardRect(i) != null)
                .ToDictionary(i => i, i => midAir.TryGetValue(i, out var p) ? p : DipToPx(Line.CardRect(i)!.Value.TopLeft));
            Line.ApplyMode(play);
            Pins.Rebuild(hold: play);
            if (!play) return;
            var order = Pins.Windows.OrderBy(w => from.TryGetValue(w.Item, out var p) ? p.X : 0).ToList();
            int step = LineWindow.Stagger(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                var w = order[i];
                if (from.TryGetValue(w.Item, out var p)) Fly(w.Card, p, w.CardPx, step * i, () => Pins.Land(w.Item));
                else Pins.Land(w.Item);
            }
        }
        else
        {
            var from = Pins.Windows.ToDictionary(w => w.Item, w => (w.Card, At: midAir.TryGetValue(w.Item, out var p) ? p : w.CardPx));
            Line.ApplyMode(play);
            if (play) // the line shows first; each card waits unseen in its slot until its flight lands
            {
                var order = from.Keys.Where(i => Line.CardOf(i) != null).OrderBy(i => Line.SlotOf(i).X).ToList();
                int step = LineWindow.Stagger(order.Count);
                for (int i = 0; i < order.Count; i++)
                {
                    var item = order[i];
                    Line.Expect(item);
                    Fly(from[item].Card, from[item].At, DipToPx(Line.SlotOf(item)), step * i, () => Line.Land(item));
                }
            }
            Pins.Rebuild();
        }
    }

    private static Point DipToPx(Point dip) => new(dip.X * PrimaryScale, dip.Y * PrimaryScale);

    /// <summary>A ghost of <paramref name="card"/> waits <paramref name="delayMs"/> at <paramref name="fromPx"/>, then flies to <paramref name="toPx"/>.</summary>
    private void Fly(CardView card, Point fromPx, Point toPx, int delayMs, Action landed)
    {
        var item = card.Item;
        var ghost = new GhostWindow(card);
        ghost.ShowAtPx(fromPx, card.Angle);
        _flights[item] = ghost;
        ghost.FlyTo(toPx, FlightMs, () => { _flights.Remove(item); landed(); }, delayMs);
    }

    /// <summary>Lands every card still in the air at once (a lift, a rope switch or a capture comes first).</summary>
    private void LandFlights()
    {
        foreach (var g in _flights.Values.ToList()) g.Finish();
        _flights.Clear();
    }

    // ───────────── ropes ─────────────

    public string ActiveRope => Settings.ActiveRope;

    /// <summary>Raised after the active rope changes, so the control panel can follow.</summary>
    public event Action? RopeSwitched;

    private string RopeName(string id) => Library.RopeById(id)?.Name ?? "";

    /// <summary>Menu headers treat "_" as an access key; rope names are the user's own text.</summary>
    private static string MenuText(string s) => s.Replace("_", "__");

    public void SwitchRope(string ropeId)
    {
        if (Library.RopeById(ropeId) == null) return;
        if (ropeId == ActiveRope) { EnsureVisible(); Line.HoldOpen(1200); return; }
        LandFlights();
        Settings.ActiveRope = ropeId;
        SettingsStore.Save(Settings);
        Line.ShowRope(ropeId, animate: true); // while lifted this swaps the cards out of sight; EnsureVisible then hangs the new ones out
        Pins.ShowRope(ropeId, animate: true);
        EnsureVisible();
        _tray?.SetTip(TrayTip());
        if (InDesktopMode) _tray?.Notify("Clothesline", L.F("mode.lineSwitched", RopeName(ropeId))); // no rope tag shows it
        RopeSwitched?.Invoke();
    }

    public void NextRope()
    {
        var ropes = Library.Ropes.ToList();
        int i = ropes.FindIndex(r => r.Id == ActiveRope);
        SwitchRope(ropes[(i + 1) % ropes.Count].Id);
    }

    public void NewRope(Window? owner = null)
    {
        var name = Prompt.Ask(L.T("rope.new").TrimEnd('…', '.', ' '), L.T("rope.namePrompt"), "", owner);
        if (name != null) SwitchRope(Library.AddRope(name).Id);
    }

    public void RenameRope(string id, Window? owner = null)
    {
        var r = Library.RopeById(id);
        if (r == null) return;
        var name = Prompt.Ask(L.T("rope.rename"), L.T("rope.namePrompt"), r.Name, owner);
        if (name != null) Library.RenameRope(id, name);
    }

    public void DeleteRope(string id, Window owner)
    {
        var r = Library.RopeById(id);
        if (r == null || id == Rope.DefaultId) return;
        if (MessageBox.Show(owner, L.F("rope.deleteConfirm", r.Name, RopeName(Rope.DefaultId)), L.T("rope.delete"), MessageBoxButton.OKCancel, MessageBoxImage.None) != MessageBoxResult.OK) return;
        if (id == ActiveRope) SwitchRope(Rope.DefaultId); // switch first, so its cards then drop onto the default line
        Library.DeleteRope(id);
    }

    /// <summary>Moves a card to another rope. If that rope isn't showing, the card says where it went before it leaves.</summary>
    public void MoveToRope(HangItem item, string ropeId)
    {
        if (Library.RopeById(ropeId) == null || item.RopeId == ropeId) return;
        if (ropeId != ActiveRope)
        {
            var toast = L.F("toast.movedTo", RopeName(ropeId));
            Pins.SeeOff(item, toast); // whichever shows it
            Line.SeeOff(item, toast);
        }
        Library.MoveToRope(item, ropeId);
    }

    /// <summary>Brings a discarded item back. Onto the active rope it also hangs the line back out; onto another rope it goes quietly.</summary>
    public void Restore(HangItem item)
    {
        if ((item.RopeId ?? Rope.DefaultId) == ActiveRope) EnsureVisible();
        Library.Restore(item);
    }

    /// <summary>The ropes (active one checked), then New line… and Manage lines…. Shared by the rope tag and the tray's Lines submenu.</summary>
    private void AddRopeEntries(ItemsControl menu)
    {
        foreach (var r in Library.Ropes)
        {
            var id = r.Id;
            menu.Items.Add(Ui.MenuItem(MenuText(r.Name), () => SwitchRope(id), isChecked: id == ActiveRope));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.MenuItem(L.T("rope.new"), () => NewRope(), ""));
        menu.Items.Add(Ui.MenuItem(L.T("rope.manage"), () => { ShowPanel(); _panel?.ShowLine(); }, ""));
    }

    public void ShowRopeMenu(FrameworkElement target)
    {
        var m = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom, VerticalOffset = 4 };
        AddRopeEntries(m);
        Line.MenuOpen = true;
        m.Closed += (_, _) => Line.MenuOpen = false;
        m.IsOpen = true;
    }

    private void ListenForSecondInstance()
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowPanelEvent);
        new Thread(() => { while (true) { ev.WaitOne(); _wpf.Dispatcher.BeginInvoke(() => ShowPanel()); } }) { IsBackground = true, Name = "Clothesline.Activation" }.Start();
    }

    // ───────────── shortcuts ─────────────

    private Action HandlerFor(string action) => action switch
    {
        Actions.Screenshot => () => _ = TakeScreenshot(),
        Actions.Note => NewNote,
        Actions.Paste => HangClipboard,
        Actions.Toggle => ToggleLift,
        Actions.NextRope => NextRope,
        Actions.Mode => ToggleMode,
        Actions.HangWindow => () => _ = TakeWindow(),
        _ => () => ShowPanel(),
    };

    private List<string> BindHotkeys()
    {
        var failed = new List<string>();
        foreach (var a in Actions.All)
        {
            if (!Settings.Hotkeys.TryGetValue(a, out var s) || string.IsNullOrEmpty(s)) continue;
            if (!Hotkey.TryParse(s, out var hk) || !_hotkeys.Bind(a, hk, HandlerFor(a))) failed.Add(s);
        }
        return failed;
    }

    public string HotkeyText(string action) =>
        Settings.Hotkeys.TryGetValue(action, out var s) && Hotkey.TryParse(s, out var hk) ? hk.ToString() : "";

    /// <summary>Returns false when the combination is already taken by another app.</summary>
    public bool SetHotkey(string action, Hotkey? key)
    {
        if (key is not { } k)
        {
            _hotkeys.Unbind(action);
            Settings.Hotkeys[action] = "";
            SettingsStore.Save(Settings);
            return true;
        }
        // Clear the same combination from any other action first.
        foreach (var other in Actions.All.Where(o => o != action && Settings.Hotkeys.TryGetValue(o, out var s) && Hotkey.TryParse(s, out var h) && h == k).ToList())
        {
            _hotkeys.Unbind(other);
            Settings.Hotkeys[other] = "";
        }
        if (!_hotkeys.Bind(action, k, HandlerFor(action)))
        {
            if (Hotkey.TryParse(Settings.Hotkeys.GetValueOrDefault(action), out var old)) _hotkeys.Bind(action, old, HandlerFor(action));
            return false;
        }
        Settings.Hotkeys[action] = k.ToString();
        SettingsStore.Save(Settings);
        return true;
    }

    public void SuspendHotkeys(bool suspend) =>
        _hotkeys.Suspend(suspend, a => Settings.Hotkeys.TryGetValue(a, out var s) && Hotkey.TryParse(s, out var hk) ? (hk, HandlerFor(a)) : null);

    public void ResetHotkeys()
    {
        Settings.Hotkeys = Settings.DefaultHotkeys();
        SettingsStore.Save(Settings);
        foreach (var a in Actions.All) _hotkeys.Unbind(a);
        var failed = BindHotkeys();
        if (failed.Count > 0) _tray.Notify("Clothesline", L.F("tray.shortcutsTaken", string.Join(", ", failed)), warning: true);
    }

    // ───────────── settings ─────────────

    public void SetTheme(string mode) { Settings.Theme = mode; SettingsStore.Save(Settings); Theme.SetMode(mode); }

    internal Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken) =>
        _updates?.CheckNowAsync(cancellationToken) ?? UpdateChecker.CheckAsync(cancellationToken);

    public void SetLanguage(string code) { LandFlights(); Settings.Language = code; SettingsStore.Save(Settings); L.Use(code); Line.Rebuild(); Pins.Rebuild(); }

    public void SetCardSize(int size)
    {
        LandFlights();
        Settings.CardSize = Math.Clamp(size, 0, 2);
        CardView.Scale = CardScales[Settings.CardSize];
        SettingsStore.Save(Settings);
        Line.Rebuild();
        Pins.Rebuild();
    }

    internal UpdateInstaller? Installer => _installer;

    public void SetAutoUpdate(bool on)
    {
        Settings.AutoUpdate = on;
        SettingsStore.Save(Settings);
        _installer?.Refresh(); // off stops the quiet-moment timer (the staged file stays); on restarts it
        // Switched off with an update waiting: say so the 0.3.0 way, or nothing would ever tell the user.
        if (!on && _installer?.ReadyManifest is { } waiting) _updates?.NotifyAvailable(waiting);
    }

    public void SetAutoHide(bool on) { Settings.AutoHide = on; Line.AutoHide = on; SettingsStore.Save(Settings); }

    public void SetKeepDays(int days) { Settings.KeepDiscardedDays = days; SettingsStore.Save(Settings); Library.PurgeDiscarded(days); Library.ScheduleSave(); }

    public bool IsStartupEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue("Clothesline") != null;
    }

    public void SetStartup(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue("Clothesline", $"\"{Environment.ProcessPath}\" --background");
            else k.DeleteValue("Clothesline", false);
        }
        catch (Exception ex) { Log.Error("Could not change startup setting", ex); }
    }

    // ───────────── capture & creation ─────────────

    public async Task TakeScreenshot()
    {
        if (_capturing) return;
        EnsureVisible(animate: false); // the line hides for the capture right away: no point playing the hang-out
        _capturing = true;
        bool panelVisible = _panel is { IsVisible: true, WindowState: not WindowState.Minimized };
        try
        {
            _spotlight?.Dismiss();
            LandFlights();
            if (panelVisible) _panel!.Hide();
            bool lineVisible = Line.IsVisible;
            Line.Hide();
            Pins.HideForCapture();
            await Task.Delay(150); // let DWM repaint without our windows

            var (shot, bounds) = ScreenCapture.MonitorUnderCursor();
            var windows = ScreenCapture.VisibleWindows(IntPtr.Zero);
            if ((lineVisible || Line.StaysOut) && !Lifted) Line.Show();
            Pins.RestoreAfterCapture();

            var w = new CaptureWindow(shot, bounds, windows);
            w.ShowDialog();
            if (w.Result is { } result)
            {
                var path = Images.NewShotPath();
                Images.SavePng(result, path);
                Hang(new HangItem { Kind = ItemKind.Image, File = path });
            }
        }
        catch (Exception ex)
        {
            Log.Error("Screenshot failed", ex);
            _tray.Notify("Clothesline", L.F("error.screenshot", ex.Message), warning: true);
        }
        finally
        {
            _capturing = false;
            Pins.RestoreAfterCapture(); // also when the capture threw before the pins came back
            if (panelVisible) _panel?.Show();
            Memory.TrimSoon(); // the full-screen bitmap is the biggest allocation Clothesline ever makes
        }
    }

    /// <summary>Pick a window and hang its live thumbnail on the line.</summary>
    public async Task TakeWindow()
    {
        Log.Info("TakeWindow invoked");
        if (_capturing) { Log.Info("TakeWindow skipped: already capturing"); return; }
        EnsureVisible(animate: false);
        _capturing = true;
        try
        {
            LandFlights();
            Line.Hide();
            Pins.HideForCapture();
            await Task.Delay(120);

            var picker = new WindowPicker();
            picker.ShowDialog();
            if (picker.Result != IntPtr.Zero)
            {
                int len = Native.GetWindowTextLength(picker.Result);
                var buf = new char[Math.Max(1, len + 1)];
                Native.GetWindowText(picker.Result, buf, buf.Length);
                string title = new string(buf).TrimEnd('\0', ' ');
                string? exe = Native.GetProcessPath(picker.Result);
                string? shot = SnapshotWindow(picker.Result);
                Log.Info($"TakeWindow: {picker.Result} '{title}' exe={exe} shot={shot}");
                Hang(new HangItem
                {
                    Kind = ItemKind.LiveWindow,
                    RuntimeHwnd = picker.Result,
                    WindowTitle = title,
                    ProcessPath = exe,
                    File = shot,
                });
                Line.HoldOpen(2000);
                Native.ShowWindow(picker.Result, Native.SW_MINIMIZE);
            }
        }
        catch (Exception ex)
        {
            Log.Error("TakeWindow failed", ex);
            _tray.Notify("Clothesline", ex.Message, warning: true);
        }
        finally
        {
            _capturing = false;
            Pins.RestoreAfterCapture();
            if (Line.StaysOut && !Settings.Lifted) Line.Show();
        }
    }

    /// <summary>New screenshots, notes and pastes always go on the active rope.</summary>
    private void Hang(HangItem item)
    {
        item.RopeId = ActiveRope;
        Library.Add(item);
    }

    /// <summary>Hang an already-chosen live window (called by drag-drop or the picker).</summary>
    private void HangLiveWindow(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            int len = Native.GetWindowTextLength(hwnd);
            var buf = new char[Math.Max(1, len + 1)];
            Native.GetWindowText(hwnd, buf, buf.Length);
            string title = new string(buf).TrimEnd('\0', ' ');
            string? exe = Native.GetProcessPath(hwnd);
            string? shot = SnapshotWindow(hwnd);
            Log.Info($"HangLiveWindow: {hwnd} '{title}' exe={exe} shot={shot}");
            Hang(new HangItem
            {
                Kind = ItemKind.LiveWindow,
                RuntimeHwnd = hwnd,
                WindowTitle = title,
                ProcessPath = exe,
                File = shot,
            });
            Line.HoldOpen(2000);
            // once it is on the line, tuck the source window away so the line takes its place
            Native.ShowWindow(hwnd, Native.SW_MINIMIZE);
        }
        catch (Exception ex) { Log.Error("HangLiveWindow failed", ex); }
    }

    /// <summary>Grabs a PNG of a window before it is minimised, so its card shows what it looked like. Null on failure.</summary>
    private static string? SnapshotWindow(IntPtr hwnd)
    {
        try
        {
            var bmp = ScreenCapture.CaptureWindow(hwnd);
            if (bmp == null) return null;
            var path = Images.NewShotPath();
            Images.SavePng(bmp, path);
            return path;
        }
        catch (Exception ex) { Log.Warn("SnapshotWindow failed", ex); return null; }
    }

    /// <summary>A window is being dragged into the top hot zone: bring the clothesline out so the user can see where to hang.</summary>
    private void OnDragEnterHotZone()
    {
        Log.Info("DragAttach: entered hot zone, summoning line");
        if (InDesktopMode) return; // no line to summon in desktop mode
        EnsureVisible(animate: true);
        Line.HoldOpen(3000);
    }

    /// <summary>The drag left the hot zone without releasing: let the line tuck back on its own.</summary>
    private void OnDragLeaveHotZone()
    {
        Log.Info("DragAttach: left hot zone");
        // HoldOpen already set a timeout; the line's own auto-hide takes it from here.
    }

    public void NewNote()
    {
        EnsureVisible();
        var item = new HangItem { Kind = ItemKind.Note, Color = Random.Shared.Next(NoteColors.Count) };
        Hang(item);
        _wpf.Dispatcher.BeginInvoke(() => Spotlight(item), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void HangClipboard()
    {
        EnsureVisible();
        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
            {
                var path = Images.NewShotPath();
                Images.SavePng(img, path);
                Hang(new HangItem { Kind = ItemKind.Image, File = path });
                return;
            }
            if (Clipboard.ContainsFileDropList())
            {
                string[] exts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };
                bool any = false;
                foreach (var f in Clipboard.GetFileDropList().Cast<string?>())
                {
                    if (f == null || !exts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                    var path = Images.NewShotPath();
                    Images.SavePng(Images.Load(f), path);
                    Hang(new HangItem { Kind = ItemKind.Image, File = path });
                    any = true;
                }
                if (any) return;
            }
            if (Clipboard.ContainsText() && Clipboard.GetText().Trim() is { Length: > 0 } text)
            {
                Hang(new HangItem { Kind = ItemKind.Note, Text = text, Color = Random.Shared.Next(NoteColors.Count) });
                return;
            }
            _tray.Notify("Clothesline", L.T("tray.clipboardEmpty"));
        }
        catch (Exception ex)
        {
            Log.Warn("Reading the clipboard failed", ex);
            _tray.Notify("Clothesline", L.T("tray.clipboardFailed"), warning: true);
        }
        finally { Memory.TrimSoon(); }
    }

    // ───────────── pictures and links on notes ─────────────

    /// <summary>Puts a picture on a note (replacing the one it had), saved next to the screenshots.</summary>
    public void AttachImage(HangItem note, BitmapSource bmp)
    {
        var path = Images.NewShotPath();
        Images.SavePng(bmp, path);
        var old = note.File;
        note.File = path;
        Library.DeleteOwned(note, old);
        Library.Touch(note);
    }

    public void RemoveImage(HangItem note)
    {
        var old = note.File;
        note.File = null;
        Library.DeleteOwned(note, old);
        Library.Touch(note);
    }

    /// <summary>
    /// A note that names a web address gets a preview of that page: fetched once, a moment after typing stops, and kept
    /// with the note. Removing the address removes the preview.
    /// </summary>
    private async void WatchLink(HangItem item)
    {
        if (item.Kind != ItemKind.Note) return;
        var url = LinkPreview.FindUrl(item.Text);
        if (url == item.Link?.Url) return;
        if (_linkWaits.Remove(item.Id, out var waiting)) waiting.Cancel();
        if (url == null)
        {
            if (item.Link == null) return;
            var gone = item.Link.Image;
            item.Link = null;
            Library.DeleteOwned(item, gone);
            Library.Touch(item);
            return;
        }
        var cts = new CancellationTokenSource();
        _linkWaits[item.Id] = cts;
        try
        {
            await Task.Delay(900, cts.Token);
            if (_linkHttp == null)
            {
                _linkHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                _linkHttp.DefaultRequestHeaders.UserAgent.ParseAdd($"Mozilla/5.0 (compatible; Clothesline/{Version}; link preview)");
            }
            var card = await LinkPreview.FetchAsync(_linkHttp, url, cts.Token);
            if (cts.IsCancellationRequested || !Library.Items.Contains(item) || LinkPreview.FindUrl(item.Text) != url)
            {
                Library.DeleteOwned(item, card.Image);
                return;
            }
            var old = item.Link?.Image;
            item.Link = card;
            Library.DeleteOwned(item, old);
            Library.Touch(item);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Warn($"Link preview failed for {url}", ex); }
        finally
        {
            if (_linkWaits.TryGetValue(item.Id, out var c) && c == cts) _linkWaits.Remove(item.Id);
            cts.Dispose();
        }
    }

    public void OpenLink(string url)
    {
        if (LinkPreview.FindUrl(url) != url) return; // only ever hand a plain web address to the shell
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { Log.Warn("Open link failed", ex); }
    }

    // ───────────── item actions ─────────────

    /// <summary>
    /// A LiveWindow card is an app launcher: clicking it brings the app's window to the front
    /// (restoring it from the taskbar if minimised), or re-launches the app from its saved exe
    /// path if the window has been closed since it was hung.
    /// </summary>
    public void ActivateApp(HangItem item)
    {
        if (item.Kind != ItemKind.LiveWindow) return;
        Log.Info($"ActivateApp: '{item.WindowTitle}' hwnd={item.RuntimeHwnd} exe={item.ProcessPath}");
        // live window: bring it forward if it still exists
        if (item.RuntimeHwnd != IntPtr.Zero && Native.IsWindow(item.RuntimeHwnd))
        {
            Native.ForceForegroundHwnd(item.RuntimeHwnd);
            RefreshSnapshotSoon(item); // keep the card's picture in step with the window
            return;
        }
        // the window was closed after hanging — relaunch the app from its exe path
        if (!string.IsNullOrWhiteSpace(item.ProcessPath) && File.Exists(item.ProcessPath))
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(item.ProcessPath) { UseShellExecute = true });
                if (p != null)
                {
                    // give it a moment to create its window, then remember the new HWND so the next
                    // click re-uses it instead of spawning a second instance
                    _ = Task.Delay(1500).ContinueWith(_ =>
                    {
                        _wpf.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var newHwnd = FindWindowForProcess(p.Id);
                            if (newHwnd != IntPtr.Zero)
                            {
                                item.RuntimeHwnd = newHwnd;
                                int len = Native.GetWindowTextLength(newHwnd);
                                var buf = new char[Math.Max(1, len + 1)];
                                Native.GetWindowText(newHwnd, buf, buf.Length);
                                item.WindowTitle = new string(buf).TrimEnd('\0', ' ');
                                // replace the stale snapshot with a fresh one from the relaunched window
                                var oldShot = item.File;
                                var newShot = SnapshotWindow(newHwnd);
                                if (newShot != null) { item.File = newShot; Library.DeleteOwned(item, oldShot); }
                                ViewOf(item)?.RefreshVisual();
                                Library.Touch(item);
                            }
                        }));
                    });
                }
            }
            catch (Exception ex) { Log.Warn("ActivateApp: relaunch failed", ex); }
            return;
        }
        // nothing to bring forward and nothing to relaunch: nudge the card and toast
        var c = ViewOf(item);
        if (c != null) { c.Pulse(); c.Toast(L.T("liveWindow.gone")); }
    }

    /// <summary>Re-snapshots a window shortly after it is brought forward, so its card shows the latest view.</summary>
    private void RefreshSnapshotSoon(HangItem item)
    {
        var hwnd = item.RuntimeHwnd;
        _ = Task.Delay(1200).ContinueWith(_ =>
        {
            _wpf.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!Native.IsWindow(hwnd)) return;
                var old = item.File;
                var shot = SnapshotWindow(hwnd);
                if (shot == null) return;
                item.File = shot;
                Library.DeleteOwned(item, old);
                ViewOf(item)?.RefreshVisual();
                Library.Touch(item);
            }));
        });
    }

    /// <summary>Finds the first visible top-level window owned by a process id (best-effort).</summary>
    private static IntPtr FindWindowForProcess(int pid)
    {
        IntPtr found = IntPtr.Zero;
        Native.EnumWindowsProc? cb = null;
        cb = (h, _) =>
        {
            Native.GetWindowThreadProcessId(h, out uint wpid);
            if (wpid == pid && Native.IsWindowVisible(h) && Native.GetWindowTextLength(h) > 0) { found = h; return false; }
            return true;
        };
        Native.EnumWindows(cb, IntPtr.Zero);
        GC.KeepAlive(cb);
        return found;
    }

    public void Spotlight(HangItem item)
    {
        if (_spotlight != null) { _spotlight.Dismiss(() => Spotlight(item)); return; }
        if (!Library.Items.Contains(item)) return;
        Line.HoldOpen(800);
        ViewOf(item)?.SetAway(true);
        var w = new SpotlightWindow(item, this, CardCenter(item));
        _spotlight = w;
        w.Finished += it =>
        {
            _spotlight = null;
            var c = ViewOf(it);
            c?.SetAway(false);
            if (c != null) Kick(it, 70);
            if (it.Kind == ItemKind.Note && string.IsNullOrWhiteSpace(it.Text)) Library.Drop(it);
            Line.HoldOpen(1200);
            Memory.TrimSoon();
        };
        w.Show();
    }

    public void Markup(HangItem item)
    {
        if (item.File == null || !File.Exists(item.File)) return;
        _markup?.Close();
        var w = new MarkupWindow(item.File);
        _markup = w;
        w.Saved += () => { var c = ViewOf(item); if (c != null) { c.ReloadImage(); c.Toast(L.T("toast.saved")); } Library.Touch(item); };
        w.Closed += (_, _) => { _markup = null; Line.HoldOpen(1200); };
        w.Show();
        Native.ForceForeground(w);
    }

    public bool Copy(HangItem item)
    {
        try
        {
            var data = new DataObject();
            if (item.File != null && File.Exists(item.File))
            {
                data.SetImage(Images.Load(item.File));
                data.SetFileDropList(new StringCollection { item.File });
            }
            if (item.Kind == ItemKind.Note) data.SetText(item.Text); // not a screenshot's note: text would win over the picture in most apps
            Clipboard.SetDataObject(data, true); // copy=true: hand the data to Windows so we don't keep it alive
            ViewOf(item)?.Toast(L.T("toast.copied"));
            Memory.TrimSoon();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Copy failed", ex);
            ViewOf(item)?.Toast(L.T("toast.copyFailed"));
            return false;
        }
    }

    public void OpenFile(HangItem item)
    {
        if (item.File == null) return;
        try { Process.Start(new ProcessStartInfo(item.File) { UseShellExecute = true }); } catch (Exception ex) { Log.Warn("Open failed", ex); }
    }

    public void ShowInFolder(HangItem item)
    {
        if (item.File == null) return;
        try { Process.Start("explorer.exe", $"/select,\"{item.File}\""); } catch (Exception ex) { Log.Warn("Show in folder failed", ex); }
    }

    public void SaveAs(HangItem item, Window? owner = null)
    {
        if (item.File == null) return;
        var dlg = new SaveFileDialog { FileName = Path.GetFileName(item.File), Filter = "PNG|*.png", AddExtension = true };
        Line.MenuOpen = true;
        try { if (dlg.ShowDialog(owner) == true) File.Copy(item.File, dlg.FileName, true); }
        catch (Exception ex) { Log.Warn("Save as failed", ex); }
        finally { Line.MenuOpen = false; }
    }

    /// <summary>Lets the card drop off the line (or the desktop), then moves it to Discarded.</summary>
    public void Discard(HangItem item)
    {
        Pins.LetGo(item); // whichever shows it
        Line.LetGo(item);
        Library.Discard(item);
    }

    // ───────────── desktop pins ─────────────

    /// <summary>The card showing an item: on the line, or pinned to the desktop.</summary>
    private CardView? ViewOf(HangItem item) => Line.CardOf(item) ?? Pins.WindowOf(item)?.Card;

    public void Kick(HangItem item, double v)
    {
        if (Pins.WindowOf(item) is { } pin) pin.Kick(v);
        else Line.Kick(item, v);
    }

    /// <summary>The DPI scale of the primary screen, where the line and the spotlight live.</summary>
    private static double PrimaryScale => Native.GetDpiForSystem() / 96.0;

    /// <summary>Centre of an item's card in DIPs of the primary screen; for a pin, the centre of the pin.</summary>
    private Point CardCenter(HangItem item)
    {
        if (Pins.WindowOf(item) is not { } pin) return Line.CardCenter(item);
        var p = pin.CardPx;
        var size = pin.CardSizePx(pin.Scale);
        return new Point((p.X + size.Width / 2) / PrimaryScale, (p.Y + size.Height / 2) / PrimaryScale);
    }

    // ───────────── menus ─────────────

    public void ShowItemMenu(HangItem item, FrameworkElement target, bool keyboard = false)
    {
        var m = new ContextMenu { PlacementTarget = target, Placement = keyboard ? PlacementMode.Bottom : PlacementMode.MousePoint };
        bool img = item.Kind == ItemKind.Image;
        m.Items.Add(Ui.MenuItem(L.T(img ? "action.view" : "action.openNote"), () => Spotlight(item), img ? "" : ""));
        m.Items.Add(Ui.MenuItem(L.T("action.copy"), () => Copy(item), ""));
        if (img)
        {
            m.Items.Add(Ui.MenuItem(L.T("action.markup"), () => Markup(item), "", L.T("hint.hold")));
            m.Items.Add(Ui.MenuItem(L.T("action.open"), () => OpenFile(item), ""));
            m.Items.Add(Ui.MenuItem(L.T("action.saveAs"), () => SaveAs(item), ""));
            m.Items.Add(Ui.MenuItem(L.T("action.showInFolder"), () => ShowInFolder(item), ""));
        }
        else
        {
            m.Items.Add(new Separator());
            for (int i = 0; i < NoteColors.Count; i++)
            {
                int idx = i;
                var sw = new Border
                {
                    Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(item.Color == i ? 2 : 1),
                    Background = new SolidColorBrush(NoteColors.Paper(i)), ToolTip = L.T(NoteColors.Keys[i]),
                };
                sw.SetResourceReference(Border.BorderBrushProperty, item.Color == i ? "J.Text" : "J.ControlStrokeBottom");
                var choice = Ui.MenuItem($"{L.T("action.noteColor")}: {L.T(NoteColors.Keys[i])}", () => { item.Color = idx; Library.Touch(item); }, isChecked: item.Color == i);
                choice.Icon = sw;
                m.Items.Add(choice);
            }
        }
        m.Items.Add(new Separator());
        var others = Library.Ropes.Where(r => r.Id != item.RopeId).ToList();
        if (others.Count > 0)
        {
            var move = Ui.MenuItem(L.T("rope.moveTo"), () => { }, "");
            foreach (var r in others)
            {
                var id = r.Id;
                move.Items.Add(Ui.MenuItem(MenuText(r.Name), () => MoveToRope(item, id)));
            }
            m.Items.Add(move);
        }
        if (!InDesktopMode) m.Items.Add(GroupMenuItem(item));
        else if (Library.GroupOf(item) != null) m.Items.Add(Ui.MenuItem(L.T("group.leave"), () => Pins.Loosen(item))); // on the desktop a group is a pile: drop a card on another to make one
        m.Items.Add(new Separator());
        m.Items.Add(Ui.MenuItem(L.T("action.discard"), () => Discard(item), "", InDesktopMode ? null : L.T("hint.dragOff"), critical: true));

        Line.MenuOpen = true;
        m.Closed += (_, _) => { Line.MenuOpen = false; if (keyboard) target.Focus(); };
        if (keyboard) m.Opened += (_, _) => ((MenuItem)m.Items[0]).Focus();
        m.IsOpen = true;
    }

    // ───────────── groups ─────────────

    /// <summary>Group ▸ New group…, the groups on this rope (its own checked), and Remove from group when it is in one.</summary>
    private MenuItem GroupMenuItem(HangItem item)
    {
        var mine = Library.GroupOf(item);
        var menu = Ui.MenuItem(L.T("group.menu"), () => { }, "");
        menu.Items.Add(Ui.MenuItem(L.T("group.new"), () => NewGroup(item)));
        var groups = Library.Groups.Where(g => g.RopeId == item.RopeId).ToList();
        if (groups.Count > 0) menu.Items.Add(new Separator());
        foreach (var g in groups)
        {
            var id = g.Id;
            menu.Items.Add(Ui.MenuItem(MenuText(g.Name), () => { if (mine?.Id != id) Library.JoinGroup(item, id, int.MaxValue); }, isChecked: mine?.Id == id));
        }
        if (mine != null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Ui.MenuItem(L.T("group.leave"), () => Line.LeaveGroup(item)));
            menu.Items.Add(new Separator());
            AddGroupActions(menu, mine);
        }
        return menu;
    }

    /// <summary>"Group 1", "Group 2", …: the first number no group on this rope uses yet.</summary>
    public string NextGroupName(string ropeId)
    {
        var used = Library.Groups.Where(g => g.RopeId == ropeId).Select(g => g.Name).ToHashSet();
        int n = 1;
        while (used.Contains(L.F("group.defaultName", n))) n++;
        return L.F("group.defaultName", n);
    }

    /// <summary>Asks for a group name while the line stays out (a menu holds it open).</summary>
    private string? AskGroupName(string title, string initial)
    {
        Line.MenuOpen = true;
        try { return Prompt.Ask(title, L.T("group.namePrompt"), initial, null); }
        finally { Line.MenuOpen = false; }
    }

    public void NewGroup(HangItem item)
    {
        var name = AskGroupName(L.T("group.new").TrimEnd('…', '.', ' '), NextGroupName(item.RopeId ?? Rope.DefaultId));
        if (name == null || !Library.Items.Contains(item) || InDesktopMode) return;
        Line.SettleForGroup(item);
        Library.CreateGroup(item, name);
    }

    public void ShowGroupMenu(CardGroup g, FrameworkElement target)
    {
        var m = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom, VerticalOffset = 4 };
        AddGroupActions(m, g);
        Line.MenuOpen = true;
        m.Closed += (_, _) => Line.MenuOpen = false;
        m.IsOpen = true;
    }

    /// <summary>
    /// Rename, Fold or Unfold, Ungroup and Discard all: on the label's menu, and under Group on a member card's menu,
    /// which the Control Panel opens from the keyboard (Apps key or Shift+F10 on a tile).
    /// </summary>
    private void AddGroupActions(ItemsControl m, CardGroup g)
    {
        var id = g.Id;
        m.Items.Add(Ui.MenuItem(L.T("group.rename"), () =>
        {
            var name = AskGroupName(L.T("group.rename"), g.Name);
            if (name != null) Library.RenameGroup(id, name);
        }, ""));
        m.Items.Add(Ui.MenuItem(L.T(g.Folded ? "group.unfold" : "group.fold"), () => Library.SetFolded(id, !g.Folded), g.Folded ? "" : ""));
        m.Items.Add(Ui.MenuItem(L.T("group.ungroup"), () => Library.Ungroup(id, Line.UngroupPositions(g)), ""));
        m.Items.Add(new Separator());
        m.Items.Add(Ui.MenuItem(L.T("group.discardAll"), () =>
        {
            foreach (var item in g.ItemIds.ToList().Select(i => Library.Items.FirstOrDefault(x => x.Id == i)).OfType<HangItem>()) Discard(item);
        }, "", critical: true));
    }

    private void ShowTrayMenu()
    {
        var m = new ContextMenu { Placement = PlacementMode.AbsolutePoint };
        Native.GetCursorPos(out var p);
        double scale = 96.0 / Native.GetDpiForSystem();
        m.HorizontalOffset = p.X * scale;
        m.VerticalOffset = p.Y * scale;

        m.Items.Add(Ui.MenuItem(L.T("action.screenshot"), () => _ = TakeScreenshot(), "", HotkeyText(Actions.Screenshot)));
        m.Items.Add(Ui.MenuItem(L.T("action.newNote"), NewNote, "", HotkeyText(Actions.Note)));
        m.Items.Add(Ui.MenuItem(L.T("action.paste"), HangClipboard, "", HotkeyText(Actions.Paste)));
        m.Items.Add(Ui.MenuItem(L.T("action.hangWindow"), () => _ = TakeWindow(), "", HotkeyText(Actions.HangWindow)));
        m.Items.Add(Ui.MenuItem(L.T(Lifted ? "action.unlift" : "action.lift"), ToggleLift, "", HotkeyText(Actions.Toggle)));
        m.Items.Add(Ui.MenuItem(L.T(InDesktopMode ? "mode.toLine" : "mode.toDesktop"), ToggleMode, "\uE718", HotkeyText(Actions.Mode)));
        var lines = Ui.MenuItem(L.T("rope.lines"), () => { }, "");
        AddRopeEntries(lines);
        m.Items.Add(lines);
        m.Items.Add(new Separator());
        var restore = Ui.MenuItem(L.T("action.restoreLast"), () => { if (Library.Discarded.Count > 0) Restore(Library.Discarded[0]); }, "");
        restore.IsEnabled = Library.Discarded.Count > 0;
        m.Items.Add(restore);
        m.Items.Add(Ui.MenuItem(L.T("action.controlPanel"), () => ShowPanel(), "", HotkeyText(Actions.Panel)));
        m.Items.Add(Ui.MenuItem(L.T("nav.settings"), () => ShowPanel(settings: true), ""));
        m.Items.Add(new Separator());
        m.Items.Add(Ui.MenuItem(L.T("action.quit"), Quit, ""));
        _trayMenuOpen = true;
        m.Closed += (_, _) => _trayMenuOpen = false;

        // A tray menu only dismisses on outside clicks if our process owns the foreground.
        Native.SetForegroundWindow(_tray.Hwnd);
        m.IsOpen = true;
    }

    public void ShowPanel(bool settings = false)
    {
        if (_panel == null)
        {
            _panel = new ControlPanel(this);
            _panel.Closed += (_, _) => _panel = null;
            _panel.Show();
        }
        if (_panel.WindowState == WindowState.Minimized) _panel.WindowState = WindowState.Normal;
        if (settings) _panel.ShowSettings();
        Native.ForceForeground(_panel);
    }
}
