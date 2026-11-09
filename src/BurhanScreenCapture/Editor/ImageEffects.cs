using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BurhanScreenCapture.Editor;

/// <summary>Pixel operations on frozen 96-DPI BGRA bitmaps. All methods return new frozen bitmaps.</summary>
public static class ImageEffects
{
    private static (byte[] px, int w, int h, int stride) Read(BitmapSource src)
    {
        var conv = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        conv.CopyPixels(px, stride, 0);
        return (px, w, h, stride);
    }

    private static BitmapSource Write(byte[] px, int w, int h, int stride)
    {
        var bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
        bs.Freeze();
        return bs;
    }

    private static Int32Rect Clip(Rect r, int w, int h)
    {
        int x0 = Math.Clamp((int)Math.Floor(r.X), 0, w), y0 = Math.Clamp((int)Math.Floor(r.Y), 0, h);
        int x1 = Math.Clamp((int)Math.Ceiling(r.Right), 0, w), y1 = Math.Clamp((int)Math.Ceiling(r.Bottom), 0, h);
        return new Int32Rect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    public static BitmapSource Pixelate(BitmapSource src, Rect area, int block)
    {
        var (px, w, h, stride) = Read(src);
        var r = Clip(area, w, h);
        block = Math.Max(2, block);
        for (int by = r.Y; by < r.Y + r.Height; by += block)
        for (int bx = r.X; bx < r.X + r.Width; bx += block)
        {
            int ex = Math.Min(bx + block, r.X + r.Width), ey = Math.Min(by + block, r.Y + r.Height);
            long sb = 0, sg = 0, sr = 0, sa = 0; int n = 0;
            for (int y = by; y < ey; y++)
            for (int x = bx; x < ex; x++)
            {
                int i = y * stride + x * 4;
                sb += px[i]; sg += px[i + 1]; sr += px[i + 2]; sa += px[i + 3]; n++;
            }
            if (n == 0) continue;
            byte b = (byte)(sb / n), g = (byte)(sg / n), rr = (byte)(sr / n), a = (byte)(sa / n);
            for (int y = by; y < ey; y++)
            for (int x = bx; x < ex; x++)
            {
                int i = y * stride + x * 4;
                px[i] = b; px[i + 1] = g; px[i + 2] = rr; px[i + 3] = a;
            }
        }
        return Write(px, w, h, stride);
    }

    /// <summary>Three-pass box blur (≈ Gaussian) restricted to an area.</summary>
    public static BitmapSource Blur(BitmapSource src, Rect area, int radius)
    {
        var (px, w, h, stride) = Read(src);
        var r = Clip(area, w, h);
        if (r.Width < 2 || r.Height < 2) return src;
        radius = Math.Max(1, radius);
        var tmp = new byte[px.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxH(px, tmp, stride, r, radius);
            BoxV(tmp, px, stride, r, radius);
        }
        return Write(px, w, h, stride);
    }

    private static void BoxH(byte[] s, byte[] d, int stride, Int32Rect r, int rad)
    {
        Buffer.BlockCopy(s, 0, d, 0, s.Length);
        for (int y = r.Y; y < r.Y + r.Height; y++)
        {
            for (int c = 0; c < 4; c++)
            {
                int sum = 0, cnt = 0;
                for (int x = r.X; x < Math.Min(r.X + rad + 1, r.X + r.Width); x++) { sum += s[y * stride + x * 4 + c]; cnt++; }
                for (int x = r.X; x < r.X + r.Width; x++)
                {
                    d[y * stride + x * 4 + c] = (byte)(sum / cnt);
                    int add = x + rad + 1, rem = x - rad;
                    if (add < r.X + r.Width) { sum += s[y * stride + add * 4 + c]; cnt++; }
                    if (rem >= r.X) { sum -= s[y * stride + rem * 4 + c]; cnt--; }
                }
            }
        }
    }

    private static void BoxV(byte[] s, byte[] d, int stride, Int32Rect r, int rad)
    {
        Buffer.BlockCopy(s, 0, d, 0, s.Length);
        for (int x = r.X; x < r.X + r.Width; x++)
        {
            for (int c = 0; c < 4; c++)
            {
                int sum = 0, cnt = 0;
                for (int y = r.Y; y < Math.Min(r.Y + rad + 1, r.Y + r.Height); y++) { sum += s[y * stride + x * 4 + c]; cnt++; }
                for (int y = r.Y; y < r.Y + r.Height; y++)
                {
                    d[y * stride + x * 4 + c] = (byte)(sum / cnt);
                    int add = y + rad + 1, rem = y - rad;
                    if (add < r.Y + r.Height) { sum += s[add * stride + x * 4 + c]; cnt++; }
                    if (rem >= r.Y) { sum -= s[rem * stride + x * 4 + c]; cnt--; }
                }
            }
        }
    }

    public static BitmapSource Crop(BitmapSource src, Rect area)
    {
        var r = Clip(area, src.PixelWidth, src.PixelHeight);
        if (r.Width < 1 || r.Height < 1) return src;
        var wb = new WriteableBitmap(new CroppedBitmap(src, r));
        wb.Freeze();
        return wb;
    }

    public static BitmapSource Rotate(BitmapSource src, int degrees)
    {
        var t = new TransformedBitmap(src, new RotateTransform(degrees));
        var wb = new WriteableBitmap(t);
        wb.Freeze();
        return wb;
    }

    public static BitmapSource Flip(BitmapSource src, bool horizontal)
    {
        var t = new TransformedBitmap(src, new ScaleTransform(horizontal ? -1 : 1, horizontal ? 1 : -1));
        var wb = new WriteableBitmap(t);
        wb.Freeze();
        return wb;
    }

    public static BitmapSource Resize(BitmapSource src, int width, int height)
    {
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (var dc = dv.RenderOpen()) dc.DrawImage(src, new Rect(0, 0, width, height));
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    public sealed record Template(string Name, Color From, Color To);

    public static readonly Template[] Templates =
    {
        new("Ocean", Color.FromRgb(0x14, 0x63, 0xE6), Color.FromRgb(0x00, 0xB8, 0xD4)),
        new("Sunset", Color.FromRgb(0xFF, 0x6B, 0x6B), Color.FromRgb(0xFF, 0xC3, 0x71)),
        new("Purple", Color.FromRgb(0x7F, 0x5A, 0xF0), Color.FromRgb(0xE0, 0x6B, 0xD8)),
        new("Forest", Color.FromRgb(0x11, 0x99, 0x8E), Color.FromRgb(0x38, 0xEF, 0x7D)),
        new("Graphite", Color.FromRgb(0x23, 0x25, 0x2B), Color.FromRgb(0x4B, 0x50, 0x5C)),
        new("Clean White", Color.FromRgb(0xF3, 0xF5, 0xF9), Color.FromRgb(0xFF, 0xFF, 0xFF)),
    };

    /// <summary>Pro: places the image on a gradient background with padding, rounded corners and a soft shadow.</summary>
    public static BitmapSource ApplyTemplate(BitmapSource src, Template t)
    {
        int pad = (int)Math.Clamp(Math.Max(src.PixelWidth, src.PixelHeight) * 0.07, 32, 160);
        int w = src.PixelWidth + pad * 2, h = src.PixelHeight + pad * 2;
        double radius = Math.Clamp(Math.Min(src.PixelWidth, src.PixelHeight) * 0.025, 6, 24);
        var imgRect = new Rect(pad, pad, src.PixelWidth, src.PixelHeight);

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(t.From, t.To, 45), null, new Rect(0, 0, w, h));
            for (int i = 6; i >= 1; i--)
            {
                var sh = imgRect; sh.Offset(0, i * 2.5); sh.Inflate(i * 2, i * 2);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(10, 0, 0, 0)), null, sh, radius + i * 2, radius + i * 2);
            }
            dc.PushClip(new RectangleGeometry(imgRect, radius, radius));
            dc.DrawImage(src, imgRect);
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
