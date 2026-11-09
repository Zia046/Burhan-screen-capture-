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
        BuildPlans();
        BuildComparison();
        LicenseService.StatusChanged += () => Dispatcher.BeginInvoke(OnShown);
    }

    public void OnShown()
    {
        var st = LicenseService.Status;
        DeviceId.Text = DeviceIdentity.DeviceId;
        StatusTitle.Text = st.Edition switch
        {
            Edition.Pro => LicensePlanInfo.DisplayName(st.Plan),
            Edition.ProTrial => "Pro Trial",
            _ => "Free Edition"
        };
        var now = DateTime.UtcNow;
        StatusDetail.Text = st.Edition switch
        {
            Edition.ProTrial => $"{st.DaysLeft(now)} day(s) left — trial ends {st.ExpiresUtc?.ToLocalTime():d}.",
            Edition.Pro when st.ExpiresUtc is { } e => $"Licensed to {(string.IsNullOrEmpty(st.LicensedTo) ? "you" : st.LicensedTo)} • valid until {e.ToLocalTime():d} ({st.DaysLeft(now)} days).",
            Edition.Pro => $"Licensed to {(string.IsNullOrEmpty(st.LicensedTo) ? "you" : st.LicensedTo)} • never expires.",
            _ => st.State == LicenseState.None ? "All free features are available with no limits on recording." : st.Message
        };
        TrialBtn.Visibility = st.Edition == Edition.Free && !LicenseService.TrialUsed ? Visibility.Visible : Visibility.Collapsed;
        DeactivateBtn.Visibility = st.Edition == Edition.Pro ? Visibility.Visible : Visibility.Collapsed;
        if (!LicenseService.IsReleaseKeyConfigured)
        {
            ActivateMsg.Text = "Developer build: no licensing key is configured, so activation is disabled. See docs/LICENSING.md.";
            ActivateMsg.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        }
    }

    private void BuildPlans()
    {
        foreach (var (plan, _, note) in LicensingConfig.Plans)
        {
            var card = new Border { Margin = new Thickness(6), Padding = new Thickness(18), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(plan == LicensePlan.Yearly ? 2 : 1) };
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceBg");
            card.SetResourceReference(Border.BorderBrushProperty, plan == LicensePlan.Yearly ? "ProGold" : "BorderBrushSoft");
            var sp = new StackPanel();
            if (plan == LicensePlan.Yearly)
            {
                var badge = new TextBlock { Text = "MOST POPULAR", FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) };
                badge.SetResourceReference(TextBlock.ForegroundProperty, "ProGold");
                sp.Children.Add(badge);
            }
            var t = new TextBlock { Text = LicensePlanInfo.DisplayName(plan), FontSize = 17, FontWeight = FontWeights.SemiBold };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            var n = new TextBlock { Text = note, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
            n.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            sp.Children.Add(t); sp.Children.Add(n);
            card.Child = sp;
            PlanCards.Children.Add(card);
        }
    }

    private void BuildComparison()
    {
        Compare.ColumnDefinitions.Add(new ColumnDefinition());
        Compare.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        Compare.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
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
                var txt = header ? (c == 1 ? "Free" : "Pro") : (c == 2 || free ? "" : "—");
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

    private void OnCopyDevice(object sender, RoutedEventArgs e) { try { Clipboard.SetText(DeviceId.Text); } catch { } }

    private void OnTrial(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = LicenseService.StartTrial();
        Show(ok, msg);
    }

    private async void OnActivate(object sender, RoutedEventArgs e)
    {
        ActivateBtn.IsEnabled = false;
        ActivateMsg.Text = "Activating…";
        ActivateMsg.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        try
        {
            var (ok, msg) = await LicenseService.ActivateOnlineAsync(KeyBox.Text);
            Show(ok, msg);
            if (ok) KeyBox.Text = "";
        }
        finally { ActivateBtn.IsEnabled = true; }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Burhan license (*.bslic)|*.bslic|All files|*.*", Title = "Import license file" };
        if (dlg.ShowDialog() != true) return;
        var (ok, msg) = LicenseService.ImportLicenseFile(dlg.FileName);
        Show(ok, msg);
    }

    private async void OnDeactivate(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Deactivate Pro on this computer? Free features stay available.", App.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var (ok, msg) = await LicenseService.DeactivateAsync();
        Show(ok, msg);
    }

    private void OnBuy(object sender, RoutedEventArgs e) => Dialogs.OpenUrl(LicensingConfig.PurchaseUrl);

    private void Show(bool ok, string msg)
    {
        ActivateMsg.Text = msg;
        ActivateMsg.SetResourceReference(TextBlock.ForegroundProperty, ok ? "Success" : "Danger");
        OnShown();
        ActivateMsg.Text = msg;
    }
}
