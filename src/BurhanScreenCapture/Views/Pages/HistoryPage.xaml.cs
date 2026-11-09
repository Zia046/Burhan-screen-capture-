using System.Windows;
using System.Windows.Controls;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views.Pages;

public partial class HistoryPage : UserControl, IPage
{
    private bool _searchBlocked;

    public HistoryPage()
    {
        InitializeComponent();
        HistoryService.Changed += () => Dispatcher.BeginInvoke(Refresh);
    }

    public void OnShown() => Refresh();

    private void Refresh()
    {
        var all = HistoryService.Entries;
        var q = Search.Text.Trim();
        IEnumerable<HistoryEntry> items = all;
        if (q.Length > 0 && Entitlements.IsAllowed(Feature.HistorySearch))
            items = all.Where(e => e.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                   || e.DateText.Contains(q, StringComparison.OrdinalIgnoreCase)
                                   || (e.OcrText?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        var list = items.ToList();
        HistoryList.ItemsSource = list;
        Empty.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LimitText.Text = Entitlements.IsAllowed(Feature.UnlimitedHistory)
            ? $"{all.Count} screenshot(s) • unlimited history (Pro)"
            : $"{all.Count} of {FeatureCatalog.FreeHistoryLimit} • Free edition keeps your latest {FeatureCatalog.FreeHistoryLimit} screenshots. Your image files are never deleted.";
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Search.Text.Length > 0 && !Entitlements.IsAllowed(Feature.HistorySearch))
        {
            if (_searchBlocked) return;
            _searchBlocked = true;
            Entitlements.Require(Feature.HistorySearch);
            if (!Entitlements.IsAllowed(Feature.HistorySearch)) Search.Text = "";
            _searchBlocked = false;
        }
        Refresh();
    }

    private static HistoryEntry? Entry(object sender) => (sender as FrameworkElement)?.Tag as HistoryEntry;

    private static bool CheckExists(HistoryEntry e)
    {
        if (e.Exists) return true;
        if (MessageBox.Show("This file was moved or deleted. Remove it from history?", App.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            HistoryService.Remove(e, false);
        return false;
    }

    private void OnOpen(object sender, RoutedEventArgs e) { if (Entry(sender) is { } h && CheckExists(h)) Dialogs.OpenFile(h.FilePath); }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Entry(sender) is not { } h || !CheckExists(h)) return;
        try { ImageExportService.CopyToClipboard(ImageExportService.Load(h.FilePath)); }
        catch (Exception ex) { MessageBox.Show("Could not copy: " + ex.Message, App.AppName); }
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (Entry(sender) is not { } h || !CheckExists(h)) return;
        try { Workflows.OpenEditor(ImageExportService.Load(h.FilePath), h.FilePath); }
        catch (Exception ex) { MessageBox.Show("Could not open: " + ex.Message, App.AppName); }
    }

    private void OnThumb(object sender, System.Windows.Input.MouseButtonEventArgs e) => OnEdit(sender, e);

    private void OnFolder(object sender, RoutedEventArgs e) { if (Entry(sender) is { } h) Dialogs.ShowInFolder(h.FilePath); }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Entry(sender) is not { } h) return;
        var r = MessageBox.Show($"Delete \"{h.FileName}\"?\n\nYes = delete the file from disk\nNo = only remove it from history",
            App.AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return;
        HistoryService.Remove(h, r == MessageBoxResult.Yes);
    }

    private void OnRemoveMissing(object sender, RoutedEventArgs e) => HistoryService.RemoveMissing();

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Clear the history list? Your image files will NOT be deleted.", App.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            HistoryService.Clear();
    }
}
