import Foundation

/// The panel-wide notice that replaces the dials when their figures cannot be
/// trusted.
///
/// A dead token freezes the dials at whatever was last fetched, and the session
/// window rolls over long before anyone notices — the figures do not merely go
/// stale, they go wrong. In that state the widget has one job, which is to say
/// how to get its numbers back.
public struct BlockingNotice: Equatable, Sendable {
    public let title: String
    public let detail: String

    /// Nil for every state whose dials still mean something, including a failed
    /// refresh over a dropped network: that one mends itself, and the last
    /// figures stay close enough until it does.
    public static func make(for state: UsageStore.State) -> BlockingNotice? {
        switch state {
        case .failed(.noCredentials):
            return BlockingNotice(title: "Not signed in", detail: "Use the menu to sign in to Claude.ai")
        case .failed(.unauthorized):
            return BlockingNotice(title: "Session expired", detail: "Sign in to Claude.ai again")
        default:
            return nil
        }
    }
}
