namespace UUVoiceClipboard.Native;

internal enum KeyboardEntryAction { None, F12, RightAlt }
internal readonly record struct KeyboardEntryDecision(KeyboardEntryAction Action, bool Suppress);

// Only the current gesture is retained, never a history of typed keys.
internal sealed class KeyboardEntryPolicy
{
    private bool f12Down;
    private bool rightAltDown;
    private bool combined;
    private readonly bool observeRightAlt;

    internal KeyboardEntryPolicy(bool observeRightAlt = true)
    {
        this.observeRightAlt = observeRightAlt;
    }

    internal KeyboardEntryDecision Handle(uint key, bool isDown, bool injected, bool otherKeyAlreadyDown = false)
    {
        if (injected)
            return default;

        if (!observeRightAlt && key != 0x7B)
            return default;

        if (key != 0xA5 && isDown && rightAltDown)
            combined = true;

        if (key == 0x7B)
        {
            var firstDown = isDown && !f12Down;
            f12Down = isDown;
            return new(firstDown ? KeyboardEntryAction.F12 : KeyboardEntryAction.None, true);
        }

        if (key != 0xA5)
            return default;

        if (isDown)
        {
            if (!rightAltDown)
            {
                rightAltDown = true;
                combined = otherKeyAlreadyDown;
            }
            return default;
        }

        var tap = rightAltDown && !combined;
        rightAltDown = false;
        combined = false;
        return new(tap ? KeyboardEntryAction.RightAlt : KeyboardEntryAction.None, false);
    }
}
