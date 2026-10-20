using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Jemur.App;
using Jemur.Core;
using Xunit;

namespace Jemur.Tests;

/// <summary>Staging against a fake server and a throwaway updates folder. Nothing here ever runs or swaps an EXE.</summary>
public sealed class UpdateInstallerTests : IDisposable
{
    private static readonly Uri ManifestUri = new("https://downloads.example.test/manifest.json");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jemur-updates-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _payload = RandomNumberGenerator.GetBytes(1_000_001);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch { }
    }

    [Fact]
    public void UpdatedArgument_SameVersion_ReportsFailure() =>
        // restarted for 0.4.0, but the old 0.3.0 is the one running: the install did not happen
        Assert.Equal(UpdateOutcome.Failed, UpdateInstaller.UpdatedOutcome("0.4.0", "0.3.0"));

    [Fact]
    public void UpdatedArgument_Newer_ReportsDone()
    {
        Assert.Equal(UpdateOutcome.Done, UpdateInstaller.UpdatedOutcome("0.4.0", "0.4.0"));
        Assert.Equal(UpdateOutcome.Done, UpdateInstaller.UpdatedOutcome("0.4.0", "0.4.1"));
        Assert.Equal(UpdateOutcome.None, UpdateInstaller.UpdatedOutcome(null, "0.4.0"));
        Assert.Equal(UpdateOutcome.None, UpdateInstaller.UpdatedOutcome("garbage", "0.4.0"));
    }

    [Fact]
    public async Task StageAsync_BadHash_LeavesNothingStaged()
    {
        using var installer = New(_payload);
        var ok = await installer.StageAsync(Manifest(new string('0', 64)), CancellationToken.None);

        Assert.False(ok);
        Assert.Equal(UpdateState.Failed, installer.State);
        Assert.Null(installer.ReadyVersion);
        Assert.Empty(Directory.Exists(_folder) ? Directory.GetFiles(_folder) : Array.Empty<string>());
    }

    [Fact]
    public async Task StageAsync_GoodFile_IsReady()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "stray.exe.partial"), "left over");
        using var installer = New(_payload);
        var changes = 0;
        installer.Changed += () => changes++;

        var ok = await installer.StageAsync(Manifest(Convert.ToHexString(SHA256.HashData(_payload))), CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(UpdateState.Ready, installer.State);
        Assert.Equal("0.4.0", installer.ReadyVersion);
        Assert.True(changes >= 2); // Downloading, then Ready
        var staged = Assert.Single(Directory.GetFiles(_folder));
        Assert.Equal("Jemur.exe", Path.GetFileName(staged));
        Assert.Equal(_payload, File.ReadAllBytes(staged));
    }

    [Fact]
    public async Task StageAsync_NameWithFolders_IsRefused()
    {
        using var installer = New(_payload);
        var m = Manifest(Convert.ToHexString(SHA256.HashData(_payload)), name: @"..\Jemur.exe");

        Assert.False(await installer.StageAsync(m, CancellationToken.None));
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void ApplyNow_WithoutAStagedUpdate_DoesNothing()
    {
        using var installer = New(_payload);
        Assert.False(installer.ApplyNow());
    }

    private UpdateInstaller New(byte[] body) => new(new HttpClient(new FakeHandler(body)), _folder, ManifestUri, installed: false, host: null);

    private UpdateManifest Manifest(string sha256, string name = "Jemur.exe")
    {
        var files = new[] { "x64", "arm64" }
            .Select(a => new UpdateFile(name, "portable", a, _payload.Length, sha256, new Uri("https://downloads.example.test/" + a + "/Jemur.exe")))
            .ToList();
        return new UpdateManifest("0.4.0", new string('a', 40), new Uri("https://github.com/revanapriyandi/jemur/releases/tag/v0.4.0"), files);
    }

    private sealed class FakeHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
