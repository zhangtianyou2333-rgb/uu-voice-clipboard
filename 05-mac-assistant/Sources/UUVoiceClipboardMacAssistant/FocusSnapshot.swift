import AppKit
import ApplicationServices
import CoreGraphics

struct FocusSnapshot {
    let application: NSRunningApplication
    let window: AXUIElement?
    let observedAt: TimeInterval
    let inputCounters: [UInt32]

    static func isUU(_ app: NSRunningApplication?) -> Bool {
        app?.bundleIdentifier == "com.netease.uuremote"
    }

    static func counters() -> [UInt32] {
        [CGEventType.keyDown, .leftMouseDown, .rightMouseDown, .otherMouseDown].map {
            CGEventSource.counterForEventType(.combinedSessionState, eventType: $0)
        }
    }

    static func capture(_ app: NSRunningApplication) -> FocusSnapshot {
        var window: AXUIElement?
        if AXIsProcessTrusted() {
            let application = AXUIElementCreateApplication(app.processIdentifier)
            AXUIElementSetMessagingTimeout(application, 0.08)
            var value: CFTypeRef?
            if AXUIElementCopyAttributeValue(application, kAXFocusedWindowAttribute as CFString, &value) == .success,
               let value, CFGetTypeID(value) == AXUIElementGetTypeID() {
                window = (value as! AXUIElement)
            }
        }
        return FocusSnapshot(application: app, window: window, observedAt: ProcessInfo.processInfo.systemUptime, inputCounters: counters())
    }

    func requestRestore() -> Bool {
        guard !application.isTerminated, !Self.isUU(application),
              application.processIdentifier != ProcessInfo.processInfo.processIdentifier,
              let window else { return false }
        AXUIElementSetMessagingTimeout(window, 0.08)
        var role: CFTypeRef?
        guard AXUIElementCopyAttributeValue(window, kAXRoleAttribute as CFString, &role) == .success,
              role as? String == kAXWindowRole as String else { return false }
        guard application.activate(options: []) else { return false }
        return AXUIElementPerformAction(window, kAXRaiseAction as CFString) == .success
    }
}
