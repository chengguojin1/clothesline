using System.Windows.Input;
using System.Windows.Interop;
using Clothesline.Core;

namespace Clothesline.Interop;

/// <summary>Registers system-wide shortcuts on a hidden message-only window.</summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _src;
    private readonly Dictionary<int, Action> _handlers = new();
    private readonly Dictionary<string, int> _ids = new();
    private int _next = 1;

    public HotkeyManager()
    {
        _src = new HwndSource(new HwndSourceParameters("ClotheslineHotkeys") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) });
        _src.AddHook(Hook);
    }

    /// <summary>Binds (or re-binds) a named action. Returns false if another app already owns the shortcut.</summary>
    public bool Bind(string name, Hotkey key, Action handler)
    {
        Unbind(name);
        if (!key.IsValid) return false;
        int id = _next++;
        uint mods = Native.MOD_NOREPEAT;
        if (key.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= Native.MOD_ALT;
        if (key.Modifiers.HasFlag(ModifierKeys.Control)) mods |= Native.MOD_CONTROL;
        if (key.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= Native.MOD_SHIFT;
        if (key.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= Native.MOD_WIN;
        if (!Native.RegisterHotKey(_src.Handle, id, mods, (uint)KeyInterop.VirtualKeyFromKey(key.Key))) return false;
        _handlers[id] = handler;
        _ids[name] = id;
        return true;
    }

    public void Unbind(string name)
    {
        if (!_ids.Remove(name, out var id)) return;
        Native.UnregisterHotKey(_src.Handle, id);
        _handlers.Remove(id);
    }

    /// <summary>Temporarily releases every shortcut (while the user is recording a new one).</summary>
    public void Suspend(bool suspend, Func<string, (Hotkey, Action)?> lookup)
    {
        foreach (var name in Actions.All)
        {
            if (suspend) Unbind(name);
            else if (lookup(name) is { } b) Bind(name, b.Item1, b.Item2);
        }
    }

    private IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _handlers.TryGetValue(w.ToInt32(), out var a))
        {
            handled = true;
            try { a(); } catch (Exception ex) { Log.Error("Hotkey action failed", ex); }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys) Native.UnregisterHotKey(_src.Handle, id);
        _src.Dispose();
    }
}
