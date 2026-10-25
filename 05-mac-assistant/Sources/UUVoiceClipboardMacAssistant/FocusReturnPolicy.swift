import Foundation

struct FocusReturnPolicy {
    let targetPID: pid_t
    let deadline: TimeInterval
    let inputBaseline: [UInt32]
    private(set) var attempted = false
    private(set) var cancelled = false
    private(set) var cancellationReason: String?
    private(set) var restoreRequestedAt: TimeInterval?

    func activationDelay(now: TimeInterval) -> TimeInterval {
        guard let restoreRequestedAt else { return 0 }
        return max(0, restoreRequestedAt + 0.08 - now)
    }

    mutating func shouldRestore(
        currentPID: pid_t?,
        currentIsUU: Bool,
        inputCounters: [UInt32],
        now: TimeInterval,
        allowed: Bool
    ) -> Bool {
        guard !attempted, !cancelled else { return false }
        guard targetPID > 0, allowed else {
            cancelled = true
            cancellationReason = "not_allowed"
            return false
        }
        guard now < deadline else {
            cancelled = true
            cancellationReason = "deadline"
            return false
        }
        guard inputCounters == inputBaseline else {
            cancelled = true
            cancellationReason = "new_input"
            return false
        }
        guard let currentPID else { return false }
        if currentPID == targetPID { return false }
        guard currentIsUU else {
            cancelled = true
            cancellationReason = "other_application"
            return false
        }
        attempted = true
        restoreRequestedAt = now
        return true
    }
}
