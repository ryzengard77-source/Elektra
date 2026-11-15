using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Elektra.Platform;

public sealed class WindowsToolLauncher
{
    private static readonly string[] ToolCommandPrefixes = ["open the ", "launch the ", "open ", "launch ", "start ", "run "];
    private static readonly string[] FolderCommandPrefixes = ["open the ", "show the ", "browse the ", "open ", "show ", "browse ", "go to "];
    private static readonly string[] FolderCommandSuffixes = [" directory", " folder"];
    private static readonly string[] HaloShortcuts =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Halo The Master Chief Collection.url"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop", "Halo The Master Chief Collection.url")
    ];

    public static bool IsGoogleEarthOpenCommand(string command) =>
        command is "open google earth" or "open the google earth" or
            "launch google earth" or "launch the google earth" or
            "start google earth" or "start the google earth" or "google earth";

    public bool TryHandle(string command, out string action, out string error)
    {
        action = string.Empty;
        error = string.Empty;
        string? folder = ParseFolderCommand(command);
        if (folder != null)
        {
            string? path = GetFolderPath(folder);
            if (path == null || !Directory.Exists(path))
            {
                error = $"I couldn't find the {folder} folder.";
                action = $"Open {folder}";
                return true;
            }
            return Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }, $"Opening {folder}.", out action, out error);
        }

        string tool = command;
        foreach (string prefix in ToolCommandPrefixes)
        {
            if (tool.StartsWith(prefix, StringComparison.Ordinal))
            {
                tool = tool[prefix.Length..].Trim();
                break;
            }
        }

        return tool switch
        {
            "steam" => Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true }, "Launching Steam.", out action, out error),
            "halo" or "halo path" or "halo the master chief collection" or "halo master chief collection" or "halo master chief collection path" => OpenHalo(out action, out error),
            "visual studio" or "visual studio ide" or "vs" => OpenVisualStudio(out action, out error),
            "notepad" => Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true }, "Opening Notepad.", out action, out error),
            "powershell" or "powershell normal" or "normal powershell" => Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = true }, "Opening PowerShell.", out action, out error),
            "powershell admin" or "admin powershell" or "administrator powershell" or "powershell as administrator" =>
                Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas" }, "Opening PowerShell as administrator.", out action, out error),
            "cmd" or "command prompt" => Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = true }, "Opening Command Prompt.", out action, out error),
            "control panel" => Start(new ProcessStartInfo("control.exe") { UseShellExecute = true }, "Opening Control Panel.", out action, out error),
            "file explorer" or "explorer" => Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }, "Opening File Explorer.", out action, out error),
            "windows terminal" or "terminal" => Start(new ProcessStartInfo("wt.exe") { UseShellExecute = true }, "Opening Windows Terminal.", out action, out error),
            "task manager" => Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }, "Opening Task Manager.", out action, out error),
            "calculator" => Start(new ProcessStartInfo("calc.exe") { UseShellExecute = true }, "Opening Calculator.", out action, out error),
            "snipping tool" => Start(new ProcessStartInfo("snippingtool.exe") { UseShellExecute = true }, "Opening Snipping Tool.", out action, out error),
            "device manager" => Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true }, "Opening Device Manager.", out action, out error),
            "services" or "services manager" => Start(new ProcessStartInfo("services.msc") { UseShellExecute = true }, "Opening Services.", out action, out error),
            "event viewer" => Start(new ProcessStartInfo("eventvwr.msc") { UseShellExecute = true }, "Opening Event Viewer.", out action, out error),
            "disk management" => Start(new ProcessStartInfo("diskmgmt.msc") { UseShellExecute = true }, "Opening Disk Management.", out action, out error),
            "registry editor" or "regedit" => Start(new ProcessStartInfo("regedit.exe") { UseShellExecute = true }, "Opening Registry Editor.", out action, out error),
            "paint" => Start(new ProcessStartInfo("mspaint.exe") { UseShellExecute = true }, "Opening Paint.", out action, out error),
            "word" or "microsoft word" => Start(new ProcessStartInfo("winword.exe") { UseShellExecute = true }, "Opening Word.", out action, out error),
            "excel" or "microsoft excel" => Start(new ProcessStartInfo("excel.exe") { UseShellExecute = true }, "Opening Excel.", out action, out error),
            "powerpoint" or "microsoft powerpoint" => Start(new ProcessStartInfo("powerpnt.exe") { UseShellExecute = true }, "Opening PowerPoint.", out action, out error),
            "outlook" or "microsoft outlook" => Start(new ProcessStartInfo("outlook.exe") { UseShellExecute = true }, "Opening Outlook.", out action, out error),
            "edge" or "microsoft edge" => Start(new ProcessStartInfo("msedge.exe") { UseShellExecute = true }, "Opening Microsoft Edge.", out action, out error),
            "settings" or "windows settings" => Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true }, "Opening Windows Settings.", out action, out error),
            "google earth" => Start(new ProcessStartInfo("https://earth.google.com/web/") { UseShellExecute = true }, "Opening Google Earth.", out action, out error),
            _ => false
        };
    }

    public bool TryOpenGoogleEarth(double latitude, double longitude, out string action, out string error)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90 ||
            !double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            action = "Open Google Earth";
            error = "The location coordinates were invalid.";
            return false;
        }

        string lat = latitude.ToString("0.######", CultureInfo.InvariantCulture);
        string lon = longitude.ToString("0.######", CultureInfo.InvariantCulture);
        string url = $"https://earth.google.com/web/@{lat},{lon},1000a,0d,35y,0h,0t,0r";
        return Start(new ProcessStartInfo(url) { UseShellExecute = true }, "Opening Google Earth.", out action, out error);
    }

    private static bool OpenHalo(out string action, out string error)
    {
        string? shortcut = HaloShortcuts.FirstOrDefault(File.Exists);
        if (shortcut == null)
        {
            action = "Open Halo: The Master Chief Collection";
            error = $"I couldn't find the Halo shortcut at {HaloShortcuts[1]}.";
            return true;
        }
        return Start(new ProcessStartInfo(shortcut) { UseShellExecute = true }, "Launching Halo: The Master Chief Collection.", out action, out error);
    }

    private static bool OpenVisualStudio(out string action, out string error)
    {
        string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (File.Exists(vswhere))
        {
            using var finder = Process.Start(new ProcessStartInfo
            {
                FileName = vswhere,
                Arguments = "-latest -products * -find Common7\\IDE\\devenv.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (finder != null)
            {
                string executable = finder.StandardOutput.ReadToEnd().Trim();
                if (finder.WaitForExit(5000) && finder.ExitCode == 0 && File.Exists(executable))
                    return Start(new ProcessStartInfo(executable) { UseShellExecute = true }, "Launching Visual Studio.", out action, out error);
            }
        }

        return Start(new ProcessStartInfo("devenv.exe") { UseShellExecute = true }, "Launching Visual Studio.", out action, out error);
    }

    private static bool Start(ProcessStartInfo startInfo, string success, out string action, out string error)
    {
        try
        {
            using Process? process = Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("Windows did not start the requested application.");
            action = success;
            error = string.Empty;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            action = "Application launch cancelled";
            error = "The administrator launch was cancelled.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            action = "Application launch failed";
            error = $"I couldn't open that application: {ex.Message}";
        }
        return true;
    }

    private static string? ParseFolderCommand(string command)
    {
        string folder = command;
        foreach (string prefix in FolderCommandPrefixes)
        {
            if (folder.StartsWith(prefix, StringComparison.Ordinal))
            {
                folder = folder[prefix.Length..].Trim();
                break;
            }
        }

        foreach (string suffix in FolderCommandSuffixes)
        {
            if (folder.EndsWith(suffix, StringComparison.Ordinal))
            {
                folder = folder[..^suffix.Length].Trim();
                break;
            }
        }

        folder = folder switch
        {
            "my pictures" => "pictures",
            "my videos" => "videos",
            "my documents" => "documents",
            "my music" => "music",
            "my downloads" => "downloads",
            _ => folder
        };
        return folder is "pictures" or "videos" or "documents" or "music" or "downloads"
            ? char.ToUpperInvariant(folder[0]) + folder[1..]
            : null;
    }

    private static string? GetFolderPath(string folder) => folder switch
    {
        "Pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "Videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "Downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        _ => null
    };
}
