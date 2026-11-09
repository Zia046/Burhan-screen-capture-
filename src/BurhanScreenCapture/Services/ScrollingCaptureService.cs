using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BurhanScreenCapture.Native;

namespace BurhanScreenCapture.Services;

/// <summary>
/// Pro: scrolls the content under the selected area with the mouse wheel, captures each step and stitches
/// the frames by detecting the vertical overlap between consecutive frames. Esc stops early.
/// </summary>
public static class ScrollingCaptureService
{
    private const int MaxHeight = 30000;
    private const int MaxSteps = 60;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    private const int VK_ESCAPE = 0x1B;

    public static async Task<BitmapSource?> RunAsync(PixelRect area)
    {
        if (area.Height < 80 || area.Width < 80)
        {
            System.Windows.MessageBox.Show("Select a larger area (at least 80 × 80 pixels) inside the scrolling content.", App.AppName);
            return null;
        }
        App.Tray?.Notify("Scrolling capture", "Capturing… keep the mouse still. Press Esc to stop.");
        NativeMethods.GetCursorPos(out var original);
        NativeMethods.SetCursorPos(area.X + area.Width / 2, area.Y + area.Height / 2);
        await Task.Delay(150);

        int w = area.Width, h = area.Height, stride = w * 4;
        var stitched = new List<byte[]>(); // list of row-blocks appended in order
        var first = Grab(area);
        stitched.Add(first);
        int total = h;
        var prev = first;
        int notches = Math.Max(1, h / 250);
        int unchanged = 0;

        try
        {
            for (int step = 0; step < MaxSteps && total < MaxHeight; step++)
            {
                if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0) break;
                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, -120 * notches, IntPtr.Zero);
                await Task.Delay(420); // let smooth scrolling settle
                if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0) break;
                var cur = Grab(area);

                int shift = FindShift(prev, cur, w, h);
                if (shift == 0)
                {
                    if (++unchanged >= 2) break; // reached the end
                    continue;
                }
                unchanged = 0;
                int newRows = Math.Min(shift, MaxHeight - total);
                var block = new byte[newRows * stride];
                Buffer.BlockCopy(cur, (h - newRows) * stride, block, 0, block.Length);
                stitched.Add(block);
                total += newRows;
                prev = cur;
            }
        }
        finally
        {
            NativeMethods.SetCursorPos(original.X, original.Y);
        }

        var all = new byte[total * stride];
        int offset = 0;
        foreach (var b in stitched) { Buffer.BlockCopy(b, 0, all, offset, b.Length); offset += b.Length; }
        var bmp = BitmapSource.Create(w, total, 96, 96, PixelFormats.Bgr32, null, all, stride);
        bmp.Freeze();
        return bmp;
    }

    private static byte[] Grab(PixelRect area)
    {
        var img = ScreenCaptureService.Capture(area);
        var px = new byte[area.Width * 4 * area.Height];
        img.CopyPixels(px, area.Width * 4, 0);
        return px;
    }

    /// <summary>
    /// Returns how many pixels the content moved up between frames (0 = no movement).
    /// Only "informative" rows (not a single flat colour) are compared so blank areas cannot cause false matches.
    /// If no overlap is found the whole frame is new (returns h).
    /// </summary>
    internal static int FindShift(byte[] prev, byte[] cur, int w, int h)
    {
        int stride = w * 4;
        var hp = RowHashes(prev, w, h, out _);
        var hc = RowHashes(cur, w, h, out var informative);

        // identical frame?
        bool same = true;
        for (int i = 0; i < h && same; i++) same = hp[i] == hc[i];
        if (same) return 0;

        int best = -1;
        double bestScore = 0;
        int minOverlap = Math.Max(20, h / 6);
        for (int s = 1; s <= h - minOverlap; s++)
        {
            int match = 0, count = 0;
            for (int i = 0; i + s < h; i++)
            {
                if (!informative[i]) continue;
                count++;
                if (hc[i] == hp[i + s]) match++;
            }
            if (count < 8) continue;
            double score = (double)match / count;
            if (score > bestScore + 1e-9) { bestScore = score; best = s; }
        }
        return bestScore >= 0.85 && best > 0 ? best : h;
    }

    private static ulong[] RowHashes(byte[] px, int w, int h, out bool[] informative)
    {
        int stride = w * 4;
        var hashes = new ulong[h];
        informative = new bool[h];
        for (int y = 0; y < h; y++)
        {
            ulong hash = 1469598103934665603UL;
            int row = y * stride;
            uint firstPx = BitConverter.ToUInt32(px, row) & 0xFFFFFF;
            bool varied = false;
            for (int x = 0; x < w; x++)
            {
                uint v = BitConverter.ToUInt32(px, row + x * 4) & 0xFFFFFF;
                if (!varied && v != firstPx) varied = true;
                hash = (hash ^ v) * 1099511628211UL;
            }
            hashes[y] = hash;
            informative[y] = varied;
        }
        return hashes;
    }
}
