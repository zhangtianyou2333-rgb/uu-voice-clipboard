import CoreGraphics
import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    if !condition() {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

func testCompleteCommandVSequence() {
    guard let events = DirectedPaster.makeCommandVEvents() else {
        fputs("FAIL: Could not create keyboard events\n", stderr)
        exit(1)
    }
    expect(events.count == 4, "Command-V requires four events")
    expect(events.map { $0.getIntegerValueField(.keyboardEventKeycode) } == [55, 9, 9, 55], "Unexpected key sequence")
    expect(events[1].type == .keyDown && events[2].type == .keyUp, "V must be pressed and released")
    expect(events.map { $0.flags.rawValue } == [CGEventFlags.maskCommand.rawValue, CGEventFlags.maskCommand.rawValue, CGEventFlags.maskCommand.rawValue, 0], "Unexpected modifier flags")
    print("PASS: complete Command-V sequence and modifier release")
}

func testAllEventsGoOnlyToCapturedProcess() {
    var recipients: [pid_t] = []
    var keys: [Int64] = []
    var delays: [TimeInterval] = []
    let result = DirectedPaster.postCommandV(
        to: 1234,
        post: { event, pid in
            recipients.append(pid)
            keys.append(event.getIntegerValueField(.keyboardEventKeycode))
        },
        pause: { delays.append($0) }
    )
    expect(result, "Valid PID should construct a paste sequence")
    expect(recipients == [1234, 1234, 1234, 1234], "Events must target only the captured PID")
    expect(keys == [55, 9, 9, 55], "Posting must preserve event order")
    expect(delays == [0.025, 0.025, 0.025], "Posting must preserve key spacing")
    print("PASS: directed recipient, event order, and spacing")
}

func testInvalidProcessDoesNotSendInput() {
    for pid: pid_t in [-1, 0] {
        var posts = 0
        var pauses = 0
        let result = DirectedPaster.postCommandV(
            to: pid,
            post: { _, _ in posts += 1 },
            pause: { _ in pauses += 1 }
        )
        expect(!result, "Invalid PID must fail")
        expect(posts == 0 && pauses == 0, "Invalid PID must not post or pause")
    }
    print("PASS: invalid PIDs never send input")
}

func testPasteMenuSelection() {
    expect(MenuPaster.isPlainPaste(title: "Paste", shortcut: "v", modifiers: 0, enabled: true), "Command-V menu should match")
    expect(MenuPaster.isPlainPaste(title: "粘贴", shortcut: nil, modifiers: nil, enabled: true), "Localized Paste should match")
    expect(!MenuPaster.isPlainPaste(title: "Paste and Match Style", shortcut: "v", modifiers: 3, enabled: true), "Shift-Option paste must not match")
    expect(!MenuPaster.isPlainPaste(title: "Paste", shortcut: "v", modifiers: 8, enabled: true), "No-Command V must not match")
    expect(!MenuPaster.isPlainPaste(title: "Paste", shortcut: "v", modifiers: nil, enabled: true), "Unknown shortcut modifiers must not match")
    expect(!MenuPaster.isPlainPaste(title: "Paste", shortcut: "v", modifiers: 0, enabled: false), "Disabled Paste must not match")
    expect(!MenuPaster.isPlainPaste(title: "Paste Special", shortcut: nil, modifiers: nil, enabled: true), "Other menu actions must not match")
    print("PASS: exact Paste menu selection and modifier safety")
}

func makePolicy() -> FocusReturnPolicy {
    FocusReturnPolicy(targetPID: 123, deadline: 12, inputBaseline: [1, 2, 3, 4])
}

func testOneTimeFocusReturn() {
    var policy = makePolicy()
    expect(!policy.shouldRestore(currentPID: 123, currentIsUU: false, inputCounters: [1, 2, 3, 4], now: 10, allowed: true), "Original target does not need restoring")
    expect(policy.shouldRestore(currentPID: 456, currentIsUU: true, inputCounters: [1, 2, 3, 4], now: 10.5, allowed: true), "UU stealing foreground can qualify once")
    expect(!policy.shouldRestore(currentPID: 456, currentIsUU: true, inputCounters: [1, 2, 3, 4], now: 11, allowed: true), "Never restore more than once")
    print("PASS: focus return is UU-only and at most once")
}

func testFocusReturnCancellation() {
    var clicked = makePolicy()
    expect(!clicked.shouldRestore(currentPID: 456, currentIsUU: true, inputCounters: [1, 3, 3, 4], now: 10, allowed: true), "New input cancels restoration")
    expect(clicked.cancelled, "New input must cancel the whole lease")
    var switched = makePolicy()
    expect(!switched.shouldRestore(currentPID: 789, currentIsUU: false, inputCounters: [1, 2, 3, 4], now: 10, allowed: true), "Other apps must never be displaced")
    expect(!switched.shouldRestore(currentPID: 456, currentIsUU: true, inputCounters: [1, 2, 3, 4], now: 11, allowed: true), "Do not restore after a manual app switch")
    var expired = makePolicy()
    expect(!expired.shouldRestore(currentPID: 456, currentIsUU: true, inputCounters: [1, 2, 3, 4], now: 12, allowed: true), "Deadline must cancel restoration")
    var disabled = makePolicy()
    expect(!disabled.shouldRestore(currentPID: 456, currentIsUU: true, inputCounters: [1, 2, 3, 4], now: 10, allowed: false), "Pause or clipboard change must cancel restoration")
    print("PASS: input, app switch, timeout, pause, and clipboard changes cancel focus return")
}

func testBoundedMetadataDiagnostics() {
    var now: TimeInterval = 100
    let report = BridgeDiagnostics(capacity: 3, clock: { now })
    report.begin(mode: "observe", appID: "test.codex", pid: 123, clipboardCount: 7)
    expect(report.isWatching, "Tagged input opens a diagnostic window")
    now = 100.1
    report.record("clipboard_cleaned")
    report.record("menu_paste_accepted")
    report.record("foreground_activated", appID: "com.netease.uuremote", pid: 456)
    let snapshot = report.snapshot()
    expect(!snapshot.contains("tag_received"), "Report must be bounded")
    expect(snapshot.contains("+100ms foreground_activated app=com.netease.uuremote pid=456"), "Report must preserve stage timing")
    now = 102
    expect(!report.isWatching, "Diagnostics must not watch app changes indefinitely")
    print("PASS: bounded, timed, metadata-only diagnostic buffer")
}

func testActivationWaitOnlyWhenNeeded() {
    var policy = makePolicy()
    expect(policy.activationDelay(now: 10) == 0, "No activation means no fixed paste wait")
    expect(!policy.shouldRestore(currentPID: 123, currentIsUU: false, inputCounters: [1, 2, 3, 4], now: 10, allowed: true), "Original app remains foreground")
    expect(policy.activationDelay(now: 10.1) == 0, "Original foreground app stays on the fast path")
    expect(policy.shouldRestore(currentPID: 456, currentIsUU: true, inputCounters: [1, 2, 3, 4], now: 10.5, allowed: true), "UU restoration still allowed once")
    expect(abs(policy.activationDelay(now: 10.5) - 0.08) < 0.000001, "Actual activation keeps full 80 ms settling")
    expect(abs(policy.activationDelay(now: 10.53) - 0.05) < 0.000001, "Only remaining activation time is waited")
    expect(policy.activationDelay(now: 10.59) == 0, "Already settled activation does not add another wait")
    expect(!policy.shouldRestore(currentPID: 456, currentIsUU: true, inputCounters: [1, 2, 3, 4], now: 10.6, allowed: true), "Timing optimization does not allow a second restore")
    print("PASS: no fixed paste delay without activation; actual activation still settles")
}

// Every post is intercepted by a fake sink; these checks never send real keys.
testCompleteCommandVSequence()
testAllEventsGoOnlyToCapturedProcess()
testInvalidProcessDoesNotSendInput()
testPasteMenuSelection()
testOneTimeFocusReturn()
testFocusReturnCancellation()
testBoundedMetadataDiagnostics()
testActivationWaitOnlyWhenNeeded()
