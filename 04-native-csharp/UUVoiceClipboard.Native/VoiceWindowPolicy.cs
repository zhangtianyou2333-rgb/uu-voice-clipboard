namespace UUVoiceClipboard.Native;

internal sealed class VoiceWindowPolicy
{
    private HashSet<IntPtr> baseline = new();

    internal void Reset(IEnumerable<IntPtr> visible) => baseline = new(visible);
    internal void Hidden(IntPtr hwnd) => baseline.Remove(hwnd);
    internal bool IsTransient(IntPtr hwnd) => !baseline.Contains(hwnd);

    internal bool Observe(HashSet<IntPtr> visible)
    {
        // A hidden and reused HWND must be eligible for the next voice session.
        baseline.IntersectWith(visible);
        return visible.Any(IsTransient);
    }

    internal static bool ShouldFinalize(
        bool hasText,
        bool voiceActive,
        long lastTextChange,
        long lastTransient,
        long now,
        bool explicitEndRequested = false,
        bool compositionActive = false)
    {
        if (compositionActive || !hasText || lastTextChange == 0 || !voiceActive)
            return false;
        var textQuiet = now - lastTextChange;
        // A physical right-Alt release is an explicit end-of-speech signal.
        // Keep the longer fallback for sessions where that signal is missed.
        if (explicitEndRequested && textQuiet >= 550)
            return true;
        return textQuiet >= 900 &&
            ((lastTransient > 0 && now - lastTransient >= 1500) || textQuiet >= 3500);
    }
}
