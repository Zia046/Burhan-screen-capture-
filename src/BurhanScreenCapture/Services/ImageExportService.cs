using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BurhanScreenCapture.Services;

public static class ImageExportService
{
    public static string Extension(ImageFormatOption f) => f switch
    {
        ImageFormatOption.Jpg => ".jpg",
        ImageFormatOption.Bmp => ".bmp",
        ImageFormatOption.Tiff => ".tiff",
        ImageFormatOption.Gif => ".gif",
        _ => ".png"
    };

    public static ImageFormatOption FormatFromPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => ImageFormatOption.Jpg,
        ".bmp" => ImageFormatOption.Bmp,
        ".tif" or ".tiff" => ImageFormatOption.Tiff,
        ".gif" => ImageFormatOption.Gif,
        _ => ImageFormatOption.Png
    };

    /// <summary>BMP/TIFF/GIF export and custom JPG quality are Pro (Advanced Export / Advanced Compression).</summary>
    public static bool IsProFormat(ImageFormatOption f) => f is ImageFormatOption.Bmp or ImageFormatOption.Tiff or ImageFormatOption.Gif;

    public static BitmapEncoder CreateEncoder(ImageFormatOption f, int jpgQuality) => f switch
    {
        ImageFormatOption.Jpg => new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpgQuality, 10, 100) },
        ImageFormatOption.Bmp => new BmpBitmapEncoder(),
        ImageFormatOption.Tiff => new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw },
        ImageFormatOption.Gif => new GifBitmapEncoder(),
        _ => new PngBitmapEncoder()
    };

    public static string BuildFileName(string pattern, DateTime t)
    {
        var name = (string.IsNullOrWhiteSpace(pattern) ? "Screenshot {date} {time}" : pattern)
            .Replace("{date}", t.ToString("yyyy-MM-dd"))
            .Replace("{time}", t.ToString("HH-mm-ss"));
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    /// <summary>Saves atomically (temp file then move) so a failure never leaves a half-written image.</summary>
    public static void Save(BitmapSource image, string path, ImageFormatOption format, int jpgQuality)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        if (AppPaths.FreeBytes(dir) < 20L * 1024 * 1024)
            throw new IOException("There is not enough free disk space to save the image.");

        BitmapSource toWrite = image;
        if (format == ImageFormatOption.Jpg && image.Format != PixelFormats.Bgr24 && image.Format != PixelFormats.Bgr32)
            toWrite = Flatten(image, Colors.White);

        var enc = CreateEncoder(format, jpgQuality);
        enc.Frames.Add(BitmapFrame.Create(toWrite));
        var tmp = path + ".part";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            enc.Save(fs);
        File.Move(tmp, path, true);
    }

    public static byte[] Encode(BitmapSource image, ImageFormatOption format, int jpgQuality)
    {
        var enc = CreateEncoder(format, jpgQuality);
        enc.Frames.Add(BitmapFrame.Create(format == ImageFormatOption.Jpg ? Flatten(image, Colors.White) : image));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    public static BitmapSource Flatten(BitmapSource src, Color background)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, src.PixelWidth, src.PixelHeight));
            dc.DrawImage(src, new Rect(0, 0, src.PixelWidth, src.PixelHeight));
        }
        var rtb = new RenderTargetBitmap(src.PixelWidth, src.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    public static BitmapSource Load(string path)
    {
        var bi = new BitmapImage();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bi.StreamSource = fs;
        bi.EndInit();
        bi.Freeze();
        return Normalize96(bi);
    }

    public static BitmapSource LoadThumbnail(string path, int decodeWidth)
    {
        var bi = new BitmapImage();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.DecodePixelWidth = decodeWidth;
        bi.StreamSource = fs;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    /// <summary>Ensures 1 image pixel == 1 DIP so editor coordinates map exactly to pixels.</summary>
    public static BitmapSource Normalize96(BitmapSource src)
    {
        if (Math.Abs(src.DpiX - 96) < 0.5 && Math.Abs(src.DpiY - 96) < 0.5) return src;
        var fmt = src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32 ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
        var conv = new FormatConvertedBitmap(src, fmt, null, 0);
        int stride = conv.PixelWidth * 4;
        var px = new byte[stride * conv.PixelHeight];
        conv.CopyPixels(px, stride, 0);
        var bs = BitmapSource.Create(conv.PixelWidth, conv.PixelHeight, 96, 96, fmt, null, px, stride);
        bs.Freeze();
        return bs;
    }

    public static bool CopyToClipboard(BitmapSource image)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetImage(image);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(60); // clipboard briefly locked by another app
            }
            catch (Exception ex)
            {
                Log.Error("Clipboard copy failed", ex);
                return false;
            }
        }
        return false;
    }
}
