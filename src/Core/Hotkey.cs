using System.Windows.Input;

namespace Clothesline.Core;

/// <summary>A global shortcut such as "Ctrl+Alt+S". Parsing and formatting are culture-independent.</summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public bool IsEmpty => Key == Key.None;

    /// <summary>Global shortcuts need at least one of Ctrl, Alt or Win so they don't eat normal typing.</summary>
    public bool IsValid => !IsEmpty && (Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0 && !IsModifierKey(Key);

    public static bool IsModifierKey(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = ModifierKeys.None;
        Key key = Key.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win" or "windows": mods |= ModifierKeys.Windows; break;
                default:
                    if (key != Key.None) return false;
                    if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                    else if (KeyFromName(raw) is { } named) key = named;
                    else if (!Enum.TryParse(raw, true, out key) || int.TryParse(raw, out _)) return false;
                    break;
            }
        }
        hotkey = new Hotkey(mods, key);
        return hotkey.IsValid;
    }

    public override string ToString() => string.Join("+", Parts());

    /// <summary>Display parts in Windows' canonical order, e.g. ["Ctrl", "Alt", "S"].</summary>
    public IEnumerable<string> Parts()
    {
        if (Modifiers.HasFlag(ModifierKeys.Windows)) yield return "Win";
        if (Modifiers.HasFlag(ModifierKeys.Control)) yield return "Ctrl";
        if (Modifiers.HasFlag(ModifierKeys.Alt)) yield return "Alt";
        if (Modifiers.HasFlag(ModifierKeys.Shift)) yield return "Shift";
        if (!IsEmpty) yield return KeyName(Key);
    }

    private static Key? KeyFromName(string name) => name switch
    {
        "," => Key.OemComma, "." => Key.OemPeriod, "-" => Key.OemMinus, "=" => Key.OemPlus, "/" => Key.Oem2,
        ";" => Key.Oem1, "`" => Key.Oem3, "[" => Key.Oem4, "]" => Key.Oem6, "\\" => Key.Oem5, "'" => Key.Oem7,
        _ when name.Equals("Enter", StringComparison.OrdinalIgnoreCase) => Key.Return,
        _ when name.Equals("PrtScn", StringComparison.OrdinalIgnoreCase) => Key.Snapshot,
        _ => null,
    };

    private static string KeyName(Key k) => k switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (k - Key.D0))).ToString(),
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.Oem2 => "/",
        Key.Oem1 => ";",
        Key.Oem3 => "`",
        Key.Oem4 => "[",
        Key.Oem6 => "]",
        Key.Oem5 => "\\",
        Key.Oem7 => "'",
        Key.Return => "Enter",
        Key.Snapshot => "PrtScn",
        _ => k.ToString(),
    };
}
