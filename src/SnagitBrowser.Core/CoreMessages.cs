using System.Text.RegularExpressions;

namespace SnagitBrowser.Core;

/// <summary>
/// Englische Fassung der (deutschen) Fehlermeldungen aus SnagitBrowser.Core, damit die App sie
/// in der gewählten Sprache anzeigen kann. Unbekannte Meldungen (z. B. von .NET selbst) bleiben unverändert.
/// </summary>
public static class CoreMessages
{
    private static readonly (Regex Pattern, string English)[] Map =
    [
        (Rx(@"^Die Datei wurde inzwischen von einem anderen Programm geändert \(z\. B\. Snagit\): (?<a>.*)$"),
            "The file has been changed by another program in the meantime (e.g. Snagit): {a}"),
        (Rx(@"^Temporäre Datei wurde nicht korrekt geschrieben\.$"),
            "The temporary file was not written correctly."),
        (Rx(@"^metadata\.json fehlt\.$"),
            "metadata.json is missing."),
        (Rx(@"^Unbekanntes Format von metadata\.json – Tags wurden nicht geändert\.$"),
            "Unknown format of metadata.json – tags were not changed."),
        (Rx(@"^Prüfung fehlgeschlagen: Einträge der Datei weichen ab\.$"),
            "Verification failed: the entries of the file differ."),
        (Rx(@"^Prüfung fehlgeschlagen: Tags wurden nicht korrekt gespeichert\.$"),
            "Verification failed: tags were not saved correctly."),
        (Rx(@"^Prüfung fehlgeschlagen: Feld (?<a>.*) wurde verändert\.$"),
            "Verification failed: field {a} was changed."),
        (Rx(@"^Prüfung fehlgeschlagen: (?<a>.*) wurde verändert\.$"),
            "Verification failed: {a} was changed."),
        (Rx(@"^Sicherungskopie konnte nicht korrekt geschrieben werden – Änderung abgebrochen\.$"),
            "The backup copy could not be written correctly – change cancelled."),
        (Rx(@"^Schreiben in den Snagit-Quellordner ist nicht erlaubt: (?<a>.*)$"),
            "Writing to the Snagit source folder is not allowed: {a}"),
    ];

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>Übersetzt eine bekannte Core-Meldung ins Englische; sonst wird sie unverändert zurückgegeben.</summary>
    public static string ToEnglish(string message)
    {
        foreach (var (pattern, english) in Map)
        {
            var m = pattern.Match(message);
            if (m.Success) return english.Replace("{a}", m.Groups["a"].Value);
        }
        return message;
    }
}
