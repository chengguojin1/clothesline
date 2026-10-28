using System.IO;
using System.Text.Json;

namespace Clothesline.Core;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        Settings s;
        try
        {
            s = File.Exists(AppPaths.SettingsFile)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new()
                : new();
        }
        catch (Exception ex)
        {
            Log.Error("settings.json is unreadable; using defaults", ex);
            s = new();
        }
        s.Normalize();
        return s;
    }

    public static void Save(Settings s)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Data);
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
            File.Move(tmp, AppPaths.SettingsFile, true);
        }
        catch (Exception ex) { Log.Error("Saving settings failed", ex); }
    }
}
