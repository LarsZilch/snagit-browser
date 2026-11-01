using SnagitBrowser.Core;

namespace SnagitBrowser.Tests;

public class CoreTests
{
    /// <summary>Erzeugte Testbibliothek mit erfundenen Inhalten (siehe <see cref="TestLibrary"/>).</summary>
    internal static string TestDataDir => TestLibrary.Directory;

    private static string File(string name) => Path.Combine(TestDataDir, name);

    [Fact]
    public void AllTestFilesAreReadable()
    {
        var files = Directory.GetFiles(TestDataDir, "*.snagx");
        Assert.NotEmpty(files);
        foreach (var f in files)
        {
            var doc = SnagxReader.Read(f);
            Assert.NotEmpty(doc.Pages);
            Assert.False(string.IsNullOrEmpty(doc.Metadata.CaptureId), f);
            Assert.True(doc.HasThumbnail, f);
        }
    }

    [Fact]
    public void ReadsCalloutTexts()
    {
        var doc = SnagxReader.Read(File("30-10-2026_13-53-44.snagx"));
        Assert.Equal("NinjaTrader", doc.Metadata.AppName);
        Assert.Equal("Chart - CL DEC26", doc.Metadata.WindowName);
        Assert.Equal(["TE Buy", "10 Tick SL"], doc.Texts.ToArray());
        Assert.All(doc.Pages[0].Objects, o => Assert.Equal("Callout", o.ToolMode));
        Assert.Equal(670, doc.Pages[0].Width);
    }

    [Fact]
    public void CaptureDateIsConvertedFromUtc()
    {
        // Datei 30-10-2026_13-53-44 (Ortszeit), CaptureDate in metadata = 12:53:44 UTC
        var doc = SnagxReader.Read(File("30-10-2026_13-53-44.snagx"));
        Assert.Equal(new DateTime(2026, 10, 30, 12, 53, 44, DateTimeKind.Utc).ToLocalTime(), doc.Metadata.CaptureDate);
    }

    [Fact]
    public void ReadsTagsAndUmlauts()
    {
        var doc = SnagxReader.Read(File("24-06-2025_12-22-04.snagx"));
        Assert.Equal(["FSV", "Personen", "Leute Musterstadt"], doc.Metadata.Tags);
        Assert.Contains("Jürgen Müller", doc.Texts);
        Assert.Contains("Käthe Großmann", doc.Texts);
        Assert.Contains("Müller", doc.Metadata.WindowName);
    }

    [Fact]
    public void RtfMultilineAndItalic()
    {
        var doc = SnagxReader.Read(File("06-07-2025_16-39-23.snagx"));
        var text = Assert.Single(doc.Texts);
        Assert.StartsWith("Die \"Legende\" feiert runden Geburtstag.", text);
        Assert.Contains("Willkommen im\nClub der 50er!", text);
        Assert.Contains("Hab Glück, Freude", text);
    }

    [Theory]
    [InlineData("{\\rtf1\\ansi{\\fonttbl{\\f0 Arial;}}\\f0 A\\u8364?B\\par}", "A€B")]
    [InlineData(@"{\rtf1\ansi Gr\'fc\'dfe\line Welt}", "Grüße\nWelt")]
    [InlineData(@"{\rtf1{\*\generator Riched20;}Hallo \{x\}}", "Hallo {x}")]
    public void RtfToPlainText(string rtf, string expected) =>
        Assert.Equal(expected, RtfText.ToPlainText(rtf));

    [Fact]
    public void ImagesCanBeRead()
    {
        var f = File("30-10-2026_13-53-44.snagx");
        var thumb = SnagxReader.ReadRenderedImage(f);
        var orig = SnagxReader.ReadOriginalImage(f);
        Assert.NotNull(thumb);
        Assert.NotNull(orig);
        Assert.Equal(0x89, thumb![0]); // PNG-Signatur
        Assert.Equal(0x89, orig![0]);
    }

    [Fact]
    public void GuardRejectsWritesIntoSource()
    {
        var root = TestDataDir;
        Assert.Throws<InvalidOperationException>(() => ReadOnlyGuard.EnsureOutside(Path.Combine(root, "x.png"), root));
        Assert.Throws<InvalidOperationException>(() => ReadOnlyGuard.EnsureOutside(root + "\\", root));
        Assert.Throws<InvalidOperationException>(() => ReadOnlyGuard.EnsureOutside(Path.Combine(root, "sub", "..", "y"), root));
        Assert.Throws<InvalidOperationException>(() => Exporter.WriteBytes(Path.Combine(root, "x.png"), [1], root));
        Assert.Throws<InvalidOperationException>(() => Exporter.CopyFiles([File("Max Muster.snagx")], root, root));
        // Ähnlich benannter Nachbarordner ist erlaubt
        ReadOnlyGuard.EnsureOutside(root + "Export", root);
        Assert.False(System.IO.File.Exists(Path.Combine(root, "x.png")));
    }
}
