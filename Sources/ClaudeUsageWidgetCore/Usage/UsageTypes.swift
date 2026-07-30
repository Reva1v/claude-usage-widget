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

    public init(buckets: [String: UsageBucket]) {
        self.buckets = buckets
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
    /// Transport failure or an unexpected status code.
    case network(String)
}

extension UsageError: LocalizedError {
    /// These reach the user directly — the update-check alert shows one when a
    /// check fails — so they read as sentences rather than as cases.
    public var errorDescription: String? {
        switch self {
        case .noCredentials:
            "No Claude Code credentials were found in the keychain."
        case .unauthorized:
            "The token was rejected. Sign in to Claude Code again."
        case .malformedResponse:
            "The server returned something unexpected."
        case let .network(message):
            message
        }
    }
}
