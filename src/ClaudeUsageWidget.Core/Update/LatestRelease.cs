using System.Text.Json;

namespace ClaudeUsageWidget.Core;

/// The newest published release on GitHub, as far as the updater cares: its
/// version, the win-x64 zip to install from and the page to fall back to.
public sealed record LatestRelease(string Version, Uri? AssetUrl, Uri PageUrl)
{
    public static readonly Uri Endpoint =
        new("https://api.github.com/repos/Reva1v/claude-usage-widget/releases/latest");

    /// The release skill names the zip `ClaudeUsageWidget-vX.Y.Z-win-x64.zip`;
    /// only the suffix is matched, so a renamed prefix does not break updates.
    internal const string AssetSuffix = "-win-x64.zip";

    /// Parses the body of <see cref="Endpoint"/>. Null when the payload has no
    /// tag or page — something that is not a release at all. A release
    /// without the zip still parses, with a null <see cref="AssetUrl"/>: the
    /// caller can still say a newer version exists and open its page.
    public static LatestRelease? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        if (!root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { Length: > 0 } tagName)
            return null;
        if (!root.TryGetProperty("html_url", out var page)
            || !Uri.TryCreate(page.GetString(), UriKind.Absolute, out var pageUrl))
            return null;

        Uri? assetUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is null || !name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                if (asset.TryGetProperty("browser_download_url", out var url)
                    && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var parsed))
                {
                    assetUrl = parsed;
                    break;
                }
            }
        }

        return new LatestRelease(tagName.TrimStart('v', 'V'), assetUrl, pageUrl);
    }

    /// True only when both versions parse and <paramref name="latest"/> is
    /// strictly greater. An unparseable side — "unknown" from a host with no
    /// informational version — never reads as an update: offering to replace
    /// a build whose own version is unknown is a guess, not a check.
    public static bool IsNewer(string latest, string current) =>
        System.Version.TryParse(latest.TrimStart('v', 'V'), out var l)
        && System.Version.TryParse(current.TrimStart('v', 'V'), out var c)
        && l > c;
}
