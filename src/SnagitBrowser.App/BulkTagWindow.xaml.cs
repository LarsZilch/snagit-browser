using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnagitBrowser.Core;

namespace SnagitBrowser.App;

/// <summary>
/// Tags für mehrere ausgewählte Captures setzen/entfernen. Änderungen werden erst bei
/// „Übernehmen“ geschrieben – pro Datei mit Sicherung, Prüfung und atomarem Austausch (SnagxWriter).
/// </summary>
public partial class BulkTagWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly List<string> _paths;
    private readonly Dictionary<string, (FileStamp Stamp, List<string> Tags)> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _add = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _remove = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _addOrder = [];
    private bool _busy = true;
    private static readonly StringComparer TagOrder = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), ignoreCase: true);

    private sealed record TagState(string Name, int Count, int Total)
    {
        public bool IsAll => Count == Total;
        public bool IsPartial => Count > 0 && Count < Total;
        public string CountText => IsPartial ? $"{Count}/{Total}" : "";
    }

    public BulkTagWindow(MainViewModel vm, IReadOnlyList<string> paths)
    {
        InitializeComponent();
        _vm = vm;
        _paths = paths.ToList();
        foreach (var t in vm.AllTagNames()) _knownTags.Add(t);
        HeaderText.Text = _paths.Count == 1 ? Loc.F("Bulk_HeaderOne", Path.GetFileName(_paths[0])) : Loc.F("Bulk_HeaderMany", _paths.Count);
        SummaryText.Text = Loc.T("Bulk_Reading");
        SetBusy(true);
        Loaded += async (_, _) => await LoadAsync();
        // Während des Schreibens nicht schließen
        Closing += (_, e) => { if (_writing) e.Cancel = true; };
    }

    private bool _writing;

    /// <summary>Tags direkt aus den Dateien lesen (aktueller als der Index) und Dateistand merken.</summary>
    private async Task LoadAsync()
    {
        var errors = new List<string>();
        var result = await Task.Run(() =>
        {
            var list = new List<(string Path, FileStamp Stamp, List<string> Tags)>();
            foreach (var p in _paths)
            {
                try
                {
                    var stamp = FileStamp.Of(p);
                    list.Add((p, stamp, SnagxReader.Read(p).Metadata.Tags.ToList()));
                }
                catch (Exception ex) { lock (errors) errors.Add($"{Path.GetFileName(p)}: {Loc.Error(ex)}"); }
            }
            return list;
        });
        foreach (var (p, stamp, tags) in result)
        {
            _files[p] = (stamp, tags);
            foreach (var t in tags) _knownTags.Add(t);
        }
        if (errors.Count > 0)
            MessageBox.Show(this, Loc.F("Bulk_Unreadable", string.Join("\n", errors.Take(15))),
                Loc.T("Common_EditTags"), MessageBoxButton.OK, MessageBoxImage.Warning);
        SetBusy(false);
        UpdateUi();
        NewTagBox.Focus();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ApplyButton.IsEnabled = !busy;
        NewTagBox.IsEnabled = !busy;
        TagList.IsEnabled = AllTagsList.IsEnabled = !busy;
        Cursor = busy ? Cursors.Wait : null;
    }

    /// <summary>Wie viele Dateien haben den Tag nach den geplanten Änderungen?</summary>
    private int CountAfter(string tag) =>
        _add.Contains(tag) ? _files.Count
        : _remove.Contains(tag) ? 0
        : _files.Values.Count(f => f.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

    private void UpdateUi()
    {
        int total = _files.Count;
        var present = _knownTags.Select(t => new TagState(t, CountAfter(t), total)).ToList();

        // Oben: Tags, die nach der Änderung bei mindestens einem Bild vorhanden sind
        var current = present.Where(s => s.Count > 0).OrderBy(s => s.IsPartial).ThenBy(s => s.Name, TagOrder).ToList();
        TagList.ItemsSource = current;
        NoTagsText.Visibility = current.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var filter = NewTagBox.Text.Trim();
        NewTagPlaceholder.Visibility = NewTagBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AllTagsList.ItemsSource = present
            .Where(s => filter.Length == 0 || s.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(s => s.Name, TagOrder).ToList();

        int changedFiles = _files.Count(f => !NewTags(f.Value.Tags).SequenceEqual(f.Value.Tags));
        var parts = new List<string>();
        if (_add.Count > 0) parts.Add($"+ {string.Join(", ", _addOrder.Where(_add.Contains))}");
        if (_remove.Count > 0) parts.Add($"− {string.Join(", ", _remove.OrderBy(t => t, TagOrder))}");
        SummaryText.Text = parts.Count == 0 ? Loc.T("Bulk_NoChanges")
            : $"{string.Join("   ", parts)}\n{Loc.F("Bulk_FilesChanged", changedFiles, total)}";
        ApplyButton.IsEnabled = !_busy && changedFiles > 0;
    }

    /// <summary>Neue Tag-Liste einer Datei: Reihenfolge bleibt, Entfernte fallen weg, Neue werden angehängt.</summary>
    private List<string> NewTags(List<string> tags)
    {
        var result = tags.Where(t => !_remove.Contains(t)).ToList();
        foreach (var t in _addOrder.Where(_add.Contains))
            if (!result.Contains(t, StringComparer.OrdinalIgnoreCase)) result.Add(t);
        return result;
    }

    private void SetForAll(string tag)
    {
        tag = tag.Trim();
        if (tag.Length == 0 || _busy) return;
        // Schreibweise eines vorhandenen Tags übernehmen
        tag = _knownTags.FirstOrDefault(k => string.Equals(k, tag, StringComparison.OrdinalIgnoreCase)) ?? tag;
        _knownTags.Add(tag);
        _remove.Remove(tag);
        bool everywhere = _files.Values.All(f => f.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        if (everywhere) _add.Remove(tag);
        else if (_add.Add(tag)) { _addOrder.Remove(tag); _addOrder.Add(tag); }
        UpdateUi();
    }

    private void RemoveFromAll(string tag)
    {
        if (_busy) return;
        _add.Remove(tag);
        if (_files.Values.Any(f => f.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))) _remove.Add(tag);
        UpdateUi();
    }

    private void ChipName_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag) SetForAll(tag);
    }

    private void ChipRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag) RemoveFromAll(tag);
    }

    private void TagChoice_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag) return;
        // Bei allen vorhanden → entfernen; sonst (keins oder ein Teil) → bei allen setzen
        if (CountAfter(tag) == _files.Count && _files.Count > 0) RemoveFromAll(tag); else SetForAll(tag);
    }

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        SetForAll(NewTagBox.Text);
        NewTagBox.Text = "";
        NewTagBox.Focus();
    }

    private void NewTagBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        SetForAll(NewTagBox.Text);
        NewTagBox.Text = "";
        e.Handled = true;
    }

    private void NewTagBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateUi();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1) { HelpWindow.ShowFor(this); e.Handled = true; return; }
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && ApplyButton.IsEnabled)
        {
            Apply_Click(this, new RoutedEventArgs()); e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (NewTagBox.Text.Length > 0) NewTagBox.Text = "";
            else if (!_busy) Close();
            e.Handled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { if (!_busy) Close(); }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var work = _files.Select(f => (Path: f.Key, f.Value.Stamp, Old: f.Value.Tags, New: NewTags(f.Value.Tags)))
                         .Where(w => !w.New.SequenceEqual(w.Old)).ToList();
        if (work.Count == 0) return;

        SetBusy(true);
        _writing = true;
        CancelButton.IsEnabled = false;
        var done = new List<string>();
        var errors = new List<string>();
        var progress = new Progress<int>(n => SummaryText.Text = Loc.F("Bulk_Writing", n, work.Count));
        await Task.Run(() =>
        {
            int n = 0;
            foreach (var w in work)
            {
                try
                {
                    SnagxWriter.SetTags(w.Path, w.New, w.Stamp);
                    done.Add(w.Path);
                }
                catch (FileChangedException)
                {
                    errors.Add(Loc.F("Bulk_ChangedSkipped", Path.GetFileName(w.Path)));
                }
                catch (Exception ex) { errors.Add($"{Path.GetFileName(w.Path)}: {Loc.Error(ex)}"); }
                ((IProgress<int>)progress).Report(++n);
            }
        });

        _vm.FilesChanged(done);
        int saved = done.Count, skipped = errors.Count;
        _vm.SetStatus(() => Loc.F("Bulk_Saved", saved) + (skipped > 0 ? Loc.F("Bulk_SavedSkipped", skipped) : ""));
        if (errors.Count > 0)
        {
            MessageBox.Show(this,
                Loc.F("Bulk_NotChanged", done.Count, string.Join("\n", errors.Take(20))) +
                (errors.Count > 20 ? Loc.F("Bulk_More", errors.Count - 20) : ""),
                Loc.T("Common_EditTags"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _busy = false;
        _writing = false;
        DialogResult = true;
    }
}
