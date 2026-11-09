using System.Windows;
using System.Windows.Controls;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views.Pages;

public partial class DashboardPage : UserControl, IPage
{
    public DashboardPage()
    {
        InitializeComponent();
        HistoryService.Changed += () => Dispatcher.BeginInvoke(LoadRecent);
    }

    public void OnShown()
    {
        var h = DateTime.Now.Hour;
        Greeting.Text = h < 12 ? "Good morning" : h < 17 ? "Good afternoon" : "Good evening";
        var region = SettingsService.Current.Hotkeys.FirstOrDefault(k => k.Action == HotkeyAction.CaptureRegion);
        HotkeyHint.Text = region is null ? "" : $"{region}: select an area from anywhere";
        LoadRecent();
    }

    private void LoadRecent()
    {
        var items = HistoryService.Entries.Where(e => e.ThumbPath != null && File.Exists(e.ThumbPath)).Take(6).ToList();
        Recent.ItemsSource = items;
        NoRecent.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private MainWindow? Main => Window.GetWindow(this) as MainWindow;

    private void OnTile(object sender, RoutedEventArgs e) => Main?.Navigate((string)((Button)sender).Tag);
    private void OnArea(object sender, RoutedEventArgs e) => Workflows.CaptureRegion();
    private void OnFull(object sender, RoutedEventArgs e) => Workflows.CaptureFullScreen(null);
    private void OnWindow(object sender, RoutedEventArgs e) => Workflows.CaptureActiveWindow(true);
    private void OnRecord(object sender, RoutedEventArgs e) => Main?.Navigate("Recorder");

    private void OnRecent(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not HistoryEntry h) return;
        if (!h.Exists) { MessageBox.Show("This file was moved or deleted.", App.AppName); HistoryService.RemoveMissing(); return; }
        try { Workflows.OpenEditor(ImageExportService.Load(h.FilePath), h.FilePath); }
        catch (Exception ex) { MessageBox.Show("The image could not be opened: " + ex.Message, App.AppName); }
    }
}
