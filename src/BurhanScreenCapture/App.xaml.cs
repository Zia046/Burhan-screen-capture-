using System.Windows;
using System.Windows.Threading;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Services;
using BurhanScreenCapture.Views;

namespace BurhanScreenCapture;

public partial class App : Application
{
    public const string AppName = "Burhan Screen Capture Pro";
    public const string Tagline = "Capture. Create. Communicate.";
    public static string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    public static TrayIconService? Tray { get; private set; }
    public static HotkeyService? Hotkeys { get; private set; }
    public static IReadOnlyList<string> FailedHotkeys { get; private set; } = Array.Empty<string>();

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\BurhanNexus.ScreenCapturePro", out bool isFirst);
        if (!isFirst)
        {
            try { EventWaitHandle.OpenExisting(@"Local\BurhanNexus.ScreenCapturePro.Show").Set(); } catch { }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Error("Fatal", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("Unobserved task", a.Exception); a.SetObserved(); };

        base.OnStartup(e);
        Log.Cleanup();
        Log.Info($"Starting {AppName} {Version} on {Environment.OSVersion}");

        SettingsService.Load();
        ThemeManager.Apply(SettingsService.Current.Theme);
        LicenseService.Initialize();
        HistoryService.Load();
        LicenseService.StatusChanged += HistoryService.EnforceLimit;

        Tray = new TrayIconService();
        Hotkeys = new HotkeyService();
        Hotkeys.Pressed += a => Dispatcher.BeginInvoke(() => Workflows.RunHotkey(a));
        ReloadHotkeys();

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\BurhanNexus.ScreenCapturePro.Show");
        var t = new Thread(() =>
        {
            while (_showSignal.WaitOne()) Dispatcher.BeginInvoke(ShowMainWindow);
        }) { IsBackground = true, Name = "SingleInstanceSignal" };
        t.Start();

        bool minimized = e.Args.Contains("--minimized") || SettingsService.Current.StartMinimized;
        MainWindow = new MainWindow();
        if (!minimized) MainWindow.Show();
        else Tray.Notify(AppName, "Running in the notification area. Press Print Screen to capture.");

        if (SettingsService.Current.ShowFloatingToolbar) FloatingToolbar.ShowToolbar();
        if (FailedHotkeys.Count > 0) Tray.Notify("Some shortcuts are in use", HotkeyHint());

        _ = LicenseService.RefreshIfDueAsync();
    }

    public static string HotkeyHint() =>
        "These shortcuts are used by another app or by Windows: " + string.Join(", ", FailedHotkeys) +
        ". On Windows 11, turn off Settings › Accessibility › Keyboard › \"Use the Print screen key to open screen capture\", or change the shortcut in Settings.";

    public static void ReloadHotkeys()
    {
        if (Hotkeys is null) return;
        FailedHotkeys = Hotkeys.RegisterAll(SettingsService.Current.Hotkeys);
        foreach (var f in FailedHotkeys) Log.Warn("Hotkey unavailable: " + f);
    }

    public static void ShowMainWindow()
    {
        var w = Current.MainWindow ?? new MainWindow();
        Current.MainWindow = w;
        if (!w.IsVisible) w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        w.Topmost = true; w.Topmost = false;
        w.Focus();
    }

    public static void ExitApplication()
    {
        if (RecorderController.IsBusy)
        {
            var r = MessageBox.Show("A recording is in progress. Stop and save it before exiting?", AppName,
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes) { RecorderController.StopAndExitWhenSaved(); return; }
        }
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { RecorderController.Abort(); } catch { }
        try { Hotkeys?.Dispose(); } catch { }
        try { Tray?.Dispose(); } catch { }
        try { _singleInstance?.ReleaseMutex(); } catch { }
        Log.Info("Exit");
        base.OnExit(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
        var msg = e.Exception switch
        {
            OutOfMemoryException => "Your computer is low on memory. Close some programs and try again.",
            UnauthorizedAccessException => "Windows blocked access to a file or folder. Check the save location in Settings.",
            IOException io => "A file operation failed: " + io.Message,
            _ => "Something went wrong, but your work is safe. The problem was written to the log file:\n" + AppPaths.Logs
        };
        MessageBox.Show(msg, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
