using System.Diagnostics;

namespace UUVoiceClipboard.Native;

internal sealed class VoiceSessionWatcher : IDisposable
{
    private readonly System.Windows.Forms.Timer timer;
    private readonly VoiceWindowPolicy policy = new();
    private readonly WindowProcessCache processCache = new(ReadProcessName);
    private HashSet<IntPtr> visibleWindows = new();
    private bool disposed;
    private bool scanning;
    private int generation;

    internal VoiceSessionWatcher()
    {
        policy.Reset(visibleWindows);
        timer = new System.Windows.Forms.Timer { Interval = 80 };
        timer.Tick += async (_, _) => await PollAsync();
        timer.Start();
    }

    internal bool PanelVisible { get; set; }
    internal bool VoiceActive { get; private set; }
    internal long LastTransientTick { get; private set; }
    internal long LastTextChangeTick { get; private set; }
    internal bool ExplicitEndRequested { get; private set; }
    internal double LastScanMilliseconds { get; private set; }

    internal string FinalizationReason
    {
        get
        {
            var now = Environment.TickCount64;
            if (ExplicitEndRequested && LastTextChangeTick > 0
                && now - LastTextChangeTick >= 550)
                return "explicit_550ms";
            if (LastTextChangeTick > 0 && now - LastTextChangeTick >= 3500)
                return "fallback_3500ms";
            if (LastTransientTick > 0 && now - LastTransientTick >= 1500)
                return "window_quiet_1500ms";
            return "text_quiet_900ms";
        }
    }

    internal void ResetForManualStart()
    {
        generation++;
        policy.Reset(visibleWindows);
        VoiceActive = false;
        LastTransientTick = 0;
        LastTextChangeTick = 0;
        ExplicitEndRequested = false;
    }

    internal void NotifyTextChanged()
    {
        VoiceActive = true;
        LastTextChangeTick = Environment.TickCount64;
    }

    internal bool ShouldFinalize(string text, bool compositionActive)
    {
        return VoiceWindowPolicy.ShouldFinalize(!string.IsNullOrWhiteSpace(text), VoiceActive,
            LastTextChangeTick, LastTransientTick, Environment.TickCount64, ExplicitEndRequested,
            compositionActive);
    }

    internal void RequestExplicitEnd()
    {
        if (VoiceActive && LastTextChangeTick > 0)
            ExplicitEndRequested = true;
    }

    internal void MarkSessionFinished()
    {
        VoiceActive = false;
        generation++;
        policy.Reset(visibleWindows);
        LastTransientTick = 0;
        LastTextChangeTick = 0;
        ExplicitEndRequested = false;
    }

    private async Task PollAsync()
    {
        if (disposed || scanning || !PanelVisible) return;
        scanning = true;
        var scanGeneration = generation;
        try
        {
            // Process queries and window enumeration must not block TSF/edit painting.
            var snapshot = await Task.Run(ScanWindows);
            if (disposed || !PanelVisible || scanGeneration != generation) return;
            visibleWindows = snapshot.Windows;
            LastScanMilliseconds = snapshot.ElapsedMilliseconds;
            if (policy.Observe(visibleWindows))
            {
                LastTransientTick = Environment.TickCount64;
                VoiceActive = true;
            }
        }
        finally { scanning = false; }
    }

    private sealed record WindowSnapshot(HashSet<IntPtr> Windows, double ElapsedMilliseconds);

    private WindowSnapshot ScanWindows()
    {
        var watch = Stopwatch.StartNew();
        var result = new HashSet<IntPtr>();
        var now = Environment.TickCount64;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (processCache.IsDoubao(pid, now))
            {
                result.Add(hwnd);
            }
            return true;
        }, IntPtr.Zero);
        return new(result, watch.Elapsed.TotalMilliseconds);
    }

    private static string? ReadProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        timer.Stop();
        timer.Dispose();
    }

}
