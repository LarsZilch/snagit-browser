using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using SnagitBrowser.Core;

namespace SnagitBrowser.App;

/// <summary>Programmname und Version (aus der Assembly, gesetzt in Directory.Build.props).</summary>
public static class AppInfo
{
    public const string Name = "Snagit Browser";

    /// <summary>Version wie „1.0“ bzw. „1.0.3“ (Build-Nummer nur, wenn sie nicht 0 ist).</summary>
    public static string VersionText { get; } = FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

    public static string Title => $"{Name} {VersionText}";

    private static string FormatVersion(Version? v) =>
        v == null ? "?" : v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
}

/// <summary>
/// Aktuelle Oberflächensprache (Deutsch/Englisch). XAML bindet über <see cref="LExtension"/> an den
/// Indexer, daher aktualisieren sich alle Texte beim Umschalten sofort. Code nutzt <see cref="T"/> und <see cref="F"/>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string German = "de", English = "en";

    public static Loc Instance { get; } = new();
    private Loc() { }

    public static string Language { get; private set; } = German;
    public static bool IsEnglish => Language == English;
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(IsEnglish ? "en-US" : "de-DE");
    public static XmlLanguage XmlLanguage => XmlLanguage.GetLanguage(Culture.IetfLanguageTag);

    /// <summary>Nach dem Umschalten der Sprache (Texte, die im Code erzeugt werden, neu aufbauen).</summary>
    public static event EventHandler? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => T(key);

    /// <summary>Text in der aktuellen Sprache.</summary>
    public static string T(string key) => Strings.Get(key, IsEnglish);

    /// <summary>Text mit Platzhaltern {0}, {1} … in der aktuellen Sprache und Zahlenformat.</summary>
    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <summary>Fehlermeldung einer Ausnahme in der aktuellen Sprache (Core-Meldungen sind deutsch).</summary>
    public static string Error(Exception ex) => IsEnglish ? CoreMessages.ToEnglish(ex.Message) : ex.Message;

    public static string Normalize(string? lang) => lang == English ? English : German;

    public static void SetLanguage(string? lang)
    {
        lang = Normalize(lang);
        bool changed = lang != Language;
        Language = lang;
        var c = Culture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = c;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = c;
        if (Application.Current != null)
            foreach (Window w in Application.Current.Windows) w.Language = XmlLanguage;
        if (!changed) return;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(Binding.IndexerName));
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }
}

/// <summary>XAML: <c>Text="{local:L Main_Rescan}"</c> – Text in der aktuellen Sprache, wechselt beim Umschalten mit.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LExtension(string key) : MarkupExtension
{
    [ConstructorArgument("key")]
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
