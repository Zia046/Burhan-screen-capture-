using System.Windows.Media.Imaging;

namespace BurhanScreenCapture.Services;

/// <summary>
/// Streams an animated GIF to disk. Each frame is palettised by WIC (GifBitmapEncoder) and its image block is
/// re-wrapped with a per-frame colour table, a frame delay and an infinite-loop extension.
/// </summary>
public sealed class GifWriter : IDisposable
{
    private readonly FileStream _out;
    private bool _headerWritten;
    public int FrameCount { get; private set; }

    public GifWriter(string path) => _out = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

    public void AddFrame(BitmapSource frame, int delayMs)
    {
        var enc = new GifBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(frame));
        using var ms = new MemoryStream();
        enc.Save(ms);
        WriteFrame(ms.ToArray(), frame.PixelWidth, frame.PixelHeight, Math.Max(2, delayMs / 10));
        FrameCount++;
    }

    private void WriteFrame(byte[] gif, int width, int height, int delayCs)
    {
        int p = 6; // "GIF89a"
        int lsdPacked = gif[p + 4];
        p += 7;
        byte[]? globalTable = null;
        int globalBits = 0;
        if ((lsdPacked & 0x80) != 0)
        {
            globalBits = lsdPacked & 7;
            int size = 3 * (1 << (globalBits + 1));
            globalTable = gif.AsSpan(p, size).ToArray();
            p += size;
        }

        if (!_headerWritten)
        {
            Write("GIF89a"u8);
            WriteU16(width); WriteU16(height);
            _out.WriteByte(0); _out.WriteByte(0); _out.WriteByte(0); // no global table
            // NETSCAPE2.0 application extension: loop forever
            _out.Write(new byte[] { 0x21, 0xFF, 0x0B });
            Write("NETSCAPE2.0"u8);
            _out.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
            _headerWritten = true;
        }

        while (p < gif.Length)
        {
            byte b = gif[p];
            if (b == 0x21) // extension: skip (we write our own graphic control extension)
            {
                p += 2;
                while (p < gif.Length && gif[p] != 0) p += gif[p] + 1;
                p++;
            }
            else if (b == 0x2C)
            {
                int imgPacked = gif[p + 9];
                // Graphic control extension: disposal = do not dispose, delay
                _out.Write(new byte[] { 0x21, 0xF9, 0x04, 0x04, (byte)(delayCs & 0xFF), (byte)(delayCs >> 8), 0x00, 0x00 });
                _out.Write(gif, p, 9); // separator + position + size
                if ((imgPacked & 0x80) != 0)
                {
                    int bits = imgPacked & 7;
                    int size = 3 * (1 << (bits + 1));
                    _out.WriteByte((byte)imgPacked);
                    _out.Write(gif, p + 10, size);
                    p += 10 + size;
                }
                else if (globalTable is not null)
                {
                    _out.WriteByte((byte)(0x80 | (imgPacked & 0x40) | globalBits));
                    _out.Write(globalTable);
                    p += 10;
                }
                else
                {
                    _out.WriteByte((byte)imgPacked);
                    p += 10;
                }
                // LZW minimum code size + data sub-blocks
                int start = p;
                p++;
                while (p < gif.Length && gif[p] != 0) p += gif[p] + 1;
                p++;
                _out.Write(gif, start, p - start);
            }
            else break; // 0x3B trailer
        }
    }

    private void Write(ReadOnlySpan<byte> b) => _out.Write(b);
    private void WriteU16(int v) { _out.WriteByte((byte)(v & 0xFF)); _out.WriteByte((byte)(v >> 8)); }

    public void Dispose()
    {
        if (_headerWritten) _out.WriteByte(0x3B);
        _out.Dispose();
    }
}
