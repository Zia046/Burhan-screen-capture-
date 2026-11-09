using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BurhanScreenCapture.Services;

/// <summary>
/// Audio-only recording (system audio and/or microphone, no video).
/// Each WASAPI input is converted to 48 kHz stereo float, mixed on a wall-clock driven pump (so silence from an idle
/// loopback device keeps the timeline continuous), written to a temporary 16-bit WAV, and encoded to AAC (.m4a) with
/// Windows Media Foundation when recording stops. If AAC encoding is unavailable the WAV is kept instead.
/// </summary>
public sealed class AudioOnlyRecorder : IDisposable
{
    private const int Rate = 48000;
    private static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);

    private readonly List<(IWaveIn Capture, BufferedWaveProvider Buffer)> _inputs = new();
    private readonly MMDeviceEnumerator _enum = new();
    private MixingSampleProvider? _mixer;
    private WaveFileWriter? _wav;
    private Thread? _pump;
    private readonly Stopwatch _clock = new();
    private long _framesWritten;
    private volatile bool _stop, _paused;
    private Exception? _error;
    private readonly string _outPath;
    private readonly string _wavPath;
    private int _stopping;

    public event Action<string>? Completed;
    public event Action<string?, string>? Failed;

    public bool IsPaused => _paused;
    public TimeSpan Elapsed => _clock.Elapsed;

    public AudioOnlyRecorder(string outputPath)
    {
        _outPath = outputPath;
        _wavPath = Path.Combine(AppPaths.Temp, Path.GetFileNameWithoutExtension(outputPath) + ".wav");
    }

    /// <param name="loopbackDeviceId">null = no system audio, "" = Windows default output.</param>
    /// <param name="micDeviceId">null = no microphone, "" = Windows default microphone.</param>
    public void Start(string? loopbackDeviceId, float systemVolume, string? micDeviceId, float micVolume)
    {
        _mixer = new MixingSampleProvider(MixFormat) { ReadFully = true };
        if (loopbackDeviceId is not null)
        {
            var dev = loopbackDeviceId == "" ? _enum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : _enum.GetDevice(loopbackDeviceId);
            AddInput(new WasapiLoopbackCapture(dev), systemVolume);
        }
        if (micDeviceId is not null)
        {
            var dev = micDeviceId == "" ? _enum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications) : _enum.GetDevice(micDeviceId);
            AddInput(new WasapiCapture(dev) { ShareMode = AudioClientShareMode.Shared }, micVolume);
        }
        if (_inputs.Count == 0) throw new InvalidOperationException("No audio source selected.");

        _wav = new WaveFileWriter(_wavPath, new WaveFormat(Rate, 16, 2));
        foreach (var (cap, _) in _inputs) cap.StartRecording();
        _clock.Start();
        _pump = new Thread(Pump) { IsBackground = true, Name = "AudioOnlyPump", Priority = ThreadPriority.AboveNormal };
        _pump.Start();
    }

    private void AddInput(IWaveIn capture, float volume)
    {
        var fmt = capture.WaveFormat;
        if (fmt.SampleRate < 8000 || fmt.Channels < 1)
            throw new InvalidOperationException($"Unsupported audio format: {fmt.SampleRate} Hz, {fmt.Channels} channel(s).");
        var buffer = new BufferedWaveProvider(fmt) { DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromSeconds(3), ReadFully = true };
        capture.DataAvailable += (_, e) =>
        {
            if (!_paused && e.BytesRecorded > 0) buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        };
        capture.RecordingStopped += (_, e) => { if (e.Exception is not null && !_stop) _error = e.Exception; };

        ISampleProvider sp = buffer.ToSampleProvider();
        sp = sp.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(sp),
            2 => sp,
            _ => new FirstTwoChannels(sp)
        };
        if (sp.WaveFormat.SampleRate != Rate) sp = new WdlResamplingSampleProvider(sp, Rate);
        sp = new VolumeSampleProvider(sp) { Volume = Math.Clamp(volume, 0f, 2f) };
        _mixer!.AddMixerInput(sp);
        _inputs.Add((capture, buffer));
    }

    private void Pump()
    {
        var floats = new float[Rate / 5 * 2];
        var bytes = new byte[floats.Length * 2];
        try
        {
            while (!_stop)
            {
                Thread.Sleep(20);
                if (_error is not null) break;
                if (_paused) continue;
                long target = (long)(_clock.Elapsed.TotalSeconds * Rate);
                int frames = (int)Math.Min(target - _framesWritten, Rate / 5);
                if (frames <= 0) continue;
                int n = _mixer!.Read(floats, 0, frames * 2);
                for (int i = 0; i < n; i++)
                {
                    // Hard limit instead of integer wrap-around when sources add up above full scale.
                    var s = (short)(Math.Clamp(floats[i], -1f, 1f) * short.MaxValue);
                    bytes[i * 2] = (byte)s;
                    bytes[i * 2 + 1] = (byte)(s >> 8);
                }
                _wav!.Write(bytes, 0, n * 2);
                _framesWritten += frames;
            }
        }
        catch (Exception ex) { _error = ex; }
        if (_error is not null && !_stop) ThreadPool.QueueUserWorkItem(_ => FinishAsync(failed: true));
    }

    public void Pause()
    {
        _paused = true;
        _clock.Stop();
    }

    public void Resume()
    {
        foreach (var (_, b) in _inputs) b.ClearBuffer();
        _paused = false;
        _clock.Start();
    }

    /// <summary>Stops all captures, flushes and closes the WAV, then encodes. Repeated calls are ignored.</summary>
    public void Stop() => ThreadPool.QueueUserWorkItem(_ => FinishAsync(failed: false));

    /// <summary>Synchronous variant used when the application is closing.</summary>
    public void StopAndWait() => FinishAsync(failed: false);

    private void FinishAsync(bool failed)
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 1) return;
        _stop = true;
        _clock.Stop();
        try { _pump?.Join(2000); } catch { }
        foreach (var (cap, _) in _inputs)
        {
            try { cap.StopRecording(); } catch { }
            try { cap.Dispose(); } catch { }
        }
        try { _wav?.Dispose(); } catch { }

        if (failed || _error is not null)
        {
            var kept = KeepWavIfUseful();
            Failed?.Invoke(kept, _error?.Message ?? "Audio capture stopped unexpectedly.");
            return;
        }
        try
        {
            MediaFoundationApi.Startup();
            using (var reader = new WaveFileReader(_wavPath))
                MediaFoundationEncoder.EncodeToAac(reader, _outPath, 192000);
            File.Delete(_wavPath);
            Completed?.Invoke(_outPath);
        }
        catch (Exception ex)
        {
            Log.Error("AAC encoding failed; keeping WAV", ex);
            var kept = KeepWavIfUseful();
            if (kept is not null) Completed?.Invoke(kept);
            else Failed?.Invoke(null, "The audio could not be saved: " + ex.Message);
        }
    }

    private string? KeepWavIfUseful()
    {
        try
        {
            if (!File.Exists(_wavPath) || new FileInfo(_wavPath).Length <= 44) return null;
            var dest = Path.ChangeExtension(_outPath, ".wav");
            File.Move(_wavPath, dest, true);
            return dest;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        _stop = true;
        foreach (var (cap, _) in _inputs) { try { cap.Dispose(); } catch { } }
        try { _wav?.Dispose(); } catch { }
        _enum.Dispose();
    }

    /// <summary>Down-mixes surround inputs by keeping front-left/front-right.</summary>
    private sealed class FirstTwoChannels : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private float[] _buf = Array.Empty<float>();
        public FirstTwoChannels(ISampleProvider src)
        {
            _src = src;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(src.WaveFormat.SampleRate, 2);
        }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            int ch = _src.WaveFormat.Channels, frames = count / 2;
            if (_buf.Length < frames * ch) _buf = new float[frames * ch];
            int read = _src.Read(_buf, 0, frames * ch) / ch;
            for (int f = 0; f < read; f++)
            {
                buffer[offset + f * 2] = _buf[f * ch];
                buffer[offset + f * 2 + 1] = _buf[f * ch + 1];
            }
            return read * 2;
        }
    }
}
