using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using BurhanScreenCapture.Native;
using static BurhanScreenCapture.Native.NativeMethods;

namespace BurhanScreenCapture.Services;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public PixelRect Intersect(PixelRect o)
    {
        int x = Math.Max(X, o.X), y = Math.Max(Y, o.Y);
        int r = Math.Min(Right, o.Right), b = Math.Min(Bottom, o.Bottom);
        return r <= x || b <= y ? default : new PixelRect(x, y, r - x, b - y);
    }
    public bool Contains(int px, int py) => px >= X && py >= Y && px < Right && py < Bottom;
    public override string ToString() => $"{Width} × {Height}";
}

public sealed record MonitorInfo(string DeviceName, PixelRect Bounds, bool IsPrimary, int Index)
{
    public string Label => $"Monitor {Index + 1}{(IsPrimary ? " (Main)" : "")} — {Bounds.Width}×{Bounds.Height}";
    public override string ToString() => Label;
}

/// <summary>GDI based screen capture. Coordinates are physical pixels (the app is Per-Monitor-V2 DPI aware).</summary>
public static class ScreenCaptureService
{
    public static PixelRect VirtualScreen => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

    public static IReadOnlyList<MonitorInfo> Monitors()
    {
        var list = new List<MonitorInfo>();
        var screens = System.Windows.Forms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var b = screens[i].Bounds;
            list.Add(new MonitorInfo(screens[i].DeviceName, new PixelRect(b.X, b.Y, b.Width, b.Height), screens[i].Primary, i));
        }
        return list;
    }

    public static MonitorInfo PrimaryMonitor => Monitors().FirstOrDefault(m => m.IsPrimary) ?? Monitors()[0];

    public static MonitorInfo MonitorAtCursor()
    {
        GetCursorPos(out var p);
        return Monitors().FirstOrDefault(m => m.Bounds.Contains(p.X, p.Y)) ?? PrimaryMonitor;
    }

    public static BitmapSource Capture(PixelRect r, bool includeCursor = false)
    {
        var clip = r.Intersect(VirtualScreen);
        if (clip.IsEmpty) throw new InvalidOperationException("The selected area is outside the visible screens.");

        IntPtr desktop = GetDesktopWindow();
        IntPtr src = GetWindowDC(desktop);
        IntPtr mem = CreateCompatibleDC(src);
        IntPtr bmp = CreateCompatibleBitmap(src, clip.Width, clip.Height);
        if (bmp == IntPtr.Zero)
        {
            DeleteDC(mem); ReleaseDC(desktop, src);
            throw new OutOfMemoryException("Not enough memory to capture this area.");
        }
        IntPtr old = SelectObject(mem, bmp);
        try
        {
            BitBlt(mem, 0, 0, clip.Width, clip.Height, src, clip.X, clip.Y, SRCCOPY | CAPTUREBLT);
            if (includeCursor) DrawCursor(mem, clip);
            SelectObject(mem, old);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // Normalise to a compact, frozen Bgra32 bitmap at 96 DPI
            var conv = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgr32, null, 0);
            var wb = new WriteableBitmap(conv);
            wb.Freeze();
            return wb;
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(desktop, src);
        }
    }

    private static void DrawCursor(IntPtr hdc, PixelRect area)
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || (ci.flags & CURSOR_SHOWING) == 0) return;
        int hx = 0, hy = 0;
        if (GetIconInfo(ci.hCursor, out var info))
        {
            hx = info.xHotspot; hy = info.yHotspot;
            if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
            if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
        }
        DrawIconEx(hdc, ci.ptScreenPos.X - area.X - hx, ci.ptScreenPos.Y - area.Y - hy, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3);
    }

    public static PixelRect? WindowBounds(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return null;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) != 0)
            if (!GetWindowRect(hwnd, out r)) return null;
        var pr = new PixelRect(r.Left, r.Top, r.Width, r.Height).Intersect(VirtualScreen);
        return pr.IsEmpty ? null : pr;
    }

    public static IntPtr WindowAtPoint(int x, int y) =>
        GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), GA_ROOT);

    public static string WindowTitle(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
