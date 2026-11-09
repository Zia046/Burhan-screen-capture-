using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views;

/// <summary>Small toast-like "recording saved" panel with Play / Show in folder.</summary>
public sealed class RecordingSavedWindow : Window
{
    public RecordingSavedWindow(string path, TimeSpan duration)
    {
        Title = "Recording saved";
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "SurfaceBg");
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["UiFont"];
        long size = 0;
        try { size = new FileInfo(path).Length; } catch { }

        var title = new TextBlock { Text = "Recording saved", FontSize = 15, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var info = new TextBlock { Text = $"{Path.GetFileName(path)}\n{duration:hh\\:mm\\:ss} • {size / 1024.0 / 1024.0:0.0} MB", Margin = new Thickness(0, 4, 0, 12), MaxWidth = 320, TextWrapping = TextWrapping.Wrap };
        info.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        var play = new Button { Content = "Play", Margin = new Thickness(0, 0, 8, 0) };
        play.SetResourceReference(StyleProperty, "PrimaryButton");
        play.Click += (_, _) => { Dialogs.OpenFile(path); Close(); };
        var folder = new Button { Content = "Show in folder", Margin = new Thickness(0, 0, 8, 0) };
        folder.Click += (_, _) => { Dialogs.ShowInFolder(path); Close(); };
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Children = { title, info, new StackPanel { Orientation = Orientation.Horizontal, Children = { play, folder, close } } }
        };
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 16;
            Top = wa.Bottom - ActualHeight - 16;
        };
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        t.Tick += (_, _) => { t.Stop(); if (!IsMouseOver) Close(); };
        t.Start();
    }
}
