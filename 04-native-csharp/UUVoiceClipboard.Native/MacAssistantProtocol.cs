namespace UUVoiceClipboard.Native;

// The marker is made only from zero-width characters so a tagged clipboard
// remains readable if the Mac assistant is not running. It is deliberately
// asymmetric to avoid treating an ordinary copied string as a bridge packet.
internal static class MacAssistantProtocol
{
    private const string Start = "\u2063\u200B\u200C\u200D";
    private const string End = "\u2060\u200D\u200C\u200B";

    internal static string Encode(string text) => text + Start + End;

    internal static bool TryDecode(string payload, out string text)
    {
        text = string.Empty;
        if (!payload.EndsWith(End, StringComparison.Ordinal))
            return false;

        var withoutEnd = payload[..^End.Length];
        if (!withoutEnd.EndsWith(Start, StringComparison.Ordinal))
            return false;

        text = withoutEnd[..^Start.Length];
        return !string.IsNullOrWhiteSpace(text);
    }
}
