import Foundation

final class BridgeDiagnostics {
    private let clock: () -> TimeInterval
    private let capacity: Int
    private var startedAt: TimeInterval = 0
    private var watchUntil: TimeInterval = 0
    private var entries: [String] = []

    init(capacity: Int = 120, clock: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime }) {
        self.capacity = max(1, capacity)
        self.clock = clock
    }

    var isWatching: Bool { clock() < watchUntil }

    func begin(mode: String, appID: String?, pid: pid_t?, clipboardCount: Int) {
        startedAt = clock()
        watchUntil = startedAt + 2.0
        record("tag_received mode=\(mode)", appID: appID, pid: pid, clipboardCount: clipboardCount)
    }

    func record(_ stage: String, appID: String? = nil, pid: pid_t? = nil, clipboardCount: Int? = nil) {
        var entry = String(format: "+%.0fms %@", (clock() - startedAt) * 1000, stage)
        if let appID { entry += " app=\(appID)" }
        if let pid { entry += " pid=\(pid)" }
        if let clipboardCount { entry += " clipboard=\(clipboardCount)" }
        entries.append(entry)
        if entries.count > capacity { entries.removeFirst(entries.count - capacity) }
    }

    func snapshot() -> String {
        (["UU Mac Paste Assistant V1.0.3", "No transcript, window titles, or clipboard content recorded."] + entries).joined(separator: "\n")
    }
}
