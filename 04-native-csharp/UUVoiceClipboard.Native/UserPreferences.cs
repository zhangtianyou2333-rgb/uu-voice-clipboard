using Microsoft.Win32;

namespace UUVoiceClipboard.Native;

internal static class UserPreferences
{
    private const string KeyPath = "Software\\UUVoiceClipboard.Native";

    internal static bool GetBool(string name, bool fallback)
    {
        if (!OperatingSystem.IsWindows())
            return fallback;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            return key?.GetValue(name) is int value ? value != 0 : fallback;
        }
        catch (UnauthorizedAccessException)
        {
            return fallback;
        }
        catch (IOException)
        {
            return fallback;
        }
    }

    internal static void SetBool(string name, bool value)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key?.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (UnauthorizedAccessException)
        {
            // Preferences are optional; the current process keeps the change.
        }
        catch (IOException)
        {
            // Preferences are optional; the current process keeps the change.
        }
    }
}
