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
    /// than assumed (2026-08-26, against a live WebView2 Runtime).
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

    /// The controller options for an account's profile: null for the implicit
    /// default profile (the pre-multi-account sign-in lives there and cannot be
    /// addressed by name), a named profile otherwise.
    ///
    /// INVARIANT: the login controller and the fetch controller of one account
    /// must be created with the SAME ProfileName. If they ever drift apart,
    /// sign-in succeeds but that account's dials stay empty forever, with no
    /// error anywhere. Both callers build their options here so they cannot.
    public static CoreWebView2ControllerOptions? ControllerOptionsFor(
        CoreWebView2Environment environment, string? profileName)
    {
        if (profileName is null) return null;

        var options = environment.CreateCoreWebView2ControllerOptions();
        options.ProfileName = profileName;
        return options;
    }

    /// A controller on the account's profile, with the name read back and
    /// checked — see <see cref="ControllerOptionsFor"/>.
    public static async Task<CoreWebView2Controller> CreateControllerAsync(
        CoreWebView2Environment environment, nint parentWindow, string? profileName, string accountLabel)
    {
        var options = ControllerOptionsFor(environment, profileName);
        var controller = options is null
            ? await environment.CreateCoreWebView2ControllerAsync(parentWindow).ConfigureAwait(true)
            : await environment.CreateCoreWebView2ControllerAsync(parentWindow, options).ConfigureAwait(true);
        VerifyProfile(controller.CoreWebView2, profileName, accountLabel);
        return controller;
    }

    /// Throws if WebView2 handed back a different profile than was asked for.
    /// Case-insensitive: the name becomes a directory on disk.
    public static void VerifyProfile(CoreWebView2 core, string? profileName, string accountLabel)
    {
        if (profileName is null) return;
        if (string.Equals(core.Profile.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)) return;

        throw new InvalidOperationException(
            $"WebView2 gave profile '{core.Profile.ProfileName}' for account '{accountLabel}', expected '{profileName}'.");
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
