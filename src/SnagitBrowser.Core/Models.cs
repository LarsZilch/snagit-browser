namespace SnagitBrowser.Core;

/// <summary>Inhalt von metadata.json einer .snagx-Datei.</summary>
public sealed class SnagxMetadata
{
    public string AppName { get; init; } = "";
    public string AppCompany { get; init; } = "";
    public string AppVersion { get; init; } = "";
    /// <summary>Aufnahmezeitpunkt in Ortszeit (CaptureDate in der Datei ist UTC).</summary>
    public DateTime? CaptureDate { get; init; }
    public string CaptureId { get; init; } = "";
    public string LanguageCode { get; init; } = "";
    public string OperatingSystemVersion { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string WebUrl { get; init; } = "";
    public string WindowName { get; init; } = "";
}

/// <summary>Ein Annotationsobjekt (Text, Callout, Pfeil, ...) auf einer Seite.</summary>
public sealed class CaptureObject
{
    public string ToolMode { get; init; } = "";
    public string? Rtf { get; init; }
    public string? PlainText { get; init; }
}

public sealed class SnagxPage
{
    public string EntryName { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public string? BackgroundImage { get; init; }
    public string SoftwareVersion { get; init; } = "";
    public IReadOnlyList<CaptureObject> Objects { get; init; } = [];
}

public sealed class SnagxDocument
{
    public string FilePath { get; init; } = "";
    public SnagxMetadata Metadata { get; init; } = new();
    public IReadOnlyList<SnagxPage> Pages { get; init; } = [];
    public bool HasThumbnail { get; init; }

    /// <summary>Alle nicht-leeren Texte aller Seiten.</summary>
    public IEnumerable<string> Texts =>
        Pages.SelectMany(p => p.Objects)
             .Select(o => o.PlainText)
             .Where(t => !string.IsNullOrWhiteSpace(t))!;

    /// <summary>Aufnahmedatum, notfalls aus dem Dateinamen oder Dateidatum.</summary>
    public DateTime EffectiveDate { get; init; }
}
