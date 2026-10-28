using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Clothesline.Tests;

/// <summary>Guards for translators: every language has every key, and every key the code asks for exists.</summary>
public class LocalizationTests
{
    private static readonly string Repo = FindRepo();

    private static string FindRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Clothesline.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }

    private static Dictionary<string, string> Load(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    public static IEnumerable<object[]> Locales() =>
        Directory.GetFiles(Path.Combine(FindRepo(), "locales"), "*.json").Select(f => new object[] { Path.GetFileName(f) });

    [Theory]
    [MemberData(nameof(Locales))]
    public void HasSameKeysAsEnglish(string file)
    {
        var en = Load(Path.Combine(Repo, "locales", "en.json"));
        var other = Load(Path.Combine(Repo, "locales", file));
        Assert.Empty(en.Keys.Except(other.Keys));
        Assert.Empty(other.Keys.Except(en.Keys));
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void KeepsPlaceholders(string file)
    {
        var en = Load(Path.Combine(Repo, "locales", "en.json"));
        var other = Load(Path.Combine(Repo, "locales", file));
        foreach (var (key, value) in en)
        {
            if (!other.TryGetValue(key, out var translated)) continue;
            var want = Regex.Matches(value, @"\{\d+\}").Select(m => m.Value).ToHashSet();
            var have = Regex.Matches(translated, @"\{\d+\}").Select(m => m.Value).ToHashSet();
            Assert.True(want.SetEquals(have), $"{file}: '{key}' must use the same {{n}} placeholders as English");
        }
    }

    [Fact]
    public void EveryKeyUsedInCodeExists()
    {
        var en = Load(Path.Combine(Repo, "locales", "en.json"));
        var used = new HashSet<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(Repo, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var code = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(code, @"L\.(T|F)\(""([\w.]*\w)"""))
                used.Add(m.Groups[2].Value);
            foreach (Match m in Regex.Matches(code, @"L\.N\(""([\w.]*\w)"""))
            {
                used.Add(m.Groups[1].Value + "_one");
                used.Add(m.Groups[1].Value + "_other");
            }
            foreach (Match m in Regex.Matches(code, @"""((?:spotlight|action|color|shortcut)\.\w+)"""))
                used.Add(m.Groups[1].Value);
        }
        foreach (var a in Clothesline.Core.Actions.All) used.Add("shortcut." + a);
        var missing = used.Where(k => !en.ContainsKey(k)).OrderBy(k => k).ToList();
        Assert.True(missing.Count == 0, "Missing in en.json: " + string.Join(", ", missing));
    }
}
