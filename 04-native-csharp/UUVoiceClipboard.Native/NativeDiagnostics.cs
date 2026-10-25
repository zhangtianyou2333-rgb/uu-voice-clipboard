namespace UUVoiceClipboard.Native;

internal sealed class NativeDiagnostics
{
    private readonly object sync = new();
    private readonly List<string> entries = new();
    private readonly int capacity;
    private long startedAt;
    private int textUpdates;
    private long lastTextSampleTick;

    internal NativeDiagnostics(int capacity = 120)
    {
        this.capacity = Math.Max(1, capacity);
    }

    internal void Begin(string source)
    {
        lock (sync)
        {
            startedAt = Environment.TickCount64;
            textUpdates = 0;
            lastTextSampleTick = 0;
            entries.Clear();
            RecordLocked($"session_begin source={source}");
        }
    }

    internal void Record(string stage)
    {
        lock (sync)
        {
            if (stage == "text_changed")
            {
                textUpdates++;
                var now = Environment.TickCount64;
                if (lastTextSampleTick != 0 && now - lastTextSampleTick < 2000) return;
                lastTextSampleTick = now;
            }
            RecordLocked(stage);
        }
    }

    internal string Snapshot(string state)
    {
        lock (sync)
        {
            var lines = new List<string>
            {
                "UU Voice Clipboard Native V1.12.0",
                "No transcript, window titles, or clipboard content recorded.",
                $"state={state}",
                $"text_updates={textUpdates} text_change_sampling_ms=2000"
            };
            lines.AddRange(entries);
            return string.Join(Environment.NewLine, lines);
        }
    }

    private void RecordLocked(string stage)
    {
        var elapsed = startedAt == 0 ? 0 : Environment.TickCount64 - startedAt;
        entries.Add($"+{elapsed}ms {stage}");
        if (entries.Count > capacity)
            entries.RemoveRange(0, entries.Count - capacity);
    }
}
