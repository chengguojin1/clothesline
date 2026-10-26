using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Jemur.Core;

/// <summary>Finds a web address in a note and reads the page's own preview (Open Graph tags, else its title).</summary>
internal static partial class LinkPreview
{
    private const int MaxPage = 512 * 1024, MaxImage = 4 * 1024 * 1024, ImageWidth = 480;

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRx();

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaRx();

    [GeneratedRegex(@"([\w:-]+)\s*=\s*(?:""([^""]*)""|'([^']*)')")]
    private static partial Regex AttrRx();

    [GeneratedRegex(@"<title[^>]*>([\s\S]*?)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRx();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRx();

    /// <summary>The first http(s) address in the text, without trailing punctuation; null if there is none.</summary>
    public static string? FindUrl(string? text)
    {
        if (string.IsNullOrEmpty(text) || UrlRx().Match(text) is not { Success: true } m) return null;
        var url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}');
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? url : null;
    }

    /// <summary>The card a page describes, and where its picture is (absolute), from the page's HTML.</summary>
    public static (LinkCard Card, Uri? Image) Parse(string html, Uri page)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaRx().Matches(html))
        {
            string? key = null, content = null;
            foreach (Match a in AttrRx().Matches(tag.Value))
            {
                var value = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value;
                switch (a.Groups[1].Value.ToLowerInvariant())
                {
                    case "property" or "name": key = value; break;
                    case "content": content = value; break;
                }
            }
            if (key != null && content != null) meta.TryAdd(key, content);
        }
        string? First(params string[] keys) => keys.Select(k => meta.TryGetValue(k, out var v) ? Clean(v) : null).FirstOrDefault(v => !string.IsNullOrEmpty(v));

        var title = First("og:title", "twitter:title") ?? (TitleRx().Match(html) is { Success: true } t ? Clean(t.Groups[1].Value) : null);
        var image = First("og:image", "og:image:url", "twitter:image");
        Uri? imageUri = image != null && Uri.TryCreate(page, image, out var iu) && iu.Scheme is "http" or "https" ? iu : null;
        var card = new LinkCard
        {
            Url = page.ToString(),
            Title = Cap(string.IsNullOrEmpty(title) ? page.Host : title, 160),
            Description = First("og:description", "description", "twitter:description") is { } d ? Cap(d, 280) : null,
        };
        return (card, imageUri);
    }

    private static string Clean(string s) => SpaceRx().Replace(WebUtility.HtmlDecode(s), " ").Trim();

    private static string Cap(string s, int n) => s.Length <= n ? s : s[..(n - 1)].TrimEnd() + "…";

    /// <summary>
    /// Reads the page (its first 512 KB) and saves its picture, scaled down, next to the screenshots. A page that isn't
    /// HTML still gets a card with its host name, so it is not asked for again. Runs on the UI thread between awaits.
    /// </summary>
    public static async Task<LinkCard> FetchAsync(HttpClient http, string url, CancellationToken ct)
    {
        var page = new Uri(url);
        using var res = await http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead, ct);
        var final = res.RequestMessage?.RequestUri ?? page;
        if (!res.IsSuccessStatusCode || res.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml"))
            return new LinkCard { Url = url, Title = page.Host };

        var bytes = await ReadAtMost(res, MaxPage, ct);
        var charset = res.Content.Headers.ContentType?.CharSet?.Trim('"');
        Encoding enc;
        try { enc = charset != null ? Encoding.GetEncoding(charset) : Encoding.UTF8; } catch (ArgumentException) { enc = Encoding.UTF8; }
        var (card, image) = Parse(enc.GetString(bytes), final);
        card.Url = url;
        if (image != null)
        {
            try
            {
                using var img = await http.GetAsync(image, HttpCompletionOption.ResponseHeadersRead, ct);
                if (img.IsSuccessStatusCode && img.Content.Headers.ContentType?.MediaType?.StartsWith("image/") == true)
                {
                    var data = await ReadAtMost(img, MaxImage, ct);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = ImageWidth;
                    bmp.StreamSource = new MemoryStream(data);
                    bmp.EndInit();
                    bmp.Freeze();
                    var path = Images.NewShotPath();
                    Images.SavePng(bmp, path);
                    card.Image = path;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Log.Warn($"Link picture failed for {image}", ex); }
        }
        return card;
    }

    private static async Task<byte[]> ReadAtMost(HttpResponseMessage res, int max, CancellationToken ct)
    {
        await using var s = await res.Content.ReadAsStreamAsync(ct);
        var buf = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int n;
        while (buf.Length < max && (n = await s.ReadAsync(chunk, ct)) > 0) buf.Write(chunk, 0, (int)Math.Min(n, max - buf.Length));
        return buf.ToArray();
    }
}
