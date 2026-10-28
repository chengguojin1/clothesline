using System.Security.Cryptography;
using Clothesline.Core;
using Xunit;

namespace Clothesline.Tests;

public sealed class UpdatePackageTests : IDisposable
{
    private static readonly Uri Manifest = new("https://jemur-downloads.example.test/manifest.json");
    private static readonly string Hash = new('a', 64);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jemur-up-" + Guid.NewGuid().ToString("N"));

    public UpdatePackageTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static UpdateFile F(string name, string kind, string arch, long size = 5_000_000, string? sha = null, string? url = null) =>
        new(name, kind, arch, size, sha ?? Hash, new Uri(url ?? "https://jemur-downloads.example.test/" + name));

    private static UpdateManifest M(params UpdateFile[] files) =>
        new("0.4.0", new string('0', 40), new Uri("https://github.com/revanapriyandi/jemur/releases/tag/v0.4.0"), files);

    private static UpdateManifest Real() => M(
        F("Clothesline-0.4.0-win-x64.exe", "portable", "x64"),
        F("Clothesline-0.4.0-win-arm64.exe", "portable", "arm64"),
        F("Clothesline-0.4.0-win-x64-setup.exe", "installer", "x64"),
        F("Clothesline-0.4.0-win-arm64-setup.exe", "installer", "arm64"));

    [Fact]
    public void Select_PicksInstallerOrPortableForArch()
    {
        Assert.Equal("Clothesline-0.4.0-win-x64-setup.exe", UpdatePackage.Select(Real(), Manifest, "x64", true)?.Name);
        Assert.Equal("Clothesline-0.4.0-win-arm64.exe", UpdatePackage.Select(Real(), Manifest, "arm64", false)?.Name);
    }

    [Fact]
    public void Select_RejectsForeignHostAndHttp()
    {
        Assert.Null(UpdatePackage.Select(M(F("a.exe", "portable", "x64", url: "https://evil.test/a.exe")), Manifest, "x64", false));
        Assert.Null(UpdatePackage.Select(M(F("a.exe", "portable", "x64", url: "http://jemur-downloads.example.test/a.exe")), Manifest, "x64", false));
    }

    [Theory]
    [InlineData(999_999L, "")]
    [InlineData(300_000_001L, "")]
    [InlineData(5_000_000L, "xyz")]
    [InlineData(5_000_000L, "abcd")]
    public void Select_RejectsBadSizeOrHash(long size, string sha)
    {
        var f = F("a.exe", "portable", "x64", size, sha == "" ? null : sha);
        Assert.Null(UpdatePackage.Select(M(f), Manifest, "x64", false));
    }

    [Fact]
    public void Select_ReturnsNullWhenNoFiles() => Assert.Null(UpdatePackage.Select(M(), Manifest, "x64", false));

    private string Write(string name, byte[] data) { var p = Path.Combine(_dir, name); File.WriteAllBytes(p, data); return p; }
    private static string Sha(byte[] d) => Convert.ToHexString(SHA256.HashData(d));

    [Fact]
    public async Task Verify_GoodFilePasses()
    {
        var d = new byte[1234]; new Random(1).NextBytes(d);
        var p = Write("ok.bin", d);
        Assert.True(await UpdatePackage.VerifyAsync(p, d.Length, Sha(d).ToLowerInvariant(), CancellationToken.None));
        Assert.True(await UpdatePackage.VerifyAsync(p, d.Length, Sha(d), CancellationToken.None));
    }

    [Fact]
    public async Task Verify_TruncatedOrWrongFile_LeavesNothingStaged()
    {
        var d = new byte[1234]; new Random(2).NextBytes(d);
        var p = Write("bad.bin", d[..1000]);
        Assert.False(await UpdatePackage.VerifyAsync(p, d.Length, Sha(d), CancellationToken.None));
        var q = Write("wrong.bin", new byte[1234]);
        Assert.False(await UpdatePackage.VerifyAsync(q, d.Length, Sha(d), CancellationToken.None));
        Assert.False(await UpdatePackage.VerifyAsync(Path.Combine(_dir, "none"), 1, Hash, CancellationToken.None));
    }

    [Fact]
    public void IsQuietMoment_RequiresNoOverlayAndTwoMinutes()
    {
        Assert.False(UpdatePackage.IsQuietMoment(false, new TimeSpan(0, 1, 59)));
        Assert.True(UpdatePackage.IsQuietMoment(false, new TimeSpan(0, 2, 0)));
        Assert.False(UpdatePackage.IsQuietMoment(true, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void HelperScript_QuotesPathsWithSpacesAndApostrophes()
    {
        var s = UpdatePackage.HelperScript(4242, @"C:\Temp\Clothesline Setup.exe", @"C:\Users\O'Neil\Clothesline App\Clothesline.exe", "0.4.0");
        Assert.Contains(@"'C:\Users\O''Neil\Clothesline App\Clothesline.exe'", s);
        Assert.Contains(@"'C:\Temp\Clothesline Setup.exe'", s);
        Assert.Contains("Wait-Process -Id 4242 -ErrorAction SilentlyContinue", s);
        Assert.Contains("'/S' -Wait", s);
        Assert.Contains("'--background','--updated','0.4.0'", s);
    }

    [Fact]
    public void PortableSwap_Succeeds()
    {
        var exe = Write("Clothesline.exe", [1, 2]); var n = Write("new.exe", [3, 4, 5]);
        Write("Clothesline.exe.old", [9]);
        Assert.True(UpdatePackage.PortableSwap(exe, n));
        Assert.Equal(new byte[] { 3, 4, 5 }, File.ReadAllBytes(exe));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(exe + ".old"));
    }

    [Fact]
    public void PortableSwap_FailedMoveRestoresOriginal()
    {
        var exe = Write("Clothesline.exe", [1, 2]);
        Assert.False(UpdatePackage.PortableSwap(exe, Path.Combine(_dir, "missing.exe")));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(exe));
    }

    [Fact]
    public void CleanupOld_DeletesLeftover()
    {
        var exe = Write("Clothesline.exe", [1]); var old = Write("Clothesline.exe.old", [2]);
        UpdatePackage.CleanupOld(exe);
        Assert.False(File.Exists(old));
        UpdatePackage.CleanupOld(exe);
    }

    [Fact]
    public void Settings_AutoUpdateDefaultsTrue()
    {
        Assert.True(new Settings().AutoUpdate);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"Theme\":\"dark\"}")!.AutoUpdate);
    }

    [Fact]
    public void HelperScript_DoublesTypographicQuotes()
    {
        var s = UpdatePackage.HelperScript(1, @"C:\a.exe", "C:\\Users\\O\u2019Neil\\Clothesline.exe", "0.4.0");
        Assert.Contains("'C:\\Users\\O\u2019\u2019Neil\\Clothesline.exe'", s);
    }

    [Fact]
    public void Select_RejectsUserinfoAndOtherPort()
    {
        Assert.Null(UpdatePackage.Select(M(F("a.exe", "portable", "x64", url: "https://jemur-downloads.example.test@evil.com/a.exe")), Manifest, "x64", false));
        Assert.Null(UpdatePackage.Select(M(F("a.exe", "portable", "x64", url: "https://jemur-downloads.example.test:8443/a.exe")), Manifest, "x64", false));
    }

    [Theory]
    [InlineData(1_000_000L, true)]
    [InlineData(300_000_000L, true)]
    [InlineData(999_999L, false)]
    [InlineData(300_000_001L, false)]
    public void Select_SizeBoundaries(long size, bool ok) =>
        Assert.Equal(ok, UpdatePackage.Select(M(F("a.exe", "portable", "x64", size)), Manifest, "x64", false) != null);

    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    public void Select_RejectsWrongHashLength(int len) =>
        Assert.Null(UpdatePackage.Select(M(F("a.exe", "portable", "x64", sha: new string('a', len))), Manifest, "x64", false));

    [Fact]
    public void Select_RejectsNonHexAndTrailingNewlineHash()
    {
        Assert.Null(UpdatePackage.Select(M(F("a.exe", "portable", "x64", sha: new string('g', 64))), Manifest, "x64", false));
        Assert.Null(UpdatePackage.Select(M(F("a.exe", "portable", "x64", sha: new string('a', 64) + "\n")), Manifest, "x64", false));
    }

    [Fact]
    public void Select_WrongKindOrArchIsNull()
    {
        var m = M(F("a.exe", "portable", "x64"));
        Assert.Null(UpdatePackage.Select(m, Manifest, "x64", true));
        Assert.Null(UpdatePackage.Select(m, Manifest, "arm64", false));
    }
}
