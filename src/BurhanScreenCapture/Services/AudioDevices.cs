using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BurhanScreenCapture.Services;

public sealed record AudioDeviceItem(string Id, string Name, bool IsDefault)
{
    public override string ToString() => IsDefault ? $"{Name} (Default)" : Name;
}

/// <summary>Audio endpoint enumeration and lightweight peak meters (Windows Core Audio).</summary>
public sealed class AudioDevices : IDisposable
{
    private readonly MMDeviceEnumerator _enum = new();
    private WasapiCapture? _micPreview;
    private MMDevice? _micDevice;
    private MMDevice? _render;

    public static List<AudioDeviceItem> Microphones()
    {
        var list = new List<AudioDeviceItem>();
        try
        {
            using var en = new MMDeviceEnumerator();
            string? def = null;
            try { def = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID; } catch { }
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                list.Add(new AudioDeviceItem(d.ID, d.FriendlyName, d.ID == def));
        }
        catch (Exception ex) { Log.Warn("Microphone enumeration failed: " + ex.Message); }
        return list.OrderByDescending(d => d.IsDefault).ToList();
    }

    public static bool HasPlaybackDevice()
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            return en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Any();
        }
        catch { return false; }
    }

    /// <summary>Peak level 0..1 of the default playback device (system audio).</summary>
    public float SystemPeak()
    {
        try
        {
            _render ??= _enum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return _render.AudioMeterInformation.MasterPeakValue;
        }
        catch { _render = null; return 0; }
    }

    /// <summary>Opens the microphone so its meter is live (used for previews when not recording).</summary>
    public string? StartMicPreview(string? deviceId)
    {
        StopMicPreview();
        try
        {
            _micDevice = string.IsNullOrEmpty(deviceId)
                ? _enum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                : _enum.GetDevice(deviceId);
            _micPreview = new WasapiCapture(_micDevice) { ShareMode = AudioClientShareMode.Shared };
            _micPreview.DataAvailable += (_, _) => { };
            _micPreview.StartRecording();
            return null;
        }
        catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x80070005)
        {
            StopMicPreview();
            return "Windows is blocking microphone access. Open Settings › Privacy & security › Microphone and allow desktop apps to use the microphone.";
        }
        catch (Exception ex)
        {
            StopMicPreview();
            Log.Warn("Mic preview: " + ex.Message);
            return "The microphone could not be opened. It may be unplugged or used exclusively by another app.";
        }
    }

    /// <summary>Peak level of the selected microphone. Live while recording or previewing.</summary>
    public float MicPeak(string? deviceId)
    {
        try
        {
            if (_micDevice is null || (!string.IsNullOrEmpty(deviceId) && _micDevice.ID != deviceId))
                _micDevice = string.IsNullOrEmpty(deviceId)
                    ? _enum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                    : _enum.GetDevice(deviceId);
            return _micDevice.AudioMeterInformation.MasterPeakValue;
        }
        catch { _micDevice = null; return 0; }
    }

    public void StopMicPreview()
    {
        try { _micPreview?.StopRecording(); } catch { }
        try { _micPreview?.Dispose(); } catch { }
        _micPreview = null;
    }

    public void Dispose()
    {
        StopMicPreview();
        _enum.Dispose();
    }
}
