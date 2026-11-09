using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views;

/// <summary>"Enter License Key" — paste a BNX-SC-PRO key or import a license file.</summary>
public sealed class LicenseKeyDialog : SmallDialog
{
    private readonly TextBox _key;
    private readonly TextBlock _msg;
    private readonly Button _activate;

    private LicenseKeyDialog(Window? owner) : base(owner, "Enter License Key")
    {
        Body.Children.Add(Label("Paste the Lifetime Pro license key from your purchase message. It starts with BNX-SC-PRO-."));
        _key = new TextBox
        {
            Width = 470, Height = 110, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Body.Children.Add(_key);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var paste = new Button { Content = "Paste", Margin = new Thickness(0, 0, 8, 0) };
        paste.Click += (_, _) => { try { if (Clipboard.ContainsText()) _key.Text = Clipboard.GetText(); } catch { } };
        var import = new Button { Content = "Import license file…" };
        import.Click += async (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Burhan Nexus license (*.bnxlic;*.txt)|*.bnxlic;*.txt|All files|*.*", Title = "Import license file" };
            if (dlg.ShowDialog(this) != true) return;
            await Run(() => LicenseService.ImportLicenseFileAsync(dlg.FileName));
        };
        tools.Children.Add(paste);
        tools.Children.Add(import);
        Body.Children.Add(tools);
        _msg = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 470, Margin = new Thickness(0, 10, 0, 0) };
        Body.Children.Add(_msg);
        var dev = Label($"This computer's Device ID: {DeviceIdentity.DeviceId}");
        dev.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        dev.FontSize = 12;
        dev.Margin = new Thickness(0, 8, 0, 0);
        Body.Children.Add(dev);
        Body.Children.Add(Buttons);
        _activate = AddButton("Activate", true, async (_, _) => await Run(() => LicenseService.ActivateAsync(_key.Text)));
        AddButton("Cancel", false, (_, _) => DialogResult = false);
        Loaded += (_, _) => _key.Focus();
    }

    private async Task Run(Func<Task<(bool ok, string message)>> action)
    {
        _activate.IsEnabled = false;
        _msg.Text = "Checking license…";
        _msg.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        try
        {
            var (ok, message) = await action();
            _msg.Text = message;
            _msg.SetResourceReference(TextBlock.ForegroundProperty, ok ? "Success" : "Danger");
            if (ok)
            {
                MessageBox.Show(this, "LICENSE STATUS: LIFETIME PRO ACTIVATED\n\nThank you for supporting Burhan Nexus. All Pro features are now unlocked permanently on this computer.",
                    App.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
        }
        finally { _activate.IsEnabled = true; }
    }

    public static bool Show(Window? owner) => new LicenseKeyDialog(owner).ShowDialog() == true;
}

/// <summary>How to buy a Lifetime license: real contact actions, no fake checkout.</summary>
public sealed class PurchaseDialog : SmallDialog
{
    private PurchaseDialog(Window? owner) : base(owner, "Purchase Lifetime License")
    {
        var title = new TextBlock { Text = "Lifetime Pro — one-time purchase", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        Body.Children.Add(title);
        Body.Children.Add(Label("1.  Contact Burhan Nexus to purchase. You pay once — no monthly or yearly payments, no renewals."));
        Body.Children.Add(Label("2.  Include your Device ID so your license key is issued for this computer:"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 2, 0, 8) };
        var id = new TextBox { Text = DeviceIdentity.DeviceId, IsReadOnly = true, Width = 200, FontFamily = new FontFamily("Consolas") };
        var copy = new Button { Content = "Copy", Margin = new Thickness(8, 0, 0, 0) };
        copy.Click += (_, _) => { try { Clipboard.SetText(DeviceIdentity.DeviceId); copy.Content = "Copied"; } catch { } };
        row.Children.Add(id);
        row.Children.Add(copy);
        Body.Children.Add(row);
        Body.Children.Add(Label("3.  You will receive your BNX-SC-PRO license key. Open Upgrade to Pro › Enter License Key and paste it."));
        Body.Children.Add(Buttons);
        AddButton("Email Burhan Nexus", true, (_, _) =>
        {
            var subject = Uri.EscapeDataString("Burhan Screen Capture Pro — Lifetime License");
            var body = Uri.EscapeDataString($"Hello Burhan Nexus,\n\nI would like to purchase a Lifetime Pro license.\n\nDevice ID: {DeviceIdentity.DeviceId}\nComputer: {DeviceIdentity.DeviceName}\n");
            Dialogs.OpenUrl($"mailto:{LicensingConfig.SupportEmail}?subject={subject}&body={body}");
        });
        AddButton("Visit website", false, (_, _) => Dialogs.OpenUrl(LicensingConfig.PurchaseUrl)).IsCancel = false;
        AddButton("Close", false, (_, _) => Close());
    }

    public static void Show(Window? owner) => new PurchaseDialog(owner).ShowDialog();
}
