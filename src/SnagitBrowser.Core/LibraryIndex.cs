using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SnagitBrowser.Core;

public sealed class CaptureSummary
{
    public long Id { get; init; }
    public string Path { get; init; } = "";
    public string FileName => System.IO.Path.GetFileNameWithoutExtension(Path);
    public DateTime Date { get; init; }
    public string AppName { get; init; } = "";
    public string WindowName { get; init; } = "";
    public string WebUrl { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string Texts { get; init; } = "";
    public string CaptureId { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public long Size { get; init; }
    /// <summary>Anzahl Annotationen auf der ersten Seite (0 = Bild ohne Bearbeitung).</summary>
    public int ObjectCount { get; init; }
    public string? Error { get; init; }
}

public sealed class CaptureQuery
{
    public string? Text { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public string? App { get; init; }
    public string? Tag { get; init; }
    /// <summary>Nur Captures, die Texte enthalten.</summary>
    public bool OnlyWithText { get; init; }
    /// <summary>Mindestanzahl vergebener Tags (null = egal).</summary>
    public int? MinTags { get; init; }
    /// <summary>Höchstanzahl vergebener Tags (null = egal).</summary>
    public int? MaxTags { get; init; }
}

public readonly record struct ScanProgress(int Done, int Total, int Changed, string? Current);

public sealed record ScanResult(int Total, int Added, int Updated, int Removed, int Errors, TimeSpan Duration);

/// <summary>
/// Lokaler Such-Index (SQLite + FTS5). Liegt außerhalb des Quellordners in %LOCALAPPDATA%.
/// </summary>
public sealed class LibraryIndex : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _lock = new();
    public string SourceRoot { get; }
    public string DatabasePath { get; }

    public static string DefaultDataDir =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnagitBrowser");

    public LibraryIndex(string sourceRoot, string? dataDir = null)
    {
        SourceRoot = System.IO.Path.GetFullPath(sourceRoot);
        dataDir ??= DefaultDataDir;
        ReadOnlyGuard.EnsureOutside(dataDir, SourceRoot);
        Directory.CreateDirectory(dataDir);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SourceRoot.ToLowerInvariant())))[..12];
        DatabasePath = System.IO.Path.Combine(dataDir, $"index-{hash}.db");
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString());
        _db.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
        Exec("""
            CREATE TABLE IF NOT EXISTS captures(
                id INTEGER PRIMARY KEY,
                path TEXT NOT NULL UNIQUE COLLATE NOCASE,
                size INTEGER NOT NULL,
                mtime INTEGER NOT NULL,
                date INTEGER NOT NULL,
                app TEXT NOT NULL DEFAULT '',
                window TEXT NOT NULL DEFAULT '',
                url TEXT NOT NULL DEFAULT '',
                tags TEXT NOT NULL DEFAULT '',
                texts TEXT NOT NULL DEFAULT '',
                capture_id TEXT NOT NULL DEFAULT '',
                width INTEGER NOT NULL DEFAULT 0,
                height INTEGER NOT NULL DEFAULT 0,
                objects INTEGER NOT NULL DEFAULT -1,
                error TEXT);
            CREATE INDEX IF NOT EXISTS ix_captures_date ON captures(date);
            CREATE INDEX IF NOT EXISTS ix_captures_app ON captures(app);
            CREATE TABLE IF NOT EXISTS capture_tags(
                capture_id INTEGER NOT NULL REFERENCES captures(id) ON DELETE CASCADE,
                tag TEXT NOT NULL COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS ix_tags_tag ON capture_tags(tag);
            CREATE INDEX IF NOT EXISTS ix_tags_capture ON capture_tags(capture_id);
            CREATE VIRTUAL TABLE IF NOT EXISTS captures_fts USING fts5(
                texts, tags, window, app, url, filename,
                tokenize = 'unicode61 remove_diacritics 2');
            """);
        Migrate();
    }

    /// <summary>Ältere Index-Versionen ohne Spalte "objects": Spalte ergänzen und Index neu aufbauen.</summary>
    private void Migrate()
    {
        bool hasObjects = false;
        using (var c = _db.CreateCommand())
        {
            c.CommandText = "PRAGMA table_info(captures)";
            using var r = c.ExecuteReader();
            while (r.Read()) if (r.GetString(1) == "objects") hasObjects = true;
        }
        if (hasObjects) return;
        Exec("""
            ALTER TABLE captures ADD COLUMN objects INTEGER NOT NULL DEFAULT -1;
            DELETE FROM captures_fts; DELETE FROM capture_tags; DELETE FROM captures;
            """);
    }

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public int Count
    {
        get { lock (_lock) { using var c = _db.CreateCommand(); c.CommandText = "SELECT COUNT(*) FROM captures"; return Convert.ToInt32(c.ExecuteScalar()); } }
    }

    /// <summary>Gleicht den Index mit dem Quellordner ab (nur geänderte Dateien werden gelesen).</summary>
    public ScanResult Scan(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var files = Directory.EnumerateFiles(SourceRoot, "*.snagx", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
        }).Select(p => new FileInfo(p)).ToList();

        Dictionary<string, (long Id, long Size, long Mtime)> known = new(StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "SELECT id, path, size, mtime FROM captures";
            using var r = c.ExecuteReader();
            while (r.Read()) known[r.GetString(1)] = (r.GetInt64(0), r.GetInt64(2), r.GetInt64(3));
        }

        var onDisk = new HashSet<string>(files.Select(f => f.FullName), StringComparer.OrdinalIgnoreCase);
        var removed = known.Keys.Where(k => !onDisk.Contains(k)).ToList();
        var todo = files.Where(f => !known.TryGetValue(f.FullName, out var k) ||
                                    k.Size != f.Length || k.Mtime != f.LastWriteTimeUtc.Ticks).ToList();

        int added = 0, updated = 0, errors = 0, done = 0;
        int total = files.Count;
        int unchanged = total - todo.Count;
        progress?.Report(new ScanProgress(unchanged, total, 0, null));

        foreach (var chunk in todo.Chunk(200))
        {
            ct.ThrowIfCancellationRequested();
            var results = new ConcurrentBag<(FileInfo File, SnagxDocument? Doc, string? Error)>();
            Parallel.ForEach(chunk, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, f =>
            {
                try { results.Add((f, SnagxReader.Read(f.FullName), null)); }
                catch (Exception ex) { results.Add((f, null, ex.Message)); }
            });

            lock (_lock)
            {
                using var tx = _db.BeginTransaction();
                foreach (var (f, doc, err) in results)
                {
                    if (known.ContainsKey(f.FullName)) updated++; else added++;
                    if (err != null) errors++;
                    Upsert(tx, f, doc, err);
                }
                tx.Commit();
            }
            done += chunk.Length;
            progress?.Report(new ScanProgress(unchanged + done, total, done, chunk[^1].Name));
        }

        if (removed.Count > 0)
        {
            lock (_lock)
            {
                using var tx = _db.BeginTransaction();
                foreach (var p in removed) Delete(tx, known[p].Id);
                tx.Commit();
            }
        }

        return new ScanResult(total, added, updated, removed.Count, errors, sw.Elapsed);
    }

    /// <summary>Liest eine einzelne Datei neu ein (nach eigener Änderung) bzw. entfernt sie, wenn sie fehlt.</summary>
    public void RefreshFile(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        var f = new FileInfo(path);
        if (!f.Exists) { Remove(path); return; }
        SnagxDocument? doc = null; string? error = null;
        try { doc = SnagxReader.Read(path); } catch (Exception ex) { error = ex.Message; }
        lock (_lock)
        {
            using var tx = _db.BeginTransaction();
            Upsert(tx, f, doc, error);
            tx.Commit();
        }
    }

    /// <summary>Entfernt eine Datei aus dem Index (z. B. nach dem Löschen).</summary>
    public void Remove(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "SELECT id FROM captures WHERE path=$p";
            c.Parameters.AddWithValue("$p", path);
            if (c.ExecuteScalar() is not long id) return;
            using var tx = _db.BeginTransaction();
            Delete(tx, id);
            tx.Commit();
        }
    }

    private void Delete(SqliteTransaction tx, long id)
    {
        using var c = _db.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "DELETE FROM captures_fts WHERE rowid=$id; DELETE FROM capture_tags WHERE capture_id=$id; DELETE FROM captures WHERE id=$id;";
        c.Parameters.AddWithValue("$id", id);
        c.ExecuteNonQuery();
    }

    private void Upsert(SqliteTransaction tx, FileInfo f, SnagxDocument? doc, string? error)
    {
        var meta = doc?.Metadata ?? new SnagxMetadata();
        var date = doc?.EffectiveDate ?? SnagxReader.DateFromFileName(f.FullName) ?? f.LastWriteTime;
        var texts = doc == null ? "" : string.Join("\n\n", doc.Texts);
        var page = doc?.Pages.FirstOrDefault();

        using var c = _db.CreateCommand();
        c.Transaction = tx;
        c.CommandText = """
            INSERT INTO captures(path,size,mtime,date,app,window,url,tags,texts,capture_id,width,height,objects,error)
            VALUES($path,$size,$mtime,$date,$app,$window,$url,$tags,$texts,$cid,$w,$h,$obj,$err)
            ON CONFLICT(path) DO UPDATE SET size=excluded.size, mtime=excluded.mtime, date=excluded.date,
                app=excluded.app, window=excluded.window, url=excluded.url, tags=excluded.tags,
                texts=excluded.texts, capture_id=excluded.capture_id, width=excluded.width,
                height=excluded.height, objects=excluded.objects, error=excluded.error
            RETURNING id;
            """;
        c.Parameters.AddWithValue("$path", f.FullName);
        c.Parameters.AddWithValue("$size", f.Length);
        c.Parameters.AddWithValue("$mtime", f.LastWriteTimeUtc.Ticks);
        c.Parameters.AddWithValue("$date", date.Ticks);
        c.Parameters.AddWithValue("$app", meta.AppName);
        c.Parameters.AddWithValue("$window", meta.WindowName);
        c.Parameters.AddWithValue("$url", meta.WebUrl);
        c.Parameters.AddWithValue("$tags", string.Join("\n", meta.Tags));
        c.Parameters.AddWithValue("$texts", texts);
        c.Parameters.AddWithValue("$cid", meta.CaptureId);
        c.Parameters.AddWithValue("$w", page?.Width ?? 0);
        c.Parameters.AddWithValue("$h", page?.Height ?? 0);
        c.Parameters.AddWithValue("$obj", page?.Objects.Count ?? -1);
        c.Parameters.AddWithValue("$err", (object?)error ?? DBNull.Value);
        var id = Convert.ToInt64(c.ExecuteScalar());

        using var d = _db.CreateCommand();
        d.Transaction = tx;
        d.CommandText = "DELETE FROM captures_fts WHERE rowid=$id; DELETE FROM capture_tags WHERE capture_id=$id;";
        d.Parameters.AddWithValue("$id", id);
        d.ExecuteNonQuery();

        using var fts = _db.CreateCommand();
        fts.Transaction = tx;
        fts.CommandText = "INSERT INTO captures_fts(rowid,texts,tags,window,app,url,filename) VALUES($id,$t,$tags,$w,$a,$u,$f)";
        fts.Parameters.AddWithValue("$id", id);
        fts.Parameters.AddWithValue("$t", texts);
        fts.Parameters.AddWithValue("$tags", string.Join(" ", meta.Tags));
        fts.Parameters.AddWithValue("$w", meta.WindowName);
        fts.Parameters.AddWithValue("$a", meta.AppName);
        fts.Parameters.AddWithValue("$u", meta.WebUrl);
        fts.Parameters.AddWithValue("$f", System.IO.Path.GetFileNameWithoutExtension(f.Name));
        fts.ExecuteNonQuery();

        foreach (var tag in meta.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var t = _db.CreateCommand();
            t.Transaction = tx;
            t.CommandText = "INSERT INTO capture_tags(capture_id, tag) VALUES($id,$tag)";
            t.Parameters.AddWithValue("$id", id);
            t.Parameters.AddWithValue("$tag", tag);
            t.ExecuteNonQuery();
        }
    }

    /// <summary>Baut aus Benutzereingabe eine FTS5-Abfrage: jedes Wort als Präfix, alle UND-verknüpft.</summary>
    public static string? BuildFtsQuery(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var tokens = text.Split((char[])[' ', '\t', '\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Replace("\"", ""))
            .Where(t => t.Length > 0)
            .Select(t => $"\"{t}\"*");
        var q = string.Join(" AND ", tokens);
        return q.Length == 0 ? null : q;
    }

    public List<CaptureSummary> Search(CaptureQuery q)
    {
        using var c = _db.CreateCommand();
        var where = BuildWhere(c, q, includeDate: true);

        c.CommandText = "SELECT id,path,date,app,window,url,tags,texts,capture_id,width,height,size,objects,error FROM captures c" +
                        (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") +
                        " ORDER BY c.date DESC";
        return ReadSummaries(c);
    }

    /// <summary>Eintrag zu einer Datei (null, wenn nicht im Index).</summary>
    public CaptureSummary? GetByPath(string path)
    {
        using var c = _db.CreateCommand();
        c.CommandText = "SELECT id,path,date,app,window,url,tags,texts,capture_id,width,height,size,objects,error FROM captures c WHERE path=$p";
        c.Parameters.AddWithValue("$p", System.IO.Path.GetFullPath(path));
        return ReadSummaries(c).FirstOrDefault();
    }

    private List<CaptureSummary> ReadSummaries(SqliteCommand c)
    {
        var list = new List<CaptureSummary>();
        lock (_lock)
        {
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                list.Add(new CaptureSummary
                {
                    Id = r.GetInt64(0),
                    Path = r.GetString(1),
                    Date = new DateTime(r.GetInt64(2)),
                    AppName = r.GetString(3),
                    WindowName = r.GetString(4),
                    WebUrl = r.GetString(5),
                    Tags = r.GetString(6).Split('\n', StringSplitOptions.RemoveEmptyEntries),
                    Texts = r.GetString(7),
                    CaptureId = r.GetString(8),
                    Width = r.GetInt32(9),
                    Height = r.GetInt32(10),
                    Size = r.GetInt64(11),
                    ObjectCount = r.GetInt32(12),
                    Error = r.IsDBNull(13) ? null : r.GetString(13),
                });
            }
        }
        return list;
    }

    /// <summary>Anzahl Captures pro Kalendertag (Ortszeit), mit allen Filtern außer dem Zeitraum.</summary>
    public List<(DateTime Day, int Count)> GetDayCounts(CaptureQuery q)
    {
        using var c = _db.CreateCommand();
        var where = BuildWhere(c, q, includeDate: false);
        c.CommandText = $"SELECT c.date / {TimeSpan.TicksPerDay} AS d, COUNT(*) FROM captures c" +
                        (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") +
                        " GROUP BY d ORDER BY d DESC";
        var list = new List<(DateTime, int)>();
        lock (_lock)
        {
            using var r = c.ExecuteReader();
            while (r.Read()) list.Add((new DateTime(r.GetInt64(0) * TimeSpan.TicksPerDay), r.GetInt32(1)));
        }
        return list;
    }

    private static List<string> BuildWhere(SqliteCommand c, CaptureQuery q, bool includeDate)
    {
        var where = new List<string>();
        var fts = BuildFtsQuery(q.Text);
        if (fts != null)
        {
            where.Add("c.id IN (SELECT rowid FROM captures_fts WHERE captures_fts MATCH $fts)");
            c.Parameters.AddWithValue("$fts", fts);
        }
        if (includeDate && q.From is { } from) { where.Add("c.date >= $from"); c.Parameters.AddWithValue("$from", from.Date.Ticks); }
        if (includeDate && q.To is { } to) { where.Add("c.date < $to"); c.Parameters.AddWithValue("$to", to.Date.AddDays(1).Ticks); }
        if (!string.IsNullOrEmpty(q.App)) { where.Add("c.app = $app"); c.Parameters.AddWithValue("$app", q.App); }
        if (!string.IsNullOrEmpty(q.Tag))
        {
            where.Add("c.id IN (SELECT capture_id FROM capture_tags WHERE tag = $tag)");
            c.Parameters.AddWithValue("$tag", q.Tag);
        }
        if (q.OnlyWithText) where.Add("c.texts <> ''");
        const string tagCount = "(SELECT COUNT(*) FROM capture_tags t WHERE t.capture_id = c.id)";
        if (q.MinTags is { } min) { where.Add($"{tagCount} >= $minTags"); c.Parameters.AddWithValue("$minTags", min); }
        if (q.MaxTags is { } max) { where.Add($"{tagCount} <= $maxTags"); c.Parameters.AddWithValue("$maxTags", max); }
        return where;
    }

    public List<(string Name, int Count)> GetApps() =>
        QueryCounts("SELECT app, COUNT(*) FROM captures WHERE app <> '' GROUP BY app ORDER BY app COLLATE NOCASE");

    /// <summary>Wie viele Captures haben 0, 1, 2, … Tags.</summary>
    public List<(int TagCount, int Captures)> GetTagCountDistribution()
    {
        var list = new List<(int, int)>();
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = """
                SELECT n, COUNT(*) FROM (
                    SELECT (SELECT COUNT(*) FROM capture_tags t WHERE t.capture_id = c.id) AS n FROM captures c)
                GROUP BY n ORDER BY n
                """;
            using var r = c.ExecuteReader();
            while (r.Read()) list.Add((r.GetInt32(0), r.GetInt32(1)));
        }
        return list;
    }

    public List<(string Name, int Count)> GetTags() =>
        QueryCounts("SELECT tag, COUNT(*) FROM capture_tags GROUP BY tag ORDER BY tag COLLATE NOCASE");

    public List<string> GetErrors()
    {
        var list = new List<string>();
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "SELECT path, error FROM captures WHERE error IS NOT NULL ORDER BY path";
            using var r = c.ExecuteReader();
            while (r.Read()) list.Add($"{r.GetString(0)}: {r.GetString(1)}");
        }
        return list;
    }

    private List<(string, int)> QueryCounts(string sql)
    {
        var list = new List<(string, int)>();
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = sql;
            using var r = c.ExecuteReader();
            while (r.Read()) list.Add((r.GetString(0), r.GetInt32(1)));
        }
        return list;
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearPool(_db);
    }
}
