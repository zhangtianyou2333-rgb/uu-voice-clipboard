namespace UUVoiceClipboard.Native;

// A close/new input invalidates all asynchronous work from the previous panel.
internal sealed class PanelSessionPolicy
{
    internal int Generation { get; private set; }
    internal bool IsOpen { get; private set; }

    internal void Begin()
    {
        Generation++;
        IsOpen = true;
    }

    internal void Close()
    {
        Generation++;
        IsOpen = false;
    }

    internal bool Accepts(int generation) => IsOpen && Generation == generation;
}
