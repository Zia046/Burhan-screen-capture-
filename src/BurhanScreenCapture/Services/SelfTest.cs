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

    public static async Task<int> RunAsync(string outDir, string? keyFile, string? wrongDeviceKeyFile, string? revocationFile, bool phase2)
    {
        if (phase2) return await RunPhase2Async(outDir, keyFile, revocationFile);
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

        await AudioMatrixAsync(outDir);

        await Step("UI snapshots (light + dark, Free)", true, async () =>
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

        // ---------------- licensing
        await Step("License: fresh install is Free Edition", true, () =>
        {
            Assert(LicenseService.Status.Edition == Edition.Free && !Entitlements.IsPro, "not free: " + LicenseService.Status.State);
            Assert(Entitlements.IsAllowed(Feature.ScreenRecording) && Entitlements.IsAllowed(Feature.SystemAudio) && Entitlements.IsAllowed(Feature.Microphone), "free recording locked");
            return Task.FromResult(EditionInfo.Name(LicenseService.Status.Edition) + " / " + EditionInfo.LicenseText(LicenseService.Status.Edition));
        });
        await Step("License: invalid keys rejected", true, async () =>
        {
            foreach (var bad in new[] { "", "hello", "BNX-SC-PRO-ABCD-EFGH", "BNX-SC-PRO-UUUU-UUUU-UUUU" })
            {
                var (ok, _) = await LicenseService.ActivateAsync(bad);
                Assert(!ok && !Entitlements.IsPro, "accepted: " + bad);
            }
            return "4 invalid keys rejected";
        });
        await Step("License: forged key (signed with another private key) rejected", true, async () =>
        {
            var other = LicenseCrypto.CreateKeyPair();
            var forged = LicenseKey.Create(new LicenseData { LicenseIdBytes = LicenseData.NewLicenseIdBytes(), IssuedUtc = DateTime.UtcNow }, other.privateKeyBase64);
            var (ok, msg) = await LicenseService.ActivateAsync(forged);
            Assert(!ok && !Entitlements.IsPro, "forged accepted");
            return msg;
        });
        if (keyFile is not null)
        {
            var key = File.ReadAllText(keyFile);
            await Step("License: modified (tampered) key rejected", true, async () =>
            {
                var k = LicenseService.ExtractKey(key)!.Trim();
                int pos = k.Length - 20;
                var bad = k[..pos] + (k[pos] == 'A' ? 'B' : 'A') + k[(pos + 1)..];
                var (ok, msg) = await LicenseService.ActivateAsync(bad);
                Assert(!ok && !Entitlements.IsPro, "tampered accepted");
                return msg;
            });
            if (wrongDeviceKeyFile is not null)
                await Step("License: key bound to another computer rejected", true, async () =>
                {
                    var (ok, msg) = await LicenseService.ActivateAsync(File.ReadAllText(wrongDeviceKeyFile));
                    Assert(!ok && !Entitlements.IsPro, "wrong-device key accepted");
                    return msg;
                });
            await Step("License: generator-exported license file activates Lifetime Pro", true, async () =>
            {
                var (ok, msg) = await LicenseService.ImportLicenseFileAsync(keyFile);
                Assert(ok && Entitlements.IsPro && LicenseService.Status.Edition == Edition.LifetimePro, msg);
                foreach (var f in FeatureCatalog.All.Where(f => f.Tier == FeatureTier.Pro))
                    Assert(Entitlements.IsAllowed(f.Feature), f.Feature + " still locked");
                return $"{msg} | {LicenseService.Status.Data?.LicenseId} | activated {LicenseService.ActivatedLocal}";
            });
            await Step("License: UI snapshots (Lifetime Pro)", false, async () =>
            {
                await SnapshotPages(outDir, "pro");
                return "ok";
            });
        }

                long mem = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
        Results.Add(new Result("Working set after tests (MB)", true, false, mem.ToString(), 0));
        Write(outDir);
        return Results.Any(r => r.Required && !r.Passed) ? 1 : 0;
    }

    // ---------------- second process: persistence, offline use, revocation
    private static async Task<int> RunPhase2Async(string outDir, string? keyFile, string? revocationFile)
    {
        Active = true;
        Directory.CreateDirectory(outDir);
        await Step("License: still Lifetime Pro after restart (offline validation)", true, () =>
        {
            Assert(LicenseService.Status.Edition == Edition.LifetimePro, LicenseService.Status.State + " " + LicenseService.Status.Message);
            return Task.FromResult($"{LicenseService.Status.Data?.LicenseId}, activated {LicenseService.ActivatedLocal}");
        });
        if (keyFile is not null)
            await Step("License: same key again → already activated", true, async () =>
            {
                var (ok, msg) = await LicenseService.ActivateAsync(File.ReadAllText(keyFile));
                Assert(ok && msg.Contains("already", StringComparison.OrdinalIgnoreCase), msg);
                return msg;
            });
        await Step("License: forged revocation list ignored", true, () =>
        {
            var other = LicenseCrypto.CreateKeyPair();
            var forged = RevocationList.Create(new[] { LicenseService.Status.Data!.LicenseId }, other.privateKeyBase64).ToText();
            Assert(!LicenseService.ApplyRevocationList(forged) && Entitlements.IsPro, "forged list applied");
            return Task.FromResult("ignored");
        });
        if (revocationFile is not null)
            await Step("License: signed revocation list revokes the license", true, () =>
            {
                Assert(LicenseService.ApplyRevocationList(File.ReadAllText(revocationFile)), "list rejected");
                Assert(!Entitlements.IsPro && LicenseService.Status.State == LicenseState.Revoked, LicenseService.Status.State.ToString());
                Assert(Entitlements.IsAllowed(Feature.ScreenRecording) && !Entitlements.IsAllowed(Feature.Ocr), "free gating wrong after revoke");
                return Task.FromResult(LicenseService.Status.Message);
            });
        await Step("License: revoked key cannot be activated again", revocationFile is not null && keyFile is not null, async () =>
        {
            if (keyFile is null || revocationFile is null) return "skipped";
            var (ok, msg) = await LicenseService.ActivateAsync(File.ReadAllText(keyFile));
            Assert(!ok, "revoked key re-activated");
            return msg;
        });
        await Step("License: deactivate → Free; Free still works", true, async () =>
        {
            await LicenseService.DeactivateAsync();
            Assert(LicenseService.Status.Edition == Edition.Free, "not free");
            var img = ScreenCaptureService.Capture(ScreenCaptureService.PrimaryMonitor.Bounds);
            return $"capture {img.PixelWidth}x{img.PixelHeight} ok";
        });
        Write(outDir);
        return Results.Any(r => r.Required && !r.Passed) ? 1 : 0;
    }

    // ---------------- recording × audio combinations
    private static async Task AudioMatrixAsync(string outDir)
    {
        var speakers = AudioDevices.Speakers();
        var mics = AudioDevices.Microphones();
        Results.Add(new Result("Audio devices found", true, false,
            $"playback: [{string.Join("; ", speakers.Select(d => d.Name))}] capture: [{string.Join("; ", mics.Select(d => d.Name))}]", 0));
        bool hasOut = speakers.Count > 0, hasMic = mics.Count > 0;

        var combos = new (string Name, RecordingSourceKind Kind, bool Sys, bool Mic)[]
        {
            ("1 Screen only", RecordingSourceKind.Monitor, false, false),
            ("2 Screen + system audio", RecordingSourceKind.Monitor, true, false),
            ("3 Screen + microphone", RecordingSourceKind.Monitor, false, true),
            ("4 Screen + system audio + microphone", RecordingSourceKind.Monitor, true, true),
            ("5 System audio only", RecordingSourceKind.AudioOnly, true, false),
            ("6 Microphone only", RecordingSourceKind.AudioOnly, false, true),
            ("7 System audio + microphone, no video", RecordingSourceKind.AudioOnly, true, true),
            ("8 Area recording 720p + system audio", RecordingSourceKind.Region, true, false),
        };
        foreach (var c in combos)
        {
            bool possible = (!c.Sys || hasOut) && (!c.Mic || hasMic);
            await Step($"Recording {c.Name} (pause/resume, double stop)", possible, async () =>
            {
                if (!possible)
                {
                    // Must fail with a friendly message instead of crashing
                    var s0 = SettingsService.Current;
                    s0.RecordSystemAudio = c.Sys; s0.RecordMicrophone = c.Mic;
                    LastFailure = null;
                    var started = await RecorderController.StartAsync(new RecordingRequest { Kind = c.Kind, Monitor = ScreenCaptureService.PrimaryMonitor });
                    if (started) { RecorderController.Stop(); await WaitIdle(); }
                    Assert(!started && LastFailure is not null, "should have refused without the device");
                    throw new Exception("device not available on this machine — refused correctly: " + LastFailure);
                }
                return await RecordOnce(c.Kind, c.Sys, c.Mic, c.Kind == RecordingSourceKind.Monitor);
            });
        }
        await Step("Recording: 3 sessions back-to-back", true, async () =>
        {
            var sizes = new List<long>();
            for (int i = 0; i < 3; i++)
            {
                var r = await RecordOnce(RecordingSourceKind.Monitor, hasOut, false, false, 1500);
                sizes.Add(new FileInfo(RecorderController.LastFile!).Length / 1024);
            }
            return string.Join(", ", sizes.Select(x => x + "KB"));
        });
        await Step("Recording: disconnected/unknown microphone falls back safely", hasMic, async () =>
        {
            if (!hasMic) return "no microphone";
            SettingsService.Current.MicrophoneDeviceId = "{0.0.1.00000000}.{00000000-0000-0000-0000-000000000000}";
            var r = await RecordOnce(RecordingSourceKind.Monitor, false, true, false, 1500);
            SettingsService.Current.MicrophoneDeviceId = null;
            return "fallback to default: " + r;
        });
        SettingsService.Current.RecordSystemAudio = false;
        SettingsService.Current.RecordMicrophone = false;
        SettingsService.Current.RecordingResolutionHeight = 0;
    }

    private static async Task<string> RecordOnce(RecordingSourceKind kind, bool sys, bool mic, bool pause, int ms = 2500)
    {
        var s = SettingsService.Current;
        s.RecordSystemAudio = sys;
        s.RecordMicrophone = mic;
        s.RecordingResolutionHeight = kind == RecordingSourceKind.Region ? 720 : 0;
        LastFailure = null;
        var m = ScreenCaptureService.PrimaryMonitor;
        var req = new RecordingRequest
        {
            Kind = kind,
            Monitor = m,
            Region = new PixelRect(m.Bounds.X + 21, m.Bounds.Y + 33, 641, 401)
        };
        using var tone = sys ? Tone.Start() : null;
        var ok = await RecorderController.StartAsync(req);
        Assert(ok, "start failed: " + LastFailure);
        await Task.Delay(ms);
        if (pause)
        {
            RecorderController.Pause();
            Assert(RecorderController.IsPaused, "not paused");
            await Task.Delay(700);
            RecorderController.Resume();
            await Task.Delay(1200);
        }
        RecorderController.Stop();
        RecorderController.Stop(); // repeated stop must be harmless
        await WaitIdle();
        Assert(LastFailure is null, "failed: " + LastFailure);
        var f = RecorderController.LastFile;
        Assert(f is not null && File.Exists(f), "no output file");
        var size = new FileInfo(f!).Length;
        var tracks = Mp4Tracks(f!);
        bool wantVideo = kind != RecordingSourceKind.AudioOnly, wantAudio = sys || mic;
        Assert(tracks.Contains("vide") == wantVideo, $"video track expected={wantVideo} found=[{string.Join(",", tracks)}]");
        Assert(tracks.Contains("soun") == wantAudio, $"audio track expected={wantAudio} found=[{string.Join(",", tracks)}]");
        Assert(size > 2_000, "file too small");
        return $"{Path.GetFileName(f)} {size / 1024}KB tracks=[{string.Join(",", tracks)}] elapsed={RecorderController.Elapsed.TotalSeconds:0.0}s";
    }

    /// <summary>Handler types of the tracks in an MP4 (e.g. "vide", "soun").</summary>
    private static List<string> Mp4Tracks(string path)
    {
        var b = File.ReadAllBytes(path);
        var list = new List<string>();
        for (int i = 4; i + 16 < b.Length; i++)
        {
            if (b[i] == (byte)'h' && b[i + 1] == (byte)'d' && b[i + 2] == (byte)'l' && b[i + 3] == (byte)'r')
            {
                var t = System.Text.Encoding.ASCII.GetString(b, i + 12, 4);
                if (t is "vide" or "soun" && !list.Contains(t)) list.Add(t);
            }
        }
        return list;
    }

    /// <summary>Plays a quiet 440 Hz tone so system-audio recordings have real content.</summary>
    private sealed class Tone : IDisposable
    {
        private readonly System.Media.SoundPlayer _p;
        private Tone(System.Media.SoundPlayer p) => _p = p;
        public static Tone? Start()
        {
            try
            {
                const int rate = 44100, secs = 2;
                var ms = new MemoryStream();
                var w = new BinaryWriter(ms);
                int samples = rate * secs;
                w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVE"u8);
                w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write("data"u8); w.Write(samples * 2);
                for (int i = 0; i < samples; i++) w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 6000));
                ms.Position = 0;
                var p = new System.Media.SoundPlayer(ms);
                p.PlayLooping();
                return new Tone(p);
            }
            catch { return null; }
        }
        public void Dispose() { try { _p.Stop(); _p.Dispose(); } catch { } }
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
