using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SnagitBrowser.Core;

/// <summary>Die Datei wurde seit dem Lesen von einem anderen Programm (z. B. Snagit) geändert.</summary>
public sealed class FileChangedException(string path)
    : IOException($"Die Datei wurde inzwischen von einem anderen Programm geändert (z. B. Snagit): {Path.GetFileName(path)}");

/// <summary>Stand einer Datei beim Lesen – für die Prüfung, ob sie inzwischen geändert wurde.</summary>
public readonly record struct FileStamp(long Size, long MtimeTicks)
{
    public static FileStamp Of(string path)
    {
        var f = new FileInfo(path);
        return new FileStamp(f.Length, f.LastWriteTimeUtc.Ticks);
    }
}

/// <summary>
/// Die einzige Stelle, die .snagx-Dateien im Snagit-Ordner verändert. Ablauf beim Ändern der Tags:
/// <list type="number">
/// <item>Prüfen, dass die Datei seit dem Lesen unverändert ist (sonst Abbruch, nichts wird geschrieben).</item>
/// <item>Sicherungskopie nach %LOCALAPPDATA%\SnagitBrowser\Backups (per Hash geprüft).</item>
/// <item>Neue Fassung in einer temporären Datei erzeugen: nur <c>metadata.json</c> wird ersetzt,
///       und darin nur die Zeile mit <c>"Tags"</c>. Alle Bilder und Annotationen bleiben Byte für Byte gleich.</item>
/// <item>Neue Fassung prüfen (alle anderen Einträge identisch, Tags korrekt lesbar).</item>
/// <item>Original atomar durch die neue Fassung ersetzen (File.Replace) – bei einem Absturz bleibt
///       entweder die alte oder die neue Datei vollständig erhalten.</item>
/// </list>
/// </summary>
public static class SnagxWriter
{
    public static string DefaultBackupDir => Path.Combine(LibraryIndex.DefaultDataDir, "Backups");

    // jsoncpp-Format von Snagit: "Tags" : [ "a", "b" ]   bzw.   "Tags" : []
    private static readonly Regex TagsRegex = new("\"Tags\"\\s*:\\s*\\[[^\\]]*\\]", RegexOptions.CultureInvariant);

    /// <summary>Bereinigt eine Tag-Liste: trimmen, leere entfernen, Duplikate (ohne Groß/Klein) entfernen.</summary>
    public static List<string> NormalizeTags(IEnumerable<string> tags)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var t in tags)
        {
            var s = (t ?? "").Trim();
            if (s.Length > 0 && seen.Add(s)) result.Add(s);
        }
        return result;
    }

    /// <summary>
    /// Ersetzt die Tags einer .snagx-Datei. <paramref name="expected"/> ist der Dateistand beim Lesen;
    /// weicht er ab, wird nichts geschrieben (<see cref="FileChangedException"/>).
    /// Gibt den Pfad der Sicherungskopie zurück.
    /// </summary>
    public static string SetTags(string path, IEnumerable<string> tags, FileStamp expected, string? backupDir = null)
    {
        path = Path.GetFullPath(path);
        var newTags = NormalizeTags(tags);
        if (FileStamp.Of(path) != expected) throw new FileChangedException(path);

        byte[] original = ReadAllShared(path);
        var backup = CreateBackup(path, original, backupDir ?? DefaultBackupDir);

        byte[] updated = BuildUpdatedArchive(original, newTags);
        Verify(original, updated, newTags);

        // Temporäre Datei im selben Ordner (gleiches Laufwerk → File.Replace ist atomar)
        var dir = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(dir, $"~{Path.GetFileName(path)}.{Guid.NewGuid():N}.sbtmp");
        try
        {
            File.WriteAllBytes(temp, updated);
            File.SetAttributes(temp, FileAttributes.Hidden | FileAttributes.Temporary);
            if (!SHA256.HashData(File.ReadAllBytes(temp)).AsSpan().SequenceEqual(SHA256.HashData(updated)))
                throw new IOException("Temporäre Datei wurde nicht korrekt geschrieben.");

            // Unmittelbar vor dem Austausch noch einmal prüfen
            if (FileStamp.Of(path) != expected) throw new FileChangedException(path);
            File.SetAttributes(temp, FileAttributes.Normal);
            File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }
        return backup;
    }

    /// <summary>Erzeugt aus dem Original-ZIP ein neues ZIP, in dem nur die Tags in metadata.json ersetzt sind.</summary>
    public static byte[] BuildUpdatedArchive(byte[] original, IReadOnlyList<string> tags)
    {
        var ms = new MemoryStream();
        ms.Write(original);
        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("metadata.json") ?? throw new InvalidDataException("metadata.json fehlt.");
            string json;
            using (var r = new StreamReader(entry.Open(), new UTF8Encoding(false))) json = r.ReadToEnd();

            var newJson = ReplaceTags(json, tags);
            var stamp = entry.LastWriteTime;
            // Nur dieser Eintrag wird neu geschrieben; alle anderen behalten ihre komprimierten Originaldaten.
            using (var s = entry.Open())
            {
                s.SetLength(0);
                var bytes = new UTF8Encoding(false).GetBytes(newJson);
                s.Write(bytes);
            }
            entry.LastWriteTime = DateTimeOffset.Now > stamp ? DateTimeOffset.Now : stamp;
        }
        return ms.ToArray();
    }

    /// <summary>Ersetzt im metadata.json-Text nur den Tags-Eintrag, im gleichen Format wie Snagit.</summary>
    public static string ReplaceTags(string json, IReadOnlyList<string> tags)
    {
        var matches = TagsRegex.Matches(json);
        if (matches.Count != 1)
            throw new InvalidDataException("Unbekanntes Format von metadata.json – Tags wurden nicht geändert.");
        var array = tags.Count == 0 ? "[]" : "[ " + string.Join(", ", tags.Select(JsonString)) + " ]";
        return json[..matches[0].Index] + "\"Tags\" : " + array + json[(matches[0].Index + matches[0].Length)..];
    }

    /// <summary>JSON-String wie jsoncpp: Umlaute bleiben UTF-8, nur Steuerzeichen/Quote/Backslash werden escaped.</summary>
    private static string JsonString(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (ch < 0x20) sb.Append($"\\u{(int)ch:x4}"); else sb.Append(ch);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>Prüft die neue Fassung: gleiche Einträge, alle außer metadata.json inhaltsgleich, Tags korrekt.</summary>
    private static void Verify(byte[] original, byte[] updated, IReadOnlyList<string> tags)
    {
        using var a = new ZipArchive(new MemoryStream(original), ZipArchiveMode.Read);
        using var b = new ZipArchive(new MemoryStream(updated), ZipArchiveMode.Read);
        var namesA = a.Entries.Select(e => e.FullName).ToList();
        var namesB = b.Entries.Select(e => e.FullName).ToList();
        if (!namesA.SequenceEqual(namesB))
            throw new InvalidDataException("Prüfung fehlgeschlagen: Einträge der Datei weichen ab.");

        foreach (var ea in a.Entries)
        {
            var eb = b.GetEntry(ea.FullName)!;
            if (ea.FullName == "metadata.json") continue;
            if (ea.Length != eb.Length || ea.Crc32 != eb.Crc32 || !SHA256.HashData(ReadEntry(ea)).AsSpan().SequenceEqual(SHA256.HashData(ReadEntry(eb))))
                throw new InvalidDataException($"Prüfung fehlgeschlagen: {ea.FullName} wurde verändert.");
        }

        // metadata.json: gültiges JSON, Tags wie gewünscht, alle anderen Felder unverändert
        var oldMeta = JsonDocument.Parse(ReadEntry(a.GetEntry("metadata.json")!)).RootElement;
        var newMeta = JsonDocument.Parse(ReadEntry(b.GetEntry("metadata.json")!)).RootElement;
        var newTags = newMeta.GetProperty("Tags").EnumerateArray().Select(t => t.GetString() ?? "").ToList();
        if (!newTags.SequenceEqual(tags))
            throw new InvalidDataException("Prüfung fehlgeschlagen: Tags wurden nicht korrekt gespeichert.");
        foreach (var p in oldMeta.EnumerateObject())
        {
            if (p.Name == "Tags") continue;
            if (!newMeta.TryGetProperty(p.Name, out var v) || v.GetRawText() != p.Value.GetRawText())
                throw new InvalidDataException($"Prüfung fehlgeschlagen: Feld {p.Name} wurde verändert.");
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] ReadAllShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Sichert die Originaldatei (Inhalt bereits im Speicher) und prüft die Kopie per Hash.</summary>
    private static string CreateBackup(string path, byte[] content, string backupDir)
    {
        var dayDir = Path.Combine(backupDir, DateTime.Now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dayDir);
        var target = Exporter.UniquePath(Path.Combine(dayDir,
            $"{DateTime.Now:HHmmss}_{Path.GetFileName(path)}"));
        File.WriteAllBytes(target, content);
        if (!SHA256.HashData(File.ReadAllBytes(target)).AsSpan().SequenceEqual(SHA256.HashData(content)))
            throw new IOException("Sicherungskopie konnte nicht korrekt geschrieben werden – Änderung abgebrochen.");
        return target;
    }

    /// <summary>Löscht Sicherungskopien, die älter als <paramref name="days"/> Tage sind.</summary>
    public static void CleanupBackups(int days = 30, string? backupDir = null)
    {
        var dir = backupDir ?? DefaultBackupDir;
        if (!Directory.Exists(dir)) return;
        foreach (var d in Directory.EnumerateDirectories(dir))
        {
            if (DateTime.TryParseExact(Path.GetFileName(d), "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out var day) && day < DateTime.Today.AddDays(-days))
            {
                try { Directory.Delete(d, recursive: true); } catch { }
            }
        }
    }
}
