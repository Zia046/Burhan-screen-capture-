using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace BurhanScreenCapture.Services;

public enum AppTheme { System, Light, Dark }
public enum ImageFormatOption { Png, Jpg, Bmp, Tiff, Gif }
public enum AfterCaptureAction { ShowPreview, OpenEditor, SaveOnly }
public enum RecordingQuality { Low, Standard, High, Ultra }
public enum HotkeyAction { CaptureRegion, CaptureFullScreen, CaptureWindow, ToggleRecording, OpenEditor, ShowToolbar }

public sealed class HotkeyBinding
{
    public HotkeyAction Action { get; set; }
    public ModifierKeys Modifiers { get; set; }
    public Key Key { get; set; }
    public bool Enabled { get; set; } = true;

    public override string ToString()
    {
        if (Key == Key.None) return "Not set";
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key switch { Key.Snapshot => "Print Screen", Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4", Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9", _ => Key.ToString() });
        return string.Join(" + ", parts);
    }
}

public sealed class AppSettings
{
    // Appearance
    public AppTheme Theme { get; set; } = AppTheme.System;
    public string Language { get; set; } = "en";

    // Screenshots
    public string ScreenshotFolder { get; set; } = AppPaths.DefaultScreenshotFolder;
    public ImageFormatOption ScreenshotFormat { get; set; } = ImageFormatOption.Png;
    public int JpgQuality { get; set; } = 90;
    public bool AutoSaveScreenshots { get; set; } = true;
    public bool CopyToClipboardAfterCapture { get; set; } = true;
    public AfterCaptureAction AfterCapture { get; set; } = AfterCaptureAction.ShowPreview;
    public bool IncludeCursorInScreenshot { get; set; } = false;
    public string FileNamePattern { get; set; } = "Screenshot {date} {time}";

    // Recording
    public string RecordingFolder { get; set; } = AppPaths.DefaultRecordingFolder;
    public string RecordingFormat { get; set; } = "MP4";
    public int RecordingFps { get; set; } = 30;
    public RecordingQuality RecordingQuality { get; set; } = RecordingQuality.Standard;
    /// <summary>0 = native resolution; otherwise output height (e.g. 1080, 720).</summary>
    public int RecordingResolutionHeight { get; set; } = 0;
    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordMicrophone { get; set; } = false;
    public string? MicrophoneDeviceId { get; set; }
    public float SystemAudioVolume { get; set; } = 1.0f;
    public float MicrophoneVolume { get; set; } = 1.0f;
    public bool RecordCursor { get; set; } = true;
    public bool HighlightClicks { get; set; } = false;
    public bool RecordingCountdown { get; set; } = true;
    public bool UseHevc { get; set; } = false;          // Pro
    public int CustomBitrateKbps { get; set; } = 0;     // Pro (0 = automatic)
    public string? RecordingProfile { get; set; }       // Pro
    public int GifFps { get; set; } = 10;               // Pro
    public int GifMaxWidth { get; set; } = 800;         // Pro

    // Startup & behaviour
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowFloatingToolbar { get; set; } = false;
    public bool ShowNotifications { get; set; } = true;

    // History
    public bool HistoryEnabled { get; set; } = true;

    // Pro: watermark & templates
    public string WatermarkText { get; set; } = "© Burhan Nexus";
    public double WatermarkOpacity { get; set; } = 0.55;
    public string TemplateName { get; set; } = "Ocean";

    // Advanced capture (Pro)
    public int FixedCaptureWidth { get; set; } = 1280;
    public int FixedCaptureHeight { get; set; } = 720;
    public int[]? LastRegion { get; set; }

    public List<HotkeyBinding> Hotkeys { get; set; } = DefaultHotkeys();

    public static List<HotkeyBinding> DefaultHotkeys() => new()
    {
        new() { Action = HotkeyAction.CaptureRegion, Modifiers = ModifierKeys.None, Key = Key.Snapshot },
        new() { Action = HotkeyAction.CaptureFullScreen, Modifiers = ModifierKeys.Control, Key = Key.Snapshot },
        new() { Action = HotkeyAction.CaptureWindow, Modifiers = ModifierKeys.Alt, Key = Key.Snapshot },
        new() { Action = HotkeyAction.ToggleRecording, Modifiers = ModifierKeys.Control | ModifierKeys.Shift, Key = Key.R },
        new() { Action = HotkeyAction.OpenEditor, Modifiers = ModifierKeys.Control | ModifierKeys.Shift, Key = Key.E },
        new() { Action = HotkeyAction.ShowToolbar, Modifiers = ModifierKeys.Control | ModifierKeys.Shift, Key = Key.T },
    };
}

public static class SettingsService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static AppSettings Current { get; private set; } = new();
    public static event Action? Changed;

    public static void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("Settings file unreadable; using defaults", ex);
            try { File.Copy(AppPaths.SettingsFile, AppPaths.SettingsFile + ".bad", true); } catch { }
            Current = new AppSettings();
        }
        Normalize(Current);
    }

    private static void Normalize(AppSettings s)
    {
        if (s.Hotkeys is null || s.Hotkeys.Count == 0) s.Hotkeys = AppSettings.DefaultHotkeys();
        foreach (var def in AppSettings.DefaultHotkeys())
            if (s.Hotkeys.All(h => h.Action != def.Action)) s.Hotkeys.Add(def);
        s.RecordingFps = Math.Clamp(s.RecordingFps, 5, 60);
        s.JpgQuality = Math.Clamp(s.JpgQuality, 10, 100);
        s.GifFps = Math.Clamp(s.GifFps, 5, 25);
        if (string.IsNullOrWhiteSpace(s.ScreenshotFolder)) s.ScreenshotFolder = AppPaths.DefaultScreenshotFolder;
        if (string.IsNullOrWhiteSpace(s.RecordingFolder)) s.RecordingFolder = AppPaths.DefaultRecordingFolder;
    }

    public static void Save()
    {
        try
        {
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, AppPaths.SettingsFile, true);
        }
        catch (Exception ex) { Log.Error("Could not save settings", ex); }
        Changed?.Invoke();
    }

    public static void ResetToDefaults()
    {
        Current = new AppSettings();
        Save();
    }
}
