import AppKit
import ApplicationServices
import CoreGraphics
import ServiceManagement

private enum BridgeMarker {
    static let start = "\u{2063}\u{200B}\u{200C}\u{200D}"
    static let end = "\u{2060}\u{200D}\u{200C}\u{200B}"

    static func decode(_ payload: String) -> String? {
        guard payload.hasSuffix(end) else { return nil }
        let withoutEnd = String(payload.dropLast(end.count))
        guard withoutEnd.hasSuffix(start) else { return nil }
        let text = String(withoutEnd.dropLast(start.count))
        return text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : text
    }
}

private enum PasteMethod: Int, CaseIterable {
    case applicationMenu
    case directedKeys
    case observeOnly

    var title: String {
        switch self {
        case .applicationMenu: return "应用菜单粘贴（不模拟按键）"
        case .directedKeys: return "定向按键粘贴（V1.0.1 兼容）"
        case .observeOnly: return "仅接收诊断（不改剪贴板、不粘贴）"
        }
    }

    var code: String {
        switch self {
        case .applicationMenu: return "menu"
        case .directedKeys: return "keys"
        case .observeOnly: return "observe"
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    private let pasteboard = NSPasteboard.general
    private var lastChangeCount: Int
    private var timer: Timer?
    private var statusItem: NSStatusItem!
    private var enabled = true
    private var busy = false
    private var lastStatus = "等待 Windows UU 剪贴板"
    private var pasteMethod = PasteMethod.applicationMenu
    private let diagnostics = BridgeDiagnostics()
    private var session = 0
    private var recentTarget: FocusSnapshot?
    private var focusReturn: FocusReturnPolicy?
    private var relayTarget: FocusSnapshot?
    private var expectedClipboard: String?
    private var expectedClipboardCount = 0
    private var relayMethod = PasteMethod.applicationMenu
    private var restoreOriginal = true
    private var lastUUActivationAt: TimeInterval?

    override init() {
        lastChangeCount = pasteboard.changeCount
        super.init()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        buildStatusItem()
        NSWorkspace.shared.notificationCenter.addObserver(
            self,
            selector: #selector(foregroundChanged(_:)),
            name: NSWorkspace.didActivateApplicationNotification,
            object: nil
        )
        timer = Timer.scheduledTimer(
            timeInterval: 0.12,
            target: self,
            selector: #selector(pollClipboard),
            userInfo: nil,
            repeats: true
        )
        updateStatus("V1.0.3 · 菜单粘贴 · 等待带标记文字")
    }

    func applicationWillTerminate(_ notification: Notification) {
        timer?.invalidate()
        NSWorkspace.shared.notificationCenter.removeObserver(self)
    }

    private func buildStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.title = "UU"

        let menu = NSMenu()
        let status = NSMenuItem(title: lastStatus, action: nil, keyEquivalent: "")
        status.isEnabled = false
        status.tag = 10
        menu.addItem(status)
        menu.addItem(.separator())

        let toggle = NSMenuItem(title: "启用自动粘贴", action: #selector(toggleEnabled(_:)), keyEquivalent: "")
        toggle.target = self
        toggle.state = .on
        toggle.tag = 11
        menu.addItem(toggle)

        let methodMenu = NSMenu()
        for method in PasteMethod.allCases {
            let item = NSMenuItem(title: method.title, action: #selector(selectPasteMethod(_:)), keyEquivalent: "")
            item.target = self
            item.tag = method.rawValue
            item.state = method == pasteMethod ? .on : .off
            methodMenu.addItem(item)
        }
        let methodItem = NSMenuItem(title: "粘贴方式", action: nil, keyEquivalent: "")
        methodItem.submenu = methodMenu
        methodItem.tag = 12
        menu.addItem(methodItem)

        let restore = NSMenuItem(title: "同步后恢复原应用（最多一次）", action: #selector(toggleRestoreOriginal(_:)), keyEquivalent: "")
        restore.target = self
        restore.state = .on
        menu.addItem(restore)

        let report = NSMenuItem(title: "复制诊断信息（不含输入文字）", action: #selector(copyDiagnostics), keyEquivalent: "")
        report.target = self
        menu.addItem(report)

        let permission = NSMenuItem(title: "打开辅助功能设置", action: #selector(openAccessibilitySettings), keyEquivalent: "")
        permission.target = self
        menu.addItem(permission)

        let login = NSMenuItem(title: "设置为开机启动", action: #selector(registerLoginItem), keyEquivalent: "")
        login.target = self
        menu.addItem(login)

        menu.addItem(.separator())
        let quit = NSMenuItem(title: "退出助手", action: #selector(quit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)
        statusItem.menu = menu
    }

    @objc private func pollClipboard() {
        observeTarget()
        restoreTargetIfNeeded()
        guard enabled, !busy else { return }
        let currentChangeCount = pasteboard.changeCount
        guard currentChangeCount != lastChangeCount else { return }
        lastChangeCount = currentChangeCount

        guard let payload = pasteboard.string(forType: .string),
              let text = BridgeMarker.decode(payload) else {
            return
        }

        let foreground = NSWorkspace.shared.frontmostApplication
        let method = pasteMethod
        session += 1
        let currentSession = session
        diagnostics.begin(mode: method.code, appID: foreground?.bundleIdentifier, pid: foreground?.processIdentifier, clipboardCount: currentChangeCount)
        if let lastUUActivationAt {
            let age = Int((ProcessInfo.processInfo.systemUptime - lastUUActivationAt) * 1000)
            diagnostics.record("last_uu_activation_before_tag_ms=\(age)")
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 2.0) { [weak self] in
            guard let self, self.session == currentSession else { return }
            self.recordStage("observation_end")
        }
        if method == .observeOnly {
            updateStatus("V1.0.3 · 已收到标记，只诊断，未改剪贴板或粘贴")
            return
        }

        let snapshot: FocusSnapshot?
        if let foreground, !FocusSnapshot.isUU(foreground),
           foreground.processIdentifier != ProcessInfo.processInfo.processIdentifier {
            snapshot = FocusSnapshot.capture(foreground)
        } else if FocusSnapshot.isUU(foreground), let recentTarget,
                  ProcessInfo.processInfo.systemUptime - recentTarget.observedAt < 2.0,
                  recentTarget.inputCounters == FocusSnapshot.counters() {
            snapshot = recentTarget
        } else {
            snapshot = nil
        }
        guard let snapshot, !snapshot.application.isTerminated else {
            recordStage("target_unavailable")
            updateStatus("未找到安全的原输入应用，已跳过；可复制诊断")
            return
        }

        busy = true
        relayTarget = snapshot
        relayMethod = method
        expectedClipboard = payload
        expectedClipboardCount = currentChangeCount
        focusReturn = FocusReturnPolicy(
            targetPID: snapshot.application.processIdentifier,
            deadline: ProcessInfo.processInfo.systemUptime + 2.0,
            inputBaseline: snapshot.inputCounters
        )
        diagnostics.record("target_captured", appID: snapshot.application.bundleIdentifier, pid: snapshot.application.processIdentifier)
        restoreTargetIfNeeded()
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.08) { [weak self] in
            guard let self else { return }
            guard self.session == currentSession, self.enabled, self.pasteMethod == method,
                  self.pasteboard.changeCount == currentChangeCount,
                  self.pasteboard.string(forType: .string) == payload else {
                self.busy = false
                self.focusReturn = nil
                self.recordStage("changed_before_relay")
                return
            }
            self.relay(text: text, payload: payload, changeCount: currentChangeCount, method: method, target: snapshot.application)
        }
    }

    private func relay(text: String, payload: String, changeCount: Int, method: PasteMethod, target: NSRunningApplication) {
        guard AXIsProcessTrusted() else {
            busy = false
            recordStage("accessibility_denied")
            updateStatus("需要辅助功能权限，未执行粘贴")
            return
        }
        var preparedMenu: MenuPaster.PreparedPaste?
        var preparationFailure: MenuPaster.Failure?
        if method == .applicationMenu {
            switch MenuPaster.prepare(to: target.processIdentifier) {
            case .success(let prepared): preparedMenu = prepared
            case .failure(let error): preparationFailure = error
            }
        }
        // AX menu discovery may take time; never overwrite a newer clipboard.
        guard enabled, pasteMethod == method, !target.isTerminated,
              NSWorkspace.shared.frontmostApplication?.processIdentifier == target.processIdentifier,
              pasteboard.changeCount == changeCount,
              pasteboard.string(forType: .string) == payload else {
            busy = false
            recordStage("changed_during_menu_discovery")
            updateStatus("目标或剪贴板已变化，已取消粘贴")
            return
        }
        guard pasteboard.clearContents() != 0, pasteboard.setString(text, forType: .string) else {
            busy = false
            recordStage("clipboard_clean_failed")
            updateStatus("无法恢复干净剪贴板")
            return
        }
        lastChangeCount = pasteboard.changeCount
        expectedClipboard = text
        expectedClipboardCount = pasteboard.changeCount
        recordStage("clipboard_cleaned")
        if let preparationFailure {
            busy = false
            recordStage(preparationFailure.code)
            updateStatus(preparationFailure.message)
            return
        }
        updateStatus("已收到文字，准备本地粘贴")

        let cleanedChangeCount = pasteboard.changeCount
        restoreTargetIfNeeded()
        let activationDelay = focusReturn?.activationDelay(now: ProcessInfo.processInfo.systemUptime) ?? 0
        let complete: () -> Void = { [weak self] in
            self?.completeRelay(text: text, changeCount: cleanedChangeCount, method: method, target: target, preparedMenu: preparedMenu)
        }
        // A local clipboard write is synchronous. Only an actual app activation
        // needs settling; all target/input/clipboard checks still run before AXPress.
        if activationDelay > 0 {
            recordStage("paste_wait_activation_ms=\(Int(ceil(activationDelay * 1000)))")
            DispatchQueue.main.asyncAfter(deadline: .now() + activationDelay, execute: complete)
        } else {
            recordStage("paste_ready_no_activation_wait")
            DispatchQueue.main.async(execute: complete)
        }
    }

    private func completeRelay(text: String, changeCount: Int, method: PasteMethod, target: NSRunningApplication, preparedMenu: MenuPaster.PreparedPaste?) {
        defer { busy = false }
        guard AXIsProcessTrusted() else {
            recordStage("accessibility_denied")
            updateStatus("需要辅助功能权限，未执行粘贴")
            return
        }
        guard NSWorkspace.shared.frontmostApplication?.processIdentifier == target.processIdentifier, !target.isTerminated else {
            recordStage("target_changed_before_paste")
            updateStatus("目标应用已切换或退出，已取消粘贴")
            return
        }
        guard enabled, pasteMethod == method else {
            recordStage("mode_changed_before_paste")
            updateStatus("助手已暂停或方式已变化，已取消粘贴")
            return
        }
        guard pasteboard.changeCount == changeCount, pasteboard.string(forType: .string) == text else {
            recordStage("clipboard_changed_before_paste")
            updateStatus("剪贴板已变化，已取消粘贴")
            return
        }
        guard let relayTarget, relayTarget.inputCounters == FocusSnapshot.counters() else {
            recordStage("new_input_before_paste")
            updateStatus("检测到新的按键或点击，已取消自动粘贴")
            return
        }
        switch method {
        case .applicationMenu:
            guard let preparedMenu else { return }
            recordStage("menu_paste_begin")
            switch MenuPaster.perform(preparedMenu) {
            case .success:
                recordStage("menu_paste_accepted")
                updateStatus("V1.0.3 · 应用已接受菜单粘贴请求")
            case .failure(let error):
                recordStage(error.code)
                updateStatus(error.message)
            }
        case .directedKeys:
            recordStage("keys_paste_begin")
            guard DirectedPaster.postCommandV(to: target.processIdentifier) else {
                recordStage("keys_paste_failed")
                updateStatus("Command + V 发送失败")
                return
            }
            recordStage("keys_paste_posted")
            updateStatus("V1.0.3 · 已向原应用发送粘贴按键")
        case .observeOnly:
            break
        }
    }

    private func recordStage(_ stage: String) {
        let foreground = NSWorkspace.shared.frontmostApplication
        diagnostics.record(stage, appID: foreground?.bundleIdentifier, pid: foreground?.processIdentifier, clipboardCount: pasteboard.changeCount)
    }

    @objc private func foregroundChanged(_ notification: Notification) {
        let app = notification.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
        if FocusSnapshot.isUU(app) { lastUUActivationAt = ProcessInfo.processInfo.systemUptime }
        if diagnostics.isWatching {
            diagnostics.record("foreground_activated", appID: app?.bundleIdentifier, pid: app?.processIdentifier, clipboardCount: pasteboard.changeCount)
        }
        observeTarget()
    }

    private func observeTarget() {
        guard let app = NSWorkspace.shared.frontmostApplication,
              !FocusSnapshot.isUU(app), !app.isTerminated,
              app.processIdentifier != ProcessInfo.processInfo.processIdentifier else { return }
        if let recentTarget, recentTarget.application.processIdentifier == app.processIdentifier,
           recentTarget.inputCounters == FocusSnapshot.counters(),
           ProcessInfo.processInfo.systemUptime - recentTarget.observedAt < 0.5 { return }
        recentTarget = FocusSnapshot.capture(app)
    }

    private func restoreTargetIfNeeded() {
        guard var policy = focusReturn, let target = relayTarget else { return }
        let wasCancelled = policy.cancelled
        let app = NSWorkspace.shared.frontmostApplication
        let allowed = restoreOriginal && enabled && pasteMethod == relayMethod &&
            relayMethod != .observeOnly && !target.application.isTerminated && AXIsProcessTrusted() &&
            pasteboard.changeCount == expectedClipboardCount && pasteboard.string(forType: .string) == expectedClipboard
        let restore = policy.shouldRestore(
            currentPID: app?.processIdentifier,
            currentIsUU: FocusSnapshot.isUU(app),
            inputCounters: FocusSnapshot.counters(),
            now: ProcessInfo.processInfo.systemUptime,
            allowed: allowed
        )
        focusReturn = policy
        if policy.cancelled && !wasCancelled {
            recordStage("focus_return_cancelled_\(policy.cancellationReason ?? "unknown")")
        }
        guard restore else { return }
        recordStage("uu_foreground_restore_begin")
        let requested = target.requestRestore()
        recordStage(requested ? "original_window_restore_requested" : "original_window_restore_failed")
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.08) { [weak self] in
            guard let self else { return }
            let restored = NSWorkspace.shared.frontmostApplication?.processIdentifier == target.application.processIdentifier
            self.recordStage(restored ? "original_app_foreground_confirmed" : "original_app_restore_unconfirmed")
        }
    }

    @objc private func toggleRestoreOriginal(_ sender: NSMenuItem) {
        restoreOriginal.toggle()
        sender.state = restoreOriginal ? .on : .off
        if !restoreOriginal { focusReturn = nil }
        updateStatus(restoreOriginal ? "V1.0.3 · 本轮同步可恢复原应用一次" : "V1.0.3 · 已关闭原应用恢复")
    }

    @objc private func selectPasteMethod(_ sender: NSMenuItem) {
        guard let method = PasteMethod(rawValue: sender.tag) else { return }
        pasteMethod = method
        focusReturn = nil
        for item in statusItem.menu?.item(withTag: 12)?.submenu?.items ?? [] {
            item.state = item.tag == method.rawValue ? .on : .off
        }
        updateStatus("V1.0.3 · \(method.title)")
    }

    @objc private func copyDiagnostics() {
        focusReturn = nil
        let identifier = Bundle.main.bundleIdentifier ?? "unknown"
        let instances = NSRunningApplication.runningApplications(withBundleIdentifier: identifier).count
        let report = diagnostics.snapshot() + "\nmode=\(pasteMethod.code) enabled=\(enabled) restore_original=\(restoreOriginal) accessibility=\(AXIsProcessTrusted()) assistant_instances=\(instances)"
        guard pasteboard.clearContents() != 0, pasteboard.setString(report, forType: .string) else {
            updateStatus("诊断信息复制失败")
            return
        }
        lastChangeCount = pasteboard.changeCount
        updateStatus("V1.0.3 · 诊断信息已复制，不含输入文字")
    }

    @objc private func toggleEnabled(_ sender: NSMenuItem) {
        enabled.toggle()
        if !enabled { focusReturn = nil }
        sender.state = enabled ? .on : .off
        updateStatus(enabled ? "助手已启用，等待带标记文字" : "助手已暂停")
    }

    @objc private func openAccessibilitySettings() {
        let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!
        NSWorkspace.shared.open(url)
    }

    @objc private func registerLoginItem() {
        do {
            if #available(macOS 13.0, *) {
                try SMAppService.mainApp.register()
                updateStatus("已设置为开机启动")
            }
        } catch {
            updateStatus("开机启动设置失败：\(error.localizedDescription)")
        }
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }

    private func updateStatus(_ message: String) {
        lastStatus = message
        statusItem?.button?.toolTip = message
        if let status = statusItem?.menu?.item(withTag: 10) {
            status.title = message
        }
    }

}

let application = NSApplication.shared
let delegate = AppDelegate()
application.delegate = delegate
application.run()
