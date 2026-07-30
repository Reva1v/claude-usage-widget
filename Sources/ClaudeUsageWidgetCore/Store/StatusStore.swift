import AppKit
import Foundation
import Observation

/// Owns the service status and its refresh cycle. Separate from `UsageStore`
/// because it reads a different, unauthenticated endpoint and a failure in one
/// must not blank the other.
@MainActor
@Observable
public final class StatusStore {
    public static let refreshInterval: TimeInterval = 300

    /// The last status successfully read. A failed refresh leaves it standing —
    /// a transient network blip should not claim the service is down.
    public private(set) var status: ServiceStatus = .unknown

    private let fetch: @Sendable () async throws -> ServiceStatus
    private var timer: Timer?
    private var wakeObserver: NSObjectProtocol?

    public init(fetch: @escaping @Sendable () async throws -> ServiceStatus = { try await StatusAPI().fetch() }) {
        self.fetch = fetch
    }

    /// Idempotent: stops any previous cycle first.
    public func start() {
        stop()
        refresh()
        timer = Timer.scheduledTimer(withTimeInterval: Self.refreshInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
        wakeObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
    }

    public func stop() {
        timer?.invalidate()
        timer = nil
        if let wakeObserver {
            NSWorkspace.shared.notificationCenter.removeObserver(wakeObserver)
            self.wakeObserver = nil
        }
    }

    public func refresh() {
        Task { await load() }
    }

    func load() async {
        guard let fetched = try? await fetch() else { return }
        status = fetched
    }
}
