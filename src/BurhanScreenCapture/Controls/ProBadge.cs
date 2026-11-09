using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BurhanScreenCapture.Controls;

/// <summary>Small gold "PRO" label. Shown for everyone so users know what is included in Pro.</summary>
public sealed class ProBadge : Border
{
    public ProBadge()
    {
        SetResourceReference(BackgroundProperty, "ProGoldSoft");
        CornerRadius = new CornerRadius(4);
        Padding = new Thickness(5, 1, 5, 1);
        Margin = new Thickness(8, 0, 0, 0);
        VerticalAlignment = VerticalAlignment.Center;
        var t = new TextBlock { Text = "PRO", FontSize = 10, FontWeight = FontWeights.Bold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "ProGold");
        Child = t;
    }
}
