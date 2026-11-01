using SnagitBrowser.Core;

namespace SnagitBrowser.Tests;

public sealed class IndexTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "SnagitBrowserTests", Guid.NewGuid().ToString("N"));
    private readonly LibraryIndex _index;

    public IndexTests()
    {
        _index = new LibraryIndex(CoreTests.TestDataDir, _dataDir);
        var r = _index.Scan();
        Assert.Equal(0, r.Errors);
    }

    public void Dispose()
    {
        _index.Dispose();
        try { Directory.Delete(_dataDir, true); } catch { }
    }

    [Fact]
    public void ScanFindsAllFiles() =>
        Assert.Equal(Directory.GetFiles(CoreTests.TestDataDir, "*.snagx").Length, _index.Count);

    [Fact]
    public void SecondScanIsIncremental()
    {
        var r = _index.Scan();
        Assert.Equal(0, r.Added + r.Updated + r.Removed);
    }

    [Theory]
    [InlineData("Müller", "24-06-2025_12-22-04")]
    [InlineData("muller", "24-06-2025_12-22-04")]      // ohne Umlaut
    [InlineData("tick sl", "30-10-2026_13-53-44")]
    [InlineData("Breako", "30-10-2026_11-54-50")]      // Präfixsuche
    [InlineData("Musterstadt", "24-06-2025_12-22-04")] // Tag
    public void FullTextSearch(string query, string expectedFile)
    {
        var hits = _index.Search(new CaptureQuery { Text = query });
        Assert.Contains(hits, h => h.FileName == expectedFile);
    }

    [Fact]
    public void FilterByAppAndTag()
    {
        Assert.All(_index.Search(new CaptureQuery { App = "NinjaTrader" }), h => Assert.Equal("NinjaTrader", h.AppName));
        var tagged = _index.Search(new CaptureQuery { Tag = "M. Muster" });
        Assert.Equal("Max Muster", Assert.Single(tagged).FileName);
        Assert.Contains(_index.GetTags(), t => t.Name == "FSV");
        Assert.Contains(_index.GetApps(), a => a.Name == "NinjaTrader");
    }

    [Fact]
    public void FilterByDate()
    {
        var hits = _index.Search(new CaptureQuery { From = new DateTime(2026, 10, 30), To = new DateTime(2026, 10, 30) });
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal(new DateTime(2026, 10, 30), h.Date.Date));
    }

    [Fact]
    public void FilterByTagCount()
    {
        var none = _index.Search(new CaptureQuery { MaxTags = 0 });
        Assert.NotEmpty(none);
        Assert.All(none, h => Assert.Empty(h.Tags));

        var three = _index.Search(new CaptureQuery { MinTags = 3, MaxTags = 3 });
        Assert.Contains(three, h => h.FileName == "24-06-2025_12-22-04"); // FSV, Personen, Leute Musterstadt
        Assert.All(three, h => Assert.Equal(3, h.Tags.Count));

        var atLeastOne = _index.Search(new CaptureQuery { MinTags = 1 });
        Assert.Equal(_index.Count, none.Count + atLeastOne.Count);

        var dist = _index.GetTagCountDistribution();
        Assert.Equal(_index.Count, dist.Sum(d => d.Captures));
        Assert.Equal(none.Count, dist.Single(d => d.TagCount == 0).Captures);
    }

    [Fact]
    public void DayCountsMatchTotal()
    {
        var days = _index.GetDayCounts(new CaptureQuery());
        Assert.Equal(_index.Count, days.Sum(d => d.Count));
        Assert.True(days.First(d => d.Day == new DateTime(2026, 10, 30)).Count >= 6);
        // Filter wirken auf die Zählung, der Zeitraum nicht
        var ninja = _index.GetDayCounts(new CaptureQuery { App = "NinjaTrader", From = new DateTime(2000, 1, 1), To = new DateTime(2000, 1, 1) });
        Assert.Equal(_index.Search(new CaptureQuery { App = "NinjaTrader" }).Count, ninja.Sum(d => d.Count));
    }

    [Fact]
    public void ObjectCountIsStored()
    {
        var all = _index.Search(new CaptureQuery());
        Assert.Equal(2, all.Single(h => h.FileName == "30-10-2026_13-53-44").ObjectCount);
        Assert.Equal(0, all.Single(h => h.FileName == "Max Muster").ObjectCount);
    }

    [Fact]
    public void OldIndexWithoutObjectsColumnIsRebuilt()
    {
        var dir = Path.Combine(_dataDir, "old");
        string dbPath;
        using (var idx = new LibraryIndex(CoreTests.TestDataDir, dir)) { idx.Scan(); dbPath = idx.DatabasePath; }
        // Alte Version simulieren: Spalte entfernen
        using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "ALTER TABLE captures DROP COLUMN objects";
            cmd.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var migrated = new LibraryIndex(CoreTests.TestDataDir, dir);
        Assert.Equal(0, migrated.Count);
        var r = migrated.Scan();
        Assert.Equal(r.Total, r.Added);
        Assert.Equal(2, migrated.Search(new CaptureQuery { Text = "Tick" }).Single().ObjectCount);
    }

    [Fact]
    public void ExportWritesOutsideSourceOnly()
    {
        var target = Path.Combine(_dataDir, "export");
        var files = Directory.GetFiles(CoreTests.TestDataDir, "*.snagx").Take(2).ToList();
        Assert.Equal(2, Exporter.ExportImages(files, target, CoreTests.TestDataDir));
        Assert.Equal(2, Exporter.CopyFiles(files, target, CoreTests.TestDataDir));
        Assert.Equal(4, Directory.GetFiles(target).Length);
    }
}
