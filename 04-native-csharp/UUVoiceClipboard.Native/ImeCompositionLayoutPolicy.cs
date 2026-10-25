using System.Drawing;

namespace UUVoiceClipboard.Native;

internal readonly record struct ImeCompositionBounds(Rectangle Area, Point Anchor);

internal static class ImeCompositionLayoutPolicy
{
    internal static ImeCompositionBounds? Bound(int width, int height, int lineHeight, int caretX, int caretY)
    {
        // No positioning while the native edit control is collapsed or being
        // created. Coordinates are editor-client pixels, including DPI scaling.
        if (width <= 0 || height <= 0) return null;
        var lastLineTop = Math.Max(0, height - Math.Max(1, lineHeight));
        return new(new Rectangle(0, 0, width, height),
            new Point(Math.Clamp(caretX, 0, width - 1), Math.Clamp(caretY, 0, lastLineTop)));
    }
}
