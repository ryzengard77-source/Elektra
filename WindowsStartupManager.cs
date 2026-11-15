/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using Microsoft.Win32;

namespace Elektra.UI.Interop;

public static class WindowsStartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ElektraVoiceAssistant";

    public static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(AppName) is string val && !string.IsNullOrWhiteSpace(val);
    }

    public static void SetStartupEnabled(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;

        if (enable && Environment.ProcessPath is string exePath)
            key.SetValue(AppName, $"\"{exePath}\"");
        else
            key.DeleteValue(AppName, throwOnMissingValue: false);
    }
}