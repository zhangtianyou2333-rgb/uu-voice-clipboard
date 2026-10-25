using UUVoiceClipboard.Native;

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

var keys = new KeyboardEntryPolicy();
Check(keys.Handle(0xA5, true, false) == default, "right Alt down is passed through");
Check(keys.Handle(0xA5, true, false) == default, "right Alt repeat is passed through");
var tap = keys.Handle(0xA5, false, false);
Check(tap.Action == KeyboardEntryAction.RightAlt && !tap.Suppress, "one physical tap opens without consuming");
Check(keys.Handle(0xA5, false, false).Action == KeyboardEntryAction.None, "duplicate up never opens");
Console.WriteLine("PASS right Alt single tap / repeat / pass-through");

keys.Handle(0xA5, true, false);
keys.Handle(0x55, true, false);
keys.Handle(0x55, false, false);
Check(keys.Handle(0xA5, false, false).Action == KeyboardEntryAction.None, "Alt+U is not a standalone tap");
keys.Handle(0xA5, true, false, otherKeyAlreadyDown: true);
Check(keys.Handle(0xA5, false, false).Action == KeyboardEntryAction.None, "existing modifier/AltGr does not open");
keys.Handle(0xA4, true, false);
Check(keys.Handle(0xA4, false, false).Action == KeyboardEntryAction.None, "left Alt does not open");
Check(keys.Handle(0xA5, true, true) == default && keys.Handle(0xA5, false, true) == default, "injected Alt ignored");
Console.WriteLine("PASS combinations / AltGr / left Alt / injected keys");

var f12 = keys.Handle(0x7B, true, false);
Check(f12.Action == KeyboardEntryAction.F12 && f12.Suppress, "F12 local fallback");
Check(keys.Handle(0x7B, true, false) == new KeyboardEntryDecision(KeyboardEntryAction.None, true), "F12 repeat consumed");
Check(keys.Handle(0x7B, false, false).Suppress, "F12 up consumed");
Check(keys.Handle(0x7B, true, true) == default, "injected F12 not consumed");
Console.WriteLine("PASS F12 fallback and injected pass-through");

var windows = new VoiceWindowPolicy();
var reused = (IntPtr)10;
windows.Reset(new[] { reused });
Check(!windows.Observe(new() { reused }), "startup-visible window is baseline");
Check(windows.Observe(new() { reused, (IntPtr)11 }), "new window starts speech");
windows.Reset(new[] { reused });
windows.Hidden(reused);
Check(windows.IsTransient(reused), "hide/show event permits reused HWND");
windows.Reset(new[] { reused });
Check(!windows.Observe(new()), "hide alone does not open");
Check(windows.Observe(new() { reused }), "poll-only reuse works when event hook unavailable");
Console.WriteLine("PASS window baseline / reuse / event and polling fallback");

Check(!VoiceWindowPolicy.ShouldFinalize(true, true, 1000, 0, 1899), "no finish below 900 ms");
Check(!VoiceWindowPolicy.ShouldFinalize(true, true, 1000, 0, 4499), "missing voice window keeps 3.5 s fallback");
Check(VoiceWindowPolicy.ShouldFinalize(true, true, 1000, 0, 4500), "fallback at 3.5 s");
Check(!VoiceWindowPolicy.ShouldFinalize(true, true, 1000, 2490, 3000), "active voice window blocks short pause");
Check(VoiceWindowPolicy.ShouldFinalize(true, true, 1000, 1499, 3000), "quiet voice window and stable text finish");
Check(!VoiceWindowPolicy.ShouldFinalize(false, true, 1000, 0, 5000), "empty text not copied");
Check(!VoiceWindowPolicy.ShouldFinalize(true, false, 1000, 0, 5000), "closed session not copied");
Check(!VoiceWindowPolicy.ShouldFinalize(true, true, 0, 0, 5000), "new session not copied");
Check(!VoiceWindowPolicy.ShouldFinalize(true, true, 1000, 0, 1549, true), "explicit stop still waits for final text");
Check(VoiceWindowPolicy.ShouldFinalize(true, true, 1000, 0, 1550, true), "explicit stop shortens only the safe final settle");
Check(!VoiceWindowPolicy.ShouldFinalize(true, true, 1000, 0, 1549), "without explicit stop the fallback remains unchanged");
Check(!VoiceWindowPolicy.ShouldFinalize(true, true, 1000, 0, 5000, compositionActive: true), "3.5 s fallback never copies during IME composition");
Check(!VoiceWindowPolicy.ShouldFinalize(true, true, 1000, 0, 5000, true, true), "explicit end never copies uncommitted composition");
Console.WriteLine("PASS unchanged end-of-speech safety thresholds");

var reads = 0;
var name = "ImeService";
var cache = new WindowProcessCache(pid => { reads++; return pid == 1 ? name : "other"; });
Check(cache.IsDoubao(1, 100) && cache.IsDoubao(1, 180), "cache same PID across scans");
Check(reads == 1, "one process-name query, not one per window/tick");
Check(!cache.IsDoubao(2, 200) && !cache.IsDoubao(2, 250) && reads == 2, "negative results cached too");
name = "other";
Check(!cache.IsDoubao(1, 2100) && reads == 3, "PID/name cache expires");
Check(!cache.IsDoubao(0, 2200) && reads == 3, "invalid PID not queried");
Console.WriteLine("PASS bounded process-name cache and expiry");

var f12Only = new KeyboardEntryPolicy(observeRightAlt: false);
Check(f12Only.Handle(0xA5, true, false) == default, "recovery mode ignores right Alt down");
Check(f12Only.Handle(0xA5, false, false) == default, "recovery mode ignores right Alt up");
f12Only.Handle(0xA5, true, false);
f12Only.Handle(0x55, true, false);
Check(f12Only.Handle(0xA5, false, false) == default, "recovery mode ignores Alt combinations");
Check(f12Only.Handle(0x7B, true, false).Action == KeyboardEntryAction.F12, "F12 fallback works with Alt observation disabled");
Check(f12Only.Handle(0x7B, false, false).Suppress, "F12 release still consumed by fallback");
Console.WriteLine("PASS F12-only recovery / right Alt untouched");

var panel = new PanelSessionPolicy();
Check(!panel.Accepts(panel.Generation), "hidden initial panel cannot accept completion");
panel.Begin();
var firstGeneration = panel.Generation;
Check(panel.Accepts(firstGeneration), "current input accepts its own completion");
panel.Close();
Check(!panel.IsOpen && !panel.Accepts(firstGeneration), "close rejects pending relay completion");
panel.Close();
Check(!panel.IsOpen && !panel.Accepts(firstGeneration), "repeated close stays closed");
panel.Begin();
Check(panel.IsOpen && !panel.Accepts(firstGeneration), "new F12 input rejects old callbacks");
Check(panel.Accepts(panel.Generation), "new session accepts only its own callbacks");
Console.WriteLine("PASS close invalidation / late completion / new input generation");

var diagnostics = new NativeDiagnostics(capacity: 3);
diagnostics.Begin("test");
diagnostics.Record("first_text");
diagnostics.Record("panel_close_requested");
diagnostics.Record("relay_cancelled");
var report = diagnostics.Snapshot("raw_input=false right_alt_listener=none entry_mode=f12_compat");
Check(report.Contains("V1.12.0") && report.Contains("entry_mode=f12_compat"), "current version and compatibility entry state in report");
Check(report.Split(Environment.NewLine).Count(line => line.StartsWith("+")) == 3, "diagnostic capacity remains bounded");
Check(report.Contains("panel_close_requested") && report.Contains("relay_cancelled"), "close and cancel preserved");
diagnostics.Begin("next_test");
Check(!diagnostics.Snapshot("test").Contains("relay_cancelled"), "new input resets old timeline");
Console.WriteLine("PASS bounded metadata diagnostics / reset");
var finish = new FinishRequestPolicy();
Check(!finish.ShouldFinish(true, false, 1000, 1000), "automatic input never gains an explicit finish request");
finish.Request(composing: false);
Check(finish.ShouldFinish(true, false, 1000, 1000), "explicit F12/button finishes already committed text without 3.5s delay");
Check(!finish.ShouldFinish(false, false, 1000, 5000), "empty input cannot finish");
Check(!finish.ShouldFinish(true, true, 1000, 5000), "even explicit finish cannot copy uncommitted text");
finish.Reset();
Check(!finish.Pending && !finish.ShouldFinish(true, false, 1000, 5000), "close/clear resets explicit finish");
finish.Request(composing: false);
Check(finish.ShouldFinish(true, false, 1000, 5000), "one explicit attempt is eligible");
finish.Reset();
Check(!finish.ShouldFinish(true, false, 1000, 10000), "consumed attempt cannot retry from timer after copy-only or failure");
Console.WriteLine("PASS explicit finish / committed input / empty input / reset");

finish.Request(composing: true);
Check(!finish.ShouldFinish(true, true, 1000, 9000), "pending finish waits for actual IME end indefinitely");
Check(!finish.ShouldFinish(true, false, 1000, 9000), "missing composition end cannot bypass commit safety");
finish.CompositionChanged(composing: false, now: 10000);
Check(!finish.ShouldFinish(true, false, 10000, 10549), "IME end requires final 550ms settle");
Check(finish.ShouldFinish(true, false, 10000, 10550), "pending finish runs after stable final commit");
Check(!finish.ShouldFinish(true, false, 10400, 10550), "late final text extends the settle window");
Check(finish.ShouldFinish(true, false, 10400, 10950), "late final text finishes after its own settle window");
finish.Request(composing: false);
Check(!finish.ShouldFinish(true, false, 10400, 10550), "repeat F12 cannot bypass pending final settle");
finish.CompositionChanged(composing: true, now: 10550);
Check(!finish.Pending, "resumed new IME phrase cancels earlier finish request");
Check(!finish.ShouldFinish(true, false, 11000, 15000), "cancelled finish cannot send a later phrase");
Console.WriteLine("PASS pending IME finish / late text / repeated request / resumed speech cancellation");

var composition = new CompositionStatePolicy();
var scrollGeneration = composition.Generation;
Check(composition.AcceptsScroll(scrollGeneration), "committed text may scroll");
composition.Begin();
Check(!composition.AcceptsScroll(scrollGeneration), "starting IME invalidates queued scroll");
scrollGeneration = composition.Generation;
Check(!composition.AcceptsScroll(scrollGeneration), "active IME never permits custom scroll");
composition.End();
Check(composition.AcceptsScroll(scrollGeneration), "IME end permits committed-text scroll");
composition.Reset();
Check(!composition.AcceptsScroll(scrollGeneration), "clear/close invalidates old scroll callbacks");
composition.Begin();
scrollGeneration = composition.Generation;
composition.End();
composition.Begin();
composition.End();
Check(!composition.AcceptsScroll(scrollGeneration), "next composition cannot receive previous scroll callback");
Console.WriteLine("PASS composition scroll generation / active-IME exclusion / close / next phrase");

// Reproduce the field report: open manually, switch to UU without closing,
// then press F12. The visible-but-background panel must never finalize.
var foregroundPanel = new PanelEntryContext(true, true, true, false, true, false);
var behindUu = foregroundPanel with { IsForeground = false, IsUuForeground = true };
var closedPanel = behindUu with { IsOpen = false, IsVisible = false };
Check(PanelEntryPolicy.Decide(KeyboardEntryAction.F12, behindUu).Action == PanelEntryAction.Restore,
    "visible panel behind UU is restored, never finished or restarted");
Check(PanelEntryPolicy.Decide(KeyboardEntryAction.F12, behindUu with { IsUuForeground = false }).Action == PanelEntryAction.Restore,
    "F12 can restore an existing panel behind another app");
Check(PanelEntryPolicy.Decide(KeyboardEntryAction.F12, foregroundPanel with { IsVisible = false }).Action == PanelEntryAction.Restore,
    "hidden/minimized open session is restored, not cleared");
Check(PanelEntryPolicy.Decide(KeyboardEntryAction.F12, foregroundPanel).Action == PanelEntryAction.Finish,
    "F12 finishes only while the open panel is in foreground");
Check(PanelEntryPolicy.Decide(KeyboardEntryAction.F12, closedPanel).Action == PanelEntryAction.StartNew,
    "after close, the next F12 starts a new session");
Console.WriteLine("PASS F12 background restoration / foreground completion / closed session");

Check(PanelEntryPolicy.Decide(KeyboardEntryAction.RightAlt, closedPanel).Action == PanelEntryAction.StartNew,
    "right Alt starts a hidden session only from actual UU foreground");
Check(PanelEntryPolicy.Decide(KeyboardEntryAction.RightAlt, behindUu).Action == PanelEntryAction.Restore,
    "right Alt restores an open background panel without restarting it");
Check(PanelEntryPolicy.Decide(KeyboardEntryAction.RightAlt, foregroundPanel).Action == PanelEntryAction.Ignore,
    "right Alt inside the panel is left entirely to Doubao");
Check(PanelEntryPolicy.Decide(KeyboardEntryAction.RightAlt, behindUu with { IsUuForeground = false }).Action == PanelEntryAction.Ignore,
    "a cached UU target cannot authorize entry from another app");
Check(PanelEntryPolicy.Decide(KeyboardEntryAction.None, behindUu).Action == PanelEntryAction.Ignore,
    "non-entry keys cannot restore or finish the panel");
Console.WriteLine("PASS right Alt UU gating / background restoration / Doubao exclusion");

var entrySession = new PanelSessionPolicy();
var queuedBeforeOpen = entrySession.Generation;
entrySession.Begin();
Check(PanelEntryPolicy.Decide(KeyboardEntryAction.F12, foregroundPanel with {
    IsCurrentGeneration = queuedBeforeOpen == entrySession.Generation }).Action == PanelEntryAction.Ignore,
    "a second queued callback from the opening gesture cannot finish the new session");
var queuedBeforeClose = entrySession.Generation;
entrySession.Close();
foreach (var key in new[] { KeyboardEntryAction.F12, KeyboardEntryAction.RightAlt })
{
    Check(PanelEntryPolicy.Decide(key, closedPanel with {
        IsCurrentGeneration = queuedBeforeClose == entrySession.Generation }).Action == PanelEntryAction.Ignore,
        "close invalidates queued entry and prevents reopening");
    Check(PanelEntryPolicy.Decide(key, behindUu with { IsBlocked = true }).Action == PanelEntryAction.Ignore,
        "transfer or modal dialog blocks background restoration");
    Check(PanelEntryPolicy.Decide(key, closedPanel with { IsBlocked = true }).Action == PanelEntryAction.Ignore,
        "transfer or modal dialog blocks a new session");
    Check(PanelEntryPolicy.Decide(key, foregroundPanel with { IsBlocked = true }).Action == PanelEntryAction.Ignore,
        "modal dialog blocks foreground completion");
}
Console.WriteLine("PASS entry generation / duplicate opening callbacks / close / busy and modal guards");
Check(EditorKeyMetadata.RightAltEdge(0x104, 0x12, 1L << 24) == "down", "extended generic Alt is right Alt");
Check(EditorKeyMetadata.RightAltEdge(0x105, 0xA5, 0) == "up", "explicit right Alt release is observed");
Check(EditorKeyMetadata.RightAltEdge(0x100, 0xA5, 0) == "down", "ordinary right Alt down is observed");
Check(EditorKeyMetadata.RightAltEdge(0x101, 0x12, 1L << 24) == "up", "ordinary extended Alt up is observed");
Check(EditorKeyMetadata.RightAltEdge(0x104, 0x12, 0) is null, "left Alt is not recorded");
Check(EditorKeyMetadata.RightAltEdge(0x100, 0x55, 1L << 24) is null, "letter keys are never recorded");
Check(EditorKeyMetadata.RightAltEdge(0x102, 0xA5, 1L << 24) is null, "character messages are never recorded");
Check(EditorKeyMetadata.RightAltEdge(0x10F, 0xA5, 1L << 24) is null, "composition messages are never treated as keys");
Console.WriteLine("PASS right Alt editor metadata / left Alt, text and other keys excluded");
Check(EditorViewportPolicy.LinesToExposeCaret(180, 200, 20) == 0, "fully visible final line never causes scrolling");
Check(EditorViewportPolicy.LinesToExposeCaret(181, 200, 20) == 1, "partially clipped final line becomes visible");
Check(EditorViewportPolicy.LinesToExposeCaret(221, 200, 20) == 3, "multiline preedit below viewport requests enough lines");
Check(EditorViewportPolicy.LinesToExposeCaret(-1, 200, 20) == -1, "caret above viewport scrolls in the opposite direction");
Check(EditorViewportPolicy.LinesToExposeCaret(210, 200, 20) == EditorViewportPolicy.LinesToExposeCaret(315, 300, 30), "caret follow is consistent at 150 percent DPI");
Check(EditorViewportPolicy.LinesToExposeCaret(200, 0, 20) == 0, "zero height during layout never scrolls");
Check(EditorViewportPolicy.AcceptFollow(7, 7, true, true, true), "current focused visible editor accepts queued preedit follow");
Check(!EditorViewportPolicy.AcceptFollow(7, 8, true, true, true), "clear or next phrase rejects stale follow");
Check(!EditorViewportPolicy.AcceptFollow(7, 7, false, true, true), "switching input focus rejects follow");
Check(!EditorViewportPolicy.AcceptFollow(7, 7, true, false, true), "closed panel rejects follow");
Check(!EditorViewportPolicy.AcceptFollow(7, 7, true, true, false), "disposed handle rejects follow");
Console.WriteLine("PASS preedit caret visibility / DPI / stale generation / focus and close guards");
var shortViewport = new EditorViewport(3, 8, 0);
Check(!shortViewport.Scrollable && shortViewport.ThumbFraction == 1, "short input has no scroll thumb");
var longViewport = new EditorViewport(100, 10, 50);
Check(longViewport.Scrollable && longViewport.Maximum == 90 && longViewport.Position == 50, "long input exposes native scroll range");
Check(longViewport.LineFromFraction(-1) == 0 && longViewport.LineFromFraction(2) == 90, "dragging outside track clamps to valid lines");
Check(new EditorViewport(5, 10, 50).Position == 0, "clearing long input removes stale scroll position");
Console.WriteLine("PASS thin scrollbar short/long text / drag limits / shrinking content");
var compositionArea = ImeCompositionLayoutPolicy.Bound(450, 260, 20, 650, 520)!.Value;
Check(compositionArea.Area == new System.Drawing.Rectangle(0, 0, 450, 260), "legacy IME area uses editor bounds, not screen or form bounds");
Check(compositionArea.Anchor == new System.Drawing.Point(449, 240), "out-of-bounds preedit anchor is kept inside the editor");
var scaledArea = ImeCompositionLayoutPolicy.Bound(675, 390, 30, 300, 600)!.Value;
Check(scaledArea.Anchor == new System.Drawing.Point(300, 360), "composition boundary is consistent at 150 percent DPI");
Check(ImeCompositionLayoutPolicy.Bound(450, 260, 20, -8, -5)!.Value.Anchor == System.Drawing.Point.Empty, "negative caret coordinates never escape the top-left edge");
Check(ImeCompositionLayoutPolicy.Bound(450, 5, 20, 0, 100)!.Value.Anchor.Y == 0, "very small layout keeps the first line inside the editor");
Check(ImeCompositionLayoutPolicy.Bound(0, 260, 20, 0, 0) is null
    && ImeCompositionLayoutPolicy.Bound(450, 0, 20, 0, 0) is null, "collapsed or uncreated editor never positions an IME area");
Console.WriteLine("PASS composition area / editor bounds / DPI / out-of-bounds caret / collapsed layout");
var browserMessages = new WebEditorMessagePolicy();
browserMessages.Reset();
Check(browserMessages.Accept(1, 1), "first browser input belongs to active epoch");
Check(!browserMessages.Accept(1, 1) && !browserMessages.Accept(1, 0), "duplicate and reordered browser input rejected");
Check(browserMessages.Accept(1, 20), "coalesced browser sequence may skip numbers");
browserMessages.Reset();
Check(!browserMessages.Accept(1, 21), "late text or compositionend from closed session rejected");
Check(!browserMessages.Accept(3, 1), "future epoch cannot authorize input");
Check(browserMessages.Accept(2, 1), "new session starts with fresh sequence");
Console.WriteLine("PASS browser epoch / close invalidation / out-of-order / new session");
var longSpeechDiagnostics = new NativeDiagnostics();
longSpeechDiagnostics.Begin("long_speech");
longSpeechDiagnostics.Record("ime_composition_begin");
for (var i = 0; i < 500; i++) longSpeechDiagnostics.Record("text_changed");
longSpeechDiagnostics.Record("ime_composition_end");
var longSpeechReport = longSpeechDiagnostics.Snapshot("test");
Check(longSpeechReport.Contains("session_begin") && longSpeechReport.Contains("ime_composition_begin")
    && longSpeechReport.Contains("ime_composition_end") && longSpeechReport.Contains("text_updates=500"),
    "frequent preedit updates preserve composition evidence without storing text");
longSpeechDiagnostics.Begin("new_speech");
Check(longSpeechDiagnostics.Snapshot("test").Contains("text_updates=0"), "text counters reset between sessions");
Console.WriteLine("PASS long speech diagnostic sampling / composition evidence / reset");
Check(WebEditorDocumentPolicy.IsEditor("https://uu-voice-editor.invalid/editor.html"), "embedded document URI is accepted");
foreach (var untrusted in new[] { "about:blank", "https://uu-voice-editor.invalid/", "https://uu-voice-editor.invalid/editor.html?other=1", "https://uu-voice-editor.invalid.evil/editor.html", "http://uu-voice-editor.invalid/editor.html", "file:///editor.html" })
    Check(!WebEditorDocumentPolicy.IsEditor(untrusted), "other document/network origins are refused");
Console.WriteLine("PASS in-memory document URI / navigation and message origin allowlist");
Console.WriteLine("All twenty-two policy groups passed. No OS hooks, clipboard or UI actions executed.");
