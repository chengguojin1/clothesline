using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Jemur.Core;

/// <summary>Pure rules for verified self-updates: what to download, whether it is intact, and how to swap it in.</summary>
internal static class UpdatePackage
{
    private const long MinSize = 1_000_000, MaxSize = 300_000_000;
    private static readonly Regex Hex64 = new(@"^[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant);

    public static UpdateFile? Select(UpdateManifest m, Uri manifestUri, string architecture, bool installed)
    {
        var kind = installed ? "installer" : "portable";
        foreach (var f in m.Files)
        {
            if (f.Kind != kind || f.Architecture != architecture) continue;
            if (f.Url.Scheme != Uri.UriSchemeHttps || !f.Url.Host.Equals(manifestUri.Host, StringComparison.OrdinalIgnoreCase)
                || f.Url.Port != manifestUri.Port || f.Url.UserInfo.Length > 0) continue;
            if (f.Size < MinSize || f.Size > MaxSize || !Hex64.IsMatch(f.Sha256)) continue;
            return f;
        }
        return null;
    }

    public static async Task<bool> VerifyAsync(string path, long size, string sha256, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            if (stream.Length != size) return false;
            var hash = await SHA256.HashDataAsync(stream, ct);
            return Convert.ToHexString(hash).Equals(sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsQuietMoment(bool overlayOpen, TimeSpan sinceLastInput) =>
        !overlayOpen && sinceLastInput >= TimeSpan.FromMinutes(2);

    public static string HelperScript(int pid, string setupPath, string exePath, string version) =>
        $"Wait-Process -Id {pid} -ErrorAction SilentlyContinue\n" +
        $"Start-Process -FilePath {Q(setupPath)} -ArgumentList '/S' -Wait\n" +
        $"Start-Process -FilePath {Q(exePath)} -ArgumentList '--background','--updated',{Q(version)}\n";

    private static string Q(string s) => "'" + Regex.Replace(s, "['‘’‚‛]", "$0$0") + "'";

    public static bool PortableSwap(string exePath, string newExePath)
    {
        var old = exePath + ".old";
        var moved = false;
        try
        {
            File.Delete(old);
            File.Move(exePath, old);
            moved = true;
            File.Move(newExePath, exePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (moved) { try { File.Move(old, exePath, true); } catch (Exception restore) { Log.Error("Could not restore " + exePath + " after a failed update swap", restore); } }
            return false;
        }
    }

    public static void CleanupOld(string exePath)
    {
        try { File.Delete(exePath + ".old"); } catch { }
    }
}
