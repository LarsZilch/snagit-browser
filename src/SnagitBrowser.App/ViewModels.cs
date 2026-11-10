using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SnagitBrowser.Core;

namespace SnagitBrowser.App;

public sealed partial class CaptureItem : ObservableObject
{
    private int _bucket = 256;
    private int _loadingBucket;
    private WeakReference<BitmapSource>? _lastShown;

    public CaptureItem(CaptureSummary s, int index)
    {
        Summary = s;
        Index = index;
        Aspect = JustifiedLayout.Aspect(s.Width, s.Height);
    }

    public CaptureSummary Summary { get; }
    /// <summary>Position in der Trefferliste (für Umschalt-Auswahl und Detailansicht).</summary>
    public int Index { get; }
    public double Aspect { get; }

    [ObservableProperty] private double _displayWidth;
    [ObservableProperty] private double _displayHeight;
    [ObservableProperty] private bool _isSelected;

    /// <summary>Größe der Kachel setzen; lädt bei Bedarf eine schärfere Bildstufe.</summary>
    public void SetDisplaySize(double width, double height, double dpiScale)
    {
        DisplayWidth = width;
        DisplayHeight = height;
        SetBucket(ImageLoader.BucketFor(Math.Max(width, height) * dpiScale));
    }
    public string Title => string.IsNullOrEmpty(Summary.WindowName) ? Summary.FileName : Summary.WindowName;
    public string DateText => Summary.Date.ToString(Loc.T("Fmt_Date"), Loc.Culture);
    public string TagsText => string.Join(", ", Summary.Tags);
    public bool HasText => Summary.Texts.Length > 0;
    public string ToolTip =>
        $"{Summary.FileName}\n{Summary.AppName}\n{Summary.WindowName}" +
        (Summary.Tags.Count > 0 ? "\n" + Loc.F("Item_Tags", TagsText) : "") +
        (HasText ? $"\n\n{Shorten(Summary.Texts, 400)}" : "");

    /// <summary>Nach dem Umschalten der Sprache: sprachabhängige Texte neu anzeigen.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(ToolTip));
    }

    private static string Shorten(string s, int n) => s.Length <= n ? s : s[..n] + " …";

    /// <summary>Neue Auflösungsstufe (z.B. nach Ändern der Vorschaugröße).</summary>
    public void SetBucket(int bucket)
    {
        if (bucket == _bucket) return;
        _bucket = bucket;
        OnPropertyChanged(nameof(Thumbnail));
    }

    /// <summary>
    /// Wird erst abgefragt, wenn die Kachel sichtbar ist (Virtualisierung). Das Bild liegt im
    /// RAM-Cache des ImageLoader; bis die neue Stufe geladen ist, bleibt das vorige Bild sichtbar.
    /// </summary>
    public BitmapSource? Thumbnail
    {
        get
        {
            var bucket = _bucket;
            if (ImageLoader.TryGet(ImageLoader.Key(Summary, bucket)) is { } bmp)
            {
                _lastShown = new WeakReference<BitmapSource>(bmp);
                return bmp;
            }
            if (_loadingBucket != bucket)
            {
                _loadingBucket = bucket;
                _ = LoadAsync(bucket);
            }
            return _lastShown != null && _lastShown.TryGetTarget(out var old) ? old : null;
        }
    }

    private async Task LoadAsync(int bucket)
    {
        await ImageLoader.LoadAsync(Summary, bucket);
        _loadingBucket = 0;
        OnPropertyChanged(nameof(Thumbnail));
    }
}

/// <summary>Eine Zeile der Galerie mit gleich hohen Kacheln.</summary>
public sealed class GalleryRow(double height, IReadOnlyList<CaptureItem> items)
{
    public double Height { get; } = height;
    public IReadOnlyList<CaptureItem> Items { get; } = items;
}

public sealed record FilterEntry(string? Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Auswahl im Filter "Anzahl Tags".</summary>
/// <summary>Ein Tag als Filter-Button in der Tag-Leiste der Übersicht.</summary>
public sealed partial class TagButton(string name, int count) : ObservableObject
{
    public string Name { get; } = name;
    public int Count { get; } = count;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isVisible = true;
}

public sealed record TagCountEntry(string Key, int? Min, int? Max, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Eintrag der Sprachauswahl (Name immer in der eigenen Sprache).</summary>
public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

public enum DateLevel { All, Year, Month, Day }

/// <summary>Knoten im Zeitraum-Baum (Alle → Jahr → Monat → Tag).</summary>
public sealed partial class DateNode : ObservableObject
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required DateLevel Level { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int Count { get; set; }
    public ObservableCollection<DateNode> Children { get; } = [];
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    public string Icon => Level switch
    {
        DateLevel.All => "",   // Ordner
        DateLevel.Year => "",  // Bibliothek
        DateLevel.Month => "", // Kalender
        _ => "",               // Kalendertag
    };
}

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private readonly AppSettings _settings = AppSettings.Load();
    private LibraryIndex? _index;
    private CancellationTokenSource? _scanCts;
    private readonly DispatcherTimer _debounce;
    private bool _suppressRefresh;
    private readonly HashSet<CaptureItem> _selected = [];
    private int _anchorIndex = -1;
    private List<double> _rowTops = [];
    private string _selectedDateKey = "all";
    private bool _treeHadYears;

    public MainViewModel()
    {
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RefreshAll(); };
        Loc.LanguageChanged += OnLanguageChanged;
    }

    // ---------- Sprache ----------

    public IReadOnlyList<LanguageOption> Languages { get; } =
        [new(Loc.German, "Deutsch"), new(Loc.English, "English")];

    /// <summary>Sprache der Oberfläche; wird gespeichert und wirkt sofort.</summary>
    public string UiLanguage
    {
        get => Loc.Language;
        set
        {
            value = Loc.Normalize(value);
            if (value == Loc.Language) return;
            _settings.Language = value;
            _settings.Save();
            Loc.SetLanguage(value);
            OnPropertyChanged();
        }
    }

    /// <summary>Im Code erzeugte Texte (Filter, Zeitraum, Statuszeile, Kacheln) in der neuen Sprache neu aufbauen.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        LoadFilters();
        BuildDateTree();
        OnPropertyChanged(nameof(TagBarHeader));
        UpdateSelectionText();
        if (_statusSource != null) Status = _statusSource();
        if (_resultSource != null) ResultText = _resultSource();
        foreach (var item in Items) item.RefreshTexts();
    }

    private Func<string>? _statusSource, _resultSource;

    /// <summary>Statusmeldung setzen; sie wird beim Umschalten der Sprache neu erzeugt.</summary>
    public void SetStatus(Func<string> text) { _statusSource = text; Status = text(); }

    private void SetResultText(Func<string> text) { _resultSource = text; ResultText = text(); }

    /// <summary>Alle Treffer in Reihenfolge.</summary>
    [ObservableProperty] private IReadOnlyList<CaptureItem> _items = [];
    /// <summary>Treffer, in Zeilen angeordnet (für die virtualisierte Galerie).</summary>
    [ObservableProperty] private IReadOnlyList<GalleryRow> _rows = [];
    [ObservableProperty] private string _selectionText = "";

    public const double RowGap = 6;
    public ObservableCollection<FilterEntry> Apps { get; } = [];
    public ObservableCollection<FilterEntry> Tags { get; } = [];
    public ObservableCollection<TagCountEntry> TagCounts { get; } = [];
    public ObservableCollection<DateNode> DateTree { get; } = [];

    public const double MinRowHeight = 120, MaxRowHeight = 1600;

    public string SourceRoot => _settings.SourceRoot;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;
    [ObservableProperty] private FilterEntry? _selectedApp;
    [ObservableProperty] private FilterEntry? _selectedTag;
    [ObservableProperty] private bool _onlyWithText;
    [ObservableProperty] private TagCountEntry? _selectedTagCount;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private double _scanPercent;
    [ObservableProperty] private string _resultText = "";

    /// <summary>Zielhöhe einer Galerie-Zeile in geräteunabhängigen Pixeln.</summary>
    [ObservableProperty] private double _rowHeight = 300;
    /// <summary>Skalierung des Monitors (1.0 = 100 %, 2.0 = 200 %).</summary>
    [ObservableProperty] private double _dpiScale = 1.0;

    partial void OnSearchTextChanged(string value) { if (_suppressRefresh) return; _debounce.Stop(); _debounce.Start(); }
    partial void OnSelectedAppChanged(FilterEntry? value) => RefreshAll();
    partial void OnSelectedTagChanged(FilterEntry? value)
    {
        foreach (var b in TagButtons) b.IsSelected = b.Name == value?.Value;
        OnPropertyChanged(nameof(TagBarHeader));
        RefreshAll();
    }

    /// <summary>Tag-Leiste der Übersicht: alle Tags als Buttons, höchstens einer aktiv.</summary>
    public ObservableCollection<TagButton> TagButtons { get; } = [];

    public bool TagBarExpanded
    {
        get => _settings.TagBarExpanded;
        set { if (_settings.TagBarExpanded == value) return; _settings.TagBarExpanded = value; _settings.Save(); OnPropertyChanged(); }
    }

    public string TagBarHeader => SelectedTag?.Value is { } t ? Loc.F("Main_TagBar_Header", t) : Loc.T("Main_TagBar_None");

    /// <summary>Klick auf einen Tag-Button: auswählen bzw. bei erneutem Klick wieder abwählen.</summary>
    public void ToggleTagFilter(string name)
    {
        SelectedTag = SelectedTag?.Value == name
            ? Tags.FirstOrDefault()
            : Tags.FirstOrDefault(t => t.Value == name) ?? Tags.FirstOrDefault();
    }

    [RelayCommand]
    private void ClearTagFilter() => SelectedTag = Tags.FirstOrDefault();

    [ObservableProperty] private string _tagBarSearch = "";
    partial void OnTagBarSearchChanged(string value) => ApplyTagButtonFilter();

    private void ApplyTagButtonFilter()
    {
        var f = TagBarSearch.Trim();
        foreach (var b in TagButtons)
            b.IsVisible = f.Length == 0 || b.IsSelected || b.Name.Contains(f, StringComparison.CurrentCultureIgnoreCase);
    }
    partial void OnOnlyWithTextChanged(bool value) => RefreshAll();
    partial void OnSelectedTagCountChanged(TagCountEntry? value) => RefreshAll();

    /// <summary>Vorschaugröße mit Strg+Mausrad: jede Raststufe ±10 %.</summary>
    public void ZoomRows(int wheelDelta) =>
        RowHeight = Math.Clamp(Math.Round(RowHeight * Math.Pow(1.1, wheelDelta / 120.0)), MinRowHeight, MaxRowHeight);

    /// <summary>Ordnet die Treffer für die gegebene Breite in Zeilen an.</summary>
    public void BuildRows(double availableWidth)
    {
        var items = Items;
        var layout = JustifiedLayout.Build(items.Select(i => i.Aspect).ToList(), availableWidth, RowHeight, RowGap);
        var rows = new List<GalleryRow>(layout.Count);
        var tops = new List<double>(layout.Count);
        double y = 0;
        foreach (var r in layout)
        {
            var rowItems = new CaptureItem[r.Count];
            for (int k = 0; k < r.Count; k++)
            {
                var item = items[r.Start + k];
                item.SetDisplaySize(r.Widths[k], r.Height, DpiScale);
                rowItems[k] = item;
            }
            rows.Add(new GalleryRow(r.Height, rowItems));
            tops.Add(y);
            y += r.Height + RowGap;
        }
        _rowTops = tops;
        Rows = rows;
    }

    /// <summary>Scroll-Position (Pixel) der Zeile, die das Element enthält.</summary>
    public double OffsetOf(CaptureItem item) => RowIndexOf(item) is int i and >= 0 ? _rowTops[i] : 0;

    public int RowIndexOf(CaptureItem item)
    {
        for (int i = 0; i < Rows.Count; i++)
            if (Rows[i].Items[^1].Index >= item.Index) return i;
        return -1;
    }

    /// <summary>Erstes Element der Zeile an der Scroll-Position (zum Halten der Position beim Umbruch).</summary>
    public CaptureItem? ItemAtOffset(double offset)
    {
        if (Rows.Count == 0) return null;
        int idx = _rowTops.BinarySearch(offset);
        if (idx < 0) idx = Math.Max(0, ~idx - 1);
        return Rows[Math.Min(idx, Rows.Count - 1)].Items[0];
    }

    // ---------- Auswahl ----------

    public IReadOnlyList<CaptureItem> SelectedItems => _selected.OrderBy(i => i.Index).ToList();

    public void Click(CaptureItem item, bool ctrl, bool shift)
    {
        if (shift && _anchorIndex >= 0 && _anchorIndex < Items.Count)
        {
            if (!ctrl) ClearSelection();
            int a = Math.Min(_anchorIndex, item.Index), b = Math.Max(_anchorIndex, item.Index);
            for (int i = a; i <= b; i++) Select(Items[i], true);
        }
        else if (ctrl)
        {
            Select(item, !item.IsSelected);
            _anchorIndex = item.Index;
        }
        else
        {
            ClearSelection();
            Select(item, true);
            _anchorIndex = item.Index;
        }
        UpdateSelectionText();
    }

    /// <summary>Auswahl per Pfeiltaste verschieben. Liefert das neu gewählte Element.</summary>
    public CaptureItem? MoveSelection(int delta)
    {
        if (Items.Count == 0) return null;
        int idx = Math.Clamp((_anchorIndex < 0 ? 0 : _anchorIndex + delta), 0, Items.Count - 1);
        Click(Items[idx], false, false);
        return Items[idx];
    }

    public void SelectAll()
    {
        foreach (var i in Items) Select(i, true);
        UpdateSelectionText();
    }

    private void Select(CaptureItem item, bool on)
    {
        item.IsSelected = on;
        if (on) _selected.Add(item); else _selected.Remove(item);
    }

    private void ClearSelection()
    {
        foreach (var i in _selected) i.IsSelected = false;
        _selected.Clear();
    }

    private void UpdateSelectionText() =>
        SelectionText = _selected.Count > 1 ? Loc.F("Vm_Selected", _selected.Count) : "";

    public async Task InitializeAsync()
    {
        ImageLoader.DeleteLegacyDiskCache(_settings.SourceRoot);
        try { SnagxWriter.CleanupBackups(days: 30); } catch { /* Sicherungen aufräumen ist optional */ }
        if (!Directory.Exists(_settings.SourceRoot))
        {
            var root = _settings.SourceRoot;
            SetStatus(() => Loc.F("Vm_SourceNotFound", root));
            return;
        }
        _index?.Dispose();
        _index = new LibraryIndex(_settings.SourceRoot);
        OnPropertyChanged(nameof(SourceRoot));
        LoadFilters();
        RefreshAll();
        await RescanAsync();
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        if (_index == null || IsScanning) return;
        IsScanning = true;
        _scanCts = new CancellationTokenSource();
        // Beim allerersten Aufbau die Galerie zwischendurch aktualisieren
        bool firstBuild = _index.Count == 0;
        var lastRefresh = DateTime.Now;
        var progress = new Progress<ScanProgress>(p =>
        {
            ScanPercent = p.Total == 0 ? 100 : 100.0 * p.Done / p.Total;
            SetStatus(() => Loc.F("Vm_Indexing", p.Done, p.Total));
            if (firstBuild && (DateTime.Now - lastRefresh).TotalSeconds > 5)
            {
                lastRefresh = DateTime.Now;
                LoadFilters(); RefreshAll();
            }
        });
        try
        {
            var index = _index;
            var r = await Task.Run(() => index.Scan(progress, _scanCts.Token));
            SetStatus(() => Loc.F("Vm_ScanDone", r.Total, r.Added, r.Updated, r.Removed) +
                            (r.Errors > 0 ? Loc.F("Vm_ScanErrors", r.Errors) : "") + Loc.F("Vm_ScanDuration", r.Duration.TotalSeconds));
            if (r.Added + r.Updated + r.Removed > 0) { LoadFilters(); RefreshAll(); }
        }
        catch (OperationCanceledException) { SetStatus(() => Loc.T("Vm_ScanCancelled")); }
        catch (Exception ex) { SetStatus(() => Loc.F("Vm_ScanError", Loc.Error(ex))); }
        finally { IsScanning = false; ScanPercent = 0; }
    }

    private void LoadFilters()
    {
        if (_index == null) return;
        var app = SelectedApp?.Value; var tag = SelectedTag?.Value; var tagCount = SelectedTagCount?.Key;
        _suppressRefresh = true;
        try
        {
            Apps.Clear(); Apps.Add(new FilterEntry(null, Loc.T("Vm_AllApps")));
            foreach (var (n, c) in _index.GetApps()) Apps.Add(new FilterEntry(n, $"{n} ({c})"));
            Tags.Clear(); Tags.Add(new FilterEntry(null, Loc.T("Vm_AllTags")));
            var tagCounts = _index.GetTags();
            foreach (var (n, c) in tagCounts) Tags.Add(new FilterEntry(n, $"{n} ({c})"));
            TagButtons.Clear();
            foreach (var (n, c) in tagCounts.OrderBy(t => t.Name, StringComparer.Create(German, ignoreCase: true)))
                TagButtons.Add(new TagButton(n, c) { IsSelected = n == tag });
            ApplyTagButtonFilter();

            // Anzahl vergebener Tags: beliebig / ohne / mindestens 1 / genau n
            var dist = _index.GetTagCountDistribution();
            int withTags = dist.Where(d => d.TagCount > 0).Sum(d => d.Captures);
            TagCounts.Clear();
            TagCounts.Add(new TagCountEntry("any", null, null, Loc.T("Vm_AnyTagCount")));
            TagCounts.Add(new TagCountEntry("0", null, 0, Loc.F("Vm_NoTags", dist.FirstOrDefault(d => d.TagCount == 0).Captures)));
            TagCounts.Add(new TagCountEntry("1+", 1, null, Loc.F("Vm_AtLeastOneTag", withTags)));
            foreach (var (n, c) in dist.Where(d => d.TagCount > 0))
                TagCounts.Add(new TagCountEntry($"{n}", n, n, n == 1 ? Loc.F("Vm_ExactlyOneTag", c) : Loc.F("Vm_ExactlyNTags", n, c)));

            SelectedApp = Apps.FirstOrDefault(a => a.Value == app) ?? Apps[0];
            SelectedTag = Tags.FirstOrDefault(t => t.Value == tag) ?? Tags[0];
            SelectedTagCount = TagCounts.FirstOrDefault(t => t.Key == tagCount) ?? TagCounts[0];
        }
        finally { _suppressRefresh = false; }
    }

    private CaptureQuery CurrentQuery() => new()
    {
        Text = SearchText,
        From = FromDate,
        To = ToDate,
        App = SelectedApp?.Value,
        Tag = SelectedTag?.Value,
        OnlyWithText = OnlyWithText,
        MinTags = SelectedTagCount?.Min,
        MaxTags = SelectedTagCount?.Max,
    };

    /// <summary>Bei Änderung von Suche/App/Tag: Zeitraum-Baum neu zählen und Galerie neu laden.</summary>
    private void RefreshAll()
    {
        if (_index == null || _suppressRefresh) return;
        BuildDateTree();
        Refresh();
    }

    private void Refresh()
    {
        if (_index == null || _suppressRefresh) return;
        try
        {
            var hits = _index.Search(CurrentQuery());
            _selected.Clear();
            _anchorIndex = -1;
            UpdateSelectionText();
            Items = hits.Select((h, i) => new CaptureItem(h, i)).ToList();
            int count = hits.Count;
            SetResultText(() => Loc.F("Vm_Hits", count));
        }
        catch (Exception ex) { SetResultText(() => Loc.F("Vm_SearchError", Loc.Error(ex))); }
    }

    private void BuildDateTree()
    {
        if (_index == null) return;
        List<(DateTime Day, int Count)> days;
        try { days = _index.GetDayCounts(CurrentQuery()); }
        catch { return; }

        // Aufklappzustand merken
        var expanded = new HashSet<string>(Flatten(DateTree).Where(n => n.IsExpanded).Select(n => n.Key));
        bool firstBuild = !_treeHadYears;
        _treeHadYears |= days.Count > 0;

        var fmt = Loc.Culture.DateTimeFormat;
        var all = new DateNode { Key = "all", Label = Loc.T("Vm_AllCaptures"), Level = DateLevel.All, Count = days.Sum(d => d.Count), IsExpanded = true };
        foreach (var year in days.GroupBy(d => d.Day.Year).OrderByDescending(g => g.Key))
        {
            var yNode = new DateNode
            {
                Key = $"{year.Key}", Label = $"{year.Key}", Level = DateLevel.Year,
                From = new DateTime(year.Key, 1, 1), To = new DateTime(year.Key, 12, 31),
                Count = year.Sum(d => d.Count),
            };
            foreach (var month in year.GroupBy(d => d.Day.Month).OrderByDescending(g => g.Key))
            {
                var first = new DateTime(year.Key, month.Key, 1);
                var mNode = new DateNode
                {
                    Key = $"{year.Key}-{month.Key:00}", Label = fmt.GetMonthName(month.Key), Level = DateLevel.Month,
                    From = first, To = first.AddMonths(1).AddDays(-1), Count = month.Sum(d => d.Count),
                };
                foreach (var (day, count) in month.OrderByDescending(d => d.Day))
                {
                    var abbr = fmt.GetAbbreviatedMonthName(day.Month).TrimEnd('.');
                    mNode.Children.Add(new DateNode
                    {
                        Key = day.ToString("yyyy-MM-dd"), Label = Loc.F("Vm_DayLabel", day.Day, abbr, fmt.GetDayName(day.DayOfWeek)),
                        Level = DateLevel.Day, From = day, To = day, Count = count,
                    });
                }
                yNode.Children.Add(mNode);
            }
            all.Children.Add(yNode);
        }

        // Beim ersten Mal das neueste Jahr aufklappen, sonst vorherigen Zustand übernehmen
        if (firstBuild && all.Children.Count > 0) expanded.Add(all.Children[0].Key);
        foreach (var n in Flatten([all]))
        {
            if (expanded.Contains(n.Key)) n.IsExpanded = true;
            if (n.Key == _selectedDateKey) n.IsSelected = true;
        }

        _suppressRefresh = true;
        try
        {
            DateTree.Clear();
            DateTree.Add(all);
        }
        finally { _suppressRefresh = false; }
    }

    private static IEnumerable<DateNode> Flatten(IEnumerable<DateNode> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    /// <summary>Vom TreeView aufgerufen, wenn der Benutzer einen Zeitraum wählt.</summary>
    public void SelectDateNode(DateNode? node)
    {
        if (node == null || _suppressRefresh) return;
        // Nach dem Neuaufbau des Baums meldet der TreeView die schon aktive Auswahl erneut
        if (node.Key == _selectedDateKey && FromDate == node.From && ToDate == node.To) return;
        _selectedDateKey = node.Key;
        FromDate = node.From;
        ToDate = node.To;
        Refresh();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressRefresh = true;
        try
        {
            SearchText = "";
            FromDate = null;
            ToDate = null;
            OnlyWithText = false;
            SelectedTagCount = TagCounts.FirstOrDefault();
            SelectedApp = Apps.FirstOrDefault();
            SelectedTag = Tags.FirstOrDefault();
            _selectedDateKey = "all";
        }
        finally { _suppressRefresh = false; }
        RefreshAll();
    }

    [RelayCommand]
    private async Task ChooseSourceAsync()
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("Vm_ChooseSource") };
        if (Directory.Exists(_settings.SourceRoot)) dlg.InitialDirectory = _settings.SourceRoot;
        if (dlg.ShowDialog() != true) return;
        _scanCts?.Cancel();
        _settings.SourceRoot = dlg.FolderName;
        _settings.Save();
        Items = [];
        DateTree.Clear();
        await InitializeAsync();
    }

    public void ExportImages(IList<CaptureItem> items) => Export(items, false);
    public void CopyFiles(IList<CaptureItem> items) => Export(items, true);

    private void Export(IList<CaptureItem> items, bool copySnagx)
    {
        if (items.Count == 0) return;
        var dlg = new OpenFolderDialog { Title = Loc.T(copySnagx ? "Vm_CopyFilesTo" : "Vm_ExportImagesTo") };
        if (_settings.LastExportDir != null && Directory.Exists(_settings.LastExportDir)) dlg.InitialDirectory = _settings.LastExportDir;
        if (dlg.ShowDialog() != true) return;
        try
        {
            var files = items.Select(i => i.Summary.Path).ToList();
            int n = copySnagx
                ? Exporter.CopyFiles(files, dlg.FolderName, _settings.SourceRoot)
                : Exporter.ExportImages(files, dlg.FolderName, _settings.SourceRoot);
            _settings.LastExportDir = dlg.FolderName;
            _settings.Save();
            var folder = dlg.FolderName;
            SetStatus(() => Loc.F("Vm_Exported", n, folder));
        }
        catch (Exception ex) { MessageBox.Show(Loc.Error(ex), Loc.T("Vm_Export_Caption"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    public void CopyTexts(IList<CaptureItem> items)
    {
        var text = string.Join("\n\n", items.Where(i => i.HasText).Select(i => i.Summary.Texts));
        if (text.Length > 0) { Clipboard.SetText(text); SetStatus(() => Loc.T("Vm_TextsCopied")); }
        else SetStatus(() => Loc.T("Vm_NoTexts"));
    }

    // ---------- Ändern: Löschen und Tags ----------

    /// <summary>Beim nächsten Neuaufbau der Galerie die Scroll-Position halten (nach Löschen/Tag-Änderung).</summary>
    public bool PreserveScroll { get; set; }

    public bool ConfirmDelete
    {
        get => _settings.ConfirmDelete;
        set { if (_settings.ConfirmDelete == value) return; _settings.ConfirmDelete = value; _settings.Save(); OnPropertyChanged(); }
    }

    /// <summary>Alle im Index vorkommenden Tags (für die Tag-Auswahl).</summary>
    public List<string> AllTagNames() => _index?.GetTags().Select(t => t.Name).ToList() ?? [];

    /// <summary>
    /// Verschiebt Dateien nach Rückfrage (optional) in den Papierkorb und nimmt sie aus Index und Galerie.
    /// Gibt die gelöschten Pfade zurück (leer bei Abbruch).
    /// </summary>
    public List<string> DeleteFiles(IReadOnlyList<string> paths, Window owner)
    {
        if (paths.Count == 0 || _index == null) return [];
        foreach (var p in paths)
            if (!ReadOnlyGuard.IsInside(p, _settings.SourceRoot))
                throw new InvalidOperationException(Loc.F("Vm_NotInSource", p));

        if (ConfirmDelete)
        {
            var msg = paths.Count == 1
                ? Loc.F("Vm_DeleteOne", Path.GetFileName(paths[0]))
                : Loc.F("Vm_DeleteMany", paths.Count);
            if (!ConfirmDeleteDialog.Ask(owner, msg, out var dontAsk)) return [];
            if (dontAsk) ConfirmDelete = false;
        }

        List<string> gone;
        try { gone = RecycleBin.Delete(paths, new System.Windows.Interop.WindowInteropHelper(owner).Handle); }
        catch (Exception ex)
        {
            MessageBox.Show(owner, Loc.Error(ex), Loc.T("Common_Delete"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return [];
        }
        foreach (var p in gone) _index.Remove(p);

        var goneSet = new HashSet<string>(gone, StringComparer.OrdinalIgnoreCase);
        foreach (var item in _selected.Where(i => goneSet.Contains(i.Summary.Path)).ToList()) _selected.Remove(item);
        ReplaceItems(Items.Where(i => !goneSet.Contains(i.Summary.Path)).Select(i => i.Summary));
        AfterLibraryChanged();
        var first = gone.Count > 0 ? Path.GetFileName(gone[0]) : "";
        int goneCount = gone.Count;
        SetStatus(() => goneCount == 1 ? Loc.F("Vm_DeletedOne", first) : Loc.F("Vm_DeletedMany", goneCount));
        return gone;
    }

    /// <summary>Tags einer Datei geändert: Index und Galerie-Eintrag aktualisieren.</summary>
    public void FileChanged(string path) => FilesChanged([path]);

    /// <summary>Mehrere Dateien geändert (z. B. Tags für eine Auswahl gesetzt).</summary>
    public void FilesChanged(IReadOnlyCollection<string> paths)
    {
        if (_index == null || paths.Count == 0) return;
        var updated = new Dictionary<string, CaptureSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            _index.RefreshFile(p);
            if (_index.GetByPath(p) is { } s) updated[s.Path] = s;
        }
        ReplaceItems(Items.Select(i => updated.TryGetValue(i.Summary.Path, out var s) ? s : i.Summary));
        AfterLibraryChanged();
    }

    /// <summary>Tags auf den Kacheln der Galerie anzeigen (Einstellung wird gespeichert).</summary>
    public bool ShowTileTags
    {
        get => _settings.ShowTileTags;
        set { if (_settings.ShowTileTags == value) return; _settings.ShowTileTags = value; _settings.Save(); OnPropertyChanged(); }
    }

    private void ReplaceItems(IEnumerable<CaptureSummary> summaries)
    {
        var selectedPaths = new HashSet<string>(_selected.Select(i => i.Summary.Path), StringComparer.OrdinalIgnoreCase);
        var list = summaries.Select((s, i) => new CaptureItem(s, i)).ToList();
        _selected.Clear();
        foreach (var item in list.Where(i => selectedPaths.Contains(i.Summary.Path))) { item.IsSelected = true; _selected.Add(item); }
        _anchorIndex = -1;
        PreserveScroll = true;
        Items = list;
        PreserveScroll = false;
        int count = list.Count;
        SetResultText(() => Loc.F("Vm_Hits", count));
        UpdateSelectionText();
    }

    private void AfterLibraryChanged()
    {
        LoadFilters();
        BuildDateTree();
    }

    public static void ShowInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    /// <summary>
    /// Öffnet die .snagx-Datei im Snagit Editor. Unser Programm schreibt dabei nichts –
    /// Änderungen entstehen nur, wenn du im Snagit Editor selbst speicherst.
    /// </summary>
    public static void OpenInSnagit(string path)
    {
        try
        {
            if (FindSnagitEditor() is { } exe)
                Process.Start(new ProcessStartInfo(exe) { ArgumentList = { path }, UseShellExecute = false });
            else // Fallback: Windows-Dateizuordnung für .snagx
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Vm_SnagitStartFailed", Loc.Error(ex)), AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string? FindSnagitEditor()
    {
        // 1. Dateizuordnung: HKCR\.snagx → ProgId → shell\open\command
        try
        {
            var progId = Registry.ClassesRoot.OpenSubKey(".snagx")?.GetValue(null) as string;
            var cmd = progId == null ? null
                : Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command")?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(cmd))
            {
                cmd = cmd.Trim();
                var exe = cmd.StartsWith('"') ? cmd[1..cmd.IndexOf('"', 1)] : cmd.Split(' ')[0];
                if (File.Exists(exe)) return exe;
            }
        }
        catch { /* Registry nicht lesbar → nächster Versuch */ }

        // 2. Übliche Installationsorte
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            var dir = Path.Combine(root, "TechSmith");
            if (!Directory.Exists(dir)) continue;
            var hit = Directory.EnumerateFiles(dir, "SnagitEditor.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (hit != null) return hit;
        }
        return null;
    }

    public void Dispose()
    {
        Loc.LanguageChanged -= OnLanguageChanged;
        _scanCts?.Cancel();
        _index?.Dispose();
    }
}
