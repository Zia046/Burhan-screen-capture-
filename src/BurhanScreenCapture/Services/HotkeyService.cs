using System.Windows.Input;
using System.Windows.Interop;
using BurhanScreenCapture.Native;

namespace BurhanScreenCapture.Services;

/// <summary>System-wide hotkeys via RegisterHotKey on a hidden message window.</summary>
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyAction> _ids = new();
    private int _nextId = 0xB500;
    public event Action<HotkeyAction>? Pressed;

    public HotkeyService()
    {
        var p = new HwndSourceParameters("BurhanScreenCaptureHotkeys") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers all enabled hotkeys. Returns human readable descriptions of the ones Windows refused.</summary>
    public List<string> RegisterAll(IEnumerable<HotkeyBinding> bindings)
    {
        UnregisterAll();
        var failed = new List<string>();
        foreach (var b in bindings.Where(b => b.Enabled && b.Key != Key.None))
        {
            int id = _nextId++;
            uint mods = NativeMethods.MOD_NOREPEAT;
            if (b.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.MOD_ALT;
            if (b.Modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.MOD_CONTROL;
            if (b.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.MOD_SHIFT;
            if (b.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= NativeMethods.MOD_WIN;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(b.Key);
            if (NativeMethods.RegisterHotKey(_source.Handle, id, mods, vk)) _ids[id] = b.Action;
            else failed.Add($"{b} ({Describe(b.Action)})");
        }
        return failed;
    }

    public void UnregisterAll()
    {
        foreach (var id in _ids.Keys) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _ids.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _ids.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(action);
        }
        return IntPtr.Zero;
    }

    public static string Describe(HotkeyAction a) => a switch
    {
        HotkeyAction.CaptureRegion => "Capture selected area",
        HotkeyAction.CaptureFullScreen => "Capture full screen",
        HotkeyAction.CaptureWindow => "Capture active window",
        HotkeyAction.ToggleRecording => "Start / stop recording",
        HotkeyAction.OpenEditor => "Open image editor",
        HotkeyAction.ShowToolbar => "Show quick toolbar",
        _ => a.ToString()
    };

    /// <summary>Shortcuts that Windows itself uses and that we refuse to take over.</summary>
    public static bool IsReserved(ModifierKeys m, Key k) =>
        (m == ModifierKeys.Alt && k == Key.F4) ||
        (m == ModifierKeys.Alt && k == Key.Tab) ||
        (m == ModifierKeys.Control && (k is Key.C or Key.V or Key.X or Key.Z or Key.Y or Key.A or Key.S)) ||
        (m == (ModifierKeys.Control | ModifierKeys.Alt) && k == Key.Delete) ||
        (m == (ModifierKeys.Windows | ModifierKeys.Shift) && k == Key.S) ||
        (m == ModifierKeys.Windows) ||
        (m == ModifierKeys.None && k != Key.Snapshot && !(k >= Key.F1 && k <= Key.F24));

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
