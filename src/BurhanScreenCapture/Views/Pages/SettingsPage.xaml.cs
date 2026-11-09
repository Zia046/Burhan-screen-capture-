using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Services;
using Microsoft.Win32;

namespace BurhanScreenCapture.Views.Pages;

public partial class SettingsPage : UserControl, IPage
{
    private bool _loading;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "BurhanScreenCapturePro";

    public SettingsPage() => InitializeComponent();

    public void OnShown()
    {
        _loading = true;
        var s = SettingsService.Current;
        ThemeBox.SelectedIndex = (int)s.Theme;
        ShotFormat.SelectedIndex = (int)s.ScreenshotFormat;
        JpgQuality.Value = s.JpgQuality;
        ShotFolder.Text = s.ScreenshotFolder;
        NamePattern.Text = s.FileNamePattern;
        AutoSave.IsChecked = s.AutoSaveScreenshots;
        ClipBox.IsChecked = s.CopyToClipboardAfterCapture;
        AfterBox.SelectedIndex = (int)s.AfterCapture;
        RecFolder.Text = s.RecordingFolder;
        FpsBox.SelectedIndex = s.RecordingFps switch { 15 => 0, 24 => 1, 60 => 3, _ => 2 };
        QualityBox.SelectedIndex = (int)s.RecordingQuality;
        var mics = AudioDevices.Microphones();
        MicBox.ItemsSource = mics;
        MicBox.SelectedItem = mics.FirstOrDefault(m => m.Id == s.MicrophoneDeviceId) ?? mics.FirstOrDefault();
        SysAudio.IsChecked = s.RecordSystemAudio;
        MicDefault.IsChecked = s.RecordMicrophone;
        StartWin.IsChecked = IsStartupEnabled();
        StartMin.IsChecked = s.StartMinimized;
        ToTray.IsChecked = s.MinimizeToTray;
        Toolbar.IsChecked = s.ShowFloatingToolbar;
        Notify.IsChecked = s.ShowNotifications;
        HistoryOn.IsChecked = s.HistoryEnabled;
        HistoryInfo.Text = $"{HistoryService.Entries.Count} entries" + (Entitlements.IsAllowed(Feature.UnlimitedHistory) ? " (unlimited)" : $" (Free keeps {FeatureCatalog.FreeHistoryLimit})");
        WmText.Text = s.WatermarkText;
        WmOpacity.Value = s.WatermarkOpacity;
        var st = LicenseService.Status;
        LicenseInfo.Text = $"{LicensePlanInfo.DisplayName(st.Edition == Edition.Free ? LicensePlan.Free : st.Plan)} • Device ID {DeviceIdentity.DeviceId}";
        BuildHotkeys();
        _loading = false;
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        var s = SettingsService.Current;

        var theme = (AppTheme)Math.Max(0, ThemeBox.SelectedIndex);
        if (theme != s.Theme) { s.Theme = theme; ThemeManager.Apply(theme); }

        var fmt = (ImageFormatOption)Math.Max(0, ShotFormat.SelectedIndex);
        if (ImageExportService.IsProFormat(fmt) && !Entitlements.Require(Feature.AdvancedExport))
        {
            _loading = true; ShotFormat.SelectedIndex = (int)s.ScreenshotFormat; _loading = false;
        }
        else s.ScreenshotFormat = fmt;

        s.FileNamePattern = string.IsNullOrWhiteSpace(NamePattern.Text) ? "Screenshot {date} {time}" : NamePattern.Text.Trim();
        s.AutoSaveScreenshots = AutoSave.IsChecked == true;
        s.CopyToClipboardAfterCapture = ClipBox.IsChecked == true;
        if (AfterBox.SelectedIndex >= 0) s.AfterCapture = (AfterCaptureAction)AfterBox.SelectedIndex;
        if (FpsBox.SelectedItem is ComboBoxItem { Tag: string f }) s.RecordingFps = int.Parse(f);
        if (QualityBox.SelectedIndex >= 0) s.RecordingQuality = (RecordingQuality)QualityBox.SelectedIndex;
        if (MicBox.SelectedItem is AudioDeviceItem mic) s.MicrophoneDeviceId = mic.Id;
        s.RecordSystemAudio = SysAudio.IsChecked == true;
        s.RecordMicrophone = MicDefault.IsChecked == true;

        if ((StartWin.IsChecked == true) != IsStartupEnabled()) SetStartup(StartWin.IsChecked == true);
        s.StartWithWindows = StartWin.IsChecked == true;
        s.StartMinimized = StartMin.IsChecked == true;
        s.MinimizeToTray = ToTray.IsChecked == true;
        s.ShowFloatingToolbar = Toolbar.IsChecked == true;
        s.ShowNotifications = Notify.IsChecked == true;
        s.HistoryEnabled = HistoryOn.IsChecked == true;

        if (sender == WmText || sender == WmOpacity)
        {
            if (Entitlements.Require(Feature.Watermark))
            {
                s.WatermarkText = WmText.Text;
                s.WatermarkOpacity = Math.Round(WmOpacity.Value, 2);
            }
            else { _loading = true; WmText.Text = s.WatermarkText; WmOpacity.Value = s.WatermarkOpacity; _loading = false; }
        }
        SettingsService.Save();
    }

    private void OnQuality(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        var s = SettingsService.Current;
        int q = (int)JpgQuality.Value;
        if (q == s.JpgQuality) return;
        if (!Entitlements.Require(Feature.AdvancedCompression)) { JpgQuality.Value = s.JpgQuality; return; }
        s.JpgQuality = q;
        SettingsService.Save();
    }

    private void OnShotFolder(object sender, RoutedEventArgs e) => PickFolder(ShotFolder, f => SettingsService.Current.ScreenshotFolder = f);
    private void OnRecFolder(object sender, RoutedEventArgs e) => PickFolder(RecFolder, f => SettingsService.Current.RecordingFolder = f);

    private static void PickFolder(TextBox box, Action<string> set)
    {
        var f = Dialogs.PickFolder(box.Text);
        if (f is null) return;
        if (AppPaths.ValidateWritableFolder(f) is { } err) { MessageBox.Show(err, App.AppName, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        set(f);
        SettingsService.Save();
        box.Text = f;
    }

    // ---------------- startup

    private static bool IsStartupEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunName) is string;
        }
        catch { return false; }
    }

    private static void SetStartup(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue(RunName, $"\"{Environment.ProcessPath}\" --minimized");
            else k.DeleteValue(RunName, false);
        }
        catch (Exception ex) { MessageBox.Show("Windows did not allow changing the startup setting: " + ex.Message, App.AppName); }
    }

    // ---------------- hotkeys

    private void BuildHotkeys()
    {
        HotkeyList.Children.Clear();
        foreach (var b in SettingsService.Current.Hotkeys.OrderBy(h => h.Action))
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var label = new TextBlock { Text = HotkeyService.Describe(b.Action) };
            label.SetResourceReference(StyleProperty, "Label");
            var box = new TextBox { Text = b.ToString(), IsReadOnly = true, Tag = b, Cursor = Cursors.Hand, ToolTip = "Click, then press a key combination" };
            box.PreviewKeyDown += OnHotkeyKey;
            // Windows only delivers key-up for Print Screen
            box.PreviewKeyUp += (sender, e) => { if (e.Key == Key.Snapshot) OnHotkeyKey(sender, e); };
            var on = new CheckBox { Content = "Enabled", IsChecked = b.Enabled, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = b };
            on.Click += (_, _) => { b.Enabled = on.IsChecked == true; ApplyHotkeys(); };
            Grid.SetColumn(box, 1); Grid.SetColumn(on, 2);
            row.Children.Add(label); row.Children.Add(box); row.Children.Add(on);
            HotkeyList.Children.Add(row);
        }
        ShowHotkeyFailures();
    }

    private void OnHotkeyKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender;
        var b = (HotkeyBinding)box.Tag;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        if (key is Key.Back or Key.Delete) { b.Key = Key.None; b.Modifiers = ModifierKeys.None; box.Text = b.ToString(); ApplyHotkeys(); return; }
        if (key == Key.Escape) { Keyboard.ClearFocus(); return; }
        var mods = Keyboard.Modifiers;
        if (HotkeyService.IsReserved(mods, key))
        {
            HotkeyStatus.Text = "That combination is used by Windows or common apps. Use Ctrl, Shift or Alt with a letter, number, F-key or Print Screen.";
            return;
        }
        var clash = SettingsService.Current.Hotkeys.FirstOrDefault(h => h != b && h.Key == key && h.Modifiers == mods);
        if (clash is not null)
        {
            HotkeyStatus.Text = $"Already used for \"{HotkeyService.Describe(clash.Action)}\".";
            return;
        }
        b.Key = key;
        b.Modifiers = mods;
        box.Text = b.ToString();
        ApplyHotkeys();
    }

    private void ApplyHotkeys()
    {
        SettingsService.Save();
        App.ReloadHotkeys();
        ShowHotkeyFailures();
    }

    private void ShowHotkeyFailures() =>
        HotkeyStatus.Text = App.FailedHotkeys.Count == 0 ? "" : App.HotkeyHint();

    private void OnResetHotkeys(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.Hotkeys = AppSettings.DefaultHotkeys();
        ApplyHotkeys();
        BuildHotkeys();
    }

    // ---------------- misc

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Clear the history list? Your image files will NOT be deleted.", App.AppName, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            HistoryService.Clear();
            OnShown();
        }
    }

    private void OnLicense(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.Navigate("Upgrade");
    private void OnLogs(object sender, RoutedEventArgs e) => Dialogs.OpenFolder(AppPaths.Logs);

    private void OnResetAll(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Reset all settings to their defaults? Your license, screenshots and recordings are not affected.", App.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SettingsService.ResetToDefaults();
        ThemeManager.Apply(SettingsService.Current.Theme);
        App.ReloadHotkeys();
        OnShown();
    }
}
