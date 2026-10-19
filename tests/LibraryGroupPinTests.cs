using System.IO;
using Jemur.Core;
using Xunit;

namespace Jemur.Tests;

/// <summary>Desktop pins and card groups in the Library.</summary>
[Collection("AppPaths")]
public sealed class LibraryGroupPinTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jemur-tests-" + Guid.NewGuid().ToString("N"));

    public LibraryGroupPinTests()
    {
        AppPaths.OverrideRoot(_root);
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static Library Loaded()
    {
        var lib = new Library();
        lib.Load(7);
        return lib;
    }

    private static HangItem Add(Library lib, string? rope = null)
    {
        var n = new HangItem { Kind = ItemKind.Note, Text = "x", RopeId = rope };
        lib.Add(n);
        return n;
    }

    [Fact]
    public void Load_SurvivesNullAndDuplicateGroups()
    {
        File.WriteAllText(AppPaths.ItemsFile, """
            {"Version":2,"Ropes":[{"Id":"default","Name":"Jemuran"}],
             "Groups":[null,{"Id":null,"ItemIds":["n1"]},{"Id":"g","Name":null,"ItemIds":["n1",null]},{"Id":"g","ItemIds":["n2"]}],
             "Items":[{"Id":"n1","Kind":"Note","Text":"a"},{"Id":"n2","Kind":"Note","Text":"b"}],"Discarded":[]}
            """);
        var lib = Loaded();
        var g = Assert.Single(lib.Groups);
        Assert.Equal("g", g.Id);
        Assert.Equal("", g.Name);
        Assert.Equal(new[] { "n1" }, g.ItemIds);
    }

    [Fact]
    public void ModeRoundTrip_KeepsSpotsSizesAndPositions()
    {
        var lib = Loaded();
        var a = Add(lib);
        a.Position = 0.4;
        var fired = 0;
        lib.Relocated += _ => fired++;
        lib.Place(a, 5, 6);
        lib.MovePin(a, 10, 20, 300, 200);
        Assert.Equal(0, fired);
        lib.SaveNow();

        var a2 = Loaded().Items.Single(i => i.Id == a.Id);
        Assert.Equal((true, 10d, 20d, 300d, 200d, 0.4), (a2.Pinned, a2.PinLeft, a2.PinTop, a2.PinWidth, a2.PinHeight, a2.Position));
    }

    [Fact]
    public void MoveToRopeAndGrouping_KeepSpotAndSize()
    {
        var lib = Loaded();
        var rope = lib.AddRope("B");
        var a = Add(lib);
        var b = Add(lib);
        lib.Place(a, 1, 2);
        lib.MovePin(a, 1, 2, 150, 130);
        lib.Place(b, 3, 4);
        var g = lib.CreateGroup(a, "g");
        lib.JoinGroup(b, g.Id, 1);
        Assert.All(new[] { a, b }, i => Assert.True(i.Pinned));
        Assert.Equal(new[] { a.Id, b.Id }, g.ItemIds);
        lib.MoveToRope(a, rope.Id);
        Assert.Equal((true, 1d, 2d, 150d, 130d), (a.Pinned, a.PinLeft, a.PinTop, a.PinWidth, a.PinHeight));
    }

    [Fact]
    public void Discard_ClearsSpotAndSize()
    {
        var lib = Loaded();
        var p = Add(lib);
        lib.Place(p, 1, 1);
        lib.MovePin(p, 1, 1, 200, 150);
        lib.Discard(p);
        Assert.Equal((false, 0d, 0d), (p.Pinned, p.PinWidth, p.PinHeight));
    }

    [Fact]
    public void Upgrade_PinnedCardsKeepSpotAndPosition()
    {
        File.WriteAllText(AppPaths.ItemsFile, """
            {"Version":2,"Ropes":[{"Id":"default","Name":"J"}],"Groups":[],
             "Items":[{"Id":"n1","Kind":"Note","Pinned":true,"PinLeft":50,"PinTop":60,"Position":0.3}],"Discarded":[]}
            """);
        var i = Assert.Single(Loaded().Items);
        Assert.Equal((true, 50d, 60d, 0.3, 0d, 0d), (i.Pinned, i.PinLeft, i.PinTop, i.Position, i.PinWidth, i.PinHeight));
    }

    [Fact]
    public void DiscardAll_DiscardsEveryLiveCardOnTheRope()
    {
        var lib = Loaded();
        Add(lib);
        var b = Add(lib);
        lib.Place(b, 1, 1);
        var other = Add(lib, lib.AddRope("B").Id);
        lib.DiscardAll("default");
        Assert.Equal(new[] { other }, lib.Items);
        Assert.Equal(2, lib.Discarded.Count);
    }

    [Fact]
    public void JoinGroup_InsertsAtIndexAndLeavesOldGroup()
    {
        var lib = Loaded();
        var a = Add(lib);
        var b = Add(lib);
        var c = Add(lib);
        var d = Add(lib);
        var g1 = lib.CreateGroup(a, "1");
        lib.JoinGroup(b, g1.Id, 5); // clamped to the end
        Assert.Equal(new[] { a.Id, b.Id }, g1.ItemIds);
        var g2 = lib.CreateGroup(c, "2");
        lib.JoinGroup(d, g2.Id, 0);
        Assert.Equal(new[] { d.Id, c.Id }, g2.ItemIds);

        lib.JoinGroup(a, g2.Id, 1);
        Assert.Equal(new[] { d.Id, a.Id, c.Id }, g2.ItemIds);
        Assert.Equal(new[] { b.Id }, g1.ItemIds);
        lib.JoinGroup(b, g2.Id, 0);
        Assert.DoesNotContain(g1, lib.Groups);
    }

    [Fact]
    public void JoinGroup_ReordersWithinSameGroup()
    {
        var lib = Loaded();
        var a = Add(lib);
        var b = Add(lib);
        var g = lib.CreateGroup(a, "g");
        lib.JoinGroup(b, g.Id, 1);
        lib.JoinGroup(b, g.Id, 0);
        Assert.Equal(new[] { b.Id, a.Id }, g.ItemIds);
        lib.LeaveGroup(b);
        lib.JoinGroup(a, g.Id, 0); // sole member rejoining its own group keeps it alive
        Assert.Equal(new[] { a.Id }, Assert.Single(lib.Groups).ItemIds);
    }

    [Fact]
    public void CreateGroup_SitsAtFirstCardPositionOnItsRope()
    {
        var lib = Loaded();
        var rope = lib.AddRope("B");
        var a = Add(lib, rope.Id);
        a.Position = 0.3;
        var g = lib.CreateGroup(a, "g");
        Assert.Equal((0.3, rope.Id, "g"), (g.Position, g.RopeId, g.Name));
        Assert.Same(g, lib.GroupOf(a));
    }

    [Fact]
    public void Discard_RemovesFromGroup()
    {
        var lib = Loaded();
        var a = Add(lib);
        var b = Add(lib);
        var g = lib.CreateGroup(a, "g");
        lib.JoinGroup(b, g.Id, 1);
        lib.Discard(a);
        Assert.Equal(new[] { b.Id }, g.ItemIds);
        lib.Discard(b);
        Assert.Empty(lib.Groups);
    }

    [Fact]
    public void Drop_AndMoveToRope_LeaveGroup()
    {
        var lib = Loaded();
        var rope = lib.AddRope("B");
        var a = Add(lib);
        var b = Add(lib);
        var c = Add(lib);
        var g = lib.CreateGroup(a, "g");
        lib.JoinGroup(b, g.Id, 1);
        lib.JoinGroup(c, g.Id, 2);
        lib.Drop(a);
        lib.MoveToRope(b, rope.Id);
        Assert.Equal(new[] { c.Id }, g.ItemIds);
    }

    [Fact]
    public void DeleteRope_DissolvesGroupsAndKeepsSpots()
    {
        var lib = Loaded();
        var rope = lib.AddRope("B");
        var a = Add(lib, rope.Id);
        var b = Add(lib, rope.Id);
        lib.JoinGroup(b, lib.CreateGroup(a, "g").Id, 1);
        var p = Add(lib, rope.Id);
        lib.Place(p, 1, 1);

        Assert.True(lib.DeleteRope(rope.Id));
        Assert.Empty(lib.Groups);
        Assert.All(new[] { a, b, p }, i => { Assert.Equal("default", i.RopeId); });
        Assert.True(p.Pinned);
    }

    [Fact]
    public void Ungroup_AppliesPositionsAndRemovesGroup()
    {
        var lib = Loaded();
        var a = Add(lib);
        var b = Add(lib);
        var g = lib.CreateGroup(a, "g");
        lib.JoinGroup(b, g.Id, 1);
        lib.Ungroup(g.Id, new Dictionary<string, double> { [a.Id] = 0.1, [b.Id] = 0.2 });
        Assert.Empty(lib.Groups);
        Assert.Equal((0.1, 0.2), (a.Position, b.Position));
    }

    [Fact]
    public void RenameFoldMove_UpdateGroup()
    {
        var lib = Loaded();
        var g = lib.CreateGroup(Add(lib), "g");
        lib.RenameGroup(g.Id, "new");
        lib.SetFolded(g.Id, true);
        lib.MoveGroup(g.Id, 0.7);
        Assert.Equal(("new", true, 0.7), (g.Name, g.Folded, g.Position));
    }

    [Fact]
    public void Load_RepairsDanglingGroupReferences()
    {
        File.WriteAllText(AppPaths.ItemsFile, """
            {"Version":2,"Ropes":[{"Id":"default","Name":"J"},{"Id":"r2","Name":"B"}],
             "Groups":[
               {"Id":"g1","RopeId":"default","ItemIds":["n1","missing","n2","n1"]},
               {"Id":"g2","RopeId":"default","ItemIds":["missing","n2"]},
               {"Id":"g3","RopeId":"gone","ItemIds":["n1"]},
               {"Id":"g4","RopeId":"default","ItemIds":[]}],
             "Items":[{"Id":"n1","Kind":"Note","RopeId":"default"},{"Id":"n2","Kind":"Note","RopeId":"r2"}],
             "Discarded":[]}
            """);
        var lib = Loaded();
        var g = Assert.Single(lib.Groups);
        Assert.Equal("g1", g.Id);
        Assert.Equal(new[] { "n1" }, g.ItemIds);
    }

    [Fact]
    public void Load_TreatsNullListsAsEmpty()
    {
        File.WriteAllText(AppPaths.ItemsFile,
            """{"Version":2,"Ropes":null,"Groups":null,"Items":[{"Id":"n1","Kind":"Note"}],"Discarded":null}""");
        var lib = Loaded();
        Assert.Single(lib.Items);
        Assert.Empty(lib.Groups);
    }

    [Fact]
    public void GroupsAndPinsRoundTrip()
    {
        var lib = Loaded();
        var a = Add(lib);
        var b = Add(lib);
        var p = Add(lib);
        var g = lib.CreateGroup(b, "g");
        lib.JoinGroup(a, g.Id, 0);
        lib.SetFolded(g.Id, true);
        lib.Place(p, 12.5, 34.5);
        lib.SaveNow();

        var again = Loaded();
        var g2 = Assert.Single(again.Groups);
        Assert.Equal(new[] { a.Id, b.Id }, g2.ItemIds);
        Assert.True(g2.Folded);
        var p2 = again.Items.Single(i => i.Id == p.Id);
        Assert.Equal((true, 12.5, 34.5), (p2.Pinned, p2.PinLeft, p2.PinTop));
    }
}
