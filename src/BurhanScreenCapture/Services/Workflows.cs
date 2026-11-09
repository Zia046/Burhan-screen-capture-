using System.Windows;
using System.Windows.Media.Imaging;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Native;
using BurhanScreenCapture.Views;

namespace BurhanScreenCapture.Services;

public enum CaptureMode { Region, FullScreen, AllMonitors, Window }

/// <summary>Orchestrates capture → save → clipboard → history → preview/editor.</summary>
public static class Workflows
{
    private static bool _busy;

    public static void RunHotkey(HotkeyAction a)
    {
        switch (a)
        {
            case HotkeyAction.CaptureRegion: CaptureRegion(); break;
            case HotkeyAction.CaptureFullScreen: CaptureFullScreen(null); break;
            case HotkeyAction.CaptureWindow: CaptureActiveWindow(false); break;
            case HotkeyAction.ToggleRecording: RecorderController.ToggleQuick(); break;
            case HotkeyAction.OpenEditor: OpenEditorEmpty(); break;
            case HotkeyAction.ShowToolbar: FloatingToolbar.ShowToolbar(); break;
        }
    }

    // ------------------------------------------------------------------ capture entry points

    public static async void CaptureRegion() => await Guarded(async () =>
    {
        using var hide = await HideOurWindowsAsync();
        var r = SelectRegion(RegionSelectMode.Free);
        if (r is null) return;
        SettingsService.Current.LastRegion = new[] { r.Value.Rect.X, r.Value.Rect.Y, r.Value.Rect.Width, r.Value.Rect.Height };
        SettingsService.Save();
        hide.Dispose();
        HandleCapture(r.Value.Image);
    });

    public static async void CaptureFullScreen(MonitorInfo? monitor) => await Guarded(async () =>
    {
        using var hide = await HideOurWindowsAsync();
        var m = monitor ?? ScreenCaptureService.MonitorAtCursor();
        var img = ScreenCaptureService.Capture(m.Bounds, SettingsService.Current.IncludeCursorInScreenshot);
        hide.Dispose();
        HandleCapture(img);
    });

    public static async void CaptureAllMonitors() => await Guarded(async () =>
    {
        using var hide = await HideOurWindowsAsync();
        var img = ScreenCaptureService.Capture(ScreenCaptureService.VirtualScreen, SettingsService.Current.IncludeCursorInScreenshot);
        hide.Dispose();
        HandleCapture(img);
    });

    /// <param name="fromUi">True when started from our own window: we hide it first so Windows activates the user's previous window.</param>
    public static async void CaptureActiveWindow(bool fromUi) => await Guarded(async () =>
    {
        IntPtr target = fromUi ? IntPtr.Zero : NativeMethods.GetForegroundWindow();
        using var hide = await HideOurWindowsAsync(fromUi ? 350 : 120);
        if (fromUi) target = NativeMethods.GetForegroundWindow();
        if (IsOurWindow(target) || target == IntPtr.Zero || WindowEnum.ClassName(target) is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            hide.Dispose();
            App.Tray?.Notify("No active window", "Click the window you want to capture, then press Alt + Print Screen.", error: true);
            return;
        }
        var bounds = ScreenCaptureService.WindowBounds(target);
        if (bounds is null)
        {
            hide.Dispose();
            App.Tray?.Notify("Window not visible", "The active window is minimised or off-screen.", error: true);
            return;
        }
        var img = ScreenCaptureService.Capture(bounds.Value, SettingsService.Current.IncludeCursorInScreenshot);
        hide.Dispose();
        HandleCapture(img);
    });

    public static async void CaptureDelayed(CaptureMode mode, int seconds)
    {
        if (_busy) return;
        _busy = true;
        bool go;
        try
        {
            using (await HideOurWindowsAsync(0))
                go = await CountdownWindow.RunAsync(seconds);
        }
        catch (Exception ex) { Log.Error("Countdown failed", ex); go = false; }
        finally { _busy = false; }
        if (!go) return;
        switch (mode)
        {
            case CaptureMode.Region: CaptureRegion(); break;
            case CaptureMode.FullScreen: CaptureFullScreen(null); break;
            case CaptureMode.AllMonitors: CaptureAllMonitors(); break;
            case CaptureMode.Window: CaptureActiveWindow(false); break;
        }
    }

    // ------------------------------------------------------------------ Pro capture modes

    public static async void CaptureLastRegion()
    {
        if (!Entitlements.Require(Feature.AdvancedCaptureModes)) return;
        var lr = SettingsService.Current.LastRegion;
        if (lr is not { Length: 4 })
        {
            MessageBox.Show("Capture an area first — then you can repeat it with one click.", App.AppName);
            return;
        }
        await Guarded(async () =>
        {
            using var hide = await HideOurWindowsAsync();
            var img = ScreenCaptureService.Capture(new PixelRect(lr[0], lr[1], lr[2], lr[3]), SettingsService.Current.IncludeCursorInScreenshot);
            hide.Dispose();
            HandleCapture(img);
        });
    }

    public static async void CaptureFixedSize()
    {
        if (!Entitlements.Require(Feature.AdvancedCaptureModes)) return;
        await Guarded(async () =>
        {
            using var hide = await HideOurWindowsAsync();
            var r = SelectRegion(RegionSelectMode.FixedSize);
            if (r is null) return;
            hide.Dispose();
            HandleCapture(r.Value.Image);
        });
    }

    public static async void CaptureScrolling()
    {
        if (!Entitlements.Require(Feature.ScrollingCapture)) return;
        await Guarded(async () =>
        {
            BitmapSource? result;
            using (await HideOurWindowsAsync())
            {
                var r = SelectRegion(RegionSelectMode.Free, "Select the scrolling area (inside the page or document), then release");
                if (r is null) return;
                result = await ScrollingCaptureService.RunAsync(r.Value.Rect);
            }
            if (result is null) return;
            HandleCapture(result);
        });
    }

    // ------------------------------------------------------------------ helpers

    public static (PixelRect Rect, BitmapSource Image)? SelectRegion(RegionSelectMode mode, string? hint = null)
    {
        var vs = ScreenCaptureService.VirtualScreen;
        var frozen = ScreenCaptureService.Capture(vs, SettingsService.Current.IncludeCursorInScreenshot);
        var sel = new RegionSelectorWindow(frozen, vs, mode, hint);
        sel.ShowDialog();
        if (sel.Result is not { } rect) return null;
        var local = new Int32Rect(rect.X - vs.X, rect.Y - vs.Y, rect.Width, rect.Height);
        var crop = new CroppedBitmap(frozen, local);
        var copy = new WriteableBitmap(crop);
        copy.Freeze();
        return (rect, copy);
    }

    private static async Task Guarded(Func<Task> work)
    {
        if (_busy) return;
        _busy = true;
        try { await work(); }
        catch (OutOfMemoryException) { ShowError("Not enough memory to capture. Close some programs and try again."); }
        catch (Exception ex)
        {
            Log.Error("Capture failed", ex);
            ShowError("The capture could not be completed: " + ex.Message);
        }
        finally { _busy = false; }
    }

    private static void ShowError(string msg) => MessageBox.Show(msg, App.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static bool IsOurWindow(IntPtr h) =>
        h != IntPtr.Zero && Application.Current.Windows.OfType<Window>().Any(w => WindowEffects.Handle(w) == h);

    /// <summary>Hides our visible windows so they are not in the screenshot; disposing restores them.</summary>
    public static async Task<IDisposable> HideOurWindowsAsync(int settleMs = 220)
    {
        var hidden = Application.Current.Windows.OfType<Window>()
            .Where(w => w.IsVisible && w is not RecordingBarWindow && w.WindowState != WindowState.Minimized)
            .ToList();
        foreach (var w in hidden) w.Hide();
        if (hidden.Count > 0 || settleMs > 0) await Task.Delay(Math.Max(settleMs, hidden.Count > 0 ? 220 : 0));
        return new Restorer(hidden);
    }

    private sealed class Restorer : IDisposable
    {
        private List<Window>? _windows;
        public Restorer(List<Window> w) => _windows = w;
        public void Dispose()
        {
            var ws = Interlocked.Exchange(ref _windows, null);
            if (ws is null) return;
            foreach (var w in ws)
            {
                try { w.Show(); } catch (InvalidOperationException) { /* window was closed meanwhile */ }
            }
        }
    }

    /// <summary>Post-capture pipeline shared by every capture mode.</summary>
    public static void HandleCapture(BitmapSource image)
    {
        var s = SettingsService.Current;
        string? saved = null;
        if (s.AutoSaveScreenshots) saved = SaveScreenshot(image, null);
        if (s.CopyToClipboardAfterCapture && !ImageExportService.CopyToClipboard(image))
            App.Tray?.Notify("Clipboard busy", "The screenshot could not be copied because another app is using the clipboard.", error: true);

        switch (s.AfterCapture)
        {
            case AfterCaptureAction.OpenEditor:
                OpenEditor(image, saved);
                break;
            case AfterCaptureAction.ShowPreview:
                new CapturePreviewWindow(image, saved).Show();
                break;
            default:
                if (saved is not null)
                    App.Tray?.Notify("Screenshot saved", Path.GetFileName(saved) + " — click to open the editor.", () => OpenEditor(image, saved));
                break;
        }
    }

    /// <summary>Saves using the configured folder/format. Returns the path or null (the user is told why).</summary>
    public static string? SaveScreenshot(BitmapSource image, string? explicitPath)
    {
        var s = SettingsService.Current;
        var format = explicitPath is null ? s.ScreenshotFormat : ImageExportService.FormatFromPath(explicitPath);
        if (ImageExportService.IsProFormat(format) && !Entitlements.IsAllowed(Feature.AdvancedExport)) format = ImageFormatOption.Png;
        int quality = Entitlements.IsAllowed(Feature.AdvancedCompression) ? s.JpgQuality : 90;

        string path;
        if (explicitPath is not null) path = explicitPath;
        else
        {
            var folder = s.ScreenshotFolder;
            var err = AppPaths.ValidateWritableFolder(folder);
            if (err is not null)
            {
                Log.Warn(err);
                folder = AppPaths.DefaultScreenshotFolder;
                if (AppPaths.ValidateWritableFolder(folder) is { } err2)
                {
                    App.Tray?.Notify("Could not save screenshot", err2, error: true);
                    return null;
                }
                App.Tray?.Notify("Save location unavailable", err + " Saved to Pictures instead.", error: true);
            }
            path = AppPaths.UniqueFile(folder, ImageExportService.BuildFileName(s.FileNamePattern, DateTime.Now), ImageExportService.Extension(format));
        }
        try
        {
            ImageExportService.Save(image, path, format, quality);
            HistoryService.Add(path, image);
            return path;
        }
        catch (Exception ex)
        {
            Log.Error("Save failed", ex);
            App.Tray?.Notify("Could not save screenshot", ex.Message, error: true);
            return null;
        }
    }

    public static void OpenEditor(BitmapSource image, string? path)
    {
        var w = new Editor.EditorWindow(image, path);
        w.Show();
        w.Activate();
    }

    public static void OpenEditorEmpty()
    {
        var w = new Editor.EditorWindow(null, null);
        w.Show();
        w.Activate();
    }
}
