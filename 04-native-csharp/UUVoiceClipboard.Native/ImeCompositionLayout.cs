using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UUVoiceClipboard.Native;

// RichEdit owns its native inline text layout. Only the compatibility EDIT
// control needs a bounded IMM composition area; never suppress its IME UI or
// copy a preedit string into the confirmed document.
internal sealed class ImeCompositionLayout
{
    private const int CFS_RECT = 1;
    private const int EM_GETIMECOMPMODE = 0x047A;
    private readonly Control editor;
    private int areaUpdates;
    private bool lastAreaAccepted;
    private long lastComposingMode = -1;

    internal ImeCompositionLayout(Control editor) => this.editor = editor;

    internal string Diagnostics
    {
        get
        {
            var rich = editor is RichTextBox;
            var mode = rich && editor.IsHandleCreated && !editor.IsDisposed
                ? NativeMethods.SendMessage(editor.Handle, EM_GETIMECOMPMODE, IntPtr.Zero, IntPtr.Zero).ToInt64() : -1;
            // Mode is an observation of IME layout, never an end-of-speech or
            // commit signal. No composition characters are queried or recorded.
            return $"ime_layout={(rich ? "native_inline" : "legacy_bounded")} ime_native_mode={mode} ime_last_composing_mode={lastComposingMode} "
                + $"ime_area_updates={areaUpdates} ime_area_accepted={lastAreaAccepted}";
        }
    }

    internal void ObserveInlineMode()
    {
        if (editor is RichTextBox && editor.IsHandleCreated && !editor.IsDisposed)
            lastComposingMode = NativeMethods.SendMessage(editor.Handle, EM_GETIMECOMPMODE, IntPtr.Zero, IntPtr.Zero).ToInt64();
    }

    internal void UpdateLegacyArea()
    {
        if (editor is not TextBox || editor.IsDisposed || !editor.IsHandleCreated || !editor.Visible || !editor.Focused)
            return;
        var caret = GetFocus() == editor.Handle && GetCaretPos(out var position) ? position : default;
        var bounds = ImeCompositionLayoutPolicy.Bound(editor.ClientSize.Width, editor.ClientSize.Height,
            editor.Font.Height, caret.X, caret.Y);
        if (bounds is not { } area) return;
        var context = ImmGetContext(editor.Handle);
        if (context == IntPtr.Zero) return;
        try
        {
            var form = new COMPOSITIONFORM
            {
                dwStyle = CFS_RECT,
                ptCurrentPos = new NativeMethods.POINT { X = area.Anchor.X, Y = area.Anchor.Y },
                rcArea = new RECT { Left = area.Area.Left, Top = area.Area.Top, Right = area.Area.Right, Bottom = area.Area.Bottom }
            };
            areaUpdates++;
            lastAreaAccepted = ImmSetCompositionWindow(context, ref form);
        }
        finally
        {
            ImmReleaseContext(editor.Handle, context);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct COMPOSITIONFORM
    {
        internal int dwStyle;
        internal NativeMethods.POINT ptCurrentPos;
        internal RECT rcArea;
    }
    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCaretPos(out NativeMethods.POINT point);
    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr window);
    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmReleaseContext(IntPtr window, IntPtr context);
    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmSetCompositionWindow(IntPtr context, ref COMPOSITIONFORM form);
}
