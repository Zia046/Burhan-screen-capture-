using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BurhanScreenCapture.Editor;
using BurhanScreenCapture.Native;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views;

/// <summary>Base for small themed modal dialogs built in code.</summary>
public abstract class SmallDialog : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(20) };
    protected readonly StackPanel Buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };

    protected SmallDialog(Window? owner, string title)
    {
        Title = title;
        if (owner is not null && owner.IsVisible) Owner = owner;
        WindowStartupLocation = owner is not null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "SurfaceBg");
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        Content = Body;
        SourceInitialized += (_, _) => WindowEffects.ApplyTitleBarTheme(this, ThemeManager.IsDark);
    }

    protected TextBlock Label(string text)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        return t;
    }

    protected Button AddButton(string text, bool primary, RoutedEventHandler click)
    {
        var b = new Button { Content = text, MinWidth = 86, Margin = new Thickness(8, 0, 0, 0), IsDefault = primary, IsCancel = !primary };
        if (primary) b.SetResourceReference(StyleProperty, "PrimaryButton");
        b.Click += click;
        Buttons.Children.Add(b);
        return b;
    }
}

public sealed class InputDialog : SmallDialog
{
    private readonly TextBox _box;
    private InputDialog(Window? owner, string title, string prompt, string value) : base(owner, title)
    {
        Body.Children.Add(Label(prompt));
        _box = new TextBox { Text = value, Width = 340 };
        Body.Children.Add(_box);
        Body.Children.Add(Buttons);
        AddButton("OK", true, (_, _) => DialogResult = true);
        AddButton("Cancel", false, (_, _) => DialogResult = false);
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    public static string? Ask(Window? owner, string title, string prompt, string value = "")
    {
        var d = new InputDialog(owner, title, prompt, value);
        return d.ShowDialog() == true ? d._box.Text : null;
    }
}

public sealed class ResizeDialog : SmallDialog
{
    private readonly TextBox _w = new() { Width = 90 }, _h = new() { Width = 90 };
    private readonly CheckBox _keep = new() { Content = "Keep proportions", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
    private readonly int _ow, _oh;
    private bool _sync;

    private ResizeDialog(Window owner, int w, int h) : base(owner, "Resize image")
    {
        _ow = w; _oh = h;
        _w.Text = w.ToString(); _h.Text = h.ToString();
        Body.Children.Add(Label($"Current size: {w} × {h} px"));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_w);
        row.Children.Add(Label(" × ").Also(t => t.Margin = new Thickness(8, 6, 8, 0)));
        row.Children.Add(_h);
        row.Children.Add(Label(" px").Also(t => t.Margin = new Thickness(8, 6, 0, 0)));
        Body.Children.Add(row);
        Body.Children.Add(_keep);
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var pct in new[] { 25, 50, 75, 150, 200 })
        {
            var b = new Button { Content = pct + "%", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 4, 8, 4) };
            b.Click += (_, _) => { _sync = true; _w.Text = Math.Max(1, _ow * pct / 100).ToString(); _h.Text = Math.Max(1, _oh * pct / 100).ToString(); _sync = false; };
            presets.Children.Add(b);
        }
        Body.Children.Add(presets);
        Body.Children.Add(Buttons);
        _w.TextChanged += (_, _) => Sync(true);
        _h.TextChanged += (_, _) => Sync(false);
        AddButton("Resize", true, (_, _) => { if (Valid(out _, out _)) DialogResult = true; else MessageBox.Show(this, "Enter a size between 1 and 20000 pixels.", App.AppName); });
        AddButton("Cancel", false, (_, _) => DialogResult = false);
    }

    private void Sync(bool fromWidth)
    {
        if (_sync || _keep.IsChecked != true) return;
        _sync = true;
        if (fromWidth && int.TryParse(_w.Text, out var w)) _h.Text = Math.Max(1, (int)Math.Round(w * (double)_oh / _ow)).ToString();
        if (!fromWidth && int.TryParse(_h.Text, out var h)) _w.Text = Math.Max(1, (int)Math.Round(h * (double)_ow / _oh)).ToString();
        _sync = false;
    }

    private bool Valid(out int w, out int h) =>
        int.TryParse(_w.Text, out w) & int.TryParse(_h.Text, out h) && w is > 0 and <= 20000 && h is > 0 and <= 20000;

    public static (int Width, int Height)? Ask(Window owner, int w, int h)
    {
        var d = new ResizeDialog(owner, w, h);
        if (d.ShowDialog() != true || !d.Valid(out var nw, out var nh)) return null;
        return (nw, nh);
    }
}

public sealed class TemplatePicker : SmallDialog
{
    private ImageEffects.Template? _picked;
    private TemplatePicker(Window owner) : base(owner, "Screenshot template")
    {
        Body.Children.Add(Label("Choose a background for your screenshot:"));
        var grid = new WrapPanel { MaxWidth = 420 };
        foreach (var t in ImageEffects.Templates)
        {
            var b = new Button { Width = 126, Height = 76, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(0), ToolTip = t.Name };
            b.Content = new Grid
            {
                Children =
                {
                    new Border { Background = new LinearGradientBrush(t.From, t.To, 45), CornerRadius = new CornerRadius(6) },
                    new Border { Background = Brushes.White, CornerRadius = new CornerRadius(3), Margin = new Thickness(22, 16, 22, 16), Opacity = 0.92 },
                    new TextBlock { Text = t.Name, FontSize = 11, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                }
            };
            var tt = t;
            b.Click += (_, _) => { _picked = tt; DialogResult = true; };
            grid.Children.Add(b);
        }
        Body.Children.Add(grid);
        Body.Children.Add(Buttons);
        AddButton("Cancel", false, (_, _) => DialogResult = false);
    }

    public static ImageEffects.Template? Pick(Window owner)
    {
        var d = new TemplatePicker(owner);
        return d.ShowDialog() == true ? d._picked : null;
    }
}

public sealed class OcrResultWindow : SmallDialog
{
    public OcrResultWindow(string text) : base(null, "Extracted text")
    {
        ResizeMode = ResizeMode.CanResize;
        SizeToContent = SizeToContent.Manual;
        Width = 560; Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var box = new TextBox { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14 };
        var dock = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(Buttons, Dock.Bottom);
        dock.Children.Add(Buttons);
        dock.Children.Add(box);
        Content = dock;
        AddButton("Copy text", true, (_, _) => { try { Clipboard.SetText(box.Text); Title = "Extracted text — copied"; } catch { } });
        AddButton("Close", false, (_, _) => Close());
    }
}

internal static class FluentExt
{
    public static T Also<T>(this T t, Action<T> a) { a(t); return t; }
}
