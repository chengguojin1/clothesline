using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clothesline.Core;

internal enum UpdateCheckKind { UpToDate, UpdateAvailable, Failed }

internal sealed record UpdateFile(string Name, string Kind, string Architecture, long Size, string Sha256, Uri Url);

internal sealed record UpdateManifest(string Version, string Commit, Uri ReleaseUrl, IReadOnlyList<UpdateFile> Files)
{
    public UpdateManifest(string version, string commit, Uri releaseUrl) : this(version, commit, releaseUrl, Array.Empty<UpdateFile>()) { }
}

internal sealed record UpdateCheckResult(UpdateCheckKind Kind, UpdateManifest? Manifest, string? Error)
{
    public static UpdateCheckResult UpToDate(UpdateManifest manifest) => new(UpdateCheckKind.UpToDate, manifest, null);
    public static UpdateCheckResult Available(UpdateManifest manifest) => new(UpdateCheckKind.UpdateAvailable, manifest, null);
    public static UpdateCheckResult Failed(string message) => new(UpdateCheckKind.Failed, null, message);
}

internal interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);
}

internal readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var pieces = text.Trim().Split('-', 2, StringSplitOptions.None);
        var numbers = pieces[0].Split('.', StringSplitOptions.None);
        if (numbers.Length != 3 || !int.TryParse(numbers[0], out var major) || !int.TryParse(numbers[1], out var minor) || !int.TryParse(numbers[2], out var patch)
            || major < 0 || minor < 0 || patch < 0) return false;
        var pre = pieces.Length == 2 ? pieces[1] : null;
        if (pre != null && (pre.Length == 0 || pre.Any(c => !(char.IsLetterOrDigit(c) || c == '.' || c == '-')))) return false;
        version = new SemanticVersion(major, minor, patch, pre);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        if (PreRelease == null) return other.PreRelease == null ? 0 : 1;
        if (other.PreRelease == null) return -1;
        var left = PreRelease.Split('.');
        var right = other.PreRelease.Split('.');
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            if (i >= left.Length) return -1;
            if (i >= right.Length) return 1;
            var l = left[i];
            var r = right[i];
            var ln = int.TryParse(l, out var li);
            var rn = int.TryParse(r, out var ri);
            if (ln && rn) { result = li.CompareTo(ri); }
            else if (ln) { result = -1; }
            else if (rn) { result = 1; }
            else { result = string.CompareOrdinal(l, r); }
            if (result != 0) return result;
        }
        return 0;
    }
}

internal sealed class UpdateChecker : IUpdateChecker
{
    private const int MaxManifestBytes = 32 * 1024;
    private static readonly Regex CommitPattern = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;
    private readonly string _localVersion;
    private readonly Uri _manifestUri;
    private readonly string _repositoryUrl;

    public UpdateChecker(HttpClient httpClient, string localVersion, Uri manifestUri, string repositoryUrl)
    {
        _http = httpClient;
        _localVersion = localVersion;
        _manifestUri = manifestUri;
        _repositoryUrl = repositoryUrl.TrimEnd('/');
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await _http.GetAsync(_manifestUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return UpdateCheckResult.Failed($"HTTP {(int)response.StatusCode}");
            var json = await ReadBoundedAsync(await response.Content.ReadAsStreamAsync(timeout.Token), timeout.Token);
            var dto = JsonSerializer.Deserialize<ManifestDto>(json, Json);
            if (dto?.Version == null || dto.Commit == null || !SemanticVersion.TryParse(dto.Version, out var remote) || !CommitPattern.IsMatch(dto.Commit))
                return UpdateCheckResult.Failed("Invalid update metadata");
            var release = string.IsNullOrWhiteSpace(dto.ReleaseUrl) ? $"{_repositoryUrl}/releases/tag/v{dto.Version}" : dto.ReleaseUrl;
            if (!Uri.TryCreate(release, UriKind.Absolute, out var releaseUri) || releaseUri.Scheme != Uri.UriSchemeHttps || !releaseUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                return UpdateCheckResult.Failed("Invalid release URL");
            var files = new List<UpdateFile>();
            foreach (var f in dto.Files ?? new())
            {
                if (f?.Name == null || f.Kind == null || f.Architecture == null || f.Sha256 == null
                    || !Uri.TryCreate(f.Url, UriKind.Absolute, out var fileUri)) continue;
                files.Add(new UpdateFile(f.Name, f.Kind, f.Architecture, f.Size, f.Sha256, fileUri));
            }
            var manifest = new UpdateManifest(dto.Version, dto.Commit, releaseUri, files);
            if (!SemanticVersion.TryParse(_localVersion, out var local)) return UpdateCheckResult.Failed("Invalid local version");
            return remote.CompareTo(local) > 0 ? UpdateCheckResult.Available(manifest) : UpdateCheckResult.UpToDate(manifest);
        }
        catch (Exception ex)
        {
            Log.Warn("Update check failed", ex);
            return UpdateCheckResult.Failed("Could not check for updates");
        }
    }

    private static async Task<string> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var ownedStream = stream;
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (memory.Length + read > MaxManifestBytes) throw new InvalidDataException("Manifest is too large");
            memory.Write(buffer, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }

    private sealed class ManifestDto
    {
        public string? Version { get; set; }
        public string? Commit { get; set; }
        public string? ReleaseUrl { get; set; }
        public List<FileDto?>? Files { get; set; }
    }

    private sealed class FileDto
    {
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public string? Architecture { get; set; }
        public long Size { get; set; }
        public string? Sha256 { get; set; }
        public string? Url { get; set; }
    }
}
