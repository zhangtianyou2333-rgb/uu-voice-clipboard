namespace UUVoiceClipboard.Native;

// ReceiverHandoff only confirms the local clipboard handoff. There is no remote acknowledgement.
internal sealed record RelayResult(bool ClipboardCopied, bool ReceiverHandoff, string Message);

internal sealed class PasteDispatcher
{
    internal async Task<RelayResult> RelayAsync(
        string text,
        bool autoForward,
        Action<string> report,
        Action hideBeforeHandoff,
        Action<string>? diagnostic,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        diagnostic?.Invoke("relay_begin");
        if (!ClipboardRelay.TrySetText(text, autoForward, out var error))
            return new RelayResult(false, false, error);
        diagnostic?.Invoke("clipboard_written");
        cancellationToken.ThrowIfCancellationRequested();
        if (!autoForward)
            return new RelayResult(true, false, "已复制纯文本；自动转发已关闭");

        report("已写入同步剪贴板，等待接收助手");
        diagnostic?.Invoke("receiver_clipboard_handoff");
        hideBeforeHandoff();
        await Task.Delay(120, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        return new RelayResult(true, true, "已写入同步剪贴板；接收端粘贴尚未确认");
    }
}
