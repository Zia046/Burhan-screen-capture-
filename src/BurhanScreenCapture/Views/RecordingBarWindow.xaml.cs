using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views;

public partial class RecordingBarWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly AudioDevices _audio = new();
    private int _tick;
    private float _sysPeak, _micPeak;
    private bool _clipWarned;

    public RecordingBarWindow(RecordingRequest req)
    {
        InitializeComponent();
        var s = SettingsService.Current;
        SysRow.Visibility = s.RecordSystemAudio ? Visibility.Visible : Visibility.Collapsed;
        MicRow.Visibility = s.RecordMicrophone ? Visibility.Visible : Visibility.Collapsed;
        if (!s.RecordSystemAudio && !s.RecordMicrophone) Meters.Visibility = Visibility.Collapsed;

        Loaded += (_, _) =>
        {
            // Top centre of the monitor being recorded (or primary)
            var mon = req.Monitor?.Bounds ?? (req.Kind == RecordingSourceKind.Region
                ? ScreenCaptureService.Monitors().First(m => m.Bounds.Intersect(req.Region).Width > 0).Bounds
                : ScreenCaptureService.PrimaryMonitor.Bounds);
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            int w = (int)(ActualWidth * dpi.DpiScaleX), h = (int)(ActualHeight * dpi.DpiScaleY);
            int y = mon.Y + 8;
            if (req.Kind == RecordingSourceKind.Region && req.Region.Y - mon.Y < h + 16) y = Math.Min(req.Region.Bottom + 8, mon.Bottom - h - 8);
            Native.WindowEffects.SetPhysicalBounds(this, mon.X + (mon.Width - w) / 2, y, w, h);
        };
        _timer.Tick += OnTick;
        _timer.Start();
        RecorderController.StateChanged += Update;
        Closed += (_, _) =>
        {
            _timer.Stop();
            RecorderController.StateChanged -= Update;
            _audio.Dispose();
        };
    }

    private void OnTick(object? sender, EventArgs e)
    {
        Time.Text = RecorderController.Elapsed.ToString(@"hh\:mm\:ss");
        if (++_tick % 5 == 0) Dot.Opacity = RecorderController.IsPaused ? 0.35 : (Dot.Opacity < 1 ? 1 : 0.55);
        var s = SettingsService.Current;
        if (s.RecordSystemAudio) { _sysPeak = Math.Max(_audio.SystemPeak(), _sysPeak * 0.8f); SysMeter.Value = _sysPeak; }
        if (s.RecordMicrophone) { _micPeak = Math.Max(_audio.MicPeak(s.MicrophoneDeviceId), _micPeak * 0.8f); MicMeter.Value = _micPeak; }
        SysMeter.Foreground = _sysPeak > 0.97f ? System.Windows.Media.Brushes.OrangeRed : System.Windows.Media.Brushes.MediumSeaGreen;
        MicMeter.Foreground = _micPeak > 0.97f ? System.Windows.Media.Brushes.OrangeRed : System.Windows.Media.Brushes.MediumSeaGreen;
        if (!_clipWarned && _micPeak > 0.99f)
        {
            _clipWarned = true;
            App.Tray?.Notify("Microphone is too loud", "Your voice may be distorted. Lower the microphone volume in Settings › Recording.");
        }
    }

    private void Update()
    {
        PauseBtn.Content = RecorderController.IsPaused ? "" : "";
        PauseBtn.ToolTip = RecorderController.IsPaused ? "Resume" : "Pause";
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { }
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        if (RecorderController.IsPaused) RecorderController.Resume(); else RecorderController.Pause();
    }

    private void OnStop(object sender, RoutedEventArgs e) => RecorderController.Stop();

    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Delete this recording?", App.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            RecorderController.Discard();
    }
}
