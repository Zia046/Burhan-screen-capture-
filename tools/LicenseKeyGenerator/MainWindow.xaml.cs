using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BurhanNexus.LicenseKeyGenerator.Core;
using Microsoft.Win32;

namespace BurhanNexus.LicenseKeyGenerator;

public partial class MainWindow : Window
{
    private readonly KeyVault _vault = new(Path.Combine(App.DataFolder, "signing-key.bnxkey"));
    private LicenseDatabase? _db;
    private LicenseRecord? _last;
    private LicenseRecord? _selected;

    public MainWindow()
    {
        InitializeComponent();
        ShowGate();
    }

    // ================================================================ gate (create / unlock)

    private void ShowGate()
    {
        MainUi.Visibility = Visibility.Collapsed;
        Gate.Visibility = Visibility.Visible;
        Pw1.Clear(); Pw2.Clear();
        GateError.Text = "";
        if (_vault.Exists)
        {
            GateText.Text = "Enter your administrator password to unlock the signing key.";
            ConfirmRow.Visibility = Visibility.Collapsed;
            ImportBtn.Visibility = Visibility.Collapsed;
            GateBtn.Content = "Unlock";
        }
        else
        {
            GateText.Text = "First run: create your private signing key. Choose a strong password — it encrypts the key on this computer. " +
                            "If you already have a key from another computer, import it instead.";
            ConfirmRow.Visibility = Visibility.Visible;
            ImportBtn.Visibility = Visibility.Visible;
            GateBtn.Content = "Create signing key";
        }
        Dispatcher.BeginInvoke(() => Pw1.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnPwKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vault.Exists) OnGate(sender, e);
    }

    private void OnGate(object sender, RoutedEventArgs e)
    {
        if (_vault.Exists)
        {
            var err = _vault.Unlock(Pw1.Password);
            if (err is not null) { GateError.Text = err; return; }
        }
        else
        {
            var err = KeyVault.CheckPassword(Pw1.Password, Pw2.Password);
            if (err is not null) { GateError.Text = err; return; }
            _vault.Create(Pw1.Password);
            MessageBox.Show(this, "Your signing key was created.\n\nNext: open \"Signing Key & Backup\", copy the PUBLIC key into the app's ProductionPublicKey.txt, and make a backup.",
                App.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        OpenMain();
    }

    private void OnImportKey(object sender, RoutedEventArgs e)
    {
        var err = KeyVault.CheckPassword(Pw1.Password, Pw2.Password);
        if (err is not null) { GateError.Text = "Enter and confirm a password first. " + err; return; }
        var dlg = new OpenFileDialog { Title = "Import private key (base64 PKCS#8 text) or a .bnxkey backup", Filter = "Key files|*.key;*.txt;*.bnxkey|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            if (dlg.FileName.EndsWith(".bnxkey", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(dlg.FileName, _vault.FilePath, true);
                var u = _vault.Unlock(Pw1.Password);
                if (u is not null) { File.Delete(_vault.FilePath); GateError.Text = "Backup restored but " + u + " Use the backup's password."; return; }
            }
            else _vault.Import(File.ReadAllText(dlg.FileName), Pw1.Password);
            OpenMain();
        }
        catch (Exception ex) { GateError.Text = "This is not a valid private key: " + ex.Message; }
    }

    private void OpenMain()
    {
        _db = new LicenseDatabase(Path.Combine(App.DataFolder, "licenses.json"));
        Gate.Visibility = Visibility.Collapsed;
        MainUi.Visibility = Visibility.Visible;
        PubKey.Text = _vault.PublicKey;
        DataPath.Text = "Data folder: " + App.DataFolder;
        Pw1.Clear(); Pw2.Clear();
        RefreshLists();
    }

    // ================================================================ navigation

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (PageGenerate is null || sender is not RadioButton { Tag: string tag }) return;
        PageGenerate.Visibility = tag == "Generate" ? Visibility.Visible : Visibility.Collapsed;
        PageLicenses.Visibility = tag == "Licenses" ? Visibility.Visible : Visibility.Collapsed;
        PageRevocations.Visibility = tag == "Revocations" ? Visibility.Visible : Visibility.Collapsed;
        PageKeys.Visibility = tag == "Keys" ? Visibility.Visible : Visibility.Collapsed;
        RefreshLists();
    }

    private void RefreshLists()
    {
        if (_db is null) return;
        var items = _db.Search(Search.Text).ToList();
        LicenseGrid.ItemsSource = items;
        CountText.Text = $"{_db.Records.Count} license(s) • {_db.Records.Count(r => r.IsRevoked)} revoked";
        var revoked = _db.Records.Where(r => r.IsRevoked).ToList();
        RevCount.Text = $"{revoked.Count} revoked license(s)";
        RevList.Text = string.Join(Environment.NewLine, revoked.Select(r => $"{r.LicenseId}   {r.CustomerName}   {r.RevokedUtc?.ToLocalTime():dd MMM yyyy}   {r.RevokeReason}"));
        if (_selected is not null) ShowDetail(_db.Records.FirstOrDefault(r => r.LicenseId == _selected.LicenseId));
    }

    // ================================================================ generate

    private void OnGenerate(object sender, RoutedEventArgs e)
    {
        GError.Text = "";
        try
        {
            int.TryParse(GLimit.Text, out var limit);
            _last = _db!.Generate(_vault, GCustomer.Text, GEmail.Text, GDevice.Text, Math.Max(1, limit), GNotes.Text);
            RId.Text = _last.LicenseId;
            RCustomer.Text = _last.CustomerName + (_last.CustomerEmail is null ? "" : $"  <{_last.CustomerEmail}>");
            RDate.Text = _last.CreatedUtc.ToLocalTime().ToString("dd MMMM yyyy, HH:mm");
            RStatus.Text = _last.ActivationStatus + (_last.BoundDeviceId is null ? "" : $" ({_last.BoundDeviceId})");
            RKey.Text = _last.LicenseKey;
            ResultCard.Visibility = Visibility.Visible;
            RefreshLists();
        }
        catch (Exception ex) { GError.Text = ex.Message; }
    }

    private void OnClearForm(object sender, RoutedEventArgs e)
    {
        GCustomer.Text = GEmail.Text = GDevice.Text = GNotes.Text = "";
        GLimit.Text = "1";
        GError.Text = "";
        ResultCard.Visibility = Visibility.Collapsed;
    }

    private void Copy(string text, string what)
    {
        try { Clipboard.SetText(text); MessageBox.Show(this, what + " copied to the clipboard.", App.Title); }
        catch (Exception ex) { MessageBox.Show(this, "Clipboard error: " + ex.Message, App.Title); }
    }

    private void Export(LicenseRecord r)
    {
        var safe = string.Concat(r.CustomerName.Split(Path.GetInvalidFileNameChars())).Trim();
        var dlg = new SaveFileDialog { Filter = "Burhan Nexus license (*.bnxlic)|*.bnxlic", FileName = $"{safe} - {r.LicenseId}.bnxlic" };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllText(dlg.FileName, LicenseDatabase.LicenseFileText(r));
        MessageBox.Show(this, "License file saved. Send it to the customer — they import it from Upgrade to Pro › Enter License Key.", App.Title);
    }

    private void OnCopyKey(object sender, RoutedEventArgs e) { if (_last is not null) Copy(_last.LicenseKey, "License key"); }
    private void OnCopyMessage(object sender, RoutedEventArgs e) { if (_last is not null) Copy(LicenseDatabase.CustomerMessage(_last), "Customer message"); }
    private void OnExportLicense(object sender, RoutedEventArgs e) { if (_last is not null) Export(_last); }

    // ================================================================ history

    private void OnSearch(object sender, TextChangedEventArgs e) => RefreshLists();

    private void OnSelect(object sender, SelectionChangedEventArgs e) => ShowDetail(LicenseGrid.SelectedItem as LicenseRecord);

    private void ShowDetail(LicenseRecord? r)
    {
        _selected = r;
        Detail.Visibility = r is null ? Visibility.Collapsed : Visibility.Visible;
        if (r is null) return;
        DTitle.Text = $"{r.LicenseId} — {r.CustomerName}";
        DInfo.Text = $"Email: {r.CustomerEmail ?? "—"}   •   Product: {r.Product}   •   Type: {r.LicenseType}   •   Device limit: {r.DeviceLimit}\n" +
                     $"Created: {r.CreatedText}   •   Device: {r.DeviceText}   •   Status: {r.Status}   •   Activation: {r.ActivationStatus}" +
                     (r.RevokeReason is null ? "" : $"\nRevoked because: {r.RevokeReason}") + (r.Notes is null ? "" : $"\nNotes: {r.Notes}");
        DRevoke.Content = r.IsRevoked ? "Reinstate" : "Revoke";
    }

    private void OnDCopy(object sender, RoutedEventArgs e) { if (_selected is not null) Copy(_selected.LicenseKey, "License key"); }
    private void OnDMessage(object sender, RoutedEventArgs e) { if (_selected is not null) Copy(LicenseDatabase.CustomerMessage(_selected), "Customer message"); }
    private void OnDExport(object sender, RoutedEventArgs e) { if (_selected is not null) Export(_selected); }

    private void OnDActivated(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var dev = Prompt.Ask(this, "Record activation", "Device ID the customer activated on (from their License page):", _selected.BoundDeviceId ?? "");
        if (dev is null) return;
        try { _db!.MarkActivated(_selected, dev); RefreshLists(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, App.Title); }
    }

    private void OnDRevoke(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        if (_selected.IsRevoked)
        {
            _db!.Reinstate(_selected);
        }
        else
        {
            var reason = Prompt.Ask(this, "Revoke license", $"Reason for revoking {_selected.LicenseId} (refund, key sharing, …):", "");
            if (reason is null) return;
            _db!.Revoke(_selected, string.IsNullOrWhiteSpace(reason) ? "Revoked" : reason);
            MessageBox.Show(this, "Revoked. Export the signed revocation list (Revocations page) and publish it so customers' apps receive it.", App.Title);
        }
        RefreshLists();
    }

    private void OnExportCsv(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"licenses-{DateTime.Now:yyyyMMdd}.csv" };
        if (dlg.ShowDialog(this) == true) File.WriteAllText(dlg.FileName, _db!.ExportCsv());
    }

    // ================================================================ revocations

    private void OnExportRevocations(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Signed revocation list (*.bnxrev)|*.bnxrev", FileName = "revocations.bnxrev" };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllText(dlg.FileName, _db!.RevocationListText(_vault));
        MessageBox.Show(this, "Signed revocation list saved. Upload it to the address set in LicensingConfig.RevocationListUrl.", App.Title);
    }

    private void OnCopyRevokedIds(object sender, RoutedEventArgs e) =>
        Copy(string.Join(Environment.NewLine, _db!.Records.Where(r => r.IsRevoked).Select(r => r.LicenseId)), "Revoked license IDs");

    // ================================================================ keys & backup

    private void OnCopyPub(object sender, RoutedEventArgs e) => Copy(_vault.PublicKey, "Public key");

    private void OnSavePub(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Text|*.txt", FileName = "ProductionPublicKey.txt" };
        if (dlg.ShowDialog(this) == true) File.WriteAllText(dlg.FileName, _vault.PublicKey);
    }

    private void OnBackup(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose a backup folder (e.g. a USB drive)" };
        if (dlg.ShowDialog(this) != true) return;
        _db!.BackupTo(dlg.FolderName, _vault);
        MessageBox.Show(this, "Backup saved. The signing key in the backup is still protected by your password.", App.Title);
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "License database|licenses*.json" };
        if (dlg.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "Replace the current license database with this backup? (The current version is kept in the backups folder.)", App.Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { _db!.RestoreFrom(dlg.FileName); RefreshLists(); }
        catch (Exception ex) { MessageBox.Show(this, "Restore failed: " + ex.Message, App.Title); }
    }

    private void OnOpenData(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(App.DataFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{App.DataFolder}\"") { UseShellExecute = true });
    }

    private void OnChangePassword(object sender, RoutedEventArgs e)
    {
        var p1 = Prompt.Ask(this, "Change password", "New password (at least 8 characters):", "", password: true);
        if (p1 is null) return;
        var p2 = Prompt.Ask(this, "Change password", "Type the new password again:", "", password: true);
        if (p2 is null) return;
        var err = KeyVault.CheckPassword(p1, p2);
        if (err is not null) { MessageBox.Show(this, err, App.Title); return; }
        _vault.ChangePassword(p1);
        MessageBox.Show(this, "Password changed. Make a new backup — old backups still use the old password.", App.Title);
    }

    private void OnLock(object sender, RoutedEventArgs e)
    {
        _vault.Lock();
        ShowGate();
    }
}

/// <summary>Minimal themed input box.</summary>
public sealed class Prompt : Window
{
    private readonly TextBox? _text;
    private readonly PasswordBox? _pw;

    private Prompt(Window owner, string title, string label, string value, bool password)
    {
        Title = title; Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "SurfaceBg");
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6), MaxWidth = 380, TextWrapping = TextWrapping.Wrap });
        if (password) { _pw = new PasswordBox { Width = 360, Padding = new Thickness(6) }; sp.Children.Add(_pw); }
        else { _text = new TextBox { Width = 360, Text = value }; sp.Children.Add(_text); }
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "PrimaryButton");
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        sp.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { ok, cancel } });
        Content = sp;
        Loaded += (_, _) => { if (_pw is not null) _pw.Focus(); else { _text!.Focus(); _text.SelectAll(); } };
    }

    public static string? Ask(Window owner, string title, string label, string value, bool password = false)
    {
        var p = new Prompt(owner, title, label, value, password);
        return p.ShowDialog() == true ? (p._pw?.Password ?? p._text!.Text) : null;
    }
}
