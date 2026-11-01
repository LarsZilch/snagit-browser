using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace SnagitBrowser.Core;

/// <summary>Liest .snagx-Dateien (ZIP-Archive). Öffnet Dateien ausschließlich lesend.</summary>
public static class SnagxReader
{
    public const string ThumbnailEntry = "thumbnail.png";

    private static FileStream OpenReadOnly(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);

    public static SnagxDocument Read(string path)
    {
        using var fs = OpenReadOnly(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

        var meta = ParseMetadata(zip.GetEntry("metadata.json"));
        var pageNames = ParseIndex(zip.GetEntry("index.json"));
        if (pageNames.Count == 0)
        {
            // Fallback: alle {GUID}.json außer Backups
            pageNames = zip.Entries
                .Select(e => e.FullName)
                .Where(n => n.StartsWith('{') && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                            && !n.Contains(".backup.", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var pages = new List<SnagxPage>();
        foreach (var name in pageNames)
        {
            var entry = zip.GetEntry(name);
            if (entry != null) pages.Add(ParsePage(entry));
        }

        return new SnagxDocument
        {
            FilePath = path,
            Metadata = meta,
            Pages = pages,
            HasThumbnail = zip.GetEntry(ThumbnailEntry) != null,
            EffectiveDate = meta.CaptureDate ?? DateFromFileName(path) ?? File.GetLastWriteTime(path),
        };
    }

    /// <summary>Liest einen Eintrag (z.B. thumbnail.png) als Bytes. Null, wenn nicht vorhanden.</summary>
    public static byte[]? ReadEntry(string path, string entryName)
    {
        using var fs = OpenReadOnly(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        var entry = zip.GetEntry(entryName);
        if (entry == null) return null;
        using var s = entry.Open();
        using var ms = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Bild mit Annotationen (thumbnail.png), sonst Originalbild der ersten Seite.</summary>
    public static byte[]? ReadRenderedImage(string path)
    {
        var bytes = ReadEntry(path, ThumbnailEntry);
        if (bytes != null) return bytes;
        return ReadOriginalImage(path);
    }

    public static byte[]? ReadOriginalImage(string path)
    {
        var doc = Read(path);
        var bg = doc.Pages.FirstOrDefault()?.BackgroundImage;
        return bg == null ? null : ReadEntry(path, bg);
    }

    private static JsonDocument? LoadJson(ZipArchiveEntry? entry)
    {
        if (entry == null) return null;
        using var s = entry.Open();
        try { return JsonDocument.Parse(s, new JsonDocumentOptions { AllowTrailingCommas = true }); }
        catch (JsonException) { return null; }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static SnagxMetadata ParseMetadata(ZipArchiveEntry? entry)
    {
        using var doc = LoadJson(entry);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return new SnagxMetadata();
        var r = doc.RootElement;

        DateTime? date = null;
        var ds = Str(r, "CaptureDate");
        if (DateTime.TryParseExact(ds, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
            date = d.ToLocalTime();

        var tags = new List<string>();
        if (r.TryGetProperty("Tags", out var t) && t.ValueKind == JsonValueKind.Array)
            foreach (var x in t.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                    tags.Add(x.GetString()!.Trim());

        return new SnagxMetadata
        {
            AppName = Str(r, "AppName"),
            AppCompany = Str(r, "AppCompany"),
            AppVersion = Str(r, "AppVersion"),
            CaptureDate = date,
            CaptureId = Str(r, "CaptureId"),
            LanguageCode = Str(r, "LanguageCode"),
            OperatingSystemVersion = Str(r, "OperatingSystemVersion"),
            Tags = tags,
            WebUrl = Str(r, "WebURL"),
            WindowName = Str(r, "WindowName"),
        };
    }

    private static List<string> ParseIndex(ZipArchiveEntry? entry)
    {
        using var doc = LoadJson(entry);
        var list = new List<string>();
        if (doc != null && doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("Pages", out var p) && p.ValueKind == JsonValueKind.Array)
            foreach (var x in p.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String) list.Add(x.GetString()!);
        return list;
    }

    private static SnagxPage ParsePage(ZipArchiveEntry entry)
    {
        using var doc = LoadJson(entry);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object)
            return new SnagxPage { EntryName = entry.FullName };
        var r = doc.RootElement;

        var objects = new List<CaptureObject>();
        if (r.TryGetProperty("CaptureObjects", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var o in arr.EnumerateArray())
            {
                if (o.ValueKind != JsonValueKind.Object) continue;
                string? rtf = null, plain = null;
                var b64 = Str(o, "RTFEncodedText");
                if (b64.Length > 0)
                {
                    try
                    {
                        rtf = RtfText.DecodeBase64Rtf(b64);
                        plain = RtfText.ToPlainText(rtf);
                    }
                    catch (FormatException) { }
                }
                objects.Add(new CaptureObject { ToolMode = Str(o, "ToolMode"), Rtf = rtf, PlainText = plain });
            }
        }

        var bg = Str(r, "CaptureBackgroundImage");
        return new SnagxPage
        {
            EntryName = entry.FullName,
            Width = Int(r, "CaptureCanvasWidth"),
            Height = Int(r, "CaptureCanvasHeight"),
            BackgroundImage = bg.Length > 0 ? bg : null,
            SoftwareVersion = Str(r, "SoftwareVersion"),
            Objects = objects,
        };
    }

    /// <summary>Snagit-Dateinamen haben das Format dd-MM-yyyy_HH-mm-ss (Ortszeit).</summary>
    public static DateTime? DateFromFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return DateTime.TryParseExact(name, "dd-MM-yyyy_HH-mm-ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var d) ? d : null;
    }
}
