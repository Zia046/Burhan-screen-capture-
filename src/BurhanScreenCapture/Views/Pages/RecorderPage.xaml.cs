using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Native;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views.Pages;

public sealed record WindowItem(IntPtr Handle, string Title);

public partial class RecorderPage : UserControl, IPage
{
    private bool _loading;
    private PixelRect? _area;
    private AudioDevices? _audio;
    private readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private float _sys, _mic;

    public RecorderPage()
    {
        InitializeComponent();
        _meterTimer.Tick += OnMeter;
        IsVisibleChanged += (_, _) => { if (IsVisible) StartMeters(); else StopMeters(); };
        RecorderController.StateChanged += () => Dispatcher.BeginInvoke(UpdateState);
    }

    public void OnShown()
    {
        _loading = true;
        var s = SettingsService.Current;
        var mons = ScreenCaptureService.Monitors();
        MonitorBox.ItemsSource = mons;
        MonitorBox.SelectedItem = mons.FirstOrDefault(m => m.IsPrimary) ?? mons.FirstOrDefault();
        LoadWindows();

        SysAudio.IsChecked = s.RecordSystemAudio;
        Mic.IsChecked = s.RecordMicrophone;
        SysVol.Value = s.SystemAudioVolume;
        MicVol.Value = s.MicrophoneVolume;
        var mics = AudioDevices.Microphones();
        MicBox.ItemsSource = mics;
        MicBox.SelectedItem = mics.FirstOrDefault(m => m.Id == s.MicrophoneDeviceId) ?? mics.FirstOrDefault();
        if (mics.Count == 0) MicBox.ItemsSource = new[] { new AudioDeviceItem("", "No microphone found", false) };

        SelectByTag(FpsBox, s.RecordingFps.ToString(), 2);
        QualityBox.SelectedIndex = (int)s.RecordingQuality;
        SelectByTag(ResBox, s.RecordingResolutionHeight.ToString(), 0);
        CursorBox.IsChecked = s.RecordCursor;
        ClicksBox.IsChecked = s.HighlightClicks;
        CountdownBox.IsChecked = s.RecordingCountdown;

        ProfileBox.ItemsSource = new[] { "None (use settings above)" }.Concat(RecorderController.Profiles.Select(p => p.Name)).ToList();
        ProfileBox.SelectedItem = RecorderController.Profiles.Any(p => p.Name == s.RecordingProfile) ? s.RecordingProfile : "None (use settings above)";
        HevcBox.IsChecked = s.UseHevc;
        BitrateBox.Text = s.CustomBitrateKbps.ToString();
        _loading = false;
        UpdateState();
        if (IsVisible) StartMeters();
    }

    private static void SelectByTag(ComboBox box, string tag, int fallback)
    {
        box.SelectedIndex = fallback;
        for (int i = 0; i < box.Items.Count; i++)
            if (box.Items[i] is ComboBoxItem { Tag: string t } && t == tag) box.SelectedIndex = i;
    }

    private void UpdateState()
    {
        StartText.Text = RecorderController.IsBusy ? "Stop Recording" : "Start Recording";
        LastText.Text = RecorderController.LastFile is { } f ? $"Last recording: {f}" : "";
    }

    // ---------------- sources

    private void LoadWindows()
    {
        var mine = Application.Current.Windows.OfType<Window>().Select(WindowEffects.Handle).ToHashSet();
        var items = WindowEnum.VisibleWindows(mine)
            .Select(w => new WindowItem(w.Handle, ScreenCaptureService.WindowTitle(w.Handle)))
            .Where(w => !string.IsNullOrWhiteSpace(w.Title) && w.Title != "Program Manager")
            .ToList();
        var prev = WindowBox.SelectedItem as WindowItem;
        WindowBox.ItemsSource = items;
        WindowBox.SelectedItem = items.FirstOrDefault(i => i.Handle == prev?.Handle) ?? items.FirstOrDefault();
    }

    private void OnWindowsOpen(object? sender, EventArgs e) => LoadWindows();
    private void OnRefreshWindows(object sender, RoutedEventArgs e) => LoadWindows();

    private async void OnSelectArea(object sender, RoutedEventArgs e) => await SelectAreaAsync();

    private async Task SelectAreaAsync()
    {
        using (await Workflows.HideOurWindowsAsync())
        {
            var r = Workflows.SelectRegion(RegionSelectMode.Free, "Select the area to record • Esc to cancel");
            if (r is null) return;
            _area = r.Value.Rect;
        }
        AreaText.Text = $"{_area.Value.Width} × {_area.Value.Height} at ({_area.Value.X}, {_area.Value.Y})";
        SrcRegion.IsChecked = true;
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (RecorderController.IsBusy) { RecorderController.Stop(); return; }
        var req = new RecordingRequest();
        if (SrcRegion.IsChecked == true)
        {
            if (_area is null) { await SelectAreaAsync(); if (_area is null) return; }
            req.Kind = RecordingSourceKind.Region;
            req.Region = _area!.Value;
        }
        else if (SrcWindow.IsChecked == true)
        {
            if (WindowBox.SelectedItem is not WindowItem w) { MessageBox.Show("Choose a window to record.", App.AppName); return; }
            req.Kind = RecordingSourceKind.Window;
            req.Window = w.Handle;
            req.WindowTitle = w.Title;
        }
        else if (SrcAudio.IsChecked == true) req.Kind = RecordingSourceKind.AudioOnly;
        else { req.Kind = RecordingSourceKind.Monitor; req.Monitor = MonitorBox.SelectedItem as MonitorInfo; }

        StopMeters(); // release the microphone preview before the recorder opens it
        if (!await RecorderController.StartAsync(req) && IsVisible) StartMeters();
    }

    private void OnGif(object sender, RoutedEventArgs e) => GifRecorder.StartInteractive();

    private void OnOpenFolder(object sender, RoutedEventArgs e) => Dialogs.OpenFolder(SettingsService.Current.RecordingFolder);

    // ---------------- audio

    private void StartMeters()
    {
        if (RecorderController.IsBusy) return;
        _audio ??= new AudioDevices();
        AudioError.Visibility = Visibility.Collapsed;
        if (Mic.IsChecked == true)
        {
            var err = _audio.StartMicPreview((MicBox.SelectedItem as AudioDeviceItem)?.Id);
            if (err is not null) { AudioError.Text = err; AudioError.Visibility = Visibility.Visible; }
        }
        _meterTimer.Start();
    }

    private void StopMeters()
    {
        _meterTimer.Stop();
        _audio?.Dispose();
        _audio = null;
        SysMeter.Value = MicMeter.Value = 0;
    }

    private void OnMeter(object? sender, EventArgs e)
    {
        if (_audio is null) return;
        _sys = SysAudio.IsChecked == true ? Math.Max(_audio.SystemPeak(), _sys * 0.8f) : 0;
        _mic = Mic.IsChecked == true ? Math.Max(_audio.MicPeak((MicBox.SelectedItem as AudioDeviceItem)?.Id), _mic * 0.8f) : 0;
        SysMeter.Value = _sys;
        MicMeter.Value = _mic;
    }

    private void OnAudioOption(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SettingsService.Current.RecordSystemAudio = SysAudio.IsChecked == true;
        SettingsService.Current.RecordMicrophone = Mic.IsChecked == true;
        SettingsService.Save();
        StopMeters();
        if (IsVisible) StartMeters();
    }

    private void OnMicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MicBox.SelectedItem is not AudioDeviceItem d || d.Id == "") return;
        SettingsService.Current.MicrophoneDeviceId = d.Id;
        SettingsService.Save();
        StopMeters();
        if (IsVisible) StartMeters();
    }

    private void OnVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || !IsLoaded) return;
        SettingsService.Current.SystemAudioVolume = (float)SysVol.Value;
        SettingsService.Current.MicrophoneVolume = (float)MicVol.Value;
        SettingsService.Save();
    }

    // ---------------- video

    private void OnVideoOption(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        var s = SettingsService.Current;
        if (FpsBox.SelectedItem is ComboBoxItem { Tag: string f }) s.RecordingFps = int.Parse(f);
        if (QualityBox.SelectedIndex >= 0) s.RecordingQuality = (RecordingQuality)QualityBox.SelectedIndex;
        if (ResBox.SelectedItem is ComboBoxItem { Tag: string r }) s.RecordingResolutionHeight = int.Parse(r);
        s.RecordCursor = CursorBox.IsChecked == true;
        s.HighlightClicks = ClicksBox.IsChecked == true;
        s.RecordingCountdown = CountdownBox.IsChecked == true;
        SettingsService.Save();
    }

    private void OnProfile(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        var name = ProfileBox.SelectedItem as string;
        bool isNone = name is null || name.StartsWith("None");
        if (!isNone && !Entitlements.Require(Feature.RecordingProfiles))
        {
            _loading = true; ProfileBox.SelectedIndex = 0; _loading = false;
            return;
        }
        SettingsService.Current.RecordingProfile = isNone ? null : name;
        SettingsService.Save();
    }

    private void OnHevc(object sender, RoutedEventArgs e)
    {
        if (HevcBox.IsChecked == true && !Entitlements.Require(Feature.AdvancedVideoExport)) { HevcBox.IsChecked = false; return; }
        SettingsService.Current.UseHevc = HevcBox.IsChecked == true;
        SettingsService.Save();
    }

    private void OnBitrate(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(BitrateBox.Text, out var kbps) || kbps < 0) kbps = 0;
        if (kbps > 0 && !Entitlements.Require(Feature.AdvancedVideoExport)) kbps = 0;
        SettingsService.Current.CustomBitrateKbps = Math.Min(kbps, 100_000);
        SettingsService.Save();
        BitrateBox.Text = SettingsService.Current.CustomBitrateKbps.ToString();
    }
}
