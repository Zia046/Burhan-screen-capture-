using System.Diagnostics;
using System.Windows;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Views;
using ScreenRecorderLib;

namespace BurhanScreenCapture.Services;

public enum RecordingSourceKind { Monitor, Region, Window, AudioOnly }

public sealed class RecordingRequest
{
    public RecordingSourceKind Kind { get; set; } = RecordingSourceKind.Monitor;
    public MonitorInfo? Monitor { get; set; }
    public PixelRect Region { get; set; }
    public IntPtr Window { get; set; }
    public string? WindowTitle { get; set; }
}

public sealed record RecordingProfile(string Name, int Fps, RecordingQuality Quality, int Height, bool Hevc, string Description);

/// <summary>
/// Screen + audio recording (H.264/AAC MP4 via Media Foundation, through ScreenRecorderLib).
/// System audio uses WASAPI loopback; the microphone is mixed in by the encoder so audio and video share one clock.
/// </summary>
public static class RecorderController
{
    private static Recorder? _rec;
    private static RecordingBarWindow? _bar;
    private static readonly Stopwatch Clock = new();
    private static string? _outputPath;
    private static bool _exitWhenDone;
    private static bool _discard;
    private static bool _starting;

    public static RecorderStatus Status => _rec?.Status ?? RecorderStatus.Idle;
    public static bool IsBusy => _starting || (_rec is not null && _rec.Status != RecorderStatus.Idle);
    public static bool IsPaused => _rec?.Status == RecorderStatus.Paused;
    public static TimeSpan Elapsed => Clock.Elapsed;
    public static RecordingRequest? LastRequest { get; private set; }
    public static string? LastFile { get; private set; }
    public static event Action? StateChanged;

    public static readonly RecordingProfile[] Profiles =
    {
        new("Tutorial (30 fps, small files)", 30, RecordingQuality.Standard, 1080, false, "Clear text, compact size"),
        new("YouTube HD (60 fps)", 60, RecordingQuality.High, 1080, false, "Smooth motion for uploads"),
        new("Gaming (60 fps, high quality)", 60, RecordingQuality.Ultra, 0, false, "Native resolution, highest bitrate"),
        new("Small file (720p, H.265)", 24, RecordingQuality.Low, 720, true, "Best for email and chat"),
        new("Presentation (15 fps)", 15, RecordingQuality.Standard, 0, false, "Slides and documents"),
    };

    public static void ToggleQuick()
    {
        if (IsBusy) { Stop(); return; }
        var req = LastRequest is { Kind: not RecordingSourceKind.Window } last ? last : new RecordingRequest { Kind = RecordingSourceKind.Monitor, Monitor = ScreenCaptureService.MonitorAtCursor() };
        if (req.Kind == RecordingSourceKind.Monitor) req.Monitor = ScreenCaptureService.MonitorAtCursor();
        _ = StartAsync(req);
    }

    public static async Task<bool> StartAsync(RecordingRequest req)
    {
        if (IsBusy) return false;
        var s = SettingsService.Current;
        try
        {
            _starting = true;
            StateChanged?.Invoke();

            // ---- validation with clear messages
            if (req.Kind == RecordingSourceKind.AudioOnly && !s.RecordSystemAudio && !s.RecordMicrophone)
                return Fail("Turn on System Audio or Microphone to record audio.");
            // Resolve audio endpoints up front. ScreenRecorderLib marshals DeviceName with marshal_as, which throws
            // "NULLPTR is not supported for this conversion" for null — so every source gets an explicit endpoint ID,
            // or "" which the native layer treats as "use and follow the Windows default device".
            _systemDevice = null;
            _micDevice = null;
            if (s.RecordSystemAudio)
            {
                var speakers = AudioDevices.Speakers();
                if (speakers.Count == 0)
                    return Fail("System audio capture could not be initialized. Please check your Windows audio output device (no speakers or headphones are enabled).");
                _systemDevice = ResolveDevice(s.SystemAudioDeviceId, speakers, "audio output device");
            }
            if (s.RecordMicrophone)
            {
                var mics = AudioDevices.Microphones();
                if (mics.Count == 0)
                    return Fail("Microphone not detected. Please connect or select a microphone.");
                _micDevice = ResolveDevice(s.MicrophoneDeviceId, mics, "microphone");
            }

            var folder = s.RecordingFolder;
            if (AppPaths.ValidateWritableFolder(folder) is { } err)
            {
                folder = AppPaths.DefaultRecordingFolder;
                if (AppPaths.ValidateWritableFolder(folder) is { } err2) return Fail(err2);
                App.Tray?.Notify("Recording folder unavailable", err + " Using Videos instead.", error: true);
            }
            if (AppPaths.FreeBytes(folder) < 500L * 1024 * 1024)
                return Fail("Less than 500 MB of free disk space is left on this drive. Free some space or choose another recording folder in Settings.");

            if (req.Kind == RecordingSourceKind.Window && ScreenCaptureService.WindowBounds(req.Window) is null)
                return Fail("The selected window is closed or minimised. Restore it and try again.");

            var ext = req.Kind == RecordingSourceKind.AudioOnly ? ".m4a" : ".mp4";
            _outputPath = AppPaths.UniqueFile(folder, (req.Kind == RecordingSourceKind.AudioOnly ? "Audio " : "Recording ") + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"), ext);

            if (s.RecordingCountdown && req.Kind != RecordingSourceKind.AudioOnly)
            {
                using (await Workflows.HideOurWindowsAsync(0))
                    if (!await CountdownWindow.RunAsync(3)) return Cancelled();
            }

            var options = BuildOptions(req, s);
            _rec = Recorder.CreateRecorder(options);
            _rec.OnRecordingComplete += OnComplete;
            _rec.OnRecordingFailed += OnFailed;
            _rec.OnStatusChanged += OnStatus;
            _discard = false;
            _stopRequested = false;
            LastRequest = req;

            Clock.Reset();
            _rec.Record(_outputPath);
            Clock.Start();
            Log.Info($"Recording started: {req.Kind} -> {_outputPath}");

            _bar = new RecordingBarWindow(req);
            _bar.Show();
            try { Recorder.SetExcludeFromCapture(Native.WindowEffects.Handle(_bar), true); } catch { }
            App.Tray?.SetRecording(true);
            if (!SelfTest.Active) Application.Current.MainWindow?.Hide();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Recording start failed", ex);
            Cleanup();
            return Fail(FriendlyError(ex.Message, starting: true));
        }
        finally
        {
            _starting = false;
            StateChanged?.Invoke();
        }
    }

    private static string? _systemDevice, _micDevice;

    /// <summary>Returns the saved endpoint ID if it is still connected, otherwise "" (Windows default) and tells the user.</summary>
    private static string ResolveDevice(string? savedId, List<AudioDeviceItem> available, string what)
    {
        if (string.IsNullOrEmpty(savedId)) return "";
        if (available.Any(d => d.Id == savedId)) return savedId;
        var def = available.FirstOrDefault(d => d.IsDefault) ?? available[0];
        Log.Warn($"Saved {what} {savedId} is not connected; using the Windows default ({def.Name}).");
        App.Tray?.Notify($"Selected {what} not found", $"Using \"{def.Name}\" instead.", error: true);
        return "";
    }

    private static RecorderOptions BuildOptions(RecordingRequest req, AppSettings s)
    {
        bool pro = Entitlements.IsAllowed(Feature.AdvancedVideoExport);
        var audioSources = new List<AudioSourceBase>();
        if (s.RecordSystemAudio)
            audioSources.Add(new LoopbackAudioSource(_systemDevice ?? "") { Volume = Math.Clamp(s.SystemAudioVolume, 0f, 2f) });
        if (s.RecordMicrophone)
            audioSources.Add(new CaptureAudioSource(_micDevice ?? "") { Volume = Math.Clamp(s.MicrophoneVolume, 0f, 2f) });

        RecordingSourceBase source;
        PixelRect area;
        switch (req.Kind)
        {
            case RecordingSourceKind.Window:
                source = new WindowRecordingSource(req.Window) { IsCursorCaptureEnabled = s.RecordCursor, IsBorderRequired = false };
                area = ScreenCaptureService.WindowBounds(req.Window) ?? ScreenCaptureService.PrimaryMonitor.Bounds;
                break;
            case RecordingSourceKind.Region:
            {
                var r = req.Region;
                var mon = ScreenCaptureService.Monitors()
                    .OrderByDescending(m => m.Bounds.Intersect(r).Width * (long)m.Bounds.Intersect(r).Height).First();
                var clip = r.Intersect(mon.Bounds);
                // H.264 needs even dimensions
                clip = clip with { Width = clip.Width & ~1, Height = clip.Height & ~1 };
                if (clip.Width < 16 || clip.Height < 16) throw new InvalidOperationException("The selected area is too small to record.");
                source = new DisplayRecordingSource(mon.DeviceName)
                {
                    IsCursorCaptureEnabled = s.RecordCursor,
                    SourceRect = new ScreenRect(clip.X - mon.Bounds.X, clip.Y - mon.Bounds.Y, clip.Width, clip.Height)
                };
                area = clip;
                break;
            }
            default:
            {
                var mon = req.Monitor ?? ScreenCaptureService.PrimaryMonitor;
                source = new DisplayRecordingSource(mon.DeviceName) { IsCursorCaptureEnabled = s.RecordCursor };
                area = mon.Bounds;
                break;
            }
        }

        int fps = s.RecordingFps;
        int targetHeight = s.RecordingResolutionHeight;
        var quality = s.RecordingQuality;
        bool hevc = pro && s.UseHevc;
        if (Entitlements.IsAllowed(Feature.RecordingProfiles) && Profiles.FirstOrDefault(p => p.Name == s.RecordingProfile) is { } prof)
        {
            fps = prof.Fps; targetHeight = prof.Height; quality = prof.Quality; hevc = prof.Hevc;
        }

        ScreenSize? outSize = null;
        if (targetHeight > 0 && area.Height > targetHeight)
        {
            double k = (double)targetHeight / area.Height;
            outSize = new ScreenSize(((int)(area.Width * k)) & ~1, targetHeight & ~1);
        }

        int q = quality switch { RecordingQuality.Low => 50, RecordingQuality.High => 80, RecordingQuality.Ultra => 92, _ => 68 };
        var video = new VideoEncoderOptions
        {
            Framerate = fps,
            IsFixedFramerate = true,
            IsHardwareEncodingEnabled = true,
            IsFragmentedMp4Enabled = true, // a crash or power loss still leaves a playable file
            IsMp4FastStartEnabled = false,
            IsThrottlingDisabled = false,
            Quality = q,
            Encoder = hevc
                ? new H265VideoEncoder { BitrateMode = H265BitrateControlMode.Quality, EncoderProfile = H265Profile.Main }
                : new H264VideoEncoder { BitrateMode = H264BitrateControlMode.Quality, EncoderProfile = H264Profile.High }
        };
        if (pro && s.CustomBitrateKbps > 0)
        {
            video.Bitrate = s.CustomBitrateKbps * 1000;
            video.Encoder = hevc
                ? new H265VideoEncoder { BitrateMode = H265BitrateControlMode.CBR, EncoderProfile = H265Profile.Main }
                : new H264VideoEncoder { BitrateMode = H264BitrateControlMode.UnconstrainedVBR, EncoderProfile = H264Profile.High };
        }

        var output = new OutputOptions
        {
            RecorderMode = RecorderMode.Video,
            Stretch = StretchMode.Uniform,
        };
        if (outSize is not null) output.OutputFrameSize = outSize;
        if (req.Kind == RecordingSourceKind.AudioOnly) output.IsVideoCaptureEnabled = false;

        return new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = new List<RecordingSourceBase> { source } },
            OutputOptions = output,
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled = audioSources.Count > 0,
                Bitrate = AudioBitrate.bitrate_192kbps,
                Channels = AudioChannels.Stereo,
                AudioSources = audioSources
            },
            VideoEncoderOptions = video,
            MouseOptions = new MouseOptions
            {
                IsMousePointerEnabled = s.RecordCursor,
                IsMouseClicksDetected = s.HighlightClicks,
                MouseClickDetectionMode = MouseDetectionMode.Polling,
                MouseLeftClickDetectionColor = "#FFD43B",
                MouseRightClickDetectionColor = "#3D8BFF"
            },
            LogOptions = new LogOptions
            {
                IsLogEnabled = true,
                LogFilePath = Path.Combine(AppPaths.Logs, "recorder-native.log"),
                LogSeverityLevel = LogLevel.Warn
            }
        };
    }

    public static void Pause()
    {
        if (_rec?.Status != RecorderStatus.Recording) return;
        _rec.Pause();
        Clock.Stop();
        StateChanged?.Invoke();
    }

    public static void Resume()
    {
        if (_rec?.Status != RecorderStatus.Paused) return;
        _rec.Resume();
        Clock.Start();
        StateChanged?.Invoke();
    }

    private static bool _stopRequested;

    /// <summary>Requests finalisation. Safe to call repeatedly: only the first call reaches the encoder;
    /// the file is closed (audio and video flushed, MP4 finalised) before OnRecordingComplete fires.</summary>
    public static void Stop()
    {
        var rec = _rec;
        if (rec is null || _stopRequested) return;
        var st = rec.Status;
        if (st is RecorderStatus.Idle or RecorderStatus.Finishing) return;
        _stopRequested = true;
        Clock.Stop();
        StateChanged?.Invoke();
        try { rec.Stop(); }
        catch (Exception ex)
        {
            Log.Error("Stop failed", ex);
            _stopRequested = false;
        }
    }

    public static bool IsStopping => _stopRequested;

    /// <summary>Stops and deletes the file.</summary>
    public static void Discard()
    {
        _discard = true;
        Stop();
    }

    public static void StopAndExitWhenSaved()
    {
        _exitWhenDone = true;
        Stop();
    }

    /// <summary>Called on application exit: finalises the file synchronously as far as possible.</summary>
    public static void Abort()
    {
        if (_rec is null || _rec.Status == RecorderStatus.Idle) return;
        try { _rec.Stop(); } catch { }
        var sw = Stopwatch.StartNew();
        while (_rec is not null && _rec.Status != RecorderStatus.Idle && sw.ElapsedMilliseconds < 5000) Thread.Sleep(50);
    }

    private static void OnStatus(object? sender, RecordingStatusEventArgs e) =>
        Application.Current.Dispatcher.BeginInvoke(() => StateChanged?.Invoke());

    private static void OnComplete(object? sender, RecordingCompleteEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var path = e.FilePath;
            Cleanup();
            if (_discard)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                _discard = false;
                App.Tray?.Notify("Recording discarded", "The recording was deleted.");
            }
            else
            {
                LastFile = path;
                Log.Info("Recording saved: " + path);
                App.Tray?.Notify("Recording saved", $"{Path.GetFileName(path)} ({Elapsed:hh\\:mm\\:ss}) — click to open the folder.", () => Dialogs.ShowInFolder(path));
                if (!_exitWhenDone && !SelfTest.Active) new RecordingSavedWindow(path, Elapsed).Show();
            }
            StateChanged?.Invoke();
            if (_exitWhenDone) Application.Current.Shutdown();
        });
    }

    private static void OnFailed(object? sender, RecordingFailedEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var path = e.FilePath;
            Log.Error("Recording failed: " + e.Error);
            Cleanup();
            bool partial = !string.IsNullOrEmpty(path) && File.Exists(path) && new FileInfo(path).Length > 0;
            var msg = FriendlyError(e.Error) + (partial ? $"\n\nThe part recorded before the problem was saved:\n{path}" : "");
            if (partial) LastFile = path;
            StateChanged?.Invoke();
            if (SelfTest.Active) { SelfTest.LastFailure = msg; return; }
            MessageBox.Show(msg, "Recording stopped", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (_exitWhenDone) Application.Current.Shutdown();
        });
    }

    private static void Cleanup()
    {
        Clock.Stop();
        _stopRequested = false;
        if (_rec is not null)
        {
            _rec.OnRecordingComplete -= OnComplete;
            _rec.OnRecordingFailed -= OnFailed;
            _rec.OnStatusChanged -= OnStatus;
            try { _rec.Dispose(); } catch { }
            _rec = null;
        }
        try { _bar?.Close(); } catch { }
        _bar = null;
        App.Tray?.SetRecording(false);
    }

    private static bool Cancelled()
    {
        Cleanup();
        return false;
    }

    private static bool Fail(string message)
    {
        Log.Warn("Recording not started: " + message);
        if (SelfTest.Active) { SelfTest.LastFailure = message; return false; }
        MessageBox.Show(message, App.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    public static string FriendlyError(string? raw, bool starting = false)
    {
        var e = (raw ?? "").ToLowerInvariant();
        if (e.Contains("0x80070005") || e.Contains("access") && e.Contains("denied"))
            return "Windows blocked access to the microphone or the output folder. Check Settings › Privacy & security › Microphone, and the recording folder.";
        if (e.Contains("audio") && (e.Contains("device") || e.Contains("endpoint") || e.Contains("0x88890004")))
            return "An audio device was disconnected or is not available. Reconnect it, or turn off System Audio / Microphone, and try again.";
        if (e.Contains("disk") || e.Contains("0x80070070"))
            return "The disk is full. Free some space or choose another recording folder.";
        if (e.Contains("mf") || e.Contains("media foundation") || e.Contains("0xc00d"))
            return "Windows Media Foundation is missing or failed. On Windows N/KN editions install the \"Media Feature Pack\" from Settings › Apps › Optional features.";
        if (e.Contains("display") || e.Contains("output") || e.Contains("dxgi") || e.Contains("0x887a"))
            return "The display configuration changed (monitor unplugged, resolution or graphics driver change). Start the recording again.";
        if (e.Contains("wasapi") || e.Contains("loopback") || e.Contains("audclnt") || e.Contains("0x8889"))
            return "Audio capture could not be initialized. Please check your Windows audio output device and microphone (Settings › System › Sound).";
        var prefix = starting ? "The recording could not be started: " : "The recording stopped: ";
        return string.IsNullOrWhiteSpace(raw) ? "The recording stopped unexpectedly." : prefix + raw + "\n\nDetails were saved to the log folder.";
    }
}
