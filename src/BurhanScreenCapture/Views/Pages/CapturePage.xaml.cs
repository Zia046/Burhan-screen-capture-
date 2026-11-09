using System.Windows;
using System.Windows.Controls;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views.Pages;

public partial class CapturePage : UserControl, IPage
{
    private bool _loading;

    public CapturePage() => InitializeComponent();

    public void OnShown()
    {
        _loading = true;
        var s = SettingsService.Current;
        string Hk(HotkeyAction a) => s.Hotkeys.FirstOrDefault(h => h.Action == a)?.ToString() ?? "";
        HkArea.Text = Hk(HotkeyAction.CaptureRegion);
        HkFull.Text = Hk(HotkeyAction.CaptureFullScreen);
        HkWindow.Text = Hk(HotkeyAction.CaptureWindow);
        var monitors = ScreenCaptureService.Monitors();
        MonitorBox.ItemsSource = monitors;
        MonitorBox.SelectedItem = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        AutoSave.IsChecked = s.AutoSaveScreenshots;
        Clip.IsChecked = s.CopyToClipboardAfterCapture;
        CursorBox.IsChecked = s.IncludeCursorInScreenshot;
        After.SelectedIndex = (int)s.AfterCapture;
        Format.SelectedIndex = s.ScreenshotFormat == ImageFormatOption.Jpg ? 1 : 0;
        Folder.Text = s.ScreenshotFolder;
        FixedW.Text = s.FixedCaptureWidth.ToString();
        FixedH.Text = s.FixedCaptureHeight.ToString();
        _loading = false;
    }

    private void OnArea(object sender, RoutedEventArgs e) => Workflows.CaptureRegion();
    private void OnFull(object sender, RoutedEventArgs e) => Workflows.CaptureFullScreen(MonitorBox.SelectedItem as MonitorInfo);
    private void OnWindow(object sender, RoutedEventArgs e) => Workflows.CaptureActiveWindow(true);
    private void OnAll(object sender, RoutedEventArgs e) => Workflows.CaptureAllMonitors();
    private void OnScrolling(object sender, RoutedEventArgs e) => Workflows.CaptureScrolling();
    private void OnRepeat(object sender, RoutedEventArgs e) => Workflows.CaptureLastRegion();
    private void OnFixed(object sender, RoutedEventArgs e) { OnFixedSizeChanged(sender, e); Workflows.CaptureFixedSize(); }

    private void OnDelay(object sender, RoutedEventArgs e)
    {
        int sec = D10.IsChecked == true ? 10 : D5.IsChecked == true ? 5 : 3;
        var mode = DelayMode.SelectedIndex switch { 1 => CaptureMode.FullScreen, 2 => CaptureMode.Window, 3 => CaptureMode.AllMonitors, _ => CaptureMode.Region };
        Workflows.CaptureDelayed(mode, sec);
    }

    private void OnFixedSizeChanged(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(FixedW.Text, out var w) && int.TryParse(FixedH.Text, out var h))
        {
            SettingsService.Current.FixedCaptureWidth = Math.Clamp(w, 16, 16384);
            SettingsService.Current.FixedCaptureHeight = Math.Clamp(h, 16, 16384);
            SettingsService.Save();
        }
        FixedW.Text = SettingsService.Current.FixedCaptureWidth.ToString();
        FixedH.Text = SettingsService.Current.FixedCaptureHeight.ToString();
    }

    private void OnOption(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        var s = SettingsService.Current;
        s.AutoSaveScreenshots = AutoSave.IsChecked == true;
        s.CopyToClipboardAfterCapture = Clip.IsChecked == true;
        s.IncludeCursorInScreenshot = CursorBox.IsChecked == true;
        if (After.SelectedIndex >= 0) s.AfterCapture = (AfterCaptureAction)After.SelectedIndex;
        if (Format.SelectedIndex >= 0) s.ScreenshotFormat = Format.SelectedIndex == 1 ? ImageFormatOption.Jpg : ImageFormatOption.Png;
        SettingsService.Save();
    }

    private void OnChangeFolder(object sender, RoutedEventArgs e)
    {
        var folder = Dialogs.PickFolder(SettingsService.Current.ScreenshotFolder);
        if (folder is null) return;
        if (AppPaths.ValidateWritableFolder(folder) is { } err) { MessageBox.Show(err, App.AppName, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        SettingsService.Current.ScreenshotFolder = folder;
        SettingsService.Save();
        Folder.Text = folder;
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) => Dialogs.OpenFolder(SettingsService.Current.ScreenshotFolder);
}
