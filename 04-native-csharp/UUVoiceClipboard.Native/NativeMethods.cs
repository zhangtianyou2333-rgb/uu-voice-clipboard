using System.Runtime.InteropServices;
using System.Text;

namespace UUVoiceClipboard.Native;

internal static class NativeMethods
{
    internal const int WM_HOTKEY = 0x0312;
    internal const int WM_INPUT = 0x00FF;
    internal const int WM_NCLBUTTONDOWN = 0x00A1;
    internal const int HTCAPTION = 2;
    internal const int MOD_ALT = 0x0001;
    internal const int MOD_CONTROL = 0x0002;
    internal const int MOD_SHIFT = 0x0004;
    internal const int MOD_NOREPEAT = 0x4000;
    internal const uint VK_SPACE = 0x20;
    internal const uint VK_F12 = 0x7B;
    internal const ushort VK_LCONTROL = 0xA2;
    internal const uint VK_CONTROL = 0x11;
    internal const uint VK_V = 0x56;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_SCANCODE = 0x0008;
    internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
    internal const ushort SCAN_LCONTROL = 0x001D;
    internal const ushort SCAN_V = 0x002F;
    internal const uint WM_KEYDOWN = 0x0100;
    internal const uint WM_KEYUP = 0x0101;
    internal const int SW_RESTORE = 9;
    internal const int SB_VERT = 1;

    internal const int INPUT_KEYBOARD = 1;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowScrollBar(IntPtr hWnd, int bar, bool show);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, ref POINT lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO threadInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct GUITHREADINFO
    {
        public uint cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    internal static string WindowTitle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return string.Empty;

        var text = new StringBuilder(512);
        GetWindowText(hWnd, text, text.Capacity);
        return text.ToString();
    }

    internal static string WindowClass(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return string.Empty;

        var text = new StringBuilder(256);
        GetClassName(hWnd, text, text.Capacity);
        return text.ToString();
    }

}
