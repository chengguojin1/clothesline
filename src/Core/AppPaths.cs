using System.IO;
namespace Clothesline.Core;

/// <summary>Every location Clothesline reads from or writes to, in one place.</summary>
internal static class AppPaths
{
    public static string Data { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clothesline");
    public static string Shots => Path.Combine(Data, "shots");
    public static string UserLocales => Path.Combine(Data, "locales");
    public static string ItemsFile => Path.Combine(Data, "items.json");
    public static string SettingsFile => Path.Combine(Data, "settings.json");
    public static string Logs { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clothesline", "logs");

    /// <summary>Points all storage at another folder (used by tests so they never touch real user data).</summary>
    internal static void OverrideRoot(string root)
    {
        Data = Path.Combine(root, "data");
        Logs = Path.Combine(root, "logs");
    }
    public static string Temp => Path.Combine(Path.GetTempPath(), "Clothesline");

    /// <summary>Folder used by the original Jemur build, for one-time data migration.</summary>
    private static string LegacyData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Jemur");

    public static void Ensure()
    {
        // CLOTHESLINE_DATA points everything at a separate folder: handy for development and demos.
        if (Environment.GetEnvironmentVariable("CLOTHESLINE_DATA") is { Length: > 0 } custom) OverrideRoot(custom);
        try
        {
            if (!Directory.Exists(Data) && Directory.Exists(LegacyData))
            {
                Directory.Move(LegacyData, Data);
                Log.Info("Migrated data folder from Jemur to Clothesline");
            }
        }
        catch (Exception ex) { Log.Error("Data migration failed", ex); }

        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Shots);
    }

    /// <summary>Rewrites paths stored by older builds so they point at the current data folder.</summary>
    public static string? Rebase(string? path)
    {
        if (path == null) return null;
        return path.StartsWith(LegacyData, StringComparison.OrdinalIgnoreCase) ? Data + path[LegacyData.Length..] : path;
    }

    public static bool IsOwnedShot(string? path) =>
        path != null && Path.GetFullPath(path).StartsWith(Path.GetFullPath(Shots), StringComparison.OrdinalIgnoreCase);
}
