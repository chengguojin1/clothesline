using System.IO;
using Clothesline.Core;
using Xunit;

namespace Clothesline.Tests;

/// <summary>Library behaviour against a throwaway data folder.</summary>
[Collection("AppPaths")]
public sealed class LibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jemur-tests-" + Guid.NewGuid().ToString("N"));

    public LibraryTests()
    {
        AppPaths.OverrideRoot(_root);
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static string FakeShot()
    {
        var p = Path.Combine(AppPaths.Shots, Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
        return p;
    }

    [Fact]
    public void DiscardRestoreRoundTrip()
    {
        var lib = new Library();
        var note = new HangItem { Kind = ItemKind.Note, Text = "hello" };
        lib.Add(note);
        lib.Discard(note);
        Assert.Empty(lib.Items);
        Assert.Single(lib.Discarded);
        Assert.NotNull(note.DiscardedAt);

        lib.Restore(note);
        Assert.Single(lib.Items);
        Assert.Empty(lib.Discarded);
        Assert.Null(note.DiscardedAt);
    }

    [Fact]
    public void PersistsAndReloads()
    {
        var lib = new Library();
        lib.Add(new HangItem { Kind = ItemKind.Note, Text = "kept", Position = 0.3 });
        var gone = new HangItem { Kind = ItemKind.Note, Text = "gone" };
        lib.Add(gone);
        lib.Discard(gone);
        lib.SaveNow();

        var again = new Library();
        again.Load(keepDiscardedDays: 7);
        Assert.Equal("kept", Assert.Single(again.Items).Text);
        Assert.Equal(0.3, again.Items[0].Position);
        Assert.Equal("gone", Assert.Single(again.Discarded).Text);
    }

    [Fact]
    public void PurgeDeletesOldDiscardedFiles()
    {
        var lib = new Library();
        var shot = new HangItem { Kind = ItemKind.Image, File = FakeShot() };
        lib.Add(shot);
        lib.Discard(shot);
        shot.DiscardedAt = DateTime.Now.AddDays(-10);

        lib.PurgeDiscarded(7);
        Assert.Empty(lib.Discarded);
        Assert.False(File.Exists(shot.File));
    }

    [Fact]
    public void NeverDeletesFilesOutsideItsOwnFolder()
    {
        var outside = Path.Combine(_root, "elsewhere.png");
        File.WriteAllBytes(outside, new byte[] { 1 });
        var lib = new Library();
        var item = new HangItem { Kind = ItemKind.Image, File = outside };
        lib.Add(item);
        lib.Discard(item);
        lib.EmptyDiscarded();
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void ReadsTheOldArrayFormat()
    {
        Directory.CreateDirectory(AppPaths.Data);
        File.WriteAllText(AppPaths.ItemsFile, """[ { "Id": "a", "Kind": 1, "Text": "legacy", "X": 120 } ]""");
        var lib = new Library();
        lib.Load(7);
        var item = Assert.Single(lib.Items);
        Assert.Equal(ItemKind.Note, item.Kind);
        Assert.Equal("legacy", item.Text);
    }

    [Fact]
    public void CorruptFileStartsEmptyAndKeepsABackup()
    {
        Directory.CreateDirectory(AppPaths.Data);
        File.WriteAllText(AppPaths.ItemsFile, "{ not json");
        var lib = new Library();
        lib.Load(7);
        Assert.Empty(lib.Items);
        Assert.NotEmpty(Directory.GetFiles(AppPaths.Data, "items.json.broken-*"));
    }
}
