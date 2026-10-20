using Microsoft.Win32;

namespace PoVoiceHud.App.Interop;

/// <summary>"Start with Windows" through the current user's Run key, so no administrator rights are needed.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "PoVoiceHud";

    /// <summary>
    /// Points the Run entry at the exe that is running now (an installed copy keeps the same path across updates),
    /// or removes it. Returns false when the registry refused.
    /// </summary>
    public static bool Set(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on && Environment.ProcessPath is { } exe) key.SetValue(Name, $"\"{exe}\"");
            else key.DeleteValue(Name, throwOnMissingValue: false);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
