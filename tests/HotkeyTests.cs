using System.Windows.Input;
using Clothesline.Core;
using Xunit;

namespace Clothesline.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Alt+Shift+S", ModifierKeys.Alt | ModifierKeys.Shift, Key.S)]
    [InlineData("ctrl+alt+1", ModifierKeys.Control | ModifierKeys.Alt, Key.D1)]
    [InlineData("Win+Shift+N", ModifierKeys.Windows | ModifierKeys.Shift, Key.N)]
    [InlineData("Ctrl+PrtScn", ModifierKeys.Control, Key.Snapshot)]
    [InlineData("Ctrl+Alt+,", ModifierKeys.Control | ModifierKeys.Alt, Key.OemComma)]
    public void Parses(string text, ModifierKeys mods, Key key)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(mods, hk.Modifiers);
        Assert.Equal(key, hk.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("S")]                 // no modifier
    [InlineData("Shift+S")]           // Shift alone would hijack typing
    [InlineData("Alt+Shift")]         // no key
    [InlineData("Alt+S+T")]           // two keys
    [InlineData("Alt+NotAKey")]
    [InlineData("Alt+65")]            // numeric enum values must not parse
    public void RejectsInvalid(string text) => Assert.False(Hotkey.TryParse(text, out _));

    [Fact]
    public void RoundTripsInCanonicalOrder()
    {
        Assert.True(Hotkey.TryParse("shift+alt+ctrl+win+k", out var hk));
        Assert.Equal("Win+Ctrl+Alt+Shift+K", hk.ToString());
        Assert.True(Hotkey.TryParse(hk.ToString(), out var again));
        Assert.Equal(hk, again);
    }

    [Fact]
    public void DefaultShortcutsAreValid()
    {
        foreach (var (_, text) in Settings.DefaultHotkeys())
            Assert.True(Hotkey.TryParse(text, out _), text);
    }
}
