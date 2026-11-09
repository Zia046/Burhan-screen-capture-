using System.Windows;
using System.Windows.Controls;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Native;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views;

public partial class UpgradeDialog : Window
{
    public UpgradeDialog(FeatureInfo feature)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowEffects.ApplyTitleBarTheme(this, ThemeManager.IsDark);
        FeatureName.Text = feature.Name;
        FeatureDesc.Text = feature.Description;
        Benefits.ItemsSource = feature.Benefits.Concat(new[] { "Plus every other Pro tool: OCR, blur, scrolling capture, GIF and more" }).ToList();
        foreach (var (plan, price, note) in LicensingConfig.Plans)
        {
            var card = new Border
            {
                Margin = new Thickness(4), Padding = new Thickness(10, 10, 10, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(plan == LicensePlan.Yearly ? 2 : 1)
            };
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBg");
            card.SetResourceReference(Border.BorderBrushProperty, plan == LicensePlan.Yearly ? "ProGold" : "BorderBrushSoft");
            var title = new TextBlock { Text = LicensePlanInfo.DisplayName(plan).Replace("Pro ", ""), FontWeight = FontWeights.SemiBold };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            var sub = new TextBlock { Text = note, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            card.Child = new StackPanel { Children = { title, sub } };
            Plans.Children.Add(card);
        }
        bool trialAvailable = !LicenseService.TrialUsed && LicenseService.Status.Edition == Edition.Free;
        TrialBtn.Visibility = TrialHint.Visibility = trialAvailable ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBack(object sender, RoutedEventArgs e) => Close();

    private void OnTrial(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = LicenseService.StartTrial();
        MessageBox.Show(this, msg, App.AppName, MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (ok) Close();
    }

    private void OnUpgrade(object sender, RoutedEventArgs e)
    {
        Close();
        App.ShowMainWindow();
        (Application.Current.MainWindow as MainWindow)?.Navigate("Upgrade");
    }
}
