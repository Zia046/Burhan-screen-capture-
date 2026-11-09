using System.Windows;
using Microsoft.Win32;

namespace BurhanScreenCapture.Services;

public static class ThemeManager
{
    private static ResourceDictionary? _current;
    public static bool IsDark { get; private set; }
    public static event Action? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        IsDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemUsesDarkTheme()
        };
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/BurhanScreenCapture;component/Themes/{(IsDark ? "Dark" : "Light")}.xaml")
        };
        // Later merged dictionaries take precedence, so remove every theme dictionary and append the new one.
        var merged = Application.Current.Resources.MergedDictionaries;
        foreach (var old in merged.Where(d => d.Source is not null &&
                     (d.Source.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                      d.Source.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase))).ToList())
            merged.Remove(old);
        merged.Add(dict);
        _current = dict;
        foreach (Window w in Application.Current.Windows) Native.WindowEffects.ApplyTitleBarTheme(w, IsDark);
        ThemeChanged?.Invoke();
    }

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }
}
