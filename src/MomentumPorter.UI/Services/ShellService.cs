namespace MomentumPorter.UI.Services;

using System.Diagnostics;

/// <summary>
/// Opens folders and files in the system's file manager.
/// </summary>
public static class ShellService
{
    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    /// <summary>
    /// Opens the file's folder with the file selected where the platform supports it.
    /// </summary>
    public static void RevealFile(string file)
    {
        if (OperatingSystem.IsWindows() && File.Exists(file))
        {
            // Explorer has its own argument parsing: only the path may be quoted
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(file)}\"") { UseShellExecute = false });
            return;
        }

        if (Path.GetDirectoryName(file) is { } folder)
            OpenFolder(folder);
    }
}
