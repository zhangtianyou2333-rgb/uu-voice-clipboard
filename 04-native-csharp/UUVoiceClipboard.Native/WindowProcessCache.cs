namespace UUVoiceClipboard.Native;

internal sealed class WindowProcessCache
{
    private readonly Func<uint, string?> readName;
    private readonly Dictionary<uint, bool> names = new();
    private long expiresAt;

    internal WindowProcessCache(Func<uint, string?> readName) => this.readName = readName;

    internal bool IsDoubao(uint pid, long now)
    {
        if (pid == 0)
            return false;
        if (now >= expiresAt || names.Count >= 512)
        {
            names.Clear();
            expiresAt = now + 2000;
        }
        if (names.TryGetValue(pid, out var cached))
            return cached;
        var name = readName(pid);
        var result = string.Equals(name, "ImeService", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "ImeService.exe", StringComparison.OrdinalIgnoreCase);
        names[pid] = result;
        return result;
    }
}
