namespace UUVoiceClipboard.Native;

// A delayed browser message from a closed/cleared session must never restore
// text, end a newer composition, request a paste, or steal focus.
internal sealed class WebEditorMessagePolicy
{
    internal int Epoch { get; private set; }
    private long lastSequence;

    internal void Reset()
    {
        Epoch++;
        lastSequence = 0;
    }

    internal bool Accept(int epoch, long sequence)
    {
        if (epoch != Epoch || sequence <= lastSequence) return false;
        lastSequence = sequence;
        return true;
    }
}
