using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using BurhanScreenCapture.Native;
using BurhanScreenCapture.Services;
using Microsoft.Win32;

namespace BurhanScreenCapture.Views;

public partial class CapturePreviewWindow : Window
{
    private readonly BitmapSource _image;
    private string? _path;

    public CapturePreviewWindow(BitmapSource image, string? savedPath)
    {
        InitializeComponent();
        _image = image;
        _path = savedPath;
        Preview.Source = image;
        UpdateInfo();
        SourceInitialized += (_, _) => WindowEffects.ApplyTitleBarTheme(this, ThemeManager.IsDark);
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 16;
            Top = wa.Bottom - ActualHeight - 16;
            Activate();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) OnCopy(this, e);
            else if (e.Key == Key.E || e.Key == Key.Enter) OnEdit(this, e);
        };
    }

    private void UpdateInfo()
    {
        Info.Text = $"{_image.PixelWidth} × {_image.PixelHeight} px  •  " + (_path is null ? "Not saved" : $"Saved: {_path}");
        FolderBtn.IsEnabled = DeleteBtn.IsEnabled = _path is not null;
        Info.ToolTip = _path;
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        Workflows.OpenEditor(_image, _path);
        Close();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        Info.Text = ImageExportService.CopyToClipboard(_image) ? "Copied to clipboard" : "Clipboard is busy — try again";
    }

    private void OnSaveAs(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG|*.png|JPG|*.jpg",
            FileName = ImageExportService.BuildFileName(SettingsService.Current.FileNamePattern, DateTime.Now),
            InitialDirectory = Directory.Exists(SettingsService.Current.ScreenshotFolder) ? SettingsService.Current.ScreenshotFolder : null
        };
        if (dlg.ShowDialog(this) != true) return;
        var saved = Workflows.SaveScreenshot(_image, dlg.FileName);
        if (saved is not null) { _path = saved; UpdateInfo(); }
    }

    private void OnFolder(object sender, RoutedEventArgs e) { if (_path is not null) Dialogs.ShowInFolder(_path); }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_path is null) return;
        if (MessageBox.Show(this, "Delete this screenshot from disk?", App.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var entry = HistoryService.Entries.FirstOrDefault(h => h.FilePath == _path);
        if (entry is not null) HistoryService.Remove(entry, true);
        else try { File.Delete(_path); } catch { }
        Close();
    }
}
