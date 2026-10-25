import CoreGraphics
import Foundation

enum DirectedPaster {
    static func makeCommandVEvents() -> [CGEvent]? {
        guard let source = CGEventSource(stateID: .privateState),
              let commandDown = CGEvent(keyboardEventSource: source, virtualKey: 55, keyDown: true),
              let vDown = CGEvent(keyboardEventSource: source, virtualKey: 9, keyDown: true),
              let vUp = CGEvent(keyboardEventSource: source, virtualKey: 9, keyDown: false),
              let commandUp = CGEvent(keyboardEventSource: source, virtualKey: 55, keyDown: false) else {
            return nil
        }

        commandDown.flags = .maskCommand
        vDown.flags = .maskCommand
        vUp.flags = .maskCommand
        commandUp.flags = []
        return [commandDown, vDown, vUp, commandUp]
    }

    // Deliver only to the captured app; a global HID post can also reach UU's
    // keyboard monitor. Never fall back to global input or activate a window.
    static func postCommandV(
        to pid: pid_t,
        post: (CGEvent, pid_t) -> Void = { event, target in event.postToPid(target) },
        pause: (TimeInterval) -> Void = { Thread.sleep(forTimeInterval: $0) }
    ) -> Bool {
        guard pid > 0, let events = makeCommandVEvents() else { return false }
        for (index, event) in events.enumerated() {
            post(event, pid)
            if index < events.count - 1 { pause(0.025) }
        }
        return true
    }
}
