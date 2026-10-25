using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UUVoiceClipboard.Native;

internal static class NativeWindowChrome
{
    internal const int WM_NCCALCSIZE = 0x0083;
    internal const int WM_DWMCOMPOSITIONCHANGED = 0x031E;
    internal const int WS_CAPTION = 0x00C00000;
    internal const int WS_THICKFRAME = 0x00040000;

    internal static string Apply(Form form)
    {
        // A rectangular HWND with native frame styles lets DWM provide real
        // shadows and anti-aliased corners. Do not apply a binary GDI Region
        // or make the editor's window layered/transparent.
        var dark = 1;
        var rounded = 2; // DWMWCP_ROUND; Windows 11 owns the corner radius.
        var border = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        _ = DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        var cornerResult = DwmSetWindowAttribute(form.Handle, 33, ref rounded, sizeof(int));
        _ = DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
        var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        var frameResult = DwmExtendFrameIntoClientArea(form.Handle, ref margins);
        return $"chrome=dwm corner_hr={cornerResult} shadow_frame_hr={frameResult}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        internal int Left;
        internal int Right;
        internal int Top;
        internal int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
}
