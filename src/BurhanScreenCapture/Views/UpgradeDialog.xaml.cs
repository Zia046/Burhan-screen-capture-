using System.Windows;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Native;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views;

/// <summary>Shown when a Free user clicks a Pro feature. Never blocks or crashes the calling code.</summary>
public partial class UpgradeDialog : Window
{
    public UpgradeDialog(FeatureInfo feature)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowEffects.ApplyTitleBarTheme(this, ThemeManager.IsDark);
        FeatureName.Text = feature.Name;
        FeatureDesc.Text = feature.Description;
        Benefits.ItemsSource = feature.Benefits.Concat(new[] { "Plus every other Pro tool: OCR, blur, scrolling capture, GIF recording and more" }).ToList();
    }

    private void OnBack(object sender, RoutedEventArgs e) => Close();

    private void OnKey(object sender, RoutedEventArgs e)
    {
        if (LicenseKeyDialog.Show(this)) Close();
    }

    private void OnPurchase(object sender, RoutedEventArgs e) => PurchaseDialog.Show(this);
}
