using System.Windows;
using BurhanScreenCapture.Licensing;

namespace BurhanScreenCapture.Services;

/// <summary>
/// Single enforcement point for Free/Pro. UI hides nothing on its own authority —
/// every Pro code path calls <see cref="Require"/> (interactive) or <see cref="IsAllowed"/> (silent).
/// </summary>
public static class Entitlements
{
    public static Edition Edition => LicenseService.Status.Edition;
    public static bool IsPro => LicenseService.Status.IsPro;

    public static bool IsAllowed(Feature f) => FeatureCatalog.IsAllowed(f, Edition);

    /// <summary>Returns true if allowed; otherwise shows the upgrade dialog and returns false. Never throws.</summary>
    public static bool Require(Feature f, Window? owner = null)
    {
        if (IsAllowed(f)) return true;
        try
        {
            var dlg = new Views.UpgradeDialog(FeatureCatalog.Get(f));
            var o = owner ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
            if (o is not null && o.IsVisible && o != dlg) dlg.Owner = o;
            dlg.ShowDialog();
        }
        catch (Exception ex) { Log.Error("Upgrade dialog failed", ex); }
        return IsAllowed(f); // user may have activated from the dialog
    }
}
