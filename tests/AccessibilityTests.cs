using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Clothesline.Core;
using Clothesline.UI;
using Clothesline.Windows;
using System.Windows.Automation.Provider;
using Xunit;

namespace Clothesline.Tests;

[Collection("AppPaths")]
public sealed class AccessibilityTests
{
    private static void OnSta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { L.Init("en"); test(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF accessibility check timed out");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void ButtonsWithGlyphContentExposeTheActionName() => OnSta(() =>
    {
        var button = Ui.Button("Screenshot", () => { }, glyph: "");
        Assert.Equal("Screenshot", new ButtonAutomationPeer(button).GetName());
    });

    [Fact]
    public void SettingsControlsExposeTheirSettingAndValue() => OnSta(() =>
    {
        var dropdown = Ui.DropDown(new[] { ("system", "Use system setting"), ("dark", "Dark") }, "system", _ => { });
        Ui.SettingsCard("", "App theme", "Choose the app theme.", dropdown);
        var peer = new ButtonAutomationPeer(dropdown);
        Assert.Contains("App theme", peer.GetName());
        Assert.Contains("Use system setting", peer.GetName());
        Assert.Equal("Choose the app theme.", peer.GetHelpText());

        var toggle = Ui.Switch(true, _ => { });
        Ui.SettingsCard("", "Tuck away when idle", "Keep the screen clear.", toggle);
        Assert.Equal("Tuck away when idle", new ToggleButtonAutomationPeer(toggle).GetName());
    });

    [Fact]
    public void ShortcutRecorderExposesActionAndShortcut() => OnSta(() =>
    {
        var recorder = new HotkeyRecorder(new Hotkey(ModifierKeys.Alt | ModifierKeys.Shift, Key.N), _ => true, _ => { });
        Ui.SettingsCard("", "New note", null, recorder);
        var button = Assert.IsType<Button>(recorder.Children[0]);
        var name = new ButtonAutomationPeer(button).GetName();
        Assert.Contains("New note", name);
        Assert.Contains("Alt+Shift+N", name);
    });

    [Fact]
    public void SearchHasANameEvenWhenItContainsText() => OnSta(() =>
    {
        var (_, box) = Ui.SearchBox("Search notes", "release checklist");
        Assert.Equal("Search notes", new TextBoxAutomationPeer(box).GetName());
    });

    [Fact]
    public void UpdateActionsExposeNamesAndFocus() => OnSta(() =>
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var check = Ui.Button("Check for updates", () => { }, Ui.Kind.Subtle);
        var download = Ui.Button("Download installer", () => { }, Ui.Kind.Accent);
        actions.Children.Add(check);
        actions.Children.Add(download);
        Ui.SettingsCard("", "Updates", "Check for a newer release.", actions);

        Assert.Equal("Check for updates", new ButtonAutomationPeer(check).GetName());
        Assert.Equal("Download installer", new ButtonAutomationPeer(download).GetName());
        Assert.True(check.Focusable);
        Assert.True(download.Focusable);
    });

    [Theory]
    [InlineData(Key.Tab, false)]
    [InlineData(Key.Escape, true)]
    public void RecordingCanBeCancelledWithoutChangingTheShortcut(Key key, bool handled) => OnSta(() =>
    {
        var recording = false;
        var applied = false;
        var recorder = new HotkeyRecorder(new Hotkey(ModifierKeys.Alt | ModifierKeys.Shift, Key.N),
            _ => { applied = true; return true; }, value => recording = value);
        Ui.SettingsCard("", "New note", null, recorder);
        var button = Assert.IsType<Button>(recorder.Children[0]);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(recording);
        Assert.Contains("Press a shortcut", new ButtonAutomationPeer(button).GetName());
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Shortcut keyboard test"));
        var input = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        button.RaiseEvent(input);
        Assert.False(recording);
        Assert.False(applied);
        Assert.Equal(handled, input.Handled);
        Assert.Contains("Alt+Shift+N", new ButtonAutomationPeer(button).GetName());
    });

    [Theory]
    [InlineData("es", "Configuración")]
    [InlineData("pt-BR", "Configurações")]
    [InlineData("ja", "設定")]
    [InlineData("ms", "Tetapan")]
    public void NewLanguagesAreEmbeddedAndSelectable(string code, string settings) => OnSta(() =>
    {
        L.Use(code);
        Assert.Equal(code.ToLowerInvariant(), L.Code);
        Assert.Equal(settings, L.T("nav.settings"));
        Assert.Contains(L.Available, language => language.Code == code.ToLowerInvariant());
    });

    [Fact]
    public void LineTagIsAnInvokableNamedButton() => OnSta(() =>
    {
        var tag = new RopeTag { Text = "Klien A" };
        int invoked = 0;
        tag.Invoked += () => invoked++;
        var peer = UIElementAutomationPeer.CreatePeerForElement(tag);
        Assert.Equal("Line: Klien A", peer.GetName());
        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
        Assert.Empty(peer.GetChildren() ?? new List<AutomationPeer>());
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        Assert.Equal(1, invoked);
    });

    [Fact]
    public void GroupLabelAndStackAreInvokableNamedButtons() => OnSta(() =>
    {
        var label = new GroupLabel("g");
        label.Set("Klien A", 3, folded: false);
        int folds = 0;
        label.Invoked += () => folds++;
        var peer = UIElementAutomationPeer.CreatePeerForElement(label);
        Assert.Equal("Group Klien A, 3 cards", peer.GetName());
        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        Assert.Equal(1, folds);

        int unfolds = 0;
        var badge = new StackBadge { Unfold = () => unfolds++ };
        badge.Set(3, "Group Klien A, 3 cards");
        var stack = UIElementAutomationPeer.CreatePeerForElement(badge);
        Assert.Equal("Group Klien A, 3 cards", stack.GetName());
        ((IInvokeProvider)stack.GetPattern(PatternInterface.Invoke)).Invoke();
        Assert.Equal(1, unfolds);
    });
}
