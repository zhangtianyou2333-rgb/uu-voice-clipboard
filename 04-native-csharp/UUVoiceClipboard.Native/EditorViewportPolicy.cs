namespace UUVoiceClipboard.Native;

internal readonly record struct EditorViewport(int TotalLines, int PageLines, int FirstLine)
{
    internal int Maximum => Math.Max(0, TotalLines - PageLines);
    internal bool Scrollable => Maximum > 0;
    internal int Position => Math.Clamp(FirstLine, 0, Maximum);

    internal int LineFromFraction(double fraction) =>
        (int)Math.Round(Math.Clamp(fraction, 0, 1) * Maximum);

    internal double ThumbFraction => Math.Clamp((double)PageLines / Math.Max(1, TotalLines), 0, 1);
}

internal static class EditorViewportPolicy
{
    // Scroll only the viewport. Never move the selection or commit IME text.
    internal static int LinesToExposeCaret(int caretTop, int clientHeight, int lineHeight)
    {
        if (clientHeight <= 0 || lineHeight <= 0)
            return 0;
        if (caretTop < 0)
            return (int)Math.Floor((double)caretTop / lineHeight);
        var overflow = (long)caretTop + lineHeight - clientHeight;
        return overflow > 0 ? (int)Math.Ceiling((double)overflow / lineHeight) : 0;
    }

    internal static bool AcceptFollow(int queuedGeneration, int currentGeneration,
        bool focused, bool visible, bool handleAvailable) =>
        queuedGeneration == currentGeneration && focused && visible && handleAvailable;

}
