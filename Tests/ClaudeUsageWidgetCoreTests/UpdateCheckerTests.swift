import Testing
@testable import ClaudeUsageWidgetCore

/// The version-comparison tests that used to live here were removed along
/// with `UpdateChecker.latestRelease` and `UpdateChecker.isNewer` — Sparkle
/// owns update detection now. The three URL constants that survive are wired
/// into the menu bar's repository and issue links, not exercised here.
@Suite("UpdateChecker")
struct UpdateCheckerTests {}
