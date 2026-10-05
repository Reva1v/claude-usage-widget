namespace ClaudeUsageWidget.Core;

/// When the hidden fetch webview of one account may be closed.
///
/// A refresh needs it for a few seconds every five minutes; kept open the
/// whole time, the four accounts held a browser process, a GPU process and a
/// renderer each — about 235 MB of private memory — for nothing. Closing the
/// last controller lets the browser process exit (measured on WebView2
/// 1.0.4129.50: the environment object does not keep it alive), and the next
/// fetch starts it again.
///
/// Not "at the end of FetchUsageAsync": the cookie check before it, the sign-in
/// probe at start-up and the profile wipe on account removal use the same
/// webview, and the usage store may skip the fetch entirely (a 429 pause), so
/// a release tied to one caller would leak the webview another one created.
/// Every use takes the lease; the last one out starts a short idle delay, and
/// any use inside it cancels the release — which keeps the cookie check, the
/// organizations page and the usage page of one refresh on one webview.
///
/// Single-threaded by contract: every caller runs on the UI thread, as the
/// rest of ClaudeWebSession does.
public sealed class FetchIdleLease
{
    /// Far longer than the gap between the fetches of one refresh (they run
    /// back to back; the 3 s navigation retry runs inside the lease), far
    /// shorter than the five-minute cycle.
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(15);

    private int _users;
    private long _generation;

    public bool InUse => _users > 0;

    public void Enter()
    {
        _users++;
        _generation++;
    }

    /// The token to hand the idle delay when this was the last user; null
    /// while another use is still running.
    public long? Exit()
    {
        if (_users == 0) throw new InvalidOperationException("Exit without a matching Enter.");

        _users--;
        return _users == 0 ? _generation : null;
    }

    /// True only if nothing used the webview since the token was handed out.
    public bool ShouldRelease(long token) => _users == 0 && token == _generation;
}
