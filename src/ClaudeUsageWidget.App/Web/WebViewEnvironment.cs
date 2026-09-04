using System.IO;
using Microsoft.Web.WebView2.Core;

namespace ClaudeUsageWidget.App.Web;

/// One WebView2 environment for the whole process.
///
/// Every account is a PROFILE inside this one environment's user data folder,
/// not a user data folder of its own: a separate folder per account starts a
/// separate browser-process collection for each, which the WebView2
/// process-model documentation calls the previous approach and warns against
/// ("avoid running a WebView2 control with too many different UDFs at the same
/// time"). Profiles give the same cookie separation inside one collection.
public static class WebViewEnvironment
{
    private static Task<CoreWebView2Environment>? _shared;

    /// The folder the pre-multi-account build used as its user data folder.
    /// Kept, so the upgrade does not strand the old store somewhere unreachable
    /// — though it does NOT preserve the sign-in, which was measured rather
    /// than assumed: see
    /// `docs/superpowers/plans/2026-08-26-multi-account-verification/profile-probe.md`.
    public static string UserDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeUsageWidget", "profiles", "default");

    /// Same FAULTED/CANCELED reset as the per-session caches this replaces: a
    /// first failure (WebView2 Runtime missing, folder locked) must not stick
    /// until the app restarts, because the user can fix it while the app runs.
    ///
    /// No lock: every caller reaches this from the UI thread — see the
    /// concurrency note on ClaudeWebSession.EnsureEnvironmentAsync.
    public static Task<CoreWebView2Environment> SharedAsync()
    {
        if (_shared is { IsFaulted: true } or { IsCanceled: true }) _shared = null;

        return _shared ??= CreateAsync();
    }

    private static async Task<CoreWebView2Environment> CreateAsync()
    {
        Directory.CreateDirectory(UserDataFolder);
        // browserExecutableFolder=null → the system Edge/WebView2 Runtime;
        // options=null → defaults. Accounts are separated by ProfileName on
        // the controller, not by this folder.
        return await CoreWebView2Environment.CreateAsync(userDataFolder: UserDataFolder)
            .ConfigureAwait(true);
    }
}
