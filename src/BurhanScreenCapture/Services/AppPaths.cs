namespace BurhanScreenCapture.Services;

public static class AppPaths
{
    public static string AppData { get; } = Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Burhan Nexus", "Screen Capture Pro"));
    public static string LocalData { get; } = Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Burhan Nexus", "Screen Capture Pro"));
    public static string Thumbnails => Ensure(Path.Combine(LocalData, "thumbs"));
    public static string Logs => Ensure(Path.Combine(LocalData, "logs"));
    public static string Temp => Ensure(Path.Combine(LocalData, "temp"));
    public static string SettingsFile => Path.Combine(AppData, "settings.json");
    public static string HistoryFile => Path.Combine(AppData, "history.json");
    public static string LicenseFile => Path.Combine(LocalData, "activation.dat");

    public static string DefaultScreenshotFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Burhan Screen Capture");
    public static string DefaultRecordingFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Burhan Screen Capture");

    public static string Ensure(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { /* reported when used */ }
        return dir;
    }

    /// <summary>Validates and creates a user-chosen folder. Returns an error message or null.</summary>
    public static string? ValidateWritableFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "No folder selected.";
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".bsc_write_test_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return null;
        }
        catch (UnauthorizedAccessException) { return $"Windows does not allow saving to \"{folder}\". Choose another folder."; }
        catch (IOException ex) { return $"The folder \"{folder}\" cannot be used: {ex.Message}"; }
        catch (Exception ex) { return $"The folder \"{folder}\" is not valid: {ex.Message}"; }
    }

    public static long FreeBytes(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            return root is null ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
        }
        catch { return long.MaxValue; }
    }

    public static string UniqueFile(string folder, string baseName, string ext)
    {
        var path = Path.Combine(folder, baseName + ext);
        int i = 2;
        while (File.Exists(path)) path = Path.Combine(folder, $"{baseName} ({i++}){ext}");
        return path;
    }
}
