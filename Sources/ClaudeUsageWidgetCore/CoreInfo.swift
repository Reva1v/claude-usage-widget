import Foundation

/// Version string shown in the menu bar.
///
/// Read from the running bundle rather than held as a literal here. The two
/// were hand-synced once, and drifted: a build shipped announcing 0.1.5 in the
/// menu while Sparkle — which reads the plist — correctly saw 0.1.6. There is
/// now one source of truth, and `Resources/Info.plist` is it.
public enum CoreInfo {
    public static let version = version(fromInfoDictionary: Bundle.main.infoDictionary)

    /// `unknown` rather than a plausible-looking fallback: a wrong version in a
    /// bug report costs more than an obviously absent one. The test bundle has
    /// no version of its own, so this is the value tests see.
    static func version(fromInfoDictionary info: [String: Any]?) -> String {
        guard let version = info?["CFBundleShortVersionString"] as? String, !version.isEmpty else {
            return "unknown"
        }
        return version
    }
}
