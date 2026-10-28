using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Clothesline.Core;

/// <summary>
/// UI strings. Each language is a flat JSON file in /locales (e.g. <c>en.json</c>, <c>id.json</c>).
/// Files are embedded in the build, and extra or overriding files are picked up at runtime from
/// <c>%APPDATA%\Clothesline\locales</c> and a <c>locales</c> folder next to Clothesline.exe — so a new language can be
/// tried without rebuilding. Missing keys fall back to English, then to the key itself.
/// </summary>
internal static class L
{
    public sealed record Language(string Code, string Name, string EnglishName);

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> _current = new();
    private static Dictionary<string, string> _fallback = new();

    public static string Code { get; private set; } = "en";
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");
    public static IReadOnlyList<Language> Available { get; private set; } = Array.Empty<Language>();
    public static event Action? Changed;

    public static void Init(string preference)
    {
        Tables.Clear();
        LoadEmbedded();
        LoadFolder(Path.Combine(AppContext.BaseDirectory, "locales"));
        LoadFolder(AppPaths.UserLocales);
        Available = Tables
            .Select(kv => new Language(kv.Key, Meta(kv.Value, "_name", kv.Key), Meta(kv.Value, "_englishName", kv.Key)))
            .OrderBy(l => l.EnglishName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _fallback = Tables.TryGetValue("en", out var en) ? en : new();
        Use(preference);
    }

    public static void Use(string preference)
    {
        var code = Resolve(preference);
        Code = code;
        _current = Tables.TryGetValue(code, out var t) ? t : _fallback;
        try { Culture = CultureInfo.GetCultureInfo(code); } catch { Culture = CultureInfo.InvariantCulture; }
        Changed?.Invoke();
    }

    /// <summary>"auto" → the Windows display language if we have it, else English.</summary>
    public static string Resolve(string preference)
    {
        if (!string.Equals(preference, "auto", StringComparison.OrdinalIgnoreCase) && Tables.ContainsKey(preference)) return preference.ToLowerInvariant();
        var ui = CultureInfo.CurrentUICulture;
        if (Tables.ContainsKey(ui.Name)) return ui.Name;
        if (Tables.ContainsKey(ui.TwoLetterISOLanguageName)) return ui.TwoLetterISOLanguageName;
        return "en";
    }

    public static string T(string key) =>
        _current.TryGetValue(key, out var v) ? v : _fallback.TryGetValue(key, out var f) ? f : key;

    public static string F(string key, params object[] args)
    {
        try { return string.Format(Culture, T(key), args); }
        catch (FormatException) { return T(key); }
    }

    /// <summary>Picks the "_one"/"_other" variant of a key for simple pluralisation.</summary>
    public static string N(string key, int n) => F(n == 1 ? key + "_one" : key + "_other", n);

    internal static IReadOnlyDictionary<string, string> Table(string code) => Tables.TryGetValue(code, out var t) ? t : new();

    // ───────────── loading ─────────────

    private static void LoadEmbedded()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith("locales.", StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal)) continue;
            var code = name["locales.".Length..^".json".Length];
            using var s = asm.GetManifestResourceStream(name);
            if (s != null) Merge(code, Parse(s, name));
        }
    }

    private static void LoadFolder(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                using var s = File.OpenRead(file);
                Merge(Path.GetFileNameWithoutExtension(file), Parse(s, file));
            }
            catch (Exception ex) { Log.Warn($"Skipping locale file {file}", ex); }
        }
    }

    private static Dictionary<string, string> Parse(Stream s, string source)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(s, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) map[p.Name] = p.Value.GetString() ?? "";
        }
        catch (Exception ex) { Log.Warn($"Invalid locale JSON in {source}", ex); }
        return map;
    }

    private static void Merge(string code, Dictionary<string, string> map)
    {
        code = code.ToLowerInvariant();
        if (!Tables.TryGetValue(code, out var t)) Tables[code] = t = new(StringComparer.Ordinal);
        foreach (var (k, v) in map) t[k] = v;
    }

    private static string Meta(Dictionary<string, string> t, string key, string fallback) =>
        t.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
}
