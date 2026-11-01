using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SnagitBrowser.Tests;

/// <summary>
/// Erzeugt beim ersten Zugriff eine kleine Snagit-Bibliothek mit erfundenen Inhalten in einem Temp-Ordner.
/// Dadurch laufen die Tests ohne private Testdateien. Die Fälle entsprechen dem, was die Tests prüfen
/// (Callouts, Umlaute, mehrzeiliges RTF, Tags, Datum aus Dateiname, Datei ohne Objekte usw.).
/// </summary>
internal static class TestLibrary
{
    private static readonly Lazy<string> Dir = new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Ordner mit den erzeugten .snagx-Dateien (wird nur gelesen; Schreibtests arbeiten auf Kopien).</summary>
    public static string Directory => Dir.Value;

    private static string Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SnagitBrowserTests", "library-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        };

        // Mehrzeiliger Text mit Kursivschrift und Umlaut (\'fc = ü), wie ihn Snagit als RTF speichert
        const string birthdayRtf =
            "{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fswiss\\fprq2\\fcharset0 Arial;}}\r\n" +
            "{\\colortbl ;\\red255\\green255\\blue255;}\r\n" +
            "\\viewkind4\\uc1\\pard\\qc\\cf1\\lang1031\\f0\\fs56 Die \"Legende\" feiert runden Geburtstag.\\par\r\n" +
            "\\line\\i Willkommen im \\line Club der 50er!\\i0\\par\r\n\\par\r\n" +
            "Hab Gl\\'fcck, Freude \\line und Erfolg im Leben.\\par\r\n\\fs52\\par\r\n}\r\n";

        new SnagxBuilder("06-07-2025_16-39-23")
        {
            AppName = "Microsoft Edge", AppCompany = "Microsoft Corporation", AppVersion = "137.0.3296.93",
            CaptureDateUtc = "2025-07-06 14:39:23", WindowName = "Glückwunschkarte – Persönlich – Microsoft Edge",
            Tags = ["Geburtstag"], Width = 1200, Height = 900,
        }.Rtf("Text", birthdayRtf).Save(dir);

        new SnagxBuilder("24-06-2025_12-22-04")
        {
            AppName = "Microsoft Edge", AppCompany = "Microsoft Corporation", AppVersion = "137.0.3296.93",
            CaptureDateUtc = "2025-06-24 10:22:04", WindowName = "Mannschaftsfoto – Jürgen Müller und Käthe Großmann",
            WebUrl = "https://example.org/verein/fotos", Tags = ["FSV", "Personen", "Leute Musterstadt"],
            Width = 1600, Height = 1000,
        }.Text("Text", "Jürgen Müller").Text("Text", "Käthe Großmann").Object("Arrow").Save(dir);

        new SnagxBuilder("30-10-2026_13-53-44")
        {
            AppName = "NinjaTrader", AppCompany = "NinjaTrader", AppVersion = "8.1.8.4",
            CaptureDateUtc = "2026-10-30 12:53:44", WindowName = "Chart - CL DEC26",
            Tags = null, // nie getaggt: kein Tags-Eintrag in metadata.json
            Width = 670, Height = 833,
        }.Text("Callout", "TE Buy").Text("Callout", "10 Tick SL").Save(dir);

        new SnagxBuilder("30-10-2026_11-54-50")
        {
            AppName = "NinjaTrader", AppCompany = "NinjaTrader", AppVersion = "8.1.8.4",
            CaptureDateUtc = "2026-10-30 10:54:50", WindowName = "Chart - ES DEC26",
            Tags = ["Trading"], Width = 1280, Height = 720,
        }.Text("Callout", "Breakout Long").Save(dir);

        new SnagxBuilder("30-10-2026_12-51-16")
        {
            AppName = "Microsoft Edge", AppCompany = "Microsoft Corporation", AppVersion = "141.0.3537.57",
            CaptureDateUtc = "2026-10-30 11:51:16", WindowName = "Wettervorhersage – Microsoft Edge",
            WebUrl = "https://example.org/wetter", Tags = [], Width = 1024, Height = 768,
        }.Save(dir);

        new SnagxBuilder("30-10-2026_14-03-06")
        {
            AppName = "Windows Explorer", AppCompany = "Microsoft Corporation", AppVersion = "10.0.26200",
            CaptureDateUtc = "2026-10-30 13:03:06", WindowName = "Dokumente",
            Tags = ["Backup", "Datensicherung"], Width = 900, Height = 600,
        }.Object("Arrow").Object("Arrow").Save(dir);

        new SnagxBuilder("30-10-2026_14-27-41")
        {
            AppName = "Editor", AppCompany = "Microsoft Corporation", AppVersion = "11.2508",
            CaptureDateUtc = "2026-10-30 13:27:41", WindowName = "Einkaufsliste.txt – Editor",
            Tags = ["Notizen"], Width = 800, Height = 600,
        }.Text("Text", "Einkaufsliste: Brot, Käse, Äpfel").Save(dir);

        // Ohne CaptureDate: Datum kommt aus dem Dateinamen
        new SnagxBuilder("30-10-2026_12-50-54")
        {
            AppName = "NinjaTrader", AppCompany = "NinjaTrader", AppVersion = "8.1.8.4",
            CaptureDateUtc = null, WindowName = "Chart - NQ DEC26", Tags = ["Trading"], Width = 1280, Height = 720,
        }.Object("Rectangle").Save(dir);

        new SnagxBuilder("31-08-2026_11-25-39")
        {
            AppName = "Microsoft Edge", AppCompany = "Microsoft Corporation", AppVersion = "139.0.3405.86",
            CaptureDateUtc = "2026-08-31 09:25:39", WindowName = "Reiseplanung – Microsoft Edge",
            WebUrl = "https://example.org/reise", Tags = ["Urlaub"], Width = 1920, Height = 1080,
        }.Text("Callout", "Hotel buchen").Save(dir);

        // Kein Datum im Namen, keine Objekte, fester Dateizeitpunkt
        var muster = new SnagxBuilder("Max Muster")
        {
            AppName = "Fotos", AppCompany = "Microsoft Corporation", AppVersion = "2025.11050",
            CaptureDateUtc = null, WindowName = "Max Muster.jpg – Fotos", Tags = ["M. Muster"], Width = 600, Height = 800,
        }.Save(dir);
        File.SetLastWriteTime(muster, new DateTime(2024, 3, 15, 10, 0, 0, DateTimeKind.Local));

        return dir;
    }
}

/// <summary>Baut eine .snagx-Datei im Format von Snagit (ZIP mit metadata.json, index.json, Seite, Bildern).</summary>
internal sealed class SnagxBuilder(string fileName)
{
    // Gültiges 1×1-PNG für Original und Vorschaubild
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public string AppName { get; init; } = "";
    public string AppCompany { get; init; } = "";
    public string AppVersion { get; init; } = "";
    /// <summary>Aufnahmezeit in UTC ("yyyy-MM-dd HH:mm:ss") oder null für „kein Eintrag“.</summary>
    public string? CaptureDateUtc { get; init; }
    public string WindowName { get; init; } = "";
    public string WebUrl { get; init; } = "";
    /// <summary>null = kein Tags-Eintrag (wie bei nie getaggten Aufnahmen), leer = <c>[]</c>.</summary>
    public string[]? Tags { get; init; }
    public int Width { get; init; } = 800;
    public int Height { get; init; } = 600;

    private readonly List<(string ToolMode, string? Rtf)> _objects = [];

    public SnagxBuilder Object(string toolMode) { _objects.Add((toolMode, null)); return this; }
    public SnagxBuilder Rtf(string toolMode, string rtf) { _objects.Add((toolMode, rtf)); return this; }
    public SnagxBuilder Text(string toolMode, string text) => Rtf(toolMode, ToRtf(text));

    /// <summary>Schreibt die Datei in den Ordner und gibt den Pfad zurück.</summary>
    public string Save(string dir)
    {
        var path = Path.Combine(dir, fileName + ".snagx");
        var guid = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Add(zip, guid + ".png", Png, CompressionLevel.NoCompression);
            Add(zip, guid + ".json", Encoding.UTF8.GetBytes(PageJson(guid)), CompressionLevel.Optimal);
            Add(zip, "thumbnail.png", Png, CompressionLevel.NoCompression);
            Add(zip, "index.json", Encoding.UTF8.GetBytes($"{{\n   \"Pages\" : [ \"{guid}.json\" ],\n   \"Version\" : \"1.0\"\n}}\n"), CompressionLevel.Optimal);
            Add(zip, "metadata.json", new UTF8Encoding(false).GetBytes(MetadataJson()), CompressionLevel.Optimal);
        }
        return path;
    }

    private static void Add(ZipArchive zip, string name, byte[] data, CompressionLevel level)
    {
        using var s = zip.CreateEntry(name, level).Open();
        s.Write(data);
    }

    /// <summary>metadata.json wie jsoncpp: 3 Leerzeichen Einrückung, "Key" : Wert, Schlüssel alphabetisch.</summary>
    private string MetadataJson()
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["AppCompany"] = Json(AppCompany),
            ["AppName"] = Json(AppName),
            ["AppVersion"] = Json(AppVersion),
            ["CaptureId"] = Json("{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}"),
            ["LanguageCode"] = Json("de-DE"),
            ["OperatingSystemVersion"] = Json("Microsoft Windows 11 Pro 25H2 (64-Bit Build 26200)"),
            ["Version"] = Json("1.0"),
            ["WebURL"] = Json(WebUrl),
            ["WindowName"] = Json(WindowName),
        };
        if (CaptureDateUtc != null) fields["CaptureDate"] = Json(CaptureDateUtc);
        if (Tags != null) fields["Tags"] = Tags.Length == 0 ? "[]" : "[ " + string.Join(", ", Tags.Select(Json)) + " ]";
        return "{\n" + string.Join(",\n", fields.Select(f => $"   \"{f.Key}\" : {f.Value}")) + "\n}\n";
    }

    private string PageJson(string guid)
    {
        var objects = _objects.Select(o => o.Rtf == null
            ? (object)new { ToolMode = o.ToolMode }
            : new { ToolMode = o.ToolMode, RTFEncodedText = Convert.ToBase64String(Encoding.Latin1.GetBytes(o.Rtf)) });
        return JsonSerializer.Serialize(new
        {
            CaptureBackgroundImage = guid + ".png",
            CaptureCanvasHeight = Height,
            CaptureCanvasWidth = Width,
            CaptureObjects = objects,
            SoftwareVersion = "2026.4.0.12714",
            Version = "1.0",
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>JSON-String wie jsoncpp: Umlaute bleiben UTF-8, nur Steuerzeichen/Anführungszeichen/Backslash escaped.</summary>
    private static string Json(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    /// <summary>Einfaches RTF wie von Snagit: Umlaute als \'xx (Codepage 1252), sonst \uN?.</summary>
    private static string ToRtf(string text)
    {
        var sb = new StringBuilder("{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fswiss\\fcharset0 Arial;}}\r\n\\viewkind4\\uc1\\pard\\f0\\fs32 ");
        foreach (var c in text)
        {
            if (c is '\\' or '{' or '}') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\par\r\n");
            else if (c < 0x80) sb.Append(c);
            else if (c <= 0xFF) sb.Append($"\\'{(int)c:x2}");
            else sb.Append($"\\u{(short)c}?");
        }
        return sb.Append("\\par\r\n}\r\n").ToString();
    }
}
