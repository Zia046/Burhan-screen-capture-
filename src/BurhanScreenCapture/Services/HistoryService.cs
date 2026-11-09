using System.Text.Json;
using System.Windows.Media.Imaging;
using BurhanScreenCapture.Licensing;

namespace BurhanScreenCapture.Services;

public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; set; } = "";
    public DateTime CreatedLocal { get; set; } = DateTime.Now;
    public string Format { get; set; } = "PNG";
    public int Width { get; set; }
    public int Height { get; set; }
    public string? ThumbPath { get; set; }
    /// <summary>Text recognised by OCR (Pro) — makes history searchable by content.</summary>
    public string? OcrText { get; set; }

    public string FileName => Path.GetFileName(FilePath);
    public string Folder => Path.GetDirectoryName(FilePath) ?? "";
    public bool Exists => File.Exists(FilePath);
    public string SizeText => $"{Width} × {Height}";
    public string DateText => CreatedLocal.ToString("dd MMM yyyy, hh:mm tt");
}

/// <summary>
/// Stores references to saved screenshots (never copies of the images) plus small JPEG thumbnails.
/// Free edition keeps the latest 20 entries; Pro keeps everything.
/// </summary>
public static class HistoryService
{
    private static readonly object Gate = new();
    private static List<HistoryEntry> _entries = new();
    public static event Action? Changed;

    public static IReadOnlyList<HistoryEntry> Entries
    {
        get { lock (Gate) return _entries.ToList(); }
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(AppPaths.HistoryFile))
                _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(AppPaths.HistoryFile)) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error("History unreadable", ex);
            _entries = new();
        }
        EnforceLimit();
    }

    public static void Add(string filePath, BitmapSource image)
    {
        if (!SettingsService.Current.HistoryEnabled) return;
        var entry = new HistoryEntry
        {
            FilePath = filePath,
            Format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
            Width = image.PixelWidth,
            Height = image.PixelHeight,
            ThumbPath = CreateThumb(image)
        };
        lock (Gate) _entries.Insert(0, entry);
        EnforceLimit();
        Persist();
    }

    public static void Remove(HistoryEntry e, bool deleteFile)
    {
        lock (Gate) _entries.RemoveAll(x => x.Id == e.Id);
        TryDelete(e.ThumbPath);
        if (deleteFile) TryDelete(e.FilePath);
        Persist();
    }

    public static void Clear()
    {
        List<HistoryEntry> old;
        lock (Gate) { old = _entries; _entries = new(); }
        foreach (var e in old) TryDelete(e.ThumbPath);
        Persist();
    }

    public static void RemoveMissing()
    {
        List<HistoryEntry> missing;
        lock (Gate) missing = _entries.Where(e => !e.Exists).ToList();
        foreach (var e in missing) Remove(e, false);
    }

    public static void UpdateOcr(HistoryEntry e, string text)
    {
        lock (Gate)
        {
            var x = _entries.FirstOrDefault(i => i.Id == e.Id);
            if (x != null) x.OcrText = text;
        }
        Persist();
    }

    public static void EnforceLimit()
    {
        if (Entitlements.IsAllowed(Feature.UnlimitedHistory)) return;
        List<HistoryEntry> removed;
        lock (Gate)
        {
            removed = _entries.Skip(FeatureCatalog.FreeHistoryLimit).ToList();
            if (removed.Count == 0) return;
            _entries = _entries.Take(FeatureCatalog.FreeHistoryLimit).ToList();
        }
        foreach (var e in removed) TryDelete(e.ThumbPath); // only the thumbnail; the user's image file is kept
        Persist();
    }

    private static string? CreateThumb(BitmapSource image)
    {
        try
        {
            double scale = Math.Min(1.0, 320.0 / Math.Max(1, image.PixelWidth));
            BitmapSource thumb = scale < 1 ? new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale)) : image;
            var path = Path.Combine(AppPaths.Thumbnails, Guid.NewGuid().ToString("N") + ".jpg");
            ImageExportService.Save(thumb, path, ImageFormatOption.Jpg, 80);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warn("Thumbnail failed: " + ex.Message);
            return null;
        }
    }

    private static void Persist()
    {
        try
        {
            List<HistoryEntry> copy;
            lock (Gate) copy = _entries.ToList();
            var tmp = AppPaths.HistoryFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(copy));
            File.Move(tmp, AppPaths.HistoryFile, true);
        }
        catch (Exception ex) { Log.Error("Could not save history", ex); }
        Changed?.Invoke();
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
