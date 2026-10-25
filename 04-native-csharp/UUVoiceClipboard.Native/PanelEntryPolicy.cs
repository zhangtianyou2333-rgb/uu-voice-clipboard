namespace UUVoiceClipboard.Native;

internal enum PanelEntryAction { Ignore, StartNew, Restore, Finish }
internal readonly record struct PanelEntryContext(
    bool IsOpen, bool IsVisible, bool IsForeground, bool IsBlocked,
    bool IsCurrentGeneration, bool IsUuForeground);
internal readonly record struct PanelEntryDecision(PanelEntryAction Action, string Reason);

// Visibility alone does not mean that the user is typing in this panel.
// Restoring an existing session must preserve its text and original paste target.
internal static class PanelEntryPolicy
{
    internal static PanelEntryDecision Decide(KeyboardEntryAction key, PanelEntryContext context)
    {
        if (!context.IsCurrentGeneration)
            return new(PanelEntryAction.Ignore, "stale_generation");
        if (context.IsBlocked)
            return new(PanelEntryAction.Ignore, "busy_or_modal");
        if (key == KeyboardEntryAction.None)
            return new(PanelEntryAction.Ignore, "unsupported_key");

        if (key == KeyboardEntryAction.RightAlt)
        {
            // Right Alt inside the panel belongs entirely to Doubao.
            if (context.IsForeground)
                return new(PanelEntryAction.Ignore, "right_alt_left_to_doubao");
            if (!context.IsUuForeground)
                return new(PanelEntryAction.Ignore, "not_uu_foreground");
            return context.IsOpen
                ? new(PanelEntryAction.Restore, "uu_existing_session")
                : new(PanelEntryAction.StartNew, "uu_new_session");
        }

        if (!context.IsOpen)
            return new(PanelEntryAction.StartNew, "closed_session");
        if (!context.IsVisible || !context.IsForeground)
            return new(PanelEntryAction.Restore, "existing_session_in_background");
        return new(PanelEntryAction.Finish, "panel_in_foreground");
    }
}
