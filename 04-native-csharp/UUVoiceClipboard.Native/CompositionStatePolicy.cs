namespace UUVoiceClipboard.Native;

internal sealed class CompositionStatePolicy
{
    internal bool IsComposing { get; private set; }
    internal int Generation { get; private set; }

    internal void Begin()
    {
        IsComposing = true;
        Generation++;
    }

    internal void End()
    {
        IsComposing = false;
    }

    internal void Reset()
    {
        IsComposing = false;
        Generation++;
    }

    internal bool AcceptsScroll(int generation) => !IsComposing && generation == Generation;
}
