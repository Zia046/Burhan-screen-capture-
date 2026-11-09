using System.Windows;
using System.Windows.Interop;

namespace BurhanScreenCapture.Native;

public static class WindowEffects
{
    /// <summary>Makes the native title bar follow the app theme (Windows 10 20H1+ / Windows 11).</summary>
    public static void ApplyTitleBarTheme(Window w, bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) { w.SourceInitialized += (_, _) => ApplyTitleBarTheme(w, dark); return; }
            int v = dark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
        }
        catch { }
    }

    /// <summary>Positions a window using physical pixels (works across monitors with different scaling).</summary>
    public static void SetPhysicalBounds(Window w, int x, int y, int width, int height)
    {
        var hwnd = new WindowInteropHelper(w).EnsureHandle();
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).EnsureHandle();
}
