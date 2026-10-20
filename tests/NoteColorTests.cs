using System.IO;
using System.Text.Json;
using System.Windows.Media;
using Jemur.Windows;
using Xunit;

namespace Jemur.Tests;

public class NoteColorTests
{
    [Fact]
    public void NoteColors_HasTwelveAndKeepsTheFirstFive()
    {
        Assert.Equal(12, NoteColors.Count);
        string[] first = { "FBEB9A", "FAD4D0", "CFE5F5", "D6ECCC", "F3EEE4" };
        for (int i = 0; i < 5; i++)
        {
            var c = NoteColors.Paper(i);
            Assert.Equal(first[i], $"{c.R:X2}{c.G:X2}{c.B:X2}");
        }
        Assert.Equal(12, NoteColors.Keys.Length);
        Assert.Equal(12, Enumerable.Range(0, 12).Select(NoteColors.Paper).Distinct().Count());
    }

    [Fact]
    public void EveryColourKey_ExistsInEnglish()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Jemur.sln"))) d = d.Parent;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(d!.FullName, "locales", "en.json")));
        foreach (var k in NoteColors.Keys) Assert.True(doc.RootElement.TryGetProperty(k, out _), k);
    }
}
