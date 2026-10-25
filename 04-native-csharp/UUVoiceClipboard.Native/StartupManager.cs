using Microsoft.Win32;

namespace UUVoiceClipboard.Native;

internal static class StartupManager
{
    private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string AppValue = "UUVoiceClipboard.Native";

    internal static void Ensure()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var exe = Application.ExecutablePath;
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return;

        var launcher = Path.Combine(AppContext.BaseDirectory, "Start-Native.vbs");
        var command = File.Exists(launcher)
            ? $"wscript.exe \"{launcher}\" --startup"
            : $"\"{exe}\" --startup";

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key?.SetValue(AppValue, command, RegistryValueKind.String);
        }
        catch (UnauthorizedAccessException)
        {
            // Startup is an enhancement, not a reason to prevent manual use.
        }
        catch (IOException)
        {
            // The app remains usable from its executable when registry access fails.
        }
    }
}
