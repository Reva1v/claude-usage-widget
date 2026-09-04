using ClaudeUsageWidget.App.Web;
using ClaudeUsageWidget.Core;

namespace ClaudeUsageWidget.App;

/// Everything one account owns at runtime. One instance per configured
/// account; nothing here is shared between accounts except the WebView2
/// environment behind the session, which is shared on purpose.
public sealed record AccountRuntime(AccountProfile Profile, ClaudeWebSession Session, UsageStore Store)
{
    /// The snapshot to draw for this account: the fresh one while it is good,
    /// the last good one while a refresh is failing — dimmed dials rather than
    /// blank ones.
    public UsageSnapshot? Snapshot =>
        Store.CurrentState is UsageState.Ok(var ok, _) ? ok : Store.LastSnapshot;
}
