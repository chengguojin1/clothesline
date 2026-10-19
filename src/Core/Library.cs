using System.IO;
using System.Text.Json;

namespace Jemur.Core;

/// <summary>
/// The model: what is on the line and what has been discarded. Views (the line, the control panel)
/// observe it; nothing here knows about windows. All members must be called on the UI thread.
/// </summary>
internal sealed class Library
{
    private sealed class FileShape
    {
        public int Version { get; set; } = 1; // a file without "Version" is pre-ropes
        public List<Rope> Ropes { get; set; } = new();
        public List<CardGroup> Groups { get; set; } = new();
        public List<HangItem> Items { get; set; } = new();
        public List<HangItem> Discarded { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly List<HangItem> _items = new();
    private readonly List<HangItem> _discarded = new();
    private readonly SynchronizationContext? _ui;
    private readonly List<Rope> _ropes = new();
    private readonly List<CardGroup> _groups = new();
    private Timer? _saveTimer;
    // Folder this library loaded from; a late debounced save must not follow AppPaths to somewhere else.
    private string _dataDir = AppPaths.Data;
    private bool _backupV1;

    public IReadOnlyList<Rope> Ropes => _ropes;
    public IReadOnlyList<CardGroup> Groups => _groups;
    public IEnumerable<HangItem> ItemsOn(string ropeId) => _items.Where(i => i.RopeId == ropeId);
    public Rope? RopeById(string id) => _ropes.FirstOrDefault(r => r.Id == id);
    public IReadOnlyList<HangItem> Items => _items;
    public IReadOnlyList<HangItem> Discarded => _discarded;

    public event Action<HangItem>? Added;
    /// <summary>Raised when an item leaves the line (discarded or deleted).</summary>
    public event Action<HangItem>? Removed;
    public event Action<HangItem>? Updated;
    /// <summary>Raised after any change, including to the discarded pile.</summary>
    public event Action? Changed;
    /// <summary>Raised when an item's rope, pin state or group changes, so views reconcile.</summary>
    public event Action<HangItem>? Relocated;

    public Library() => _ui = SynchronizationContext.Current;

    // ───────────── persistence ─────────────

    public void Load(int keepDiscardedDays, string defaultRopeName = "Jemuran")
    {
        _dataDir = AppPaths.Data;
        try
        {
            if (!File.Exists(AppPaths.ItemsFile)) return;
            var json = File.ReadAllText(AppPaths.ItemsFile);
            FileShape shape;
            if (json.TrimStart().StartsWith('['))
                shape = new FileShape { Items = JsonSerializer.Deserialize<List<HangItem>>(json) ?? new() }; // pre-0.1 format
            else
                shape = JsonSerializer.Deserialize<FileShape>(json) ?? new();

            shape.Items ??= new();
            shape.Discarded ??= new();
            shape.Items.RemoveAll(i => i == null);
            shape.Discarded.RemoveAll(i => i == null);
            foreach (var i in shape.Items.Concat(shape.Discarded)) i.File = AppPaths.Rebase(i.File);
            _items.AddRange(shape.Items.Where(IsUsable));
            _discarded.AddRange(shape.Discarded.Where(IsUsable));
            _ropes.AddRange(shape.Ropes ?? new());
            _groups.AddRange(shape.Groups ?? new());
            _backupV1 = shape.Version < 2;
            PurgeDiscarded(keepDiscardedDays);
        }
        catch (Exception ex)
        {
            Log.Error("Could not read items.json; starting with an empty line", ex);
            TryBackupCorrupt();
        }
        finally { Repair(defaultRopeName); }
    }

    /// <summary>Guarantees the default rope exists and every item points at a real rope.</summary>
    private void Repair(string defaultRopeName)
    {
        // A hand-edited or half-written file may hold nulls, missing ids or duplicates; keep the first of each.
        var ropeIds = new HashSet<string>();
        _ropes.RemoveAll(r => r == null || string.IsNullOrWhiteSpace(r.Id) || !ropeIds.Add(r.Id));
        foreach (var r in _ropes)
            if (string.IsNullOrWhiteSpace(r.Name)) r.Name = defaultRopeName;
        var groupIds = new HashSet<string>();
        _groups.RemoveAll(g => g == null || string.IsNullOrWhiteSpace(g.Id) || !groupIds.Add(g.Id));
        foreach (var g in _groups) g.Name ??= "";

        if (RopeById(Rope.DefaultId) == null) _ropes.Insert(0, new Rope { Id = Rope.DefaultId, Name = defaultRopeName });
        foreach (var i in _items.Concat(_discarded)) EnsureRope(i);

        // Groups may only hold live items of their own rope, each in at most one group.
        _groups.RemoveAll(g => RopeById(g.RopeId) == null);
        var seen = new HashSet<string>();
        foreach (var g in _groups)
        {
            g.ItemIds ??= new();
            g.ItemIds = g.ItemIds.Where(id => _items.Any(i => i.Id == id && i.RopeId == g.RopeId) && seen.Add(id)).ToList();
        }
        _groups.RemoveAll(g => g.ItemIds.Count == 0);
    }

    private void EnsureRope(HangItem i)
    {
        if (i.RopeId == null || RopeById(i.RopeId) == null) i.RopeId = Rope.DefaultId;
    }

    private static bool IsUsable(HangItem i) => i.Kind == ItemKind.Note || (i.File != null && File.Exists(i.File));

    private static void TryBackupCorrupt()
    {
        try { File.Copy(AppPaths.ItemsFile, AppPaths.ItemsFile + $".broken-{DateTime.Now:yyyyMMddHHmmss}", true); } catch { }
    }

    /// <summary>Coalesces bursts of edits (typing, dragging) into one write.</summary>
    public void ScheduleSave()
    {
        _saveTimer ??= new Timer(_ => Post(SaveNow));
        _saveTimer.Change(500, Timeout.Infinite);
    }

    public void SaveNow()
    {
        _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        var dir = _dataDir;
        var file = Path.Combine(dir, "items.json");
        try
        {
            Directory.CreateDirectory(dir);
            if (_backupV1)
            {
                // Keep the pre-ropes file once, so a downgrade or a bug never costs anyone their line.
                var bak = Path.Combine(dir, "items.v1-backup.json");
                if (File.Exists(file) && !File.Exists(bak)) File.Copy(file, bak);
                _backupV1 = false;
            }
            var shape = new FileShape { Version = 2, Ropes = _ropes.ToList(), Groups = _groups.ToList(), Items = _items.ToList(), Discarded = _discarded.ToList() };
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(shape, Json));
            File.Move(tmp, file, true);
        }
        catch (Exception ex) { Log.Error("Saving items failed", ex); }
    }

    private void Post(Action a)
    {
        if (_ui != null) _ui.Post(_ => a(), null);
        else a();
    }

    // ───────────── mutations ─────────────

    public void Add(HangItem item)
    {
        item.DiscardedAt = null;
        EnsureRope(item);
        _items.Add(item);
        Added?.Invoke(item);
        Changed?.Invoke();
        ScheduleSave();
    }

    public void Touch(HangItem item)
    {
        Updated?.Invoke(item);
        Changed?.Invoke();
        ScheduleSave();
    }

    public void Discard(HangItem item)
    {
        if (!_items.Remove(item)) return;
        LeaveGroupQuietly(item);
        item.Pinned = false;
        item.PinWidth = item.PinHeight = 0;
        item.Position = -1;
        item.DiscardedAt = DateTime.Now;
        _discarded.Insert(0, item);
        Removed?.Invoke(item);
        Changed?.Invoke();
        ScheduleSave();
    }

    /// <summary>Removes an item without keeping it (e.g. an empty note that was never written).</summary>
    public void Drop(HangItem item)
    {
        if (!_items.Remove(item)) return;
        LeaveGroupQuietly(item);
        DeleteFile(item);
        Removed?.Invoke(item);
        Changed?.Invoke();
        ScheduleSave();
    }

    /// <summary>Discards every live item of one rope.</summary>
    public void DiscardAll(string ropeId)
    {
        foreach (var i in _items.Where(i => i.RopeId == ropeId).ToList()) Discard(i);
    }

    // ───────────── ropes ─────────────

    public Rope AddRope(string name)
    {
        var r = new Rope { Name = name };
        _ropes.Add(r);
        Changed?.Invoke();
        ScheduleSave();
        return r;
    }

    public void RenameRope(string id, string name)
    {
        var r = RopeById(id);
        if (r == null) return;
        r.Name = name;
        Changed?.Invoke();
        ScheduleSave();
    }

    /// <summary>Removes a rope; its items (live and discarded) go to the default rope.</summary>
    public bool DeleteRope(string id)
    {
        var r = RopeById(id);
        if (r == null || id == Rope.DefaultId) return false;
        _ropes.Remove(r);
        _groups.RemoveAll(g => g.RopeId == id);
        foreach (var i in _discarded.Where(i => i.RopeId == id)) i.RopeId = Rope.DefaultId;
        foreach (var i in _items.Where(i => i.RopeId == id).ToList()) Relocate(i, Rope.DefaultId);
        Changed?.Invoke();
        ScheduleSave();
        return true;
    }

    public void MoveToRope(HangItem item, string ropeId)
    {
        if (RopeById(ropeId) == null) return;
        Relocate(item, ropeId);
        Changed?.Invoke();
        ScheduleSave();
    }

    private void Relocate(HangItem item, string ropeId)
    {
        LeaveGroupQuietly(item);
        item.RopeId = ropeId;
        item.Position = -1;
        Relocated?.Invoke(item);
    }

    // ───────────── desktop spots ─────────────

    /// <summary>Gives a card its first desktop spot. Silent: the view is already there.</summary>
    public void Place(HangItem item, double left, double top)
    {
        item.Pinned = true;
        item.PinLeft = left;
        item.PinTop = top;
        ScheduleSave();
    }

    /// <summary>Remembers where a desktop card was dragged or resized. Silent: the view is already there.</summary>
    public void MovePin(HangItem item, double left, double top, double width, double height)
    {
        item.PinLeft = left;
        item.PinTop = top;
        item.PinWidth = width;
        item.PinHeight = height;
        ScheduleSave();
    }

    // ───────────── groups ─────────────

    public CardGroup? GroupOf(HangItem item) => _groups.FirstOrDefault(g => g.ItemIds.Contains(item.Id));

    public CardGroup CreateGroup(HangItem first, string name)
    {
        LeaveGroupQuietly(first);
        var g = new CardGroup { RopeId = first.RopeId ?? Rope.DefaultId, Name = name, Position = first.Position, ItemIds = { first.Id } };
        _groups.Add(g);
        Relocated?.Invoke(first);
        Commit();
        return g;
    }

    /// <summary>Moves a card into a group of its own rope at <paramref name="index"/> (clamped).</summary>
    public void JoinGroup(HangItem item, string groupId, int index)
    {
        var g = _groups.FirstOrDefault(x => x.Id == groupId);
        if (g == null || g.RopeId != item.RopeId) return;
        if (!g.ItemIds.Remove(item.Id)) LeaveGroupQuietly(item); // already a member: just reordering
        g.ItemIds.Insert(Math.Clamp(index, 0, g.ItemIds.Count), item.Id);
        Relocated?.Invoke(item);
        Commit();
    }

    public void LeaveGroup(HangItem item)
    {
        if (!LeaveGroupQuietly(item)) return;
        Relocated?.Invoke(item);
        Commit();
    }

    public void RenameGroup(string id, string name) => EditGroup(id, g => g.Name = name);
    public void SetFolded(string id, bool folded) => EditGroup(id, g => g.Folded = folded);
    public void MoveGroup(string id, double position) => EditGroup(id, g => g.Position = position);

    /// <summary>Dissolves a group. <paramref name="positions"/> gives each card its new place on the rope (by item id).</summary>
    public void Ungroup(string id, IReadOnlyDictionary<string, double>? positions = null)
    {
        var g = _groups.FirstOrDefault(x => x.Id == id);
        if (g == null) return;
        _groups.Remove(g);
        foreach (var item in g.ItemIds.Select(i => _items.FirstOrDefault(x => x.Id == i)).OfType<HangItem>())
        {
            if (positions != null && positions.TryGetValue(item.Id, out var p)) item.Position = p;
            Relocated?.Invoke(item);
        }
        Commit();
    }

    private void EditGroup(string id, Action<CardGroup> edit)
    {
        var g = _groups.FirstOrDefault(x => x.Id == id);
        if (g == null) return;
        edit(g);
        Commit();
    }

    /// <summary>Takes a card out of its group (removing the group if that empties it), without raising events.</summary>
    private bool LeaveGroupQuietly(HangItem item)
    {
        var g = GroupOf(item);
        if (g == null) return false;
        g.ItemIds.Remove(item.Id);
        if (g.ItemIds.Count == 0) _groups.Remove(g);
        return true;
    }

    private void Commit()
    {
        Changed?.Invoke();
        ScheduleSave();
    }

    public void Restore(HangItem item)
    {
        if (!_discarded.Remove(item)) return;
        EnsureRope(item);
        if (item.Kind == ItemKind.Image && (item.File == null || !File.Exists(item.File))) { Changed?.Invoke(); ScheduleSave(); return; }
        Add(item);
    }

    public void DeleteForever(HangItem item)
    {
        if (!_discarded.Remove(item)) return;
        DeleteFile(item);
        Changed?.Invoke();
        ScheduleSave();
    }

    public void EmptyDiscarded()
    {
        foreach (var i in _discarded) DeleteFile(i);
        _discarded.Clear();
        Changed?.Invoke();
        ScheduleSave();
    }

    public void PurgeDiscarded(int days)
    {
        var cutoff = DateTime.Now.AddDays(-Math.Max(1, days));
        foreach (var i in _discarded.Where(i => i.DiscardedAt < cutoff).ToList())
        {
            _discarded.Remove(i);
            DeleteFile(i);
        }
    }

    private void DeleteFile(HangItem item)
    {
        // Only ever delete PNGs we created, and only if nothing else still points at them.
        if (item.Kind != ItemKind.Image || !AppPaths.IsOwnedShot(item.File)) return;
        if (_items.Concat(_discarded).Any(o => o != item && string.Equals(o.File, item.File, StringComparison.OrdinalIgnoreCase))) return;
        try { File.Delete(item.File!); } catch (Exception ex) { Log.Warn($"Could not delete {item.File}", ex); }
    }
}
