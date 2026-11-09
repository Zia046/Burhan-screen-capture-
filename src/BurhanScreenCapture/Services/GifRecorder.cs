using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Views;

namespace BurhanScreenCapture.Services;

/// <summary>Pro: records a screen area as an animated GIF.</summary>
public static class GifRecorder
{
    public const int MaxSeconds = 300;
    private static CancellationTokenSource? _cts;
    public static bool IsRecording => _cts is not null;

    public static async void StartInteractive()
    {
        if (IsRecording) { _cts?.Cancel(); return; }
        if (RecorderController.IsBusy) { MessageBox.Show("Stop the current video recording first.", App.AppName); return; }
        if (!Entitlements.Require(Feature.GifRecording)) return;

        PixelRect area;
        using (await Workflows.HideOurWindowsAsync())
        {
            var r = Workflows.SelectRegion(RegionSelectMode.Free, "Select the area for your GIF • Esc to cancel");
            if (r is null) return;
            area = r.Value.Rect;
        }

        var s = SettingsService.Current;
        var folder = AppPaths.ValidateWritableFolder(s.ScreenshotFolder) is null ? s.ScreenshotFolder : AppPaths.DefaultScreenshotFolder;
        var path = AppPaths.UniqueFile(folder, "GIF " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"), ".gif");
        int fps = Math.Clamp(s.GifFps, 5, 25);
        double scale = Math.Min(1.0, (double)Math.Max(160, s.GifMaxWidth) / area.Width);

        _cts = new CancellationTokenSource();
        var bar = CreateBar(area);
        bar.Show();
        try { ScreenRecorderLib.Recorder.SetExcludeFromCapture(Native.WindowEffects.Handle(bar), true); } catch { }
        var timeText = (TextBlock)((StackPanel)((Border)bar.Content).Child).Children[1];
        var uiTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        var sw = Stopwatch.StartNew();
        uiTimer.Tick += (_, _) => timeText.Text = sw.Elapsed.ToString(@"mm\:ss");
        uiTimer.Start();

        int frames = 0;
        string? error = null;
        var token = _cts.Token;
        try
        {
            frames = await Task.Run(() =>
            {
                using var writer = new GifWriter(path);
                var frameTime = TimeSpan.FromMilliseconds(1000.0 / fps);
                var next = sw.Elapsed;
                while (!token.IsCancellationRequested && sw.Elapsed.TotalSeconds < MaxSeconds)
                {
                    BitmapSource img = ScreenCaptureService.Capture(area, s.RecordCursor);
                    if (scale < 1)
                    {
                        var t = new TransformedBitmap(img, new ScaleTransform(scale, scale));
                        var wb = new WriteableBitmap(t);
                        wb.Freeze();
                        img = wb;
                    }
                    writer.AddFrame(img, (int)frameTime.TotalMilliseconds);
                    next += frameTime;
                    var wait = next - sw.Elapsed;
                    if (wait > TimeSpan.Zero) token.WaitHandle.WaitOne(wait);
                    else next = sw.Elapsed; // falling behind: drop timing debt instead of bursting
                }
                return writer.FrameCount;
            });
        }
        catch (Exception ex)
        {
            Log.Error("GIF recording failed", ex);
            error = ex.Message;
        }
        finally
        {
            uiTimer.Stop();
            bar.Close();
            _cts = null;
        }

        if (error is not null || frames == 0)
        {
            try { File.Delete(path); } catch { }
            MessageBox.Show("The GIF could not be recorded" + (error is null ? "." : ": " + error), App.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        App.Tray?.Notify("GIF saved", $"{Path.GetFileName(path)} — {frames} frames. Click to show.", () => Dialogs.ShowInFolder(path));
        Dialogs.ShowInFolder(path);
    }

    private static Window CreateBar(PixelRect area)
    {
        var stop = new Button { Content = "Stop GIF", Margin = new Thickness(12, 0, 0, 0) };
        stop.SetResourceReference(FrameworkElement.StyleProperty, "DangerButton");
        stop.Click += (_, _) => _cts?.Cancel();
        var w = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(235, 22, 26, 34)),
                Padding = new Thickness(12, 6, 6, 6),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = "GIF ●", Foreground = Brushes.OrangeRed, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0) },
                        new TextBlock { Text = "00:00", Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center },
                        stop
                    }
                }
            }
        };
        w.Loaded += (_, _) =>
        {
            var dpi = VisualTreeHelper.GetDpi(w);
            int bw = (int)(w.ActualWidth * dpi.DpiScaleX), bh = (int)(w.ActualHeight * dpi.DpiScaleY);
            var mon = ScreenCaptureService.Monitors().First(m => m.Bounds.Intersect(area).Width > 0).Bounds;
            int y = area.Bottom + 8 + bh < mon.Bottom ? area.Bottom + 8 : Math.Max(mon.Y + 8, area.Y - bh - 8);
            Native.WindowEffects.SetPhysicalBounds(w, Math.Clamp(area.X + area.Width / 2 - bw / 2, mon.X, mon.Right - bw), y, bw, bh);
        };
        return w;
    }
}
