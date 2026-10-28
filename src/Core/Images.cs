using System.IO;
using System.Windows.Media.Imaging;

namespace Clothesline.Core;

internal static class Images
{
    public static string NewShotPath()
    {
        Directory.CreateDirectory(AppPaths.Shots);
        var name = $"Clothesline {DateTime.Now:yyyy-MM-dd HH.mm.ss}";
        var p = Path.Combine(AppPaths.Shots, name + ".png");
        for (int i = 2; File.Exists(p); i++) p = Path.Combine(AppPaths.Shots, $"{name} ({i}).png");
        return p;
    }

    public static void SavePng(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp)) enc.Save(fs);
        File.Move(tmp, path, true);
    }

    /// <summary>
    /// Decodes an image without locking the file. Pass <paramref name="decodeWidth"/> to decode straight
    /// into a small bitmap — a thumbnail costs ~1/50th of the memory of the full screenshot.
    /// </summary>
    public static BitmapImage Load(string path, int decodeWidth = 0)
    {
        var bi = new BitmapImage();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        // No IgnoreImageCache: with a StreamSource there is no Uri to cache by, and WPF throws trying to evict a null key.
        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
        bi.StreamSource = fs;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    private static readonly Dictionary<string, WeakReference<BitmapSource>> Thumbs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A downscaled copy, shared between the line and the control panel. Weakly cached: as long as something
    /// shows it, it is decoded once; when nothing does, the GC can take it back.
    /// </summary>
    public static BitmapSource Thumbnail(string path, int width)
    {
        width = Math.Max(16, (width + 31) / 32 * 32); // bucket sizes so near-identical requests share a bitmap
        var key = $"{path}|{width}|{File.GetLastWriteTimeUtc(path).Ticks}";
        if (Thumbs.TryGetValue(key, out var weak) && weak.TryGetTarget(out var hit)) return hit;
        var (pw, _) = Size(path);
        var bmp = Load(path, pw > 0 ? Math.Min(pw, width) : width);
        Thumbs[key] = new WeakReference<BitmapSource>(bmp);
        if (Thumbs.Count > 256)
            foreach (var dead in Thumbs.Where(kv => !kv.Value.TryGetTarget(out _)).Select(kv => kv.Key).ToList()) Thumbs.Remove(dead);
        return bmp;
    }

    /// <summary>Pixel size without decoding: reads the PNG header directly, falls back to WIC for other formats.</summary>
    public static (int w, int h) Size(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> head = stackalloc byte[24];
            if (fs.Read(head) == 24 && head[0] == 0x89 && head[1] == (byte)'P' && head[2] == (byte)'N' && head[3] == (byte)'G')
                return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[16..]), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[20..]));
            fs.Position = 0;
            var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            return (dec.Frames[0].PixelWidth, dec.Frames[0].PixelHeight);
        }
        catch { return (0, 0); }
    }

    /// <summary>Writes a note to a temporary .txt so it can be dragged into other apps as a file.</summary>
    public static string NoteToTempFile(HangItem note)
    {
        Directory.CreateDirectory(AppPaths.Temp);
        var invalid = Path.GetInvalidFileNameChars();
        var first = new string((note.Text.Split('\n').FirstOrDefault() ?? "").Trim().Where(c => !invalid.Contains(c)).Take(48).ToArray()).Trim();
        if (first.Length == 0) first = "Note";
        var p = Path.Combine(AppPaths.Temp, first + ".txt");
        File.WriteAllText(p, note.Text);
        return p;
    }
}
