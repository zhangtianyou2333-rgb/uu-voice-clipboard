using System.Windows.Forms;

namespace UUVoiceClipboard.Native;

internal interface ICompositionEditor
{
    Control Control { get; }
    CompositionEditorSupport Composition { get; }
    bool EditorFocused => Control.Focused;
    string Mode => Control is RichTextBox ? "richedit" : "textbox";
    void Clear() => ((TextBoxBase)Control).Clear();
    void FocusEditor() => Control.Focus();
}

internal sealed class CompositionEditorSupport
{
    private const int WM_IME_STARTCOMPOSITION = 0x010D;
    private const int WM_IME_ENDCOMPOSITION = 0x010E;
    private const int WM_IME_COMPOSITION = 0x010F;
    private readonly Control editor;
    private readonly CompositionStatePolicy policy = new();
    private readonly ImeCompositionLayout layout;
    private int? scrollQueuedGeneration;
    internal bool IsComposing => policy.IsComposing;
    internal int ViewportGeneration => policy.Generation;
    internal string LayoutDiagnostics => editor is TextBoxBase
        ? $"{layout.Diagnostics} preedit_notifications=enabled_guarded native_comp_probe=disabled"
        : "ime_layout=browser_textarea preedit_notifications=dom_composition native_comp_probe=disabled";
    internal event EventHandler? CompositionStateChanged;
    internal event Action<string>? RightAltMessage;
    internal event Action<int>? ViewportFollowRequested;

    internal CompositionEditorSupport(Control editor)
    {
        this.editor = editor;
        layout = new(editor);
        editor.Resize += (_, _) =>
        {
            if (IsComposing) layout.UpdateLegacyArea();
        };
    }

    internal void ObserveRightAltMessage(int message, long key, long flags)
    {
        var edge = EditorKeyMetadata.RightAltEdge(message, key, flags);
        if (edge is not null)
            RightAltMessage?.Invoke(edge);
    }

    internal bool BeforeMessage(int message)
    {
        var wasComposing = policy.IsComposing;
        if (message == WM_IME_STARTCOMPOSITION)
            policy.Begin();
        if (policy.IsComposing && message is WM_IME_STARTCOMPOSITION or WM_IME_COMPOSITION)
            layout.UpdateLegacyArea();
        return wasComposing;
    }

    internal void AfterMessage(int message, bool wasComposing)
    {
        if (message is WM_IME_STARTCOMPOSITION or WM_IME_COMPOSITION)
            layout.ObserveInlineMode();
        if (message == WM_IME_ENDCOMPOSITION)
        {
            policy.End();
            QueueScrollAfterCommit();
        }
        if (wasComposing != policy.IsComposing)
            CompositionStateChanged?.Invoke(this, EventArgs.Empty);
        if (message == WM_IME_COMPOSITION
            && policy.IsComposing
            && editor is not RichTextBox)
        {
            layout.UpdateLegacyArea();
            ViewportFollowRequested?.Invoke(policy.Generation);
        }
    }

    internal void ScrollToLatest()
    {
        // Selection-based scrolling still waits for confirmation. Preedit
        // caret following is a separate viewport-only operation in the host.
        if (IsComposing || editor is not TextBoxBase native || editor.IsDisposed || !editor.IsHandleCreated)
            return;
        native.SelectionStart = native.TextLength;
        native.SelectionLength = 0;
        native.ScrollToCaret();
    }

    internal void ObserveBrowserComposition(bool composing)
    {
        if (composing == policy.IsComposing) return;
        if (composing) policy.Begin();
        else policy.End();
        CompositionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void ResetCompositionTracking() => policy.Reset();

    private void QueueScrollAfterCommit()
    {
        if (editor.IsDisposed || !editor.IsHandleCreated)
            return;

        var generation = policy.Generation;
        if (scrollQueuedGeneration == generation)
            return;
        scrollQueuedGeneration = generation;
        editor.BeginInvoke((Action)(() =>
        {
            if (scrollQueuedGeneration == generation)
                scrollQueuedGeneration = null;
            if (editor.IsDisposed || !editor.IsHandleCreated || !policy.AcceptsScroll(generation))
                return;

            ScrollToLatest();
        }));
    }
}
