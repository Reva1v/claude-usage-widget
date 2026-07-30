import Foundation

/// Canonical GitHub links.
///
/// Update detection itself is Sparkle's job now (see `SPUStandardUpdaterController`
/// in the app target); this enum only keeps the links the repository and issue
/// menu items still use.
public enum UpdateChecker {
    public static let repoPageURL = URL(string: "https://github.com/TadelUnso/claude-usage-widget")!
    public static let issuesPageURL = URL(string: "https://github.com/TadelUnso/claude-usage-widget/issues")!
    public static let releasesPageURL = URL(string: "https://github.com/TadelUnso/claude-usage-widget/releases")!
}
