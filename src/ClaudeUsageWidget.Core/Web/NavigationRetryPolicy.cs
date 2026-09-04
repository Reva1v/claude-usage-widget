namespace ClaudeUsageWidget.Core;

/// Which navigation failures deserve a second attempt, and how long to wait.
///
/// A string, not CoreWebView2WebErrorStatus: Core has no WebView2 reference,
/// and the caller has the enum. The trade is that a status renamed upstream
/// stops matching here silently — the log records the raw status of every
/// failure, which is what would show that.
public static class NavigationRetryPolicy
{
    /// Long enough for a dropped connection or a resuming network adapter to
    /// come back, short enough to stay inside the 5-minute refresh cycle.
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    /// Blips, not verdicts. `Unknown` is on the list because that is what a
    /// transient claude.ai failure actually reports here (observed
    /// 2026-09-04) and it is undocumented beyond "an unknown error"; DNS,
    /// certificate and proxy failures are left off because a second attempt
    /// three seconds later fails identically and only doubles the noise.
    ///
    /// Ordinal and exact: this compares an enum's ToString(), so a near-match
    /// like "Unknown " is a bug in the caller, not a status to honour.
    public static bool IsTransient(string webErrorStatus) => Transient.Contains(webErrorStatus);

    private static readonly HashSet<string> Transient = new(StringComparer.Ordinal)
    {
        "Unknown",
        "ConnectionAborted",
        "ConnectionReset",
        "OperationCanceled",
        "Timeout",
    };
}
