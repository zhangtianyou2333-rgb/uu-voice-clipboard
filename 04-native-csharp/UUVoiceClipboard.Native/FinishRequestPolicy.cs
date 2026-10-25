namespace UUVoiceClipboard.Native;

internal sealed class FinishRequestPolicy
{
    internal const int CommitSettleMilliseconds = 550;
    internal bool Pending { get; private set; }
    private bool waitingForCommit;
    private long commitTick;

    internal void Request(bool composing)
    {
        if (Pending)
            return;
        Pending = true;
        waitingForCommit = composing;
        commitTick = 0;
    }

    internal void CompositionChanged(bool composing, long now)
    {
        if (!Pending)
            return;
        if (!composing && waitingForCommit)
            commitTick = now;
        else if (composing && commitTick > 0)
            Reset();
    }

    internal bool ShouldFinish(bool hasText, bool composing, long lastTextChange, long now)
    {
        if (!Pending || !hasText || composing)
            return false;
        if (!waitingForCommit)
            return true;
        return commitTick > 0 && now - Math.Max(commitTick, lastTextChange) >= CommitSettleMilliseconds;
    }

    internal void Reset()
    {
        Pending = false;
        waitingForCommit = false;
        commitTick = 0;
    }
}
