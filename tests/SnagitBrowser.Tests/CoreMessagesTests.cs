using SnagitBrowser.Core;

namespace SnagitBrowser.Tests;

/// <summary>Englische Fassung der Core-Fehlermeldungen: Muster müssen zu den echten Meldungen passen.</summary>
public class CoreMessagesTests
{
    [Fact]
    public void FileChanged_IsTranslated_WithFileName()
    {
        var ex = new FileChangedException(@"C:\x\Bild 1.snagx");
        Assert.Equal("The file has been changed by another program in the meantime (e.g. Snagit): Bild 1.snagx",
            CoreMessages.ToEnglish(ex.Message));
    }

    [Fact]
    public void UnknownMetadataFormat_FromReplaceTags_IsTranslated()
    {
        var ex = Assert.Throws<InvalidDataException>(() => SnagxWriter.ReplaceTags("{}", ["a"]));
        Assert.Equal("Unknown format of metadata.json – tags were not changed.", CoreMessages.ToEnglish(ex.Message));
    }

    [Fact]
    public void ReadOnlyGuard_IsTranslated_WithPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnagitRoot");
        var target = Path.Combine(root, "a.png");
        var ex = Assert.Throws<InvalidOperationException>(() => ReadOnlyGuard.EnsureOutside(target, root));
        Assert.Equal($"Writing to the Snagit source folder is not allowed: {target}", CoreMessages.ToEnglish(ex.Message));
    }

    [Theory]
    [InlineData("Temporäre Datei wurde nicht korrekt geschrieben.", "The temporary file was not written correctly.")]
    [InlineData("metadata.json fehlt.", "metadata.json is missing.")]
    [InlineData("Prüfung fehlgeschlagen: Einträge der Datei weichen ab.", "Verification failed: the entries of the file differ.")]
    [InlineData("Prüfung fehlgeschlagen: Tags wurden nicht korrekt gespeichert.", "Verification failed: tags were not saved correctly.")]
    [InlineData("Prüfung fehlgeschlagen: Feld AppName wurde verändert.", "Verification failed: field AppName was changed.")]
    [InlineData("Prüfung fehlgeschlagen: Image.png wurde verändert.", "Verification failed: Image.png was changed.")]
    [InlineData("Sicherungskopie konnte nicht korrekt geschrieben werden – Änderung abgebrochen.",
                "The backup copy could not be written correctly – change cancelled.")]
    public void KnownMessages_AreTranslated(string german, string english) =>
        Assert.Equal(english, CoreMessages.ToEnglish(german));

    [Fact]
    public void UnknownMessage_IsUnchanged() =>
        Assert.Equal("Access to the path is denied.", CoreMessages.ToEnglish("Access to the path is denied."));
}
