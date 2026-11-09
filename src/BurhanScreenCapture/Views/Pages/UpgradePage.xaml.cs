using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views.Pages;

public partial class UpgradePage : UserControl, IPage
{
    public UpgradePage()
    {
        InitializeComponent();
        BuildComparison();
        LicenseService.StatusChanged += () => Dispatcher.BeginInvoke(OnShown);
    }

    public void OnShown()
    {
        var st = LicenseService.Status;
        DeviceText.Text = $"{DeviceIdentity.DeviceName}  •  Device ID {DeviceIdentity.DeviceId}";
        PageTitle.Text = st.IsPro ? "Your License" : "Upgrade to Pro";
        ActiveCard.Visibility = st.IsPro ? Visibility.Visible : Visibility.Collapsed;
        ProButtons.Visibility = st.IsPro ? Visibility.Collapsed : Visibility.Visible;
        FreeBtn.Visibility = st.IsPro ? Visibility.Collapsed : Visibility.Visible;
        if (st.IsPro)
        {
            ActDate.Text = LicenseService.ActivatedLocal?.ToString("dd MMMM yyyy, hh:mm tt") ?? "—";
            ActId.Text = st.Data?.LicenseId ?? "";
            ActDevice.Text = $"{LicenseService.ActivatedDeviceName} • Device ID {DeviceIdentity.DeviceId}" +
                             (st.Data?.BoundDeviceId is null ? "" : " (key locked to this computer)");
        }
        bool problem = !st.IsPro && st.State != LicenseState.None;
        ProblemCard.Visibility = problem ? Visibility.Visible : Visibility.Collapsed;
        ProblemText.Text = problem ? st.Message : "";
        if (!LicenseService.IsReleaseKeyConfigured)
        {
            ProblemCard.Visibility = Visibility.Visible;
            ProblemText.Text = "Developer build: no licensing public key is built in, so license keys cannot be activated. See docs/LICENSING.md.";
        }
    }

    private void BuildComparison()
    {
        Compare.ColumnDefinitions.Add(new ColumnDefinition());
        Compare.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        Compare.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        int row = 0;
        void Add(string name, bool free, bool header = false)
        {
            Compare.RowDefinitions.Add(new RowDefinition());
            var a = new TextBlock { Text = name, Margin = new Thickness(0, 4, 0, 4), FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal };
            a.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            Grid.SetRow(a, row);
            Compare.Children.Add(a);
            for (int c = 1; c <= 2; c++)
            {
                var txt = header ? (c == 1 ? "Free" : "Lifetime Pro") : (c == 2 || free ? "" : "—");
                var m = new TextBlock { Text = txt, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4), FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal };
                if (!header && txt != "—") m.FontFamily = (FontFamily)Application.Current.Resources["IconFont"];
                m.SetResourceReference(TextBlock.ForegroundProperty, header ? "TextPrimary" : txt == "—" ? "TextSecondary" : (c == 2 ? "ProGold" : "Success"));
                Grid.SetRow(m, row); Grid.SetColumn(m, c);
                Compare.Children.Add(m);
            }
            row++;
        }
        Add("Feature", false, true);
        foreach (var name in new[] { "Screenshots: area, window, full screen, multi-monitor, delay", "Screen recording to MP4 — no watermark, no time limit",
                     "System audio + microphone recording", "Editor: crop, resize, rotate, arrows, shapes, text, undo/redo", "History (latest 20)" })
            Add(name, true);
        foreach (var f in FeatureCatalog.All.Where(f => f.Tier == FeatureTier.Pro)) Add(f.Name, false);
    }

    private void OnContinueFree(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.Navigate("Home");
    private void OnPurchase(object sender, RoutedEventArgs e) => PurchaseDialog.Show(Window.GetWindow(this));
    private void OnEnterKey(object sender, RoutedEventArgs e) { LicenseKeyDialog.Show(Window.GetWindow(this)); OnShown(); }
    private void OnCopyDevice(object sender, RoutedEventArgs e) { try { Clipboard.SetText(DeviceIdentity.DeviceId); } catch { } }

    private async void OnDeactivate(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Remove Lifetime Pro from this computer?\n\nFree features stay available. You can activate the same key again on this computer at any time.",
                App.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var (_, msg) = await LicenseService.DeactivateAsync();
        MessageBox.Show(msg, App.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        OnShown();
    }
}
