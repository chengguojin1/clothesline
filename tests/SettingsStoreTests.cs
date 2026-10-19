using System.IO;
using System.Text.Json;
using Jemur.Core;
using Xunit;

namespace Jemur.Tests;

[Collection("AppPaths")]
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jemur-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsStoreTests()
    {
        AppPaths.OverrideRoot(_root);
        Directory.CreateDirectory(AppPaths.Data);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void AssertDefaults(Settings actual)
    {
        Assert.Equal(JsonSerializer.Serialize(new Settings()), JsonSerializer.Serialize(actual));
        Assert.Equal(Settings.DefaultHotkeys(), actual.Hotkeys);
    }

    [Fact]
    public void MissingFileReturnsDefaults() => AssertDefaults(SettingsStore.Load());

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"CardSize\":\"large\"}")]
    public void InvalidFileReturnsDefaultsWithoutChangingTheFile(string json)
    {
        File.WriteAllText(AppPaths.SettingsFile, json);
        AssertDefaults(SettingsStore.Load());
        Assert.Equal(json, File.ReadAllText(AppPaths.SettingsFile));
    }

    [Fact]
    public void UnreadableFileReturnsDefaults()
    {
        File.WriteAllText(AppPaths.SettingsFile, "{\"Theme\":\"dark\"}");
        using var held = new FileStream(AppPaths.SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        AssertDefaults(SettingsStore.Load());
    }

    [Fact]
    public void OlderSettingsWithoutNewPropertiesKeepDefaults()
    {
        File.WriteAllText(AppPaths.SettingsFile, """{"Theme":"dark","AutoHide":false,"Welcomed":true,"OldProperty":120}""");
        var actual = SettingsStore.Load();
        Assert.Equal("dark", actual.Theme);
        Assert.False(actual.AutoHide);
        Assert.True(actual.Welcomed);
        Assert.Equal("auto", actual.Language);
        Assert.Equal(1, actual.CardSize);
        Assert.Equal(7, actual.KeepDiscardedDays);
        Assert.Equal(Settings.DefaultHotkeys(), actual.Hotkeys);
    }

    [Fact]
    public void NormalizeCompletesPartialHotkeysAndPreservesDisabledActions()
    {
        File.WriteAllText(AppPaths.SettingsFile, """{"Hotkeys":{"note":"Ctrl+Alt+N","paste":""},"Theme":"invalid","Language":" ","CardSize":99,"KeepDiscardedDays":8}""");
        var actual = SettingsStore.Load();
        Assert.Equal("Ctrl+Alt+N", actual.Hotkeys[Actions.Note]);
        Assert.Equal("", actual.Hotkeys[Actions.Paste]);
        Assert.Equal("Alt+Shift+S", actual.Hotkeys[Actions.Screenshot]);
        Assert.Equal("system", actual.Theme);
        Assert.Equal("auto", actual.Language);
        Assert.Equal(2, actual.CardSize);
        Assert.Equal(7, actual.KeepDiscardedDays);
    }

    [Fact]
    public void Normalize_AddsRopeShortcutAndDefaultRope()
    {
        File.WriteAllText(AppPaths.SettingsFile, """{"Hotkeys":{"note":"Ctrl+Alt+N"},"Theme":"dark"}""");
        var actual = SettingsStore.Load();
        Assert.Equal("Alt+Shift+G", actual.Hotkeys["rope"]);
        Assert.Equal("default", actual.ActiveRope);
        Assert.False(actual.Lifted);
    }

    [Fact]
    public void Normalize_LeavesNewDefaultEmptyWhenItsCombinationIsTaken()
    {
        File.WriteAllText(AppPaths.SettingsFile, """{"Hotkeys":{"note":"shift+ALT+g"}}""");
        var actual = SettingsStore.Load();
        Assert.Equal("shift+ALT+g", actual.Hotkeys[Actions.Note]);
        Assert.Equal("", actual.Hotkeys[Actions.NextRope]);
        Assert.Equal("Alt+Shift+S", actual.Hotkeys[Actions.Screenshot]);
    }

    [Fact]
    public void Normalize_RepairsModeAndAddsModeShortcut()
    {
        File.WriteAllText(AppPaths.SettingsFile, """{"Mode":"x","Hotkeys":{"note":"Ctrl+Alt+N"}}""");
        var actual = SettingsStore.Load();
        Assert.Equal("line", actual.Mode);
        Assert.Equal("Alt+Shift+M", actual.Hotkeys[Actions.Mode]);

        File.WriteAllText(AppPaths.SettingsFile, """{"Mode":"desktop"}""");
        Assert.Equal(Settings.DesktopMode, SettingsStore.Load().Mode);

        File.WriteAllText(AppPaths.SettingsFile, """{"Hotkeys":{"note":"shift+alt+m"}}""");
        Assert.Equal("", SettingsStore.Load().Hotkeys[Actions.Mode]);
    }

    [Fact]
    public void Normalize_KeepsLifted()
    {
        File.WriteAllText(AppPaths.SettingsFile, """{"Lifted":true}""");
        Assert.True(SettingsStore.Load().Lifted);
    }

    [Fact]
    public void NullHotkeysAreReplacedWithDefaults()
    {
        File.WriteAllText(AppPaths.SettingsFile, """{"Hotkeys":null,"CardSize":-1}""");
        var actual = SettingsStore.Load();
        Assert.Equal(Settings.DefaultHotkeys(), actual.Hotkeys);
        Assert.Equal(0, actual.CardSize);
    }

    [Fact]
    public void SaveAndLoadRoundTripAndReplaceAnExistingFile()
    {
        SettingsStore.Save(new Settings());
        var expected = new Settings
        {
            Theme = "dark", Language = "pt-BR", AutoHide = false, CardSize = 2,
            KeepDiscardedDays = 30, Welcomed = true,
            Hotkeys = new Dictionary<string, string>(Settings.DefaultHotkeys())
            {
                [Actions.Note] = "Ctrl+Alt+N", [Actions.Paste] = ""
            }
        };
        SettingsStore.Save(expected);
        var actual = SettingsStore.Load();
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        Assert.False(File.Exists(AppPaths.SettingsFile + ".tmp"));
    }
}
