using System.Windows;
using System.Windows.Controls;
using BurhanScreenCapture.Editor;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views.Pages;

public partial class QuickToolsPage : UserControl, IPage
{
    private string[] _batchFiles = Array.Empty<string>();

    public QuickToolsPage()
    {
        InitializeComponent();
        BatchTemplate.ItemsSource = new[] { "No template" }.Concat(ImageEffects.Templates.Select(t => t.Name)).ToList();
        BatchTemplate.SelectedIndex = 0;
    }

    public void OnShown()
    {
        if (string.IsNullOrEmpty(BatchFolder.Text))
            BatchFolder.Text = Path.Combine(SettingsService.Current.ScreenshotFolder, "Processed");
    }

    private void OnToolbar(object sender, RoutedEventArgs e) => FloatingToolbar.ShowToolbar();
    private void OnShots(object sender, RoutedEventArgs e) => Dialogs.OpenFolder(SettingsService.Current.ScreenshotFolder);
    private void OnRecs(object sender, RoutedEventArgs e) => Dialogs.OpenFolder(SettingsService.Current.RecordingFolder);
    private void OnAudioOnly(object sender, RoutedEventArgs e) => _ = RecorderController.StartAsync(new RecordingRequest { Kind = RecordingSourceKind.AudioOnly });

    private async void OnOcrScreen(object sender, RoutedEventArgs e)
    {
        if (!Entitlements.Require(Feature.Ocr)) return;
        (PixelRect, System.Windows.Media.Imaging.BitmapSource)? r;
        using (await Workflows.HideOurWindowsAsync()) r = Workflows.SelectRegion(RegionSelectMode.Free, "Select the text to extract • Esc to cancel");
        if (r is not { } res) return;
        await ShowOcr(res.Item2);
    }

    private async void OnOcrFile(object sender, RoutedEventArgs e)
    {
        if (!Entitlements.Require(Feature.Ocr)) return;
        if (Dialogs.PickImage() is not { } f) return;
        try { await ShowOcr(ImageExportService.Load(f)); }
        catch (Exception ex) { MessageBox.Show("The image could not be opened: " + ex.Message, App.AppName); }
    }

    private static async Task ShowOcr(System.Windows.Media.Imaging.BitmapSource img)
    {
        var (text, err) = await OcrService.RecognizeAsync(img);
        if (err is not null) { MessageBox.Show(err, App.AppName, MessageBoxButton.OK, MessageBoxImage.Information); return; }
        new OcrResultWindow(text).Show();
    }

    private void OnTemplate(object sender, RoutedEventArgs e)
    {
        if (!Entitlements.Require(Feature.ScreenshotTemplates)) return;
        if (Dialogs.PickImage() is not { } f) return;
        try
        {
            var img = ImageExportService.Load(f);
            var t = TemplatePicker.Pick(Window.GetWindow(this));
            if (t is null) return;
            Workflows.OpenEditor(ImageEffects.ApplyTemplate(img, t), null);
        }
        catch (Exception ex) { MessageBox.Show("The image could not be opened: " + ex.Message, App.AppName); }
    }

    private void OnBatchFiles(object sender, RoutedEventArgs e)
    {
        if (!Entitlements.Require(Feature.BatchProcessing)) return;
        _batchFiles = Dialogs.PickImages();
        BatchCount.Text = _batchFiles.Length == 0 ? "No images selected" : $"{_batchFiles.Length} image(s) selected";
    }

    private void OnBatchFolder(object sender, RoutedEventArgs e)
    {
        if (Dialogs.PickFolder(BatchFolder.Text) is { } f) BatchFolder.Text = f;
    }

    private async void OnBatchRun(object sender, RoutedEventArgs e)
    {
        if (!Entitlements.Require(Feature.BatchProcessing)) return;
        if (_batchFiles.Length == 0) { MessageBox.Show("Choose some images first.", App.AppName); return; }
        if (AppPaths.ValidateWritableFolder(BatchFolder.Text) is { } err) { MessageBox.Show(err, App.AppName); return; }
        int.TryParse(BatchWidth.Text, out var maxW);
        var opts = new BatchOptions
        {
            OutputFolder = BatchFolder.Text,
            Format = BatchFormat.SelectedIndex switch { 0 => ImageFormatOption.Png, 2 => ImageFormatOption.Bmp, 3 => ImageFormatOption.Tiff, _ => ImageFormatOption.Jpg },
            Quality = (int)BatchQuality.Value,
            MaxWidth = Math.Max(0, maxW),
            Watermark = BatchWatermark.IsChecked == true ? SettingsService.Current.WatermarkText : null,
            TemplateName = BatchTemplate.SelectedIndex > 0 ? BatchTemplate.SelectedItem as string : null
        };
        BatchRun.IsEnabled = false;
        BatchProgress.Visibility = Visibility.Visible;
        BatchProgress.Maximum = _batchFiles.Length;
        BatchProgress.Value = 0;
        try
        {
            var progress = new Progress<int>(n => { BatchProgress.Value = n; BatchStatus.Text = $"{n} / {_batchFiles.Length}"; });
            var (ok, errors) = await BatchProcessor.RunAsync(_batchFiles, opts, progress, CancellationToken.None);
            BatchStatus.Text = $"Done: {ok} saved" + (errors.Count > 0 ? $", {errors.Count} failed" : "");
            if (errors.Count > 0) MessageBox.Show("Some images could not be processed:\n\n" + string.Join("\n", errors.Take(10)), App.AppName);
            Dialogs.OpenFolder(opts.OutputFolder);
        }
        finally
        {
            BatchRun.IsEnabled = true;
            BatchProgress.Visibility = Visibility.Collapsed;
        }
    }
}
