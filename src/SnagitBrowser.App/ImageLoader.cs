using System.IO;
using System.Windows.Media.Imaging;
using SnagitBrowser.Core;

namespace SnagitBrowser.App;

/// <summary>
/// Lädt Bilder aus .snagx in der passenden Pixelgröße und hält sie in einem RAM-Cache (LRU).
/// Es wird nichts auf die Festplatte geschrieben.
/// </summary>
public static class ImageLoader
{
    /// <summary>Snagit speichert das Bild mit Annotationen (thumbnail.png) mit höchstens 1024 px.</summary>
    public const int SnagitThumbnailLimit = 1024;

    private static readonly int[] Buckets = [256, 384, 512, 768, 1024, 1536, 2048, 3072, 4096];
    private static readonly SemaphoreSlim Gate = new(Math.Clamp(Environment.ProcessorCount / 2, 2, 6));
    private static readonly LruCache Cache = new(400L * 1024 * 1024);

    /// <summary>Kleinste Stufe, die mindestens die benötigten Pixel liefert.</summary>
    public static int BucketFor(double physicalPixels)
    {
        foreach (var b in Buckets) if (b >= physicalPixels) return b;
        return Buckets[^1];
    }

    public static string Key(CaptureSummary c, int bucket) => $"{c.Path}|{c.Size}|{bucket}";

    public static BitmapSource? TryGet(string key) => Cache.Get(key);

    /// <summary>Dekodiert so, dass die lange Seite höchstens maxLongSide Pixel hat (nie hochskaliert).</summary>
    public static BitmapSource Decode(byte[] bytes, int maxLongSide = 0)
    {
        int decodeW = 0, decodeH = 0;
        if (maxLongSide > 0)
        {
            using var probe = new MemoryStream(bytes);
            var frame = BitmapFrame.Create(probe, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            int w = frame.PixelWidth, h = frame.PixelHeight;
            if (Math.Max(w, h) > maxLongSide)
            {
                if (w >= h) decodeW = maxLongSide; else decodeH = maxLongSide;
            }
        }

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (decodeW > 0) bmp.DecodePixelWidth = decodeW;
        if (decodeH > 0) bmp.DecodePixelHeight = decodeH;
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Vorschaubild für eine Kachel. Über 1024 px wird bei Captures ohne Annotationen das
    /// schärfere Original verwendet, sonst das von Snagit gerenderte Bild.
    /// </summary>
    public static async Task<BitmapSource?> LoadAsync(CaptureSummary c, int bucket)
    {
        var key = Key(c, bucket);
        if (Cache.Get(key) is { } hit) return hit;

        await Gate.WaitAsync();
        try
        {
            if (Cache.Get(key) is { } hit2) return hit2;
            var bmp = await Task.Run(() =>
            {
                byte[]? bytes = null;
                if (bucket > SnagitThumbnailLimit && c.ObjectCount == 0)
                    bytes = SnagxReader.ReadOriginalImage(c.Path);
                bytes ??= SnagxReader.ReadRenderedImage(c.Path);
                return bytes == null ? null : Decode(bytes, bucket);
            });
            if (bmp != null) Cache.Add(key, bmp);
            return bmp;
        }
        catch { return null; }
        finally { Gate.Release(); }
    }

    /// <summary>Entfernt den Vorschau-Cache früherer Versionen (lag in %LOCALAPPDATA%\SnagitBrowser\thumbs).</summary>
    public static void DeleteLegacyDiskCache(string sourceRoot)
    {
        try
        {
            var dir = Path.Combine(LibraryIndex.DefaultDataDir, "thumbs");
            if (!Directory.Exists(dir)) return;
            ReadOnlyGuard.EnsureOutside(dir, sourceRoot);
            Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    private sealed class LruCache(long maxBytes)
    {
        private readonly Dictionary<string, LinkedListNode<(string Key, BitmapSource Bmp, long Bytes)>> _map = new();
        private readonly LinkedList<(string Key, BitmapSource Bmp, long Bytes)> _list = new();
        private long _bytes;

        public BitmapSource? Get(string key)
        {
            lock (_map)
            {
                if (!_map.TryGetValue(key, out var node)) return null;
                _list.Remove(node);
                _list.AddFirst(node);
                return node.Value.Bmp;
            }
        }

        public void Add(string key, BitmapSource bmp)
        {
            long size = (long)bmp.PixelWidth * bmp.PixelHeight * 4;
            lock (_map)
            {
                if (_map.ContainsKey(key)) return;
                _map[key] = _list.AddFirst((key, bmp, size));
                _bytes += size;
                while (_bytes > maxBytes && _list.Count > 1)
                {
                    var last = _list.Last!;
                    _list.RemoveLast();
                    _map.Remove(last.Value.Key);
                    _bytes -= last.Value.Bytes;
                }
            }
        }
    }
}
