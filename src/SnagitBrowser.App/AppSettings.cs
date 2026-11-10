using System.IO;
using System.Text.Json;
using SnagitBrowser.Core;

namespace SnagitBrowser.App;

public sealed class AppSettings
{
    public string SourceRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Snagit");
    public string? LastExportDir { get; set; }
    /// <summary>Vor dem Löschen nachfragen (abschaltbar im Dialog).</summary>
    public bool ConfirmDelete { get; set; } = true;
    /// <summary>Tags auf den Kacheln der Galerie anzeigen.</summary>
    public bool ShowTileTags { get; set; }
    /// <summary>Tag-Leiste unter der Suche aufgeklappt.</summary>
    public bool TagBarExpanded { get; set; } = true;
    /// <summary>Sprache der Oberfläche: "de" (Standard) oder "en".</summary>
    public string Language { get; set; } = Loc.German;

    private static string FilePath => Path.Combine(LibraryIndex.DefaultDataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(LibraryIndex.DefaultDataDir);
        ReadOnlyGuard.EnsureOutside(FilePath, SourceRoot);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
