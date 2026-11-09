using global::Windows.Graphics.Imaging;
using global::Windows.Media.Ocr;
using global::Windows.Storage.Streams;
using BitmapSource = System.Windows.Media.Imaging.BitmapSource;

namespace BurhanScreenCapture.Services;

/// <summary>Pro: offline OCR using the Windows 10/11 built-in OCR engine (no internet, no extra download).</summary>
public static class OcrService
{
    public static async Task<(string Text, string? Error)> RecognizeAsync(BitmapSource image)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                         ?? OcrEngine.TryCreateFromLanguage(new global::Windows.Globalization.Language("en-US"));
            if (engine is null)
                return ("", "No OCR language is installed. Add a language with \"Optical character recognition\" in Windows Settings › Time & language › Language & region.");

            // Small text is recognised better when upscaled
            BitmapSource src = image;
            if (image.PixelHeight < 600 || image.PixelWidth < 600)
            {
                double k = Math.Min(3.0, Math.Min(OcrEngine.MaxImageDimension / (double)Math.Max(image.PixelWidth, image.PixelHeight), 2.0));
                if (k > 1.05) src = Editor.ImageEffects.Resize(image, (int)(image.PixelWidth * k), (int)(image.PixelHeight * k));
            }
            if (Math.Max(src.PixelWidth, src.PixelHeight) > OcrEngine.MaxImageDimension)
            {
                double k = OcrEngine.MaxImageDimension / (double)Math.Max(src.PixelWidth, src.PixelHeight);
                src = Editor.ImageEffects.Resize(src, Math.Max(1, (int)(src.PixelWidth * k)), Math.Max(1, (int)(src.PixelHeight * k)));
            }

            var png = ImageExportService.Encode(src, ImageFormatOption.Png, 100);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bmp);
            var text = string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
            return string.IsNullOrWhiteSpace(text) ? ("", "No text was found in this image.") : (text, null);
        }
        catch (Exception ex)
        {
            Log.Error("OCR failed", ex);
            return ("", "Text recognition failed: " + ex.Message);
        }
    }
}
