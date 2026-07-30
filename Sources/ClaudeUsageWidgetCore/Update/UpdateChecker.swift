import Foundation

/// Canonical GitHub links, and a check for a newer published release.
///
/// The sibling mole-widget delegates update detection to Sparkle; this widget
/// has no third-party dependencies, so it asks GitHub directly. The repository
/// is public, so the request is unauthenticated.
public enum UpdateChecker {
    public static let repoPageURL = URL(string: "https://github.com/TadelUnso/claude-usage-widget")!
    public static let issuesPageURL = URL(string: "https://github.com/TadelUnso/claude-usage-widget/issues")!
    public static let releasesPageURL = URL(string: "https://github.com/TadelUnso/claude-usage-widget/releases")!

    private static let latestReleaseAPI = URL(string: "https://api.github.com/repos/TadelUnso/claude-usage-widget/releases/latest")!

    /// The newest published release tag, or nil when the repository has none
    /// yet — a fresh repository answers 404, which is not an error worth
    /// showing the user.
    public static func latestRelease(session: URLSession = UsageAPI.connectivityAwareSession) async throws -> String? {
        var request = URLRequest(url: latestReleaseAPI)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")

        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch {
            throw UsageError.network(error.localizedDescription)
        }

        guard let http = response as? HTTPURLResponse else { throw UsageError.malformedResponse }
        if http.statusCode == 404 { return nil }
        try UsageAPI.validate(statusCode: http.statusCode)

        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tag = root["tag_name"] as? String
        else { throw UsageError.malformedResponse }
        return tag
    }

    /// Whether `candidate` is a later version than `current`.
    ///
    /// Compares dot-separated numeric components, so 0.10.0 beats 0.9.0 where a
    /// string comparison would not. A leading "v" is dropped because GitHub
    /// tags carry one and `CFBundleShortVersionString` does not. Anything that
    /// does not parse as numbers is treated as not newer — a malformed tag
    /// should never nag the user to upgrade.
    public static func isNewer(_ candidate: String, than current: String) -> Bool {
        let left = components(candidate)
        let right = components(current)
        guard !left.isEmpty else { return false }

        for index in 0..<max(left.count, right.count) {
            let a = index < left.count ? left[index] : 0
            let b = index < right.count ? right[index] : 0
            if a != b { return a > b }
        }
        return false
    }

    private static func components(_ version: String) -> [Int] {
        let trimmed = version.hasPrefix("v") ? String(version.dropFirst()) : version
        let parts = trimmed.split(separator: ".")
        guard !parts.isEmpty else { return [] }
        var numbers: [Int] = []
        for part in parts {
            guard let number = Int(part) else { return [] }
            numbers.append(number)
        }
        return numbers
    }
}
