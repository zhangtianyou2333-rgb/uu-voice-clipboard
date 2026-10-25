using System.Reflection;

namespace UUVoiceClipboard.Native;

internal static class RelayAppIcon
{
    internal static Icon Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UUVoiceClipboard.Native.Assets.Relay.ico")
            ?? throw new InvalidOperationException("程序图标资源缺失。");
        using var source = new Icon(stream, new Size(32, 32));
        return (Icon)source.Clone();
    }
}
