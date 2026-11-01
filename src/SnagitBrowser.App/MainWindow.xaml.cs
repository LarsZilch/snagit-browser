using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SnagitBrowser.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly DispatcherTimer _relayoutTimer;
    private ScrollViewer? _scroll;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _relayoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _relayoutTimer.Tick += (_, _) => { _relayoutTimer.Stop(); Relayout(keepPosition: true); };

        _vm.PropertyChanged += Vm_PropertyChanged;
        Loaded += async (_, _) =>
        {
            _scroll = Gallery.Template.FindName("PART_Scroll", Gallery) as ScrollViewer;
            _vm.DpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            SearchBox.Focus();
            await _vm.InitializeAsync();
        };
        // Fenster auf einen Monitor mit anderer Skalierung verschoben
        DpiChanged += (_, e) => _vm.DpiScale = e.NewDpi.DpiScaleX;
        Closed += (_, _) => _vm.Dispose();
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Items):
                Relayout(keepPosition: _vm.PreserveScroll);
                break;
            case nameof(MainViewModel.RowHeight):
            case nameof(MainViewModel.DpiScale):
                _relayoutTimer.Stop(); _relayoutTimer.Start();
                break;
        }
    }

    private void Gallery_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) { _relayoutTimer.Stop(); _relayoutTimer.Start(); }
    }

    /// <summary>Zeilen für die aktuelle Breite neu berechnen; optional die Scroll-Position halten.</summary>
    private void Relayout(bool keepPosition)
    {
        // Breite: Galerie minus Scrollbar minus linker Rand minus rechter Abstand der letzten Kachel
        double width = Gallery.ActualWidth - SystemParameters.VerticalScrollBarWidth - Gallery.Padding.Left - 6;
        if (width <= 50) return;

        var anchor = keepPosition && _scroll != null ? _vm.ItemAtOffset(_scroll.VerticalOffset) : null;
        _vm.BuildRows(width);
        if (_scroll == null) return;
        if (anchor != null)
        {
            var offset = _vm.OffsetOf(anchor);
            Dispatcher.BeginInvoke(() => _scroll.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
        }
        else _scroll.ScrollToTop();
    }

    private void ScrollIntoView(CaptureItem item)
    {
        if (_scroll == null) return;
        double top = _vm.OffsetOf(item);
        double bottom = top + item.DisplayHeight;
        if (top < _scroll.VerticalOffset) _scroll.ScrollToVerticalOffset(top);
        else if (bottom > _scroll.VerticalOffset + _scroll.ViewportHeight)
            _scroll.ScrollToVerticalOffset(bottom - _scroll.ViewportHeight + 6);
    }

    // ---------- Maus ----------

    /// <summary>Strg + Mausrad ändert die Vorschaugröße statt zu scrollen.</summary>
    private void Gallery_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        _vm.ZoomRows(e.Delta);
        e.Handled = true;
    }

    private void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CaptureItem item) return;
        Gallery.Focus();
        if (e.ClickCount == 2) { Open(item); e.Handled = true; return; }
        var mods = Keyboard.Modifiers;
        _vm.Click(item, mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
    }

    private void Tile_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Rechtsklick auf nicht ausgewählte Kachel: diese auswählen, dann Kontextmenü
        if ((sender as FrameworkElement)?.DataContext is CaptureItem { IsSelected: false } item)
            _vm.Click(item, false, false);
        Gallery.Focus();
    }

    // ---------- Tastatur ----------

    private void Gallery_KeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.Enter:
                OpenSelected(); e.Handled = true; break;
            case Key.Delete:
                DeleteSelected(); e.Handled = true; break;
            case Key.T when ctrl:
                EditTagsOfSelection(); e.Handled = true; break;
            case Key.A when ctrl:
                _vm.SelectAll(); e.Handled = true; break;
            case Key.C when ctrl:
                _vm.CopyTexts(_vm.SelectedItems.ToList()); e.Handled = true; break;
            case Key.Left:
            case Key.Right:
                if (_vm.MoveSelection(e.Key == Key.Left ? -1 : 1) is { } moved) ScrollIntoView(moved);
                e.Handled = true; break;
        }
    }

    // ---------- Aktionen ----------

    private void Open(CaptureItem item) =>
        new DetailWindow(_vm, _vm.Items, item.Index) { Owner = this }.Show();

    private void DeleteSelected()
    {
        var paths = _vm.SelectedItems.Select(i => i.Summary.Path).ToList();
        if (paths.Count == 0) return;
        _vm.DeleteFiles(paths, this);
        Gallery.Focus();
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    /// <summary>Modales Fenster zum Setzen/Entfernen von Tags für alle ausgewählten Captures.</summary>
    private void EditTagsOfSelection()
    {
        var paths = _vm.SelectedItems.Select(i => i.Summary.Path).ToList();
        if (paths.Count == 0)
        {
            MessageBox.Show(this, "Bitte zuerst ein oder mehrere Bilder auswählen (Klick, Strg+Klick, Umschalt+Klick).",
                "Tags bearbeiten", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new BulkTagWindow(_vm, paths) { Owner = this }.ShowDialog();
        Gallery.Focus();
    }

    private void EditTags_Click(object sender, RoutedEventArgs e) => EditTagsOfSelection();

    private void OpenSelected()
    {
        if (_vm.SelectedItems.FirstOrDefault() is { } item) Open(item);
    }

    private List<CaptureItem> Selected => _vm.SelectedItems.ToList();

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void OpenInSnagit_Click(object sender, RoutedEventArgs e)
    {
        // Mehrfachauswahl: höchstens 10 Dateien auf einmal, damit nicht versehentlich hunderte Fenster aufgehen
        foreach (var item in _vm.SelectedItems.Take(10)) MainViewModel.OpenInSnagit(item.Summary.Path);
    }
    private void CopyTexts_Click(object sender, RoutedEventArgs e) => _vm.CopyTexts(Selected);
    private void ExportImages_Click(object sender, RoutedEventArgs e) => _vm.ExportImages(Selected);
    private void CopyFiles_Click(object sender, RoutedEventArgs e) => _vm.CopyFiles(Selected);

    private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItems.FirstOrDefault() is { } item) MainViewModel.ShowInExplorer(item.Summary.Path);
    }

    private void TagFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string name) _vm.ToggleTagFilter(name);
    }

    private void DateTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _vm.SelectDateNode(e.NewValue as DateNode);
}
