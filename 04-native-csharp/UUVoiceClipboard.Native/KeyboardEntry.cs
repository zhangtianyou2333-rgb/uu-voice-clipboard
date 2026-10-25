using System.Runtime.InteropServices;

namespace UUVoiceClipboard.Native;

// F12 is consumed locally. A standalone physical right Alt is observed and
// always passed to Doubao; UI work is queued by the caller, never run here.
internal sealed class KeyboardEntry : IDisposable
{
    private readonly HookProc callback;
    private readonly Action<KeyboardEntryAction> open;
    private readonly KeyboardEntryPolicy policy;
    private readonly bool observeRightAlt;
    private IntPtr hook;
    internal bool Available => hook != IntPtr.Zero;
    internal int RightAltTaps { get; private set; }
    internal int F12Events { get; private set; }

    internal KeyboardEntry(Action<KeyboardEntryAction> open, bool observeRightAlt = true)
    {
        this.open = open;
        this.observeRightAlt = observeRightAlt;
        policy = new KeyboardEntryPolicy(observeRightAlt);
        callback = OnKey;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
    }

    private IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KeyData>(data);
            var msg = message.ToInt32();
            if (msg is 0x100 or 0x104 or 0x101 or 0x105)
            {
                var normalized = key.Key == 0x12 && (key.Flags & 1) != 0 ? 0xA5u : key.Key;
                var isDown = msg is 0x100 or 0x104;
                var injected = (key.Flags & 0x12) != 0;
                var decision = policy.Handle(normalized, isDown, injected,
                    observeRightAlt && normalized == 0xA5 && isDown && !injected && OtherKeyIsDown());
                if (decision.Action == KeyboardEntryAction.RightAlt) RightAltTaps++;
                if (decision.Action == KeyboardEntryAction.F12) F12Events++;
                if (decision.Action != KeyboardEntryAction.None) open(decision.Action);
                if (decision.Suppress) return (IntPtr)1;
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    private static bool OtherKeyIsDown()
    {
        for (var key = 8; key < 255; key++)
        {
            if (key is 0x12 or 0xA5) continue;
            if ((GetAsyncKeyState(key) & 0x8000) != 0) return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyData
    {
        public uint Key, Scan, Flags, Time;
        public UIntPtr Extra;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
