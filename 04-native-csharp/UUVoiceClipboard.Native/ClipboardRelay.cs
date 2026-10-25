using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UUVoiceClipboard.Native;

internal static class ClipboardRelay
{
    internal static bool TrySetText(string text, bool tagForMacAssistant, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            {
            error = "没有可复制的文字";
            return false;
        }

        var payload = tagForMacAssistant ? MacAssistantProtocol.Encode(text) : text;

        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                Clipboard.SetText(payload, TextDataFormat.UnicodeText);
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText)
                    && string.Equals(Clipboard.GetText(TextDataFormat.UnicodeText), payload, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (ExternalException)
            {
                // Another process may briefly own the clipboard. Retry below.
            }

            Thread.Sleep(35);
        }

        error = "Windows 剪贴板暂时被其他程序占用";
        return false;
    }

    internal static bool TrySetText(string text, out string error) =>
        TrySetText(text, tagForMacAssistant: false, out error);

    internal static bool Matches(string text)
    {
        try
        {
            return Clipboard.ContainsText(TextDataFormat.UnicodeText)
                && string.Equals(Clipboard.GetText(TextDataFormat.UnicodeText), text, StringComparison.Ordinal);
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    internal static void Clear()
    {
        try
        {
            Clipboard.Clear();
        }
        catch (ExternalException)
        {
            // Clearing is best effort; the recognized text is never persisted by this app.
        }
    }
}
