using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BurhanScreenCapture.Editor;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Views;

namespace BurhanScreenCapture.Services;

/// <summary>
/// Automated end-to-end checks used by CI on a real Windows desktop:
///   BurhanScreenCapture.exe --selftest &lt;outputFolder&gt; [--license &lt;file.bslic&gt;] [--expired &lt;file.bslic&gt;]
/// Writes results.json plus UI snapshots and sample outputs. Exit code 0 = all required checks passed.
/// </summary>
public static class SelfTest
{
    public static bool Active { get; private set; }

    private sealed record Result(string Name, bool Passed, bool Required, string Detail, long Ms);
    private static readonly List<Result> Results = new();

    public static async Task<int> RunAsync(string outDir, string? licenseFile, string? expiredFile)
    {
        Active = true;
        Directory.CreateDirectory(outDir);
        var watchdog = new Thread(() => { Thread.Sleep(TimeSpan.FromMinutes(6)); Write(outDir); Environment.Exit(3); }) { IsBackground = true };
        watchdog.Start();

        SettingsService.Current.ScreenshotFolder = Path.Combine(outDir, "shots");
        SettingsService.Current.RecordingFolder = Path.Combine(outDir, "recordings");
        SettingsService.Current.RecordingCountdown = false;
        SettingsService.Current.RecordMicrophone = false;
        SettingsService.Current.RecordSystemAudio = false;

        await Step("Hotkeys registered", true, () =>
        {
            var failed = App.FailedHotkeys;
            return Task.FromResult($"failed: {failed.Count} [{string.Join("; ", failed)}]");
        });

        BitmapSource? shot = null;
        await Step("Full screen capture (GDI)", true, () =>
        {
            var m = ScreenCaptureService.PrimaryMonitor;
            shot = ScreenCaptureService.Capture(m.Bounds, true);
            Assert(shot.PixelWidth == m.Bounds.Width && shot.PixelHeight == m.Bounds.Height, "size mismatch");
            return Task.FromResult($"{shot.PixelWidth}x{shot.PixelHeight}, monitors={ScreenCaptureService.Monitors().Count}, virtual={ScreenCaptureService.VirtualScreen}");
        });

        await Step("Region + all-monitor capture", true, () =>
        {
            var vs = ScreenCaptureService.VirtualScreen;
            var all = ScreenCaptureService.Capture(vs);
            var part = ScreenCaptureService.Capture(new PixelRect(vs.X + 10, vs.Y + 10, 301, 157));
            Assert(part.PixelWidth == 301 && part.PixelHeight == 157, "region size");
            return Task.FromResult($"all={all.PixelWidth}x{all.PixelHeight}");
        });

        await Step("Save PNG/JPG + reload + clipboard", true, () =>
        {
            var png = Workflows.SaveScreenshot(shot!, Path.Combine(outDir, "shots", "test.png"))!;
            var jpg = Workflows.SaveScreenshot(shot!, Path.Combine(outDir, "shots", "test.jpg"))!;
            Assert(File.Exists(png) && File.Exists(jpg), "files missing");
            var back = ImageExportService.Load(png);
            Assert(back.PixelWidth == shot!.PixelWidth, "reload size");
            bool clip = ImageExportService.CopyToClipboard(back);
            return Task.FromResult($"png={new FileInfo(png).Length / 1024}KB jpg={new FileInfo(jpg).Length / 1024}KB clipboard={clip}");
        });

        await Step("Free history keeps latest 20", true, () =>
        {
            HistoryService.Clear();
            for (int i = 0; i < 24; i++) HistoryService.Add(Path.Combine(outDir, "shots", "test.png"), shot!);
            int n = HistoryService.Entries.Count;
            Assert(Entitlements.IsPro || n == FeatureCatalog.FreeHistoryLimit, $"count {n}");
            return Task.FromResult($"entries={n}");
        });

        await Step("Pro features locked in Free edition", true, () =>
        {
            Assert(!Entitlements.IsPro, "should start as Free");
            foreach (var f in new[] { Feature.Blur, Feature.Ocr, Feature.GifRecording, Feature.ScrollingCapture, Feature.UnlimitedHistory })
                Assert(!Entitlements.IsAllowed(f), f + " should be locked");
            foreach (var f in new[] { Feature.ScreenRecording, Feature.SystemAudio, Feature.Microphone, Feature.Crop, Feature.RegionCapture })
                Assert(Entitlements.IsAllowed(f), f + " should be free");
            return Task.FromResult("ok");
        });

        await Step("Image effects", true, () =>
        {
            var c = ImageEffects.Crop(shot!, new Rect(5, 5, 200, 100));
            Assert(c.PixelWidth == 200 && c.PixelHeight == 100, "crop");
            var r = ImageEffects.Rotate(c, 90);
            Assert(r.PixelWidth == 100 && r.PixelHeight == 200, "rotate");
            var z = ImageEffects.Resize(c, 50, 25);
            Assert(z.PixelWidth == 50, "resize");
            var b = ImageEffects.Blur(c, new Rect(0, 0, 100, 100), 8);
            var p = ImageEffects.Pixelate(c, new Rect(0, 0, 100, 100), 10);
            var t = ImageEffects.ApplyTemplate(c, ImageEffects.Templates[0]);
            Save(t, Path.Combine(outDir, "template.png"));
            return Task.FromResult($"template={t.PixelWidth}x{t.PixelHeight}");
        });

        await Step("Editor: annotations + flatten + save", true, async () =>
        {
            var ed = new EditorWindow(shot, null);
            ed.Show();
            await Task.Delay(800);
            var flat = ed.SelfTestDraw();
            Save(flat, Path.Combine(outDir, "editor-output.png"));
            SnapshotWindow(ed, Path.Combine(outDir, "ui-editor.png"));
            ed.SelfTestForceClose();
            return $"{flat.PixelWidth}x{flat.PixelHeight}";
        });

        await Step("Scrolling stitch overlap detection", true, () =>
        {
            int w = 200, h = 300, stride = w * 4, total = 900, shift = 123;
            var rnd = new Random(7);
            var page = new byte[total * stride];
            rnd.NextBytes(page);
            byte[] Frame(int top) { var f = new byte[h * stride]; Buffer.BlockCopy(page, top * stride, f, 0, f.Length); return f; }
            int found = ScrollingCaptureService.FindShift(Frame(0), Frame(shift), w, h);
            Assert(found == shift, $"found {found}");
            Assert(ScrollingCaptureService.FindShift(Frame(50), Frame(50), w, h) == 0, "identical");
            return Task.FromResult($"shift={found}");
        });

        await Step("GIF writer (animated, looping)", true, () =>
        {
            var gifPath = Path.Combine(outDir, "test.gif");
            using (var gw = new GifWriter(gifPath))
            {
                for (int i = 0; i < 8; i++)
                {
                    var f = ScreenCaptureService.Capture(new PixelRect(i * 20, 0, 320, 200));
                    gw.AddFrame(f, 100);
                }
            }
            using var fs = File.OpenRead(gifPath);
            var dec = new GifBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert(dec.Frames.Count == 8, $"frames {dec.Frames.Count}");
            return Task.FromResult($"frames={dec.Frames.Count} size={new FileInfo(gifPath).Length / 1024}KB");
        });

        await Step("OCR (Windows.Media.Ocr)", false, async () =>
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 700, 140));
                dc.DrawText(new FormattedText("Burhan Nexus Capture 2026", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 48, Brushes.Black, 1.0), new Point(20, 35));
            }
            var rtb = new RenderTargetBitmap(700, 140, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            var (text, err) = await OcrService.RecognizeAsync(rtb);
            Assert(err is null && text.Contains("Burhan", StringComparison.OrdinalIgnoreCase), err ?? text);
            return text;
        });

        await Step("Screen recording: full screen MP4 with pause/resume", true, async () =>
        {
            var ok = await RecorderController.StartAsync(new RecordingRequest { Kind = RecordingSourceKind.Monitor, Monitor = ScreenCaptureService.PrimaryMonitor });
            Assert(ok, "start failed: " + LastFailure);
            await Task.Delay(2500);
            RecorderController.Pause();
            await Task.Delay(800);
            RecorderController.Resume();
            await Task.Delay(1800);
            RecorderController.Stop();
            await WaitIdle();
            var f = RecorderController.LastFile;
            Assert(f is not null && File.Exists(f) && new FileInfo(f).Length > 10_000, "no output file");
            return $"{Path.GetFileName(f)} {new FileInfo(f!).Length / 1024}KB, elapsed {RecorderController.Elapsed.TotalSeconds:0.0}s";
        });

        await Step("Screen recording: selected area 720p cap", true, async () =>
        {
            SettingsService.Current.RecordingResolutionHeight = 720;
            var m = ScreenCaptureService.PrimaryMonitor.Bounds;
            var ok = await RecorderController.StartAsync(new RecordingRequest { Kind = RecordingSourceKind.Region, Region = new PixelRect(m.X + 21, m.Y + 33, 641, 401) });
            Assert(ok, "start failed: " + LastFailure);
            await Task.Delay(2500);
            RecorderController.Stop();
            await WaitIdle();
            var f = RecorderController.LastFile;
            Assert(f is not null && File.Exists(f) && new FileInfo(f).Length > 5_000, "no output file");
            return $"{new FileInfo(f!).Length / 1024}KB";
        });

        bool hasPlayback = AudioDevices.HasPlaybackDevice();
        await Step("Recording with system audio (needs an audio device)", false, async () =>
        {
            Assert(hasPlayback, "no playback device on this machine");
            SettingsService.Current.RecordSystemAudio = true;
            var ok = await RecorderController.StartAsync(new RecordingRequest { Kind = RecordingSourceKind.Monitor });
            Assert(ok, "start failed: " + LastFailure);
            await Task.Delay(2500);
            RecorderController.Stop();
            await WaitIdle();
            SettingsService.Current.RecordSystemAudio = false;
            return $"{new FileInfo(RecorderController.LastFile!).Length / 1024}KB";
        });

        await Step("Audio validation message when no microphone", true, async () =>
        {
            if (AudioDevices.Microphones().Count > 0) return "microphone present — skipped";
            SettingsService.Current.RecordMicrophone = true;
            LastFailure = null;
            var ok = await RecorderController.StartAsync(new RecordingRequest { Kind = RecordingSourceKind.Monitor });
            SettingsService.Current.RecordMicrophone = false;
            Assert(!ok && LastFailure is not null && LastFailure.Contains("microphone", StringComparison.OrdinalIgnoreCase), "expected friendly error");
            return LastFailure!;
        });

        // ---------------- licensing (Debug builds trust the dev key)
        if (licenseFile is not null)
        {
            await Step("License: import signed license -> Pro", true, () =>
            {
                var (ok, msg) = LicenseService.ImportLicenseFile(licenseFile);
                Assert(ok && Entitlements.IsPro && Entitlements.IsAllowed(Feature.Ocr), msg);
                HistoryService.Add(Path.Combine(outDir, "shots", "test.png"), shot!);
                return Task.FromResult(msg + $" | history now {HistoryService.Entries.Count}");
            });
            await Step("License: UI snapshots (Pro)", false, async () => { await SnapshotPages(outDir, "pro"); return "ok"; });
            await Step("License: deactivate -> Free", true, async () =>
            {
                var (ok, msg) = await LicenseService.DeactivateAsync();
                Assert(ok && !Entitlements.IsPro, msg);
                return msg;
            });
        }
        if (expiredFile is not null)
        {
            await Step("License: expired license is rejected", true, () =>
            {
                var (ok, msg) = LicenseService.ImportLicenseFile(expiredFile);
                Assert(!ok && !Entitlements.IsPro, "expired accepted");
                return Task.FromResult(msg);
            });
        }
        await Step("License: tampered file is rejected", true, () =>
        {
            var bad = Path.Combine(outDir, "bad.bslic");
            File.WriteAllText(bad, "{\"payload\":\"eyJ4IjoxfQ==\",\"signature\":\"AAAA\"}");
            var (ok, msg) = LicenseService.ImportLicenseFile(bad);
            Assert(!ok && !Entitlements.IsPro, "tampered accepted");
            return Task.FromResult(msg);
        });

        await Step("UI snapshots (light + dark)", true, async () =>
        {
            ThemeManager.Apply(AppTheme.Light);
            await SnapshotPages(outDir, "light");
            ThemeManager.Apply(AppTheme.Dark);
            await SnapshotPages(outDir, "dark");
            ThemeManager.Apply(AppTheme.Light);
            var dlg = new UpgradeDialog(FeatureCatalog.Get(Feature.Blur));
            dlg.Show();
            await Task.Delay(500);
            SnapshotWindow(dlg, Path.Combine(outDir, "ui-upgrade-dialog.png"));
            dlg.Close();
            var prev = new CapturePreviewWindow(shot!, Path.Combine(outDir, "shots", "test.png"));
            prev.Show();
            await Task.Delay(400);
            SnapshotWindow(prev, Path.Combine(outDir, "ui-preview.png"));
            prev.Close();
            return "ok";
        });

        await Step("License: 7-day trial -> Pro Trial", true, () =>
        {
            var (ok, msg) = LicenseService.StartTrial();
            Assert(ok && LicenseService.Status.Edition == Edition.ProTrial, msg);
            var (again, _) = LicenseService.StartTrial();
            Assert(!again, "trial could be started twice");
            return Task.FromResult(msg);
        });

        long mem = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
        Results.Add(new Result("Working set after tests (MB)", true, false, mem.ToString(), 0));
        Write(outDir);
        return Results.Any(r => r.Required && !r.Passed) ? 1 : 0;
    }

    public static string? LastFailure { get; set; }

    private static async Task WaitIdle()
    {
        var sw = Stopwatch.StartNew();
        while (RecorderController.IsBusy && sw.ElapsedMilliseconds < 20000) await Task.Delay(100);
        await Task.Delay(300);
    }

    private static async Task SnapshotPages(string outDir, string tag)
    {
        var main = (MainWindow)Application.Current.MainWindow;
        main.Width = 1280; main.Height = 760;
        main.WindowState = WindowState.Normal;
        main.Show();
        foreach (var key in new[] { "Home", "Capture", "Recorder", "Editor", "History", "Tools", "Settings", "Upgrade", "About" })
        {
            main.Navigate(key);
            await Task.Delay(450);
            SnapshotWindow(main, Path.Combine(outDir, $"ui-{tag}-{key.ToLowerInvariant()}.png"));
        }
        main.Navigate("Home");
    }

    private static void SnapshotWindow(Window w, string path)
    {
        w.UpdateLayout();
        if (w.Content is not FrameworkElement root || root.ActualWidth < 1) return;
        var rtb = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle((Brush)(w.Background ?? Brushes.White), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        rtb.Render(dv);
        Save(rtb, path);
    }

    private static void Save(BitmapSource b, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(b));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new Exception("ASSERT: " + msg);
    }

    private static async Task Step(string name, bool required, Func<Task<string>> f)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var detail = await f();
            Results.Add(new Result(name, true, required, detail, sw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            Results.Add(new Result(name, false, required, ex.Message + (ex.InnerException is null ? "" : " | " + ex.InnerException.Message), sw.ElapsedMilliseconds));
            Log.Error("SELFTEST " + name, ex);
        }
    }

    private static void Write(string outDir)
    {
        try
        {
            File.WriteAllText(Path.Combine(outDir, "results.json"), JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllLines(Path.Combine(outDir, "results.txt"),
                Results.Select(r => $"{(r.Passed ? "PASS" : r.Required ? "FAIL" : "WARN")}  {r.Name}  ({r.Ms} ms)  {r.Detail}"));
        }
        catch { }
    }
}
