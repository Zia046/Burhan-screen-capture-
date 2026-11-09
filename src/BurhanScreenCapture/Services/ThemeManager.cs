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
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Insert(0, dict);
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
