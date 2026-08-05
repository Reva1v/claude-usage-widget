using System.Reflection;

namespace ClaudeUsageWidget.Core;

/// Version string shown in the UI.
///
/// Read from the entry assembly rather than held as a literal here, for the
/// same reason as the Swift original (`Sources/ClaudeUsageWidgetCore/CoreInfo.swift`):
/// hand-syncing a version string against the real build number drifts. There
/// the source of truth was `Resources/Info.plist`; here it is the
/// `AssemblyInformationalVersion` MSBuild stamps from the package version,
/// which — unlike `AssemblyVersion` — commonly carries a `+`-delimited build
/// suffix (e.g. a commit hash) that is not part of the version a user should see.
public static class CoreInfo
{
    /// `unknown` rather than a plausible-looking fallback: a wrong version in
    /// a bug report costs more than an obviously absent one. The test host
    /// has no informational version of its own, so this is the value the
    /// unit tests see — see <see cref="ParseVersion"/> for a version of this
    /// that takes the raw string directly and so is testable without one.
    public static string Version { get; } = ParseVersion(
        Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion);

    internal static string ParseVersion(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion)) return "unknown";

        var plusIndex = informationalVersion.IndexOf('+');
        var version = plusIndex >= 0 ? informationalVersion[..plusIndex] : informationalVersion;
        return string.IsNullOrEmpty(version) ? "unknown" : version;
    }
}
