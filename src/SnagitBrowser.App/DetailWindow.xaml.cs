using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SnagitBrowser.Core;

namespace SnagitBrowser.App;

public partial class DetailWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly List<CaptureItem> _items;
    private readonly string _sourceRoot;
    private int _pos;
    private SnagxDocument? _doc;
    private byte[]? _imageBytes;

    // Tag-Bearbeitung: _savedTags = Stand in der Datei, _editTags = aktuelle (evtl. ungespeicherte) Auswahl
    private FileStamp _stamp;
    private List<string> _savedTags = [];
    private List<string> _editTags = [];
    private readonly HashSet<string> _knownTags = new(StringComparer.OrdinalIgnoreCase);
    private static readonly StringComparer TagOrder = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), ignoreCase: true);

    public DetailWindow(MainViewModel vm, IReadOnlyList<CaptureItem> items, int pos)
    {
        InitializeComponent();
        _vm = vm;
        _items = items.ToList();
        _pos = Math.Max(0, pos);
        _sourceRoot = vm.SourceRoot;
        foreach (var t in vm.AllTagNames()) _knownTags.Add(t);
        Loaded += (_, _) => Show(_pos);
        Closing += (_, e) => { if (!SavePendingTags()) e.Cancel = true; };
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>Im Code erzeugte Texte (Metadaten, Hinweis, Fehlertext) in der neuen Sprache anzeigen.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_items.Count == 0) return;
        if (_doc != null) { FillMetadata(); UpdateHint(); }
        else if (_readError != null) TextsBox.Text = Loc.F("Detail_ReadError", Loc.Error(_readError));
    }

    private Exception? _readError;

    private CaptureItem Current => _items[_pos];
    private bool TagsDirty => _doc != null && !_editTags.SequenceEqual(_savedTags);

    private void Show(int pos)
    {
        if (pos < 0 || pos >= _items.Count) return;
        if (pos != _pos && !SavePendingTags()) return;
        _pos = pos;
        PrevButton.IsEnabled = _pos > 0;
        NextButton.IsEnabled = _pos < _items.Count - 1;
        var path = Current.Summary.Path;
        TagList.ItemsSource = null;
        NoTagsText.Visibility = Visibility.Visible;
        _readError = null;
        try
        {
            _stamp = FileStamp.Of(path);
            _doc = SnagxReader.Read(path);
        }
        catch (Exception ex)
        {
            _doc = null;
            _readError = ex;
            _savedTags = []; _editTags = [];
            UpdateTagUi();
            TextsBox.Text = Loc.F("Detail_ReadError", Loc.Error(ex));
            ImageView.Source = null;
            MetaList.ItemsSource = null;
            return;
        }

        var m = _doc.Metadata;
        var page = _doc.Pages.FirstOrDefault();
        _savedTags = m.Tags.ToList();
        _editTags = m.Tags.ToList();
        foreach (var t in m.Tags) _knownTags.Add(t);
        NewTagBox.Text = "";
        UpdateTagUi();
        FillMetadata();

        TextsBox.Text = string.Join("\n\n────────\n\n", _doc.Texts);

        // Ohne Annotationen ist das Original identisch, aber in voller Auflösung
        _hasAnnotations = page?.Objects.Count > 0;
        OriginalToggle.IsChecked = false;
        // Eigener Zoomfaktor (≠ 100 %) bleibt beim Blättern erhalten, sonst startet das Bild eingepasst
        if (_zoom is { } z && Math.Abs(z - 1.0) < 0.005) _zoom = null;
        ImageScroll.ScrollToHome();
        OriginalToggle.Visibility = _hasAnnotations ? Visibility.Visible : Visibility.Collapsed;
        LoadImage();
    }

    private bool _hasAnnotations;

    private void FillMetadata()
    {
        if (_doc == null) return;
        var m = _doc.Metadata;
        var page = _doc.Pages.FirstOrDefault();
        MetaList.ItemsSource = new List<KeyValuePair<string, string>>
        {
            new(Loc.T("Meta_Date"), _doc.EffectiveDate.ToString(Loc.T("Fmt_DateTime"), Loc.Culture)),
            new(Loc.T("Meta_App"), m.AppName),
            new(Loc.T("Meta_Company"), m.AppCompany),
            new(Loc.T("Meta_Version"), m.AppVersion),
            new(Loc.T("Meta_Window"), m.WindowName),
            new(Loc.T("Meta_Url"), m.WebUrl),
            new(Loc.T("Meta_Size"), page == null ? "" : $"{page.Width} × {page.Height} px"),
            new(Loc.T("Meta_Objects"), page == null ? "" : string.Join(", ", page.Objects.GroupBy(o => o.ToolMode).Select(g => $"{g.Count()}× {g.Key}"))),
            new(Loc.T("Meta_Snagit"), page?.SoftwareVersion ?? ""),
            new(Loc.T("Meta_System"), m.OperatingSystemVersion),
            new(Loc.T("Meta_File"), Current.Summary.Path),
        }.Where(kv => !string.IsNullOrEmpty(kv.Value)).ToList();
    }

    private void LoadImage()
    {
        var path = Current.Summary.Path;
        bool original = !_hasAnnotations || OriginalToggle.IsChecked == true;
        try
        {
            _imageBytes = original ? SnagxReader.ReadOriginalImage(path) : null;
            _imageBytes ??= SnagxReader.ReadRenderedImage(path);
            ImageView.Source = _imageBytes == null ? null : ImageLoader.Decode(_imageBytes);
        }
        catch { ImageView.Source = null; _imageBytes = null; }

        UpdateHint();
        ApplyFit();
    }

    /// <summary>Hinweis, wenn Snagit das Bild mit Annotationen verkleinert gespeichert hat.</summary>
    private void UpdateHint()
    {
        bool original = !_hasAnnotations || OriginalToggle.IsChecked == true;
        var page = _doc?.Pages.FirstOrDefault();
        if (_hasAnnotations && ImageView.Source is BitmapSource b && page != null &&
            Math.Max(page.Width, page.Height) > Math.Max(b.PixelWidth, b.PixelHeight))
        {
            HintText.Text = original
                ? Loc.F("Detail_HintOriginal", page.Width, page.Height)
                : Loc.F("Detail_HintReduced", b.PixelWidth, b.PixelHeight, page.Width, page.Height);
            HintBar.Visibility = Visibility.Visible;
        }
        else HintBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Zoomfaktor relativ zu 1:1 Bildschirmpixeln. null = "Einpassen" (verkleinert bei Bedarf,
    /// vergrößert aber nie über 100 %, damit es scharf bleibt).
    /// </summary>
    private double? _zoom;
    private const double MinZoom = 0.05, MaxZoom = 8;

    /// <summary>Bildgröße bei 100 % in geräteunabhängigen Einheiten (1 Bildpixel = 1 Bildschirmpixel).</summary>
    private Size NaturalSize()
    {
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return ImageView.Source is BitmapSource b ? new Size(b.PixelWidth / dpi, b.PixelHeight / dpi) : Size.Empty;
    }

    private void ApplyFit()
    {
        var size = NaturalSize();
        bool fit = _zoom == null;
        FitToggle.IsChecked = fit;

        if (fit || size.IsEmpty)
        {
            ImageView.Stretch = Stretch.Uniform;
            ImageView.Width = ImageView.Height = double.NaN;
            ImageView.MaxWidth = size.IsEmpty ? double.PositiveInfinity : size.Width;
            ImageView.MaxHeight = size.IsEmpty ? double.PositiveInfinity : size.Height;
        }
        else
        {
            ImageView.Stretch = Stretch.Fill;
            ImageView.MaxWidth = ImageView.MaxHeight = double.PositiveInfinity;
            ImageView.Width = size.Width * _zoom!.Value;
            ImageView.Height = size.Height * _zoom.Value;
        }
        ImageScroll.HorizontalScrollBarVisibility = fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        ImageScroll.VerticalScrollBarVisibility = fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        ImageScroll.Cursor = fit ? null : Cursors.SizeAll;
        UpdateZoomText();
    }

    private void UpdateZoomText()
    {
        var size = NaturalSize();
        double scale = _zoom ?? (size.IsEmpty || ImageView.ActualWidth <= 0 ? 1 : ImageView.ActualWidth / size.Width);
        ZoomText.Text = $"{scale * 100:0} %";
    }

    private void ImageView_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateZoomText();

    /// <summary>Zoomt um den Faktor; der Bildpunkt unter der Maus (oder die Mitte) bleibt an seiner Stelle.</summary>
    private void ZoomBy(double factor, Point? mouseInViewport = null)
    {
        var size = NaturalSize();
        if (size.IsEmpty || ImageView.ActualWidth <= 0) return;

        double current = _zoom ?? ImageView.ActualWidth / size.Width;
        double next = Math.Clamp(current * factor, MinZoom, MaxZoom);

        // Position des Ankerpunkts relativ zum Bild merken (0..1)
        var vp = mouseInViewport ?? new Point(ImageScroll.ViewportWidth / 2, ImageScroll.ViewportHeight / 2);
        var onImage = ImageScroll.TranslatePoint(vp, ImageView);
        double fx = Math.Clamp(onImage.X / ImageView.ActualWidth, 0, 1);
        double fy = Math.Clamp(onImage.Y / ImageView.ActualHeight, 0, 1);

        _zoom = next;
        ApplyFit();
        ImageScroll.UpdateLayout();
        ImageScroll.ScrollToHorizontalOffset(Math.Max(0, fx * ImageView.ActualWidth - vp.X));
        ImageScroll.ScrollToVerticalOffset(Math.Max(0, fy * ImageView.ActualHeight - vp.Y));
    }

    private void ResetZoom()
    {
        _zoom = null;
        ApplyFit();
    }

    // ---------- Mausrad: allein = blättern, mit Strg = zoomen ----------

    private int _wheelAccumulator;

    private void ImageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            ZoomBy(Math.Pow(1.15, e.Delta / 120.0), e.GetPosition(ImageScroll));
            return;
        }
        // Feinstufige Mausräder/Touchpads senden kleine Schritte: erst ab einer Raste blättern
        _wheelAccumulator += e.Delta;
        while (_wheelAccumulator >= 120) { _wheelAccumulator -= 120; Show(_pos - 1); }
        while (_wheelAccumulator <= -120) { _wheelAccumulator += 120; Show(_pos + 1); }
    }

    // ---------- Verschieben mit gedrückter Maustaste (wenn gezoomt) ----------

    private Point? _dragStart;
    private Point _dragOffset;

    private static bool IsOnScrollBar(object source)
    {
        for (var d = source as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.ScrollBar) return true;
        return false;
    }

    private void ImageScroll_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsOnScrollBar(e.OriginalSource)) return;
        if (e.ClickCount == 2)
        {
            // Doppelklick: zwischen Einpassen und 100 % wechseln
            if (_zoom == null) ZoomBy(1 / Math.Max(0.0001, ImageView.ActualWidth / NaturalSize().Width), e.GetPosition(ImageScroll));
            else ResetZoom();
            e.Handled = true;
            return;
        }
        if (_zoom == null) return;
        _dragStart = e.GetPosition(ImageScroll);
        _dragOffset = new Point(ImageScroll.HorizontalOffset, ImageScroll.VerticalOffset);
        ImageScroll.CaptureMouse();
        e.Handled = true;
    }

    private void ImageScroll_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var p = e.GetPosition(ImageScroll);
        ImageScroll.ScrollToHorizontalOffset(_dragOffset.X - (p.X - start.X));
        ImageScroll.ScrollToVerticalOffset(_dragOffset.Y - (p.Y - start.Y));
    }

    private void ImageScroll_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        ImageScroll.ReleaseMouseCapture();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyFit();
    }

    /// <summary>
    /// Blättern per Tastatur. PreviewKeyDown, damit Buttons/Textfelder die Pfeiltasten nicht abfangen.
    /// Mit Umschalt bleiben die Pfeiltasten im Textfeld für die Textauswahl.
    /// </summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { SavePendingTags(); e.Handled = true; return; }
        if (e.Key == Key.F1) { HelpWindow.ShowFor(this); e.Handled = true; return; }
        // Im Tag-Eingabefeld bleiben Pfeile, Pos1/Ende und Entf beim Textfeld
        if (e.OriginalSource == NewTagBox && e.Key != Key.Escape && Keyboard.Modifiers != ModifierKeys.Control) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && e.OriginalSource is TextBox) return;
        bool inTexts = e.OriginalSource == TextsBox;
        switch (e.Key)
        {
            case Key.Delete when !inTexts && Keyboard.Modifiers == ModifierKeys.None:
                DeleteCurrent(); e.Handled = true; break;
            case Key.Escape when e.OriginalSource == NewTagBox && NewTagBox.Text.Length > 0:
                NewTagBox.Text = ""; e.Handled = true; break;
            case Key.Left:
            case Key.PageUp:
            case Key.Up when !inTexts:
                Show(_pos - 1); e.Handled = true; break;
            case Key.Right:
            case Key.PageDown:
            case Key.Down when !inTexts:
                Show(_pos + 1); e.Handled = true; break;
            case Key.Home when !inTexts:
                Show(0); e.Handled = true; break;
            case Key.End when !inTexts:
                Show(_items.Count - 1); e.Handled = true; break;
            case Key.Escape:
                Close(); e.Handled = true; break;
            case Key.E when Keyboard.Modifiers == ModifierKeys.Control:
                MainViewModel.OpenInSnagit(Current.Summary.Path); e.Handled = true; break;
            // Zoom per Tastatur: Strg+Plus / Strg+Minus / Strg+0 (Einpassen)
            case Key.OemPlus or Key.Add when Keyboard.Modifiers == ModifierKeys.Control:
                ZoomBy(1.15); e.Handled = true; break;
            case Key.OemMinus or Key.Subtract when Keyboard.Modifiers == ModifierKeys.Control:
                ZoomBy(1 / 1.15); e.Handled = true; break;
            case Key.D0 or Key.NumPad0 when Keyboard.Modifiers == ModifierKeys.Control:
                ResetZoom(); e.Handled = true; break;
        }
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => Show(_pos - 1);
    private void Next_Click(object sender, RoutedEventArgs e) => Show(_pos + 1);
    private void OriginalToggle_Click(object sender, RoutedEventArgs e) => LoadImage();

    /// <summary>Einpassen an = Bild ins Fenster; aus = 100 % (1 Bildpixel = 1 Bildschirmpixel).</summary>
    private void FitToggle_Click(object sender, RoutedEventArgs e)
    {
        _zoom = FitToggle.IsChecked == true ? null : 1.0;
        ApplyFit();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomBy(1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomBy(1 / 1.25);

    private void SaveImage_Click(object sender, RoutedEventArgs e)
    {
        if (_imageBytes == null) return;
        var dlg = new SaveFileDialog
        {
            Filter = Loc.T("Detail_PngFilter"),
            FileName = Path.GetFileNameWithoutExtension(Current.Summary.Path) + (OriginalToggle.IsChecked == true ? "_original" : "") + ".png",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dlg.ShowDialog(this) != true) return;
        try { Exporter.WriteBytes(dlg.FileName, _imageBytes, _sourceRoot); }
        catch (Exception ex) { MessageBox.Show(this, Loc.Error(ex), Loc.T("Detail_Save_Caption"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        if (ImageView.Source is BitmapSource b) Clipboard.SetImage(b);
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (_doc != null && _doc.Texts.Any()) Clipboard.SetText(string.Join("\n\n", _doc.Texts));
    }

    private void Explorer_Click(object sender, RoutedEventArgs e) => MainViewModel.ShowInExplorer(Current.Summary.Path);
    private void OpenInSnagit_Click(object sender, RoutedEventArgs e) => MainViewModel.OpenInSnagit(Current.Summary.Path);

    // ---------- Löschen ----------

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteCurrent();

    private void DeleteCurrent()
    {
        if (_items.Count == 0) return;
        var path = Current.Summary.Path;
        if (_vm.DeleteFiles([path], this).Count == 0) return;
        _items.RemoveAt(_pos);
        _savedTags = []; _editTags = []; _doc = null; // nichts mehr zu speichern
        if (_items.Count == 0) { Close(); return; }
        int next = Math.Min(_pos, _items.Count - 1);
        _pos = -1; // erzwingt Neuladen auch bei gleicher Position
        Show(next);
    }

    // ---------- Tags bearbeiten ----------

    private sealed record TagChoice(string Name, bool IsAssigned);

    private void UpdateTagUi()
    {
        TagList.ItemsSource = _editTags.ToList();
        NoTagsText.Visibility = _editTags.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TagSaveBar.Visibility = TagsDirty ? Visibility.Visible : Visibility.Collapsed;
        NewTagPlaceholder.Visibility = NewTagBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var filter = NewTagBox.Text.Trim();
        var assigned = new HashSet<string>(_editTags, StringComparer.OrdinalIgnoreCase);
        AllTagsList.ItemsSource = _knownTags
            .Where(t => filter.Length == 0 || t.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(t => t, TagOrder)
            .Select(t => new TagChoice(t, assigned.Contains(t)))
            .ToList();

        if (_items.Count > 0)
            Title = $"{(TagsDirty ? "● " : "")}{Path.GetFileName(Current.Summary.Path)}  ({_pos + 1}/{_items.Count})";
    }

    private void AddTag(string tag)
    {
        tag = tag.Trim();
        if (tag.Length == 0 || _doc == null) return;
        if (!_editTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            // Schreibweise eines vorhandenen Tags übernehmen (z. B. "backup" → "Backup")
            var existing = _knownTags.FirstOrDefault(k => string.Equals(k, tag, StringComparison.OrdinalIgnoreCase));
            _editTags.Add(existing ?? tag);
            _knownTags.Add(existing ?? tag);
        }
        UpdateTagUi();
    }

    private void RemoveTag(string tag)
    {
        _editTags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        UpdateTagUi();
    }

    private void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag) RemoveTag(tag);
    }

    private void TagChoice_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag) return;
        if (_editTags.Contains(tag, StringComparer.OrdinalIgnoreCase)) RemoveTag(tag); else AddTag(tag);
    }

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        AddTag(NewTagBox.Text);
        NewTagBox.Text = "";
        NewTagBox.Focus();
    }

    private void NewTagBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddTag(NewTagBox.Text);
        NewTagBox.Text = "";
        e.Handled = true;
    }

    private void NewTagBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateTagUi();

    private void SaveTags_Click(object sender, RoutedEventArgs e) => SavePendingTags();

    private void DiscardTags_Click(object sender, RoutedEventArgs e)
    {
        _editTags = _savedTags.ToList();
        UpdateTagUi();
    }

    /// <summary>
    /// Schreibt geänderte Tags in die .snagx-Datei (mit Sicherung, Prüfung und atomarem Austausch).
    /// Gibt false zurück, wenn der Benutzer nach einem Fehler beim aktuellen Bild bleiben möchte.
    /// </summary>
    private bool SavePendingTags()
    {
        if (!TagsDirty || _items.Count == 0) return true;
        var path = Current.Summary.Path;
        try
        {
            SnagxWriter.SetTags(path, _editTags, _stamp);
            _stamp = FileStamp.Of(path);
            _savedTags = _editTags.ToList();
            _vm.FileChanged(path);
            var name = Path.GetFileName(path);
            _vm.SetStatus(() => Loc.F("Detail_TagsSaved", name));
            UpdateTagUi();
            return true;
        }
        catch (FileChangedException)
        {
            MessageBox.Show(this, Loc.T("Detail_FileChanged"),
                Loc.T("Detail_SaveTags_Caption"), MessageBoxButton.OK, MessageBoxImage.Warning);
            _vm.FileChanged(path);
            var keep = _pos; _pos = -1; _savedTags = []; _editTags = [];
            Show(keep);
            return false;
        }
        catch (Exception ex)
        {
            var r = MessageBox.Show(this, Loc.F("Detail_SaveFailed", Loc.Error(ex)),
                Loc.T("Detail_SaveTags_Caption"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return false;
            _editTags = _savedTags.ToList();
            UpdateTagUi();
            return true;
        }
    }
}
