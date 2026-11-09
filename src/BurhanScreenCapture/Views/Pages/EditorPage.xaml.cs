using System.Windows;
using System.Windows.Controls;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views.Pages;

public partial class EditorPage : UserControl, IPage
{
    public EditorPage() => InitializeComponent();
    public void OnShown() { }

    private static void OpenPath(string f)
    {
        try { Workflows.OpenEditor(ImageExportService.Load(f), f); }
        catch (Exception ex) { MessageBox.Show("This image could not be opened: " + ex.Message, App.AppName, MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OnOpen(object sender, RoutedEventArgs e) { if (Dialogs.PickImage() is { } f) OpenPath(f); }

    private void OnPaste(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img) Workflows.OpenEditor(img, null);
            else MessageBox.Show("There is no image on the clipboard.", App.AppName);
        }
        catch (Exception ex) { MessageBox.Show("The clipboard could not be read: " + ex.Message, App.AppName); }
    }

    private async void OnCapture(object sender, RoutedEventArgs e)
    {
        (PixelRect, System.Windows.Media.Imaging.BitmapSource)? r;
        using (await Workflows.HideOurWindowsAsync()) r = Workflows.SelectRegion(RegionSelectMode.Free);
        if (r is { } res) Workflows.OpenEditor(res.Item2, null);
    }

    private void OnLast(object sender, RoutedEventArgs e)
    {
        var last = HistoryService.Entries.FirstOrDefault(h => h.Exists);
        if (last is null) { MessageBox.Show("No screenshots yet.", App.AppName); return; }
        OpenPath(last.FilePath);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) OpenPath(files[0]);
    }
}
