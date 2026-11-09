using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Native;
using BurhanScreenCapture.Services;
using BurhanScreenCapture.Views.Pages;

namespace BurhanScreenCapture.Views;

public interface IPage
{
    void OnShown();
}

public partial class MainWindow : Window
{
    private readonly Dictionary<string, Func<UserControl>>? _factories;
    private bool _navigating;
    private readonly Dictionary<string, UserControl> _pages = new();
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();
        _factories = new()
        {
            ["Home"] = () => new DashboardPage(),
            ["Capture"] = () => new CapturePage(),
            ["Recorder"] = () => new RecorderPage(),
            ["Editor"] = () => new EditorPage(),
            ["History"] = () => new HistoryPage(),
            ["Tools"] = () => new QuickToolsPage(),
            ["Settings"] = () => new SettingsPage(),
            ["Upgrade"] = () => new UpgradePage(),
            ["About"] = () => new AboutPage(),
        };
        SourceInitialized += (_, _) => WindowEffects.ApplyTitleBarTheme(this, ThemeManager.IsDark);
        LicenseService.StatusChanged += () => Dispatcher.BeginInvoke(UpdateEdition);
        UpdateEdition();
        Navigate("Home");
    }

    private void UpdateEdition()
    {
        var st = LicenseService.Status;
        EditionText.Text = EditionInfo.Name(st.Edition);
        EditionSub.Text = st.IsPro ? "Permanently activated" : "Free Forever";
        SidebarUpgrade.Content = st.IsPro ? "License details" : "Upgrade to Pro";
    }

    public void Navigate(string key)
    {
        if (_factories is null || _navigating) return;
        _navigating = true;
        try
        {
            if (!_pages.TryGetValue(key, out var page))
            {
                page = _factories[key]();
                _pages[key] = page;
            }
            PageHost.Content = page;
            (page as IPage)?.OnShown();
            foreach (var rb in Nav.Children.OfType<RadioButton>())
                if ((string)rb.Tag == key && rb.IsChecked != true) rb.IsChecked = true;
        }
        finally { _navigating = false; }
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key }) Navigate(key);
    }

    private void OnUpgradeClick(object sender, RoutedEventArgs e) => Navigate("Upgrade");

    protected override void OnClosing(CancelEventArgs e)
    {
        if (SettingsService.Current.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                App.Tray?.Notify(App.AppName, "Still running in the notification area. Right-click the icon to exit.");
            }
            return;
        }
        e.Cancel = true;
        App.ExitApplication();
    }
}
