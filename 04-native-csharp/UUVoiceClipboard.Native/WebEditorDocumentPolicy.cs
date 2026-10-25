namespace UUVoiceClipboard.Native;

// The HTTPS-shaped address is intercepted in memory; no server or network
// connection is used. A concrete document URI keeps navigation/message checks
// independent of NavigateToString's opaque about:blank document.
internal static class WebEditorDocumentPolicy
{
    internal const string Uri = "https://uu-voice-editor.invalid/editor.html";
    internal static bool IsEditor(string source) => string.Equals(source, Uri, StringComparison.Ordinal);
}
