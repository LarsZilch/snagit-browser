using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SnagitBrowser.Core;

namespace SnagitBrowser.Tests;

/// <summary>Schreibtests – arbeiten ausschließlich auf Kopien der Testdaten in einem Temp-Ordner.</summary>
public sealed class WriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SnagitBrowserTests", Guid.NewGuid().ToString("N"));
    private readonly string _lib;
    private readonly string _backups;

    public WriterTests()
    {
        _lib = Path.Combine(_root, "lib");
        _backups = Path.Combine(_root, "backups");
        Directory.CreateDirectory(_lib);
        foreach (var f in Directory.GetFiles(CoreTests.TestDataDir, "*.snagx"))
            File.Copy(f, Path.Combine(_lib, Path.GetFileName(f)));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string AnyFile => Directory.GetFiles(_lib, "*.snagx").OrderBy(f => f).First();

    private static Dictionary<string, byte[]> Entries(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var s = e.Open(); using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
        });
    }

    [Fact]
    public void SetTagsChangesOnlyTags()
    {
        var path = AnyFile;
        var before = Entries(path);
        var original = File.ReadAllBytes(path);
        var oldMeta = SnagxReader.Read(path).Metadata;

        var backup = SnagxWriter.SetTags(path, ["Neu", "Größe \"Test\"", "Backup"], FileStamp.Of(path), _backups);

        var doc = SnagxReader.Read(path);
        Assert.Equal(["Neu", "Größe \"Test\"", "Backup"], doc.Metadata.Tags);
        Assert.Equal(oldMeta.CaptureId, doc.Metadata.CaptureId);
        Assert.Equal(oldMeta.WindowName, doc.Metadata.WindowName);
        Assert.Equal(oldMeta.CaptureDate, doc.Metadata.CaptureDate);

        var after = Entries(path);
        Assert.Equal(before.Keys, after.Keys);
        foreach (var (name, data) in before.Where(e => e.Key != "metadata.json"))
            Assert.True(data.AsSpan().SequenceEqual(after[name]), name);

        // Sicherung = Original, keine Temp-Dateien zurückgelassen
        Assert.True(SHA256.HashData(File.ReadAllBytes(backup)).AsSpan().SequenceEqual(SHA256.HashData(original)));
        Assert.Single(Directory.GetFiles(_lib), f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.GetFiles(_lib, "*.sbtmp"));
    }

    [Fact]
    public void ChangedFileIsNotOverwritten()
    {
        var path = AnyFile;
        var stamp = FileStamp.Of(path);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5)); // "Snagit hat gespeichert"
        var bytes = File.ReadAllBytes(path);

        Assert.Throws<FileChangedException>(() => SnagxWriter.SetTags(path, ["X"], stamp, _backups));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_lib, "*.sbtmp"));
    }

    [Fact]
    public void RemoveAllTagsWritesEmptyArray()
    {
        var path = AnyFile;
        SnagxWriter.SetTags(path, [], FileStamp.Of(path), _backups);
        Assert.Empty(SnagxReader.Read(path).Metadata.Tags);
        var json = Encoding.UTF8.GetString(Entries(path)["metadata.json"]);
        Assert.Contains("\"Tags\" : []", json);
    }

    [Fact]
    public void ReplaceTagsKeepsSnagitFormat()
    {
        const string json = "{\n   \"AppName\" : \"Edge\",\n   \"Tags\" : [ \"a\" ],\n   \"Version\" : \"1.0\"\n}";
        var result = SnagxWriter.ReplaceTags(json, ["Bäume", "b"]);
        Assert.Equal("{\n   \"AppName\" : \"Edge\",\n   \"Tags\" : [ \"Bäume\", \"b\" ],\n   \"Version\" : \"1.0\"\n}", result);
    }

    [Fact]
    public void NormalizeRemovesDuplicatesAndBlanks() =>
        Assert.Equal(["a", "B"], SnagxWriter.NormalizeTags([" a ", "", "B", "A", "b "]));

    [Fact]
    public void RepeatedEditsStayReadable()
    {
        var path = AnyFile;
        for (int i = 0; i < 5; i++)
            SnagxWriter.SetTags(path, [$"t{i}"], FileStamp.Of(path), _backups);
        Assert.Equal(["t4"], SnagxReader.Read(path).Metadata.Tags);
        Assert.NotNull(SnagxReader.ReadRenderedImage(path));
    }

    [Fact]
    public void IndexRefreshAndRemove()
    {
        using var index = new LibraryIndex(_lib, Path.Combine(_root, "db"));
        index.Scan();
        var path = AnyFile;
        SnagxWriter.SetTags(path, ["Zebra42"], FileStamp.Of(path), _backups);
        index.RefreshFile(path);
        Assert.Equal(["Zebra42"], index.GetByPath(path)!.Tags);
        Assert.Single(index.Search(new CaptureQuery { Tag = "Zebra42" }));

        File.Delete(path);
        index.RefreshFile(path);
        Assert.Null(index.GetByPath(path));
        Assert.Equal(0, index.Scan().Removed);
    }
}
