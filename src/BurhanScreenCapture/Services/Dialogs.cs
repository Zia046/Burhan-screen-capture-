using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace BurhanScreenCapture.Services;

public static class Dialogs
{
    public static string? PickFolder(string? initial)
    {
        var dlg = new OpenFolderDialog { Title = "Choose a folder", Multiselect = false };
        if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) dlg.InitialDirectory = initial;
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*";

    public static string? PickImage()
    {
        var dlg = new OpenFileDialog { Filter = ImageFilter, Title = "Open image" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public static string[] PickImages()
    {
        var dlg = new OpenFileDialog { Filter = ImageFilter, Title = "Choose images", Multiselect = true };
        return dlg.ShowDialog() == true ? dlg.FileNames : Array.Empty<string>();
    }

    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show("The folder could not be opened: " + ex.Message, App.AppName); }
    }

    public static void ShowInFolder(string file)
    {
        try
        {
            if (File.Exists(file)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
            else OpenFolder(Path.GetDirectoryName(file) ?? "");
        }
        catch (Exception ex) { MessageBox.Show("The folder could not be opened: " + ex.Message, App.AppName); }
    }

    public static void OpenFile(string file)
    {
        try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("The file could not be opened: " + ex.Message, App.AppName); }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
