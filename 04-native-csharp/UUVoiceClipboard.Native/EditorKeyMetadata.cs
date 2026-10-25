namespace UUVoiceClipboard.Native;

// Only right-Alt message metadata is exposed; never text or other typed keys.
// A missing message is inconclusive: an IME hook can consume a working hotkey.
internal static class EditorKeyMetadata
{
    internal static string? RightAltEdge(int message, long key, long flags)
    {
        if (message is not (0x0100 or 0x0101 or 0x0104 or 0x0105))
            return null;
        if (key != 0xA5 && !(key == 0x12 && (flags & (1L << 24)) != 0))
            return null;
        return message is 0x0100 or 0x0104 ? "down" : "up";
    }
}
