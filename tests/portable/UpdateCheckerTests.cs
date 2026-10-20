using System.Net;
using System.Net.Http;
using System.Text;
using Jemur.Core;
using Xunit;

namespace Jemur.Tests;

public sealed class UpdateCheckerTests
{
    [Theory]
    [InlineData("0.1.3", "0.1.3", 0)]
    [InlineData("0.1.4", "0.1.3", 1)]
    [InlineData("0.1.3", "0.1.4", -1)]
    [InlineData("1.0.0", "0.9.9", 1)]
    [InlineData("0.1.3", "0.1.3-beta.1", 1)]
    [InlineData("0.1.3-beta.2", "0.1.3-beta.10", -1)]
    public void SemanticVersionsUseReleaseOrdering(string left, string right, int expected)
    {
        Assert.True(SemanticVersion.TryParse(left, out var a));
        Assert.True(SemanticVersion.TryParse(right, out var b));
        Assert.Equal(expected, Math.Sign(a.CompareTo(b)));
    }

    [Fact]
    public async Task ValidManifestReturnsUpdateAvailable()
    {
        var checker = CreateChecker("0.1.4", "0123456789012345678901234567890123456789");
        var result = await checker.CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateCheckKind.UpdateAvailable, result.Kind);
        Assert.Equal("0.1.4", result.Manifest?.Version);
        Assert.Equal("https://github.com/revanapriyandi/jemur/releases/tag/v0.1.4", result.Manifest?.ReleaseUrl.ToString());
    }

    [Fact]
    public async Task EqualOrOlderManifestIsUpToDate()
    {
        var equal = await CreateChecker("0.1.3", "0123456789012345678901234567890123456789").CheckAsync(CancellationToken.None);
        var older = await CreateChecker("0.1.2", "0123456789012345678901234567890123456789").CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateCheckKind.UpToDate, equal.Kind);
        Assert.Equal(UpdateCheckKind.UpToDate, older.Kind);
    }

    [Theory]
    [InlineData("{\"version\":\"bad\",\"commit\":\"0000000000000000000000000000000000000000\"}")]
    [InlineData("{\"version\":\"0.1.4\",\"commit\":\"short\"}")]
    [InlineData("null")]
    public async Task InvalidManifestReturnsFailure(string json)
    {
        var checker = CreateChecker(json);
        var result = await checker.CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateCheckKind.Failed, result.Kind);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExplicitReleaseUrlIsPreserved()
    {
        var checker = CreateChecker("0.1.4", "0123456789012345678901234567890123456789", "https://github.com/revanapriyandi/jemur/releases/tag/v0.1.4");
        var result = await checker.CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateCheckKind.UpdateAvailable, result.Kind);
        Assert.Equal("https://github.com/revanapriyandi/jemur/releases/tag/v0.1.4", result.Manifest?.ReleaseUrl.ToString());
    }

    [Fact]
    public async Task Parse_ManifestFiles()
    {
        var sha = new string('b', 64);
        var json = "{\"version\":\"0.4.0\",\"commit\":\"0123456789012345678901234567890123456789\",\"files\":[{\"name\":\"Jemur-0.4.0-win-x64.exe\",\"variant\":\"x\",\"kind\":\"portable\",\"architecture\":\"x64\",\"runtimeIncluded\":true,\"size\":5000000,\"sha256\":\"" + sha + "\",\"url\":\"https://updates.example.test/Jemur-0.4.0-win-x64.exe\"}]}";
        var checker = new UpdateChecker(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") })), "0.1.3", ManifestUri, RepoUrl);
        var result = await checker.CheckAsync(CancellationToken.None);
        var f = Assert.Single(result.Manifest!.Files);
        Assert.Equal(new UpdateFile("Jemur-0.4.0-win-x64.exe", "portable", "x64", 5000000, sha, new Uri("https://updates.example.test/Jemur-0.4.0-win-x64.exe")), f);
        var none = await CreateChecker("0.4.0").CheckAsync(CancellationToken.None);
        Assert.Empty(none.Manifest!.Files);
    }

    [Fact]
    public async Task HttpFailureAndCancellationReturnFailureWithoutThrowing()
    {
        var failure = new UpdateChecker(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))), "0.1.3", ManifestUri, RepoUrl);
        Assert.Equal(UpdateCheckKind.Failed, (await failure.CheckAsync(CancellationToken.None)).Kind);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var result = await CreateChecker("0.1.4", "0123456789012345678901234567890123456789").CheckAsync(cancelled.Token);
        Assert.Equal(UpdateCheckKind.Failed, result.Kind);
    }

    private static readonly Uri ManifestUri = new("https://updates.example.test/manifest.json");
    private const string RepoUrl = "https://github.com/revanapriyandi/jemur";

    private static UpdateChecker CreateChecker(string version, string commit = "0123456789012345678901234567890123456789", string? releaseUrl = null) =>
        new(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"version\":\"{version}\",\"commit\":\"{commit}\"{(releaseUrl == null ? "" : $",\"releaseUrl\":\"{releaseUrl}\"")}}}", Encoding.UTF8, "application/json"),
        })), "0.1.3", ManifestUri, RepoUrl);

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
