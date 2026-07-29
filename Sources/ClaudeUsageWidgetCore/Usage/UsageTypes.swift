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
