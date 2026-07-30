import AppKit
import Foundation
import Observation

/// Owns the usage snapshot and the refresh cycle.
///
/// The fetch, the token lookup and the clock are all injected so the whole
/// state machine is testable without a network, a keychain, or real time.
@MainActor
@Observable
public final class UsageStore {
    public enum State: Equatable {
        case loading
        case ok(UsageSnapshot, fetchedAt: Date)
        case failed(UsageError)
    }

    /// The server's figures move slowly; a faster poll would only add requests.
    public static let refreshInterval: TimeInterval = 300

    public private(set) var state: State = .loading

    /// The most recent successful snapshot. Kept so a failing refresh dims the
    /// dials instead of blanking them.
    public private(set) var lastSnapshot: UsageSnapshot?

    private let fetch: @Sendable (String) async throws -> UsageSnapshot
    private let tokenProvider: @Sendable () -> String?
    private let now: @Sendable () -> Date
    private var timer: Timer?
    private var wakeObserver: NSObjectProtocol?

    /// The load currently in flight, if any. The timer, a manual refresh and
    /// the wake handler can all fire at once; without this they race and
    /// whichever finishes last wins, which can put an older snapshot on screen
    /// than the one already shown.
    private var inFlight: Task<Void, Never>?

    public init(
        fetch: @escaping @Sendable (String) async throws -> UsageSnapshot = { try await UsageAPI().fetch(token: $0) },
        tokenProvider: @escaping @Sendable () -> String? = { ClaudeCredentials.accessToken() },
        now: @escaping @Sendable () -> Date = { Date() }
    ) {
        self.fetch = fetch
        self.tokenProvider = tokenProvider
        self.now = now
    }

    /// Idempotent: stops any previous cycle first, so it is safe to call again.
    public func start() {
        stop()
        refresh()
        timer = Timer.scheduledTimer(withTimeInterval: Self.refreshInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
        // A machine asleep for hours wakes with a stale snapshot; refresh at once
        // rather than waiting out the rest of the interval.
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
        if let inFlight {
            await inFlight.value
            return
        }
        let task = Task { await performLoad() }
        inFlight = task
        await task.value
        inFlight = nil
    }

    private func performLoad() async {
        guard let token = tokenProvider() else {
            state = .failed(.noCredentials)
            return
        }
        do {
            let snapshot = try await fetch(token)
            lastSnapshot = snapshot
            state = .ok(snapshot, fetchedAt: now())
        } catch let error as UsageError {
            state = .failed(error)
        } catch {
            state = .failed(.network(error.localizedDescription))
        }
    }
}
