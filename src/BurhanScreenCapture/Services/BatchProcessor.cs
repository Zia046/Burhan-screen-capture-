using System.Windows.Media.Imaging;
using BurhanScreenCapture.Editor;

namespace BurhanScreenCapture.Services;

public sealed class BatchOptions
{
    public string OutputFolder { get; set; } = "";
    public ImageFormatOption Format { get; set; } = ImageFormatOption.Jpg;
    public int Quality { get; set; } = 85;
    public int MaxWidth { get; set; }   // 0 = keep
    public string? Watermark { get; set; }
    public string? TemplateName { get; set; }
}

/// <summary>Pro: converts/resizes/compresses/watermarks many images. Originals are never modified.</summary>
public static class BatchProcessor
{
    public static async Task<(int ok, List<string> errors)> RunAsync(IReadOnlyList<string> files, BatchOptions o, IProgress<int> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(o.OutputFolder);
        int ok = 0;
        var errors = new List<string>();
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var f = files[i];
            try
            {
                // Imaging is done on the UI thread in small steps to keep WPF objects simple; yield between files.
                BitmapSource img = ImageExportService.Load(f);
                if (o.MaxWidth > 0 && img.PixelWidth > o.MaxWidth)
                    img = ImageEffects.Resize(img, o.MaxWidth, Math.Max(1, (int)Math.Round(img.PixelHeight * (double)o.MaxWidth / img.PixelWidth)));
                if (!string.IsNullOrWhiteSpace(o.TemplateName) && ImageEffects.Templates.FirstOrDefault(t => t.Name == o.TemplateName) is { } tpl)
                    img = ImageEffects.ApplyTemplate(img, tpl);
                if (!string.IsNullOrWhiteSpace(o.Watermark))
                    img = Burn(img, new WatermarkAnnotation(o.Watermark!, SettingsService.Current.WatermarkOpacity));
                var name = Path.GetFileNameWithoutExtension(f);
                var dest = AppPaths.UniqueFile(o.OutputFolder, name, ImageExportService.Extension(o.Format));
                ImageExportService.Save(img, dest, o.Format, o.Quality);
                ok++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(f)}: {ex.Message}");
                Log.Warn($"Batch failed for {f}: {ex.Message}");
            }
            progress.Report(i + 1);
            await Task.Yield();
        }
        return (ok, errors);
    }

    private static BitmapSource Burn(BitmapSource img, Annotation a)
    {
        int w = img.PixelWidth, h = img.PixelHeight;
        var canvas = new System.Windows.Controls.Canvas { Width = w, Height = h };
        canvas.Children.Add(a.Build(img));
        var root = new System.Windows.Controls.Grid { Width = w, Height = h };
        root.Children.Add(new System.Windows.Controls.Image { Source = img });
        root.Children.Add(canvas);
        root.Measure(new System.Windows.Size(w, h));
        root.Arrange(new System.Windows.Rect(0, 0, w, h));
        root.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(root);
        rtb.Freeze();
        return rtb;
    }
}
