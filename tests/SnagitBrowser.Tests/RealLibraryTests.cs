using SnagitBrowser.Core;
using Xunit.Abstractions;

namespace SnagitBrowser.Tests;

/// <summary>
/// Optionaler Test gegen die echte Bibliothek (nur lesend).
/// Aktivieren: Umgebungsvariable SNAGIT_REAL_LIBRARY auf den Ordner setzen.
/// </summary>
public class RealLibraryTests(ITestOutputHelper output)
{
    [Fact]
    public void ScanRealLibrary()
    {
        var root = Environment.GetEnvironmentVariable("SNAGIT_REAL_LIBRARY");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

        var dataDir = Path.Combine(Path.GetTempPath(), "SnagitBrowserTests", "real");
        using var index = new LibraryIndex(root, dataDir);
        var r = index.Scan();
        output.WriteLine($"Total {r.Total}, added {r.Added}, updated {r.Updated}, removed {r.Removed}, errors {r.Errors}, {r.Duration.TotalSeconds:N1}s");
        output.WriteLine($"Apps: {index.GetApps().Count}, Tags: {index.GetTags().Count}, mit Text: {index.Search(new CaptureQuery { OnlyWithText = true }).Count}");
        foreach (var e in index.GetErrors().Take(20)) output.WriteLine("ERR " + e);
    }
}
