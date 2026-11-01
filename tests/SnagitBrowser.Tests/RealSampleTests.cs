using SnagitBrowser.Core;

namespace SnagitBrowser.Tests;

/// <summary>
/// Optionale Zusatztests mit echten Snagit-Dateien. Sie laufen nur, wenn ein Ordner „Testdaten“
/// oberhalb des Testprojekts liegt oder die Umgebungsvariable SNAGIT_TESTDATA auf einen Ordner zeigt.
/// Ohne echte Dateien enden sie sofort. Die echten Dateien gehören nicht ins Repository.
/// Geschrieben wird nur auf Kopien in einem Temp-Ordner.
/// </summary>
public sealed class RealSampleTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "SnagitBrowserTests", "real-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_temp, true); } catch { }
    }

    private static string? RealDataDir()
    {
        var env = Environment.GetEnvironmentVariable("SNAGIT_TESTDATA");
        if (!string.IsNullOrEmpty(env)) return Directory.Exists(env) ? env : null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Testdaten");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string[] RealFiles() =>
        RealDataDir() is { } dir ? Directory.GetFiles(dir, "*.snagx") : [];

    [Fact]
    public void RealFilesAreReadable()
    {
        foreach (var f in RealFiles())
        {
            var doc = SnagxReader.Read(f);
            Assert.NotEmpty(doc.Pages);
            Assert.False(string.IsNullOrEmpty(doc.Metadata.CaptureId), f);
            Assert.True(doc.HasThumbnail, f);
            Assert.NotNull(SnagxReader.ReadRenderedImage(f));
        }
    }

    [Fact]
    public void RealFilesAreIndexedWithoutErrors()
    {
        if (RealDataDir() is not { } dir) return;
        using var index = new LibraryIndex(dir, Path.Combine(_temp, "db"));
        var r = index.Scan();
        Assert.Equal(0, r.Errors);
        Assert.Equal(RealFiles().Length, index.Count);
    }

    [Fact]
    public void TagsCanBeWrittenToCopiesOfRealFiles()
    {
        var lib = Path.Combine(_temp, "lib");
        foreach (var f in RealFiles())
        {
            Directory.CreateDirectory(lib);
            var copy = Path.Combine(lib, Path.GetFileName(f));
            File.Copy(f, copy);
            var before = SnagxReader.Read(copy).Metadata;
            var tags = before.Tags.Append("SnagitBrowserTest").ToList();

            SnagxWriter.SetTags(copy, tags, FileStamp.Of(copy), Path.Combine(_temp, "backups"));

            var after = SnagxReader.Read(copy).Metadata;
            Assert.Equal(tags, after.Tags);
            Assert.Equal(before.CaptureId, after.CaptureId);
            Assert.Equal(before.WindowName, after.WindowName);
        }
    }
}
