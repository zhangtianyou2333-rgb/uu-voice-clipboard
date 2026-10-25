import ApplicationServices
import Foundation

enum MenuPaster {
    struct PreparedPaste {
        let application: AXUIElement
        let menuItem: AXUIElement
        let focusedElement: AXUIElement?
    }

    enum Failure: Error {
        case menuUnavailable
        case pasteUnavailable
        case searchTimeout
        case focusChanged
        case actionRejected(Int32)

        var code: String {
            switch self {
            case .menuUnavailable: return "menu_unavailable"
            case .pasteUnavailable: return "paste_unavailable"
            case .searchTimeout: return "menu_search_timeout"
            case .focusChanged: return "focused_element_changed"
            case .actionRejected(let value): return "menu_action_error_\(value)"
            }
        }

        var message: String {
            switch self {
            case .menuUnavailable: return "无法读取应用菜单，文字保留在剪贴板"
            case .pasteUnavailable: return "应用没有可用的粘贴菜单，文字保留在剪贴板"
            case .searchTimeout: return "读取粘贴菜单超时，未执行粘贴"
            case .focusChanged: return "应用内输入焦点已变化，已取消粘贴"
            case .actionRejected(let value): return "应用拒绝菜单粘贴（\(value)）"
            }
        }
    }

    static func isPlainPaste(title: String?, shortcut: String?, modifiers: Int?, enabled: Bool) -> Bool {
        guard enabled else { return false }
        if let shortcut, !shortcut.isEmpty {
            // AX modifiers use zero for Command-only, not CGEvent's maskCommand.
            return shortcut.lowercased() == "v" && modifiers == 0
        }
        guard modifiers == nil || modifiers == 0 else { return false }
        return ["Paste", "粘贴", "貼上", "贴上", "ペースト", "붙여넣기"].contains(title ?? "")
    }

    static func prepare(to pid: pid_t) -> Result<PreparedPaste, Failure> {
        guard pid > 0 else { return .failure(.menuUnavailable) }
        let application = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(application, 0.15)
        guard let menuBar = element(application, kAXMenuBarAttribute) else {
            return .failure(.menuUnavailable)
        }

        let deadline = ProcessInfo.processInfo.systemUptime + 0.75
        var remaining = 200
        var expired = false
        guard let menuItem = findPaste(in: menuBar, depth: 0, deadline: deadline, remaining: &remaining, expired: &expired) else {
            return .failure(expired ? .searchTimeout : .pasteUnavailable)
        }
        return .success(PreparedPaste(
            application: application,
            menuItem: menuItem,
            focusedElement: element(application, kAXFocusedUIElementAttribute)
        ))
    }

    static func perform(_ prepared: PreparedPaste) -> Result<Void, Failure> {
        if let originalFocus = prepared.focusedElement {
            guard let currentFocus = element(prepared.application, kAXFocusedUIElementAttribute),
                  CFEqual(originalFocus, currentFocus) else {
                return .failure(.focusChanged)
            }
        }
        let result = AXUIElementPerformAction(prepared.menuItem, kAXPressAction as CFString)
        return result == .success ? .success(()) : .failure(.actionRejected(result.rawValue))
    }

    private static func findPaste(
        in node: AXUIElement,
        depth: Int,
        deadline: TimeInterval,
        remaining: inout Int,
        expired: inout Bool
    ) -> AXUIElement? {
        guard ProcessInfo.processInfo.systemUptime < deadline, remaining > 0 else {
            expired = true
            return nil
        }
        guard depth <= 4 else { return nil }
        remaining -= 1
        AXUIElementSetMessagingTimeout(node, 0.15)

        if value(node, kAXRoleAttribute) as? String == kAXMenuItemRole as String,
           isPlainPaste(
                title: value(node, kAXTitleAttribute) as? String,
                shortcut: value(node, kAXMenuItemCmdCharAttribute) as? String,
                modifiers: (value(node, kAXMenuItemCmdModifiersAttribute) as? NSNumber)?.intValue,
                enabled: (value(node, kAXEnabledAttribute) as? NSNumber)?.boolValue == true
           ) {
            return node
        }
        guard let children = value(node, kAXChildrenAttribute) as? [AXUIElement] else { return nil }
        for child in children {
            if let found = findPaste(in: child, depth: depth + 1, deadline: deadline, remaining: &remaining, expired: &expired) {
                return found
            }
            if expired { break }
        }
        return nil
    }

    private static func value(_ node: AXUIElement, _ attribute: String) -> CFTypeRef? {
        var result: CFTypeRef?
        guard AXUIElementCopyAttributeValue(node, attribute as CFString, &result) == .success else { return nil }
        return result
    }

    private static func element(_ node: AXUIElement, _ attribute: String) -> AXUIElement? {
        guard let result = value(node, attribute), CFGetTypeID(result) == AXUIElementGetTypeID() else { return nil }
        return (result as! AXUIElement)
    }
}
