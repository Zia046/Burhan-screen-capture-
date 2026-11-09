using System.Windows;
using System.Windows.Controls;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views.Pages;

public partial class AboutPage : UserControl, IPage
{
    public AboutPage()
    {
        InitializeComponent();
        WebText.Text = LicensingConfig.Website;
        MailText.Text = LicensingConfig.SupportEmail;
    }

    public void OnShown()
    {
        VersionText.Text = $"Version {App.Version} • Windows 10 / 11 (64-bit)";
        var st = LicenseService.Status;
        EditionText.Text = EditionInfo.Name(st.Edition);
        LicenseText.Text = EditionInfo.LicenseText(st.Edition);
    }

    private void OnWeb(object sender, RoutedEventArgs e) => Dialogs.OpenUrl(LicensingConfig.Website);
    private void OnMail(object sender, RoutedEventArgs e) => Dialogs.OpenUrl("mailto:" + LicensingConfig.SupportEmail);
    private void OnLogs(object sender, RoutedEventArgs e) => Dialogs.OpenFolder(AppPaths.Logs);
    private void OnData(object sender, RoutedEventArgs e) => Dialogs.OpenFolder(AppPaths.AppData);
}
