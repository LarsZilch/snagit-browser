using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SnagitBrowser.App;

/// <summary>Hilfe mit Funktionsübersicht und Version. Es gibt höchstens ein Hilfefenster; F1 holt es nach vorne.</summary>
public partial class HelpWindow : Window
{
    private static HelpWindow? _instance;

    public static void ShowFor(Window? caller)
    {
        if (_instance != null)
        {
            if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
            _instance.Activate();
            return;
        }
        var owner = Application.Current?.MainWindow;
        _instance = new HelpWindow();
        if (owner != null && owner.IsVisible) _instance.Owner = owner;
        else if (caller != null) _instance.Owner = caller;
        _instance.Show();
    }

    private HelpWindow()
    {
        InitializeComponent();
        Render();
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => { Loc.LanguageChanged -= OnLanguageChanged; _instance = null; };
        Loaded += (_, _) => Viewer.Focus();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        Title = Loc.F("Help_Title", AppInfo.Title);
        AppNameText.Text = AppInfo.Name;
        VersionText.Text = Loc.F("Help_Version", AppInfo.VersionText);
        Viewer.Document = Build(HelpContent.Get(Loc.IsEnglish));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }

    // ---------- Einfache Textauszeichnung → FlowDocument ----------
    //   # Überschrift   ## Unterüberschrift   - Aufzählung   | Tabelle | Zeile |   > Hinweis
    //   **fett** und `Code` innerhalb einer Zeile; Leerzeilen trennen Absätze.

    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    private static readonly Brush NoteBack = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1));
    private static readonly Brush CodeBack = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));

    public static FlowDocument Build(string markup)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"), FontSize = 13.5, PagePadding = new Thickness(26, 16, 26, 26),
            LineHeight = 20, TextAlignment = TextAlignment.Left,
        };
        List? list = null;
        Table? table = null;

        void EndBlocks() { list = null; table = null; }

        foreach (var raw in markup.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) { EndBlocks(); continue; }

            if (line.StartsWith("# "))
            {
                EndBlocks();
                var p = Inline(line[2..]);
                p.FontSize = 19; p.FontWeight = FontWeights.SemiBold; p.Foreground = Accent;
                p.Margin = new Thickness(0, doc.Blocks.Count == 0 ? 0 : 22, 0, 6);
                p.BorderBrush = Line; p.BorderThickness = new Thickness(0, 0, 0, 1); p.Padding = new Thickness(0, 0, 0, 4);
                doc.Blocks.Add(p);
            }
            else if (line.StartsWith("## "))
            {
                EndBlocks();
                var p = Inline(line[3..]);
                p.FontSize = 15; p.FontWeight = FontWeights.SemiBold; p.Margin = new Thickness(0, 12, 0, 4);
                doc.Blocks.Add(p);
            }
            else if (line.StartsWith("- "))
            {
                table = null;
                if (list == null)
                {
                    list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(0, 2, 0, 8), Padding = new Thickness(22, 0, 0, 0) };
                    doc.Blocks.Add(list);
                }
                var p = Inline(line[2..]);
                p.Margin = new Thickness(0, 1, 0, 1);
                list.ListItems.Add(new ListItem(p));
            }
            else if (line.StartsWith('|'))
            {
                list = null;
                var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                bool header = table == null;
                if (table == null)
                {
                    table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 10), BorderBrush = Line, BorderThickness = new Thickness(1, 1, 0, 0) };
                    for (int i = 0; i < cells.Length; i++)
                        table.Columns.Add(new TableColumn { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(2, GridUnitType.Star) });
                    table.RowGroups.Add(new TableRowGroup());
                    doc.Blocks.Add(table);
                }
                var row = new TableRow();
                if (header) { row.Background = CodeBack; row.FontWeight = FontWeights.SemiBold; }
                foreach (var c in cells)
                {
                    var p = Inline(c);
                    p.Margin = new Thickness(0);
                    row.Cells.Add(new TableCell(p) { Padding = new Thickness(8, 4, 8, 4), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 1, 1) });
                }
                table.RowGroups[0].Rows.Add(row);
            }
            else if (line.StartsWith("> "))
            {
                EndBlocks();
                var p = Inline(line[2..]);
                p.Background = NoteBack; p.Padding = new Thickness(10, 6, 10, 6); p.Margin = new Thickness(0, 4, 0, 10);
                p.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xD8, 0x8A)); p.BorderThickness = new Thickness(3, 0, 0, 0);
                doc.Blocks.Add(p);
            }
            else
            {
                EndBlocks();
                var p = Inline(line);
                p.Margin = new Thickness(0, 2, 0, 8);
                doc.Blocks.Add(p);
            }
        }
        return doc;
    }

    private static readonly Regex InlineRegex = new(@"\*\*(.+?)\*\*|`(.+?)`", RegexOptions.CultureInvariant);

    /// <summary>Absatz mit **fett** und `Code`.</summary>
    private static Paragraph Inline(string text)
    {
        var p = new Paragraph();
        int pos = 0;
        foreach (Match m in InlineRegex.Matches(text))
        {
            if (m.Index > pos) p.Inlines.Add(new Run(text[pos..m.Index]));
            if (m.Groups[1].Success) p.Inlines.Add(new Bold(new Run(m.Groups[1].Value)));
            else p.Inlines.Add(new Run(m.Groups[2].Value) { FontFamily = new FontFamily("Consolas"), Background = CodeBack, Foreground = Muted });
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) p.Inlines.Add(new Run(text[pos..]));
        return p;
    }
}
