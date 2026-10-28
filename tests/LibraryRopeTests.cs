using System.IO;
using Clothesline.Core;
using Xunit;

namespace Clothesline.Tests;

/// <summary>Ropes in the Library: v2 file format, migration from v1, and the rope API.</summary>
[Collection("AppPaths")]
public sealed class LibraryRopeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jemur-tests-" + Guid.NewGuid().ToString("N"));

    public LibraryRopeTests()
    {
        AppPaths.OverrideRoot(_root);
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private const string NoteJson = """{"Id":"n1","Kind":"Note","Text":"hi","Position":0.5}""";
    private const string V1 = "{\"Version\":1,\"Items\":[" + NoteJson + "],\"Discarded\":[]}";

    private static Library Loaded()
    {
        var lib = new Library();
        lib.Load(7);
        return lib;
    }

    private static HangItem Note(string? rope = null) => new() { Kind = ItemKind.Note, Text = "x", RopeId = rope };

    [Fact]
    public void LoadsV1FileOntoDefaultRope()
    {
        File.WriteAllText(AppPaths.ItemsFile, V1);
        var lib = Loaded();
        var rope = Assert.Single(lib.Ropes);
        Assert.Equal("default", rope.Id);
        Assert.Equal("Clotheslinean", rope.Name);
        Assert.Equal("default", Assert.Single(lib.Items).RopeId);
    }

    [Fact]
    public void LoadsArrayFormatOntoDefaultRope()
    {
        File.WriteAllText(AppPaths.ItemsFile, "[" + NoteJson + "]");
        var lib = Loaded();
        Assert.Equal("default", Assert.Single(lib.Ropes).Id);
        Assert.Equal("default", Assert.Single(lib.Items).RopeId);
    }

    [Fact]
    public void V2RoundTrips()
    {
        var lib = Loaded();
        var rope = lib.AddRope("Klien A");
        lib.Add(Note(rope.Id));
        lib.SaveNow();

        var again = Loaded();
        Assert.Equal(2, again.Ropes.Count);
        Assert.Equal("Klien A", again.RopeById(rope.Id)!.Name);
        Assert.Equal(rope.Id, Assert.Single(again.Items).RopeId);
    }

    [Fact]
    public void FirstV2SaveBacksUpV1File()
    {
        File.WriteAllText(AppPaths.ItemsFile, V1);
        var bak = Path.Combine(AppPaths.Data, "items.v1-backup.json");
        var lib = Loaded();
        lib.SaveNow();
        Assert.Equal(V1, File.ReadAllText(bak));

        lib.Add(Note());
        lib.SaveNow();
        Assert.Equal(V1, File.ReadAllText(bak));
    }

    [Theory]
    [InlineData("[" + NoteJson + "]")]
    [InlineData("{\"Items\":[" + NoteJson + "],\"Discarded\":[]}")]
    public void FirstV2SaveBacksUpArrayAndVersionlessFiles(string original)
    {
        File.WriteAllText(AppPaths.ItemsFile, original);
        Loaded().SaveNow();
        Assert.Equal(original, File.ReadAllText(Path.Combine(AppPaths.Data, "items.v1-backup.json")));
    }

    [Fact]
    public void V2FileIsNotBackedUp()
    {
        var lib = Loaded();
        lib.Add(Note());
        lib.SaveNow();
        Loaded().SaveNow();
        Assert.False(File.Exists(Path.Combine(AppPaths.Data, "items.v1-backup.json")));
    }

    [Fact]
    public void DeleteRope_MovesLiveAndDiscardedItemsToDefault()
    {
        var lib = Loaded();
        var a = lib.AddRope("A");
        var live = Note(a.Id);
        var gone = Note(a.Id);
        lib.Add(live);
        lib.Add(gone);
        lib.Discard(gone);

        Assert.True(lib.DeleteRope(a.Id));
        Assert.Equal("default", live.RopeId);
        Assert.Equal("default", gone.RopeId);
        Assert.Null(lib.RopeById(a.Id));
    }

    [Fact]
    public void DeleteRope_RefusesDefault()
    {
        var lib = Loaded();
        Assert.False(lib.DeleteRope("default"));
        Assert.NotNull(lib.RopeById("default"));
    }

    [Fact]
    public void Restore_LandsOnTheOriginalRopeWhenItStillExists()
    {
        var lib = Loaded();
        var a = lib.AddRope("A");
        var item = Note(a.Id);
        lib.Add(item);
        lib.Discard(item);

        lib.Restore(item);
        Assert.Equal(a.Id, item.RopeId);
        Assert.Contains(item, lib.ItemsOn(a.Id));
    }

    [Fact]
    public void Load_SurvivesNullAndDuplicateRopes()
    {
        File.WriteAllText(AppPaths.ItemsFile, """
            {"Version":2,"Ropes":[null,{"Id":null,"Name":"x"},{"Id":"a","Name":" "},{"Id":"a","Name":"dup"}],
             "Items":[null,{"Id":"n1","Kind":"Note","Text":"hi","RopeId":"a"}],"Discarded":[]}
            """);
        var lib = Loaded();
        Assert.Equal(new[] { "default", "a" }, lib.Ropes.Select(r => r.Id));
        Assert.Equal("Clotheslinean", lib.RopeById("a")!.Name);
        Assert.Equal("a", Assert.Single(lib.Items).RopeId);
    }

    [Fact]
    public void Restore_ReturnsToDefaultWhenRopeIsGone()
    {
        var lib = Loaded();
        var a = lib.AddRope("A");
        var item = Note(a.Id);
        lib.Add(item);
        lib.Discard(item);
        lib.DeleteRope(a.Id);

        lib.Restore(item);
        Assert.Equal("default", item.RopeId);
        Assert.Contains(item, lib.ItemsOn("default"));
    }

    [Fact]
    public void MoveToRope_SetsRopeAndResetsPosition()
    {
        var lib = Loaded();
        var a = lib.AddRope("A");
        var item = Note();
        item.Position = 0.4;
        lib.Add(item);
        var fired = 0;
        lib.Relocated += i => { Assert.Same(item, i); fired++; };

        lib.MoveToRope(item, a.Id);
        Assert.Equal(-1, item.Position);
        Assert.Equal(a.Id, item.RopeId);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void DiscardAll_OnlyTouchesThatRope()
    {
        var lib = Loaded();
        var a = lib.AddRope("A");
        var onA = Note(a.Id);
        var onDefault = Note();
        var pinnedOnA = Note(a.Id);
        pinnedOnA.Pinned = true;
        lib.Add(onA);
        lib.Add(onDefault);
        lib.Add(pinnedOnA);

        lib.DiscardAll(a.Id);
        Assert.Equal(new[] { onDefault }, lib.Items);
        Assert.Equal(2, lib.Discarded.Count);
    }

    [Fact]
    public void Load_RepairsDanglingRopeReference()
    {
        File.WriteAllText(AppPaths.ItemsFile,
            """{"Version":2,"Ropes":[{"Id":"default","Name":"J"}],"Items":[{"Id":"n1","Kind":"Note","RopeId":"ghost"}],"Discarded":[]}""");
        Assert.Equal("default", Assert.Single(Loaded().Items).RopeId);
    }
}
