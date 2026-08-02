import Foundation

/// One usage bucket as returned by /api/oauth/usage.
public struct UsageBucket: Equatable, Sendable {
    /// Percentage of the limit consumed, on the server's 0...100 scale.
    public let utilization: Double
    /// When this window rolls over. Absent for buckets the account does not use.
    public let resetsAt: Date?

    public init(utilization: Double, resetsAt: Date?) {
        self.utilization = utilization
        self.resetsAt = resetsAt
    }
}

/// A decoded /api/oauth/usage response.
///
/// Deliberately a dictionary rather than a struct with fixed properties: the
/// set of bucket keys changes as models come and go, and neither a new key nor
/// a vanished one should require a code change.
public struct UsageSnapshot: Equatable, Sendable {
    public let buckets: [String: UsageBucket]
    /// When the source actually observed these figures. Network responses are
    /// current and leave this nil; Claude Code's statusline bridge supplies the
    /// capture time so stale cached figures are labelled honestly.
    public let sourceUpdatedAt: Date?

    public init(buckets: [String: UsageBucket], sourceUpdatedAt: Date? = nil) {
        self.buckets = buckets
        self.sourceUpdatedAt = sourceUpdatedAt
    }

    public subscript(key: String) -> UsageBucket? { buckets[key] }
}

public enum UsageError: Error, Equatable, Sendable {
    /// No Claude Code credentials were found in the Keychain.
    case noCredentials
    /// The endpoint rejected the token — Claude Code needs a fresh login.
    case unauthorized
    /// The body was not JSON, or carried no usage buckets at all.
    case malformedResponse
    /// The endpoint answered 429; the payload is the server's Retry-After in
    /// seconds, when it sent one.
    case rateLimited(retryAfterSeconds: Int?)
    /// Transport failure or an unexpected status code.
    case network(String)
}

extension UsageError: LocalizedError {
    /// These reach the user directly — the update-check alert shows one when a
    /// check fails — so they read as sentences rather than as cases.
    public var errorDescription: String? {
        switch self {
        case .noCredentials:
            "No Claude.ai web session was found."
        case .unauthorized:
            "The Claude.ai session expired. Sign in again from the widget menu."
        case .malformedResponse:
            "The server returned something unexpected."
        case .rateLimited:
            "The API is rate limited. The widget retries on its own."
        case let .network(message):
            message
        }
    }
}
