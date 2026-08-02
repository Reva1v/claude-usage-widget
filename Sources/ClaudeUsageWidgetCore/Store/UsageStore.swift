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

    /// While set, refreshes are skipped: the server answered 429, and polling
    /// through the ban only prolongs it. The timer keeps ticking — the first
    /// tick past the deadline fetches again.
    public private(set) var retryPausedUntil: Date?

    /// Padding added past the server's Retry-After. Observed live: the header
    /// understates the ban window by minutes, and the ban's violation counter
    /// survives its own expiry — a retry that lands seconds early re-trips a
    /// fresh hour-long ban, forever. Waiting a little longer than asked breaks
    /// that loop.
    public static let retryMargin: TimeInterval = 300

    /// A Retry-After deadline has repeatedly proved insufficient in production.
    /// One retry is allowed after an hour; further failures open the circuit for
    /// six hours and then a day. The state is persisted across app restarts.
    private static let rateLimitBackoff: [TimeInterval] = [3600, 6 * 3600, 24 * 3600]
    private var consecutiveRateLimits = 0

    private let cachedSnapshot: @Sendable () -> UsageSnapshot?
    private let fetch: @Sendable (String) async throws -> UsageSnapshot
    private let tokenProvider: @Sendable () -> String?
    private let now: @Sendable () -> Date
    private let saveRetryState: @Sendable (UsageRetryState?) -> Void
    private var timer: Timer?
    private var wakeObserver: NSObjectProtocol?

    /// The load currently in flight, if any. The timer, a manual refresh and
    /// the wake handler can all fire at once; without this they race and
    /// whichever finishes last wins, which can put an older snapshot on screen
    /// than the one already shown.
    private var inFlight: Task<Void, Never>?

    public init(
        cachedSnapshot: @escaping @Sendable () -> UsageSnapshot? = { StatuslineUsageCache.snapshot() },
        fetch: @escaping @Sendable (String) async throws -> UsageSnapshot = { try await UsageAPI().fetch(token: $0) },
        tokenProvider: @escaping @Sendable () -> String? = { ClaudeCredentials.accessToken() },
        now: @escaping @Sendable () -> Date = { Date() },
        loadRetryState: @escaping @Sendable () -> UsageRetryState? = { UsageRetryStateStore.load() },
        saveRetryState: @escaping @Sendable (UsageRetryState?) -> Void = { UsageRetryStateStore.save($0) }
    ) {
        self.cachedSnapshot = cachedSnapshot
        self.fetch = fetch
        self.tokenProvider = tokenProvider
        self.now = now
        self.saveRetryState = saveRetryState

        if let saved = loadRetryState(), saved.until > now() {
            retryPausedUntil = saved.until
            consecutiveRateLimits = saved.consecutiveRateLimits
            state = .failed(.rateLimited(retryAfterSeconds: nil))
        } else {
            saveRetryState(nil)
        }
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
        // Claude Code's own statusline data is safe to use even while the
        // network circuit is open, and reading it requires no OAuth token.
        if let snapshot = cachedSnapshot() {
            publish(snapshot)
            return
        }
        if let retryPausedUntil, now() < retryPausedUntil { return }
        retryPausedUntil = nil
        guard let token = tokenProvider() else {
            state = .failed(.noCredentials)
            return
        }
        do {
            let snapshot = try await fetch(token)
            publish(snapshot)
        } catch let error as UsageError {
            if case let .rateLimited(retryAfterSeconds) = error {
                consecutiveRateLimits += 1
                let index = min(consecutiveRateLimits - 1, Self.rateLimitBackoff.count - 1)
                let backoff = Self.rateLimitBackoff[index]
                let wait = max(TimeInterval(retryAfterSeconds ?? 0), backoff)
                retryPausedUntil = now().addingTimeInterval(wait + Self.retryMargin)
                saveRetryState(UsageRetryState(
                    until: retryPausedUntil!,
                    consecutiveRateLimits: consecutiveRateLimits
                ))
            }
            state = .failed(error)
        } catch {
            state = .failed(.network(error.localizedDescription))
        }
    }

    private func publish(_ snapshot: UsageSnapshot) {
        lastSnapshot = snapshot
        consecutiveRateLimits = 0
        retryPausedUntil = nil
        saveRetryState(nil)
        state = .ok(snapshot, fetchedAt: snapshot.sourceUpdatedAt ?? now())
    }
}

public struct UsageRetryState: Equatable, Sendable {
    public let until: Date
    public let consecutiveRateLimits: Int

    public init(until: Date, consecutiveRateLimits: Int) {
        self.until = until
        self.consecutiveRateLimits = consecutiveRateLimits
    }
}

public enum UsageRetryStateStore {
    private static let untilKey = "usageRetryPausedUntil"
    private static let countKey = "usageConsecutiveRateLimits"

    public static func load(defaults: UserDefaults = .standard) -> UsageRetryState? {
        guard let until = defaults.object(forKey: untilKey) as? Date else { return nil }
        return UsageRetryState(
            until: until,
            consecutiveRateLimits: max(1, defaults.integer(forKey: countKey))
        )
    }

    public static func save(_ state: UsageRetryState?, defaults: UserDefaults = .standard) {
        if let state {
            defaults.set(state.until, forKey: untilKey)
            defaults.set(state.consecutiveRateLimits, forKey: countKey)
        } else {
            defaults.removeObject(forKey: untilKey)
            defaults.removeObject(forKey: countKey)
        }
    }
}
