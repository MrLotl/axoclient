using System.Diagnostics;

namespace AxoClient.UI.Dialogs;

public static class Shell
{
    public static void OpenFolder(string dir, bool create = false)
    {
        try
        {
            if (create)
                Directory.CreateDirectory(dir);
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException($"Den Ordner \"{dir}\" gibt es (noch) nicht.");
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorDialog.Report("Ordner konnte nicht geöffnet werden", ex, "Gemeint war: " + dir);
        }
    }

    public static void ShowFile(string path)
    {
        try
        {
            var argument = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", argument) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorDialog.Report("Datei konnte nicht angezeigt werden", ex, "Gemeint war: " + path);
        }
    }

    public static void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Die Datei \"{path}\" gibt es nicht mehr.", path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorDialog.Report("Datei konnte nicht geöffnet werden", ex, "Gemeint war: " + path);
        }
    }

    public static void OpenUrl(string? url)
    {
        if (url == null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorDialog.Report("Link konnte nicht geöffnet werden", ex, "Gemeint war: " + url);
        }
    }
}
