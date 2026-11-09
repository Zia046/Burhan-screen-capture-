using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BurhanScreenCapture.Views;

/// <summary>Small click-through-free countdown bubble. Esc cancels.</summary>
public sealed class CountdownWindow : Window
{
    private readonly TextBlock _text = new() { FontSize = 54, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
    private bool _cancelled;

    private CountdownWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Width = 140; Height = 140;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new Border
        {
            CornerRadius = new CornerRadius(70),
            Background = new SolidColorBrush(Color.FromArgb(215, 20, 24, 32)),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _text, new TextBlock { Text = "Right-click to cancel", Foreground = Brushes.LightGray, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center } }
            }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) _cancelled = true; };
        MouseRightButtonUp += (_, _) => _cancelled = true;
    }

    public static async Task<bool> RunAsync(int seconds)
    {
        var w = new CountdownWindow();
        w.Show();
        for (int i = seconds; i > 0 && !w._cancelled; i--)
        {
            w._text.Text = i.ToString();
            for (int t = 0; t < 10 && !w._cancelled; t++) await Task.Delay(100);
        }
        w.Close();
        await Task.Delay(180); // let the bubble disappear from the screen
        return !w._cancelled;
    }
}
