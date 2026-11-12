using System.Text.Json;

namespace ClaudeUsageWidget.Core;

/// One account's figures in the shape another tool reads them.
///
/// Field names and units are the contract and are not ours to choose: the
/// consumer this exists for already reads `atMs` in epoch MILLISECONDS and the
/// two reset stamps in epoch SECONDS. Mixing them up produces a plausible
/// number that is wrong by a factor of a thousand, which is exactly the kind of
/// mistake a reader cannot spot.
///
/// The five fields up to <paramref name="SevenResetAt"/> are v1 and must keep
/// their names and units: a shipped reader already parses them. The fields
/// through <paramref name="ModelResetAt"/> are v2, added for the multi-account
/// export, and are null on an account that has no per-model limit.
/// <paramref name="AvailableModels"/> and <paramref name="Plan"/> are v3,
/// added for the launcher's model-availability filter.
/// <param name="Account">The account's DisplayName — which sign-in these
/// figures belong to, now that every account writes its own file.</param>
/// <param name="ModelKey">The per-model bucket key the third dial shows, e.g.
/// `seven_day_fable`.</param>
/// <param name="ModelResetAt">Epoch SECONDS, like the other two reset stamps.</param>
/// <param name="AvailableModels">v3. The account's model-bucket families from
/// <see cref="ModelBuckets.Available"/> with the `seven_day_` prefix stripped
/// and lower-cased (`seven_day_fable` → `"fable"`). A list whenever a snapshot
/// exists — empty when the account has no model bucket, never null. Absent in
/// JSON only from pre-v3 writers.</param>
/// <param name="Plan">v3. The account's subscription plan label, as
/// <see cref="SubscriptionTier.LabelFor"/> reads it off the account's stored
/// profile and this snapshot — "Team", "Team Premium", "Max", "Ultra", "Pro", … — which
/// is what entitlement the launcher filters by; the model buckets above are
/// not. Null when the three raw subscription fields were never fetched, and
/// written as `"plan":null` rather than omitted (the writer sets no ignore
/// condition), so the launcher reads null and a pre-v3 absent key the same
/// way: plan unknown, hide nothing.</param>
public sealed record UsageExportPayload(
    double? FiveHour,
    double? SevenDay,
    long AtMs,
    long? FiveResetAt,
    long? SevenResetAt,
    string? Account = null,
    string? ModelKey = null,
    string? ModelLabel = null,
    double? ModelSevenDay = null,
    long? ModelResetAt = null,
    IReadOnlyList<string>? AvailableModels = null,
    string? Plan = null);

/// Turns a snapshot into <see cref="UsageExportPayload"/>. Pure — the writing
/// itself belongs to the App layer.
public static class UsageExport
{
    /// The options the export file is actually written with. Here, next to the
    /// payload, rather than private to the App-layer writer: the round-trip
    /// pins used to serialize through a hand-copied set of options, so a change
    /// to the real policy left them green and lying.
    ///
    /// camelCase because that is the key shape the reader already parses, and
    /// NO ignore condition on purpose: an absent value is written as `null`
    /// rather than dropped, so `"plan":null` from this build and a missing
    /// `plan` from a pre-v3 one are the two cases the launcher must treat the
    /// same, not one case it never sees.
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// Null when there is nothing worth writing: no snapshot at all, or one
    /// carrying neither window. Writing zeros for "no answer yet" would make a
    /// stale file look like a live one.
    ///
    /// <paramref name="preferredBucket"/> is the user's pinned model bucket;
    /// the one actually exported is what <see cref="ModelBuckets.Resolve"/>
    /// makes of it, so the file agrees with the dial rather than with the raw
    /// response order.
    ///
    /// <paramref name="plan"/> arrives ready-made rather than being derived
    /// here: the three raw subscription fields live in the settings, and
    /// loading them is the App layer's business — this stays pure.
    public static UsageExportPayload? Payload(
        UsageSnapshot? snapshot, DateTimeOffset now, string? account = null, string? preferredBucket = null,
        string? plan = null)
    {
        var fiveHour = snapshot?["five_hour"];
        var sevenDay = snapshot?["seven_day"];
        if (fiveHour is null && sevenDay is null) return null;

        // snapshot is non-null here: one of the two buckets above came out of it.
        var modelKey = ModelBuckets.Resolve(preferredBucket, snapshot!);
        var modelBucket = modelKey is null ? null : snapshot![modelKey];

        return new UsageExportPayload(
            fiveHour?.Utilization,
            sevenDay?.Utilization,
            now.ToUnixTimeMilliseconds(),
            fiveHour?.ResetsAt?.ToUnixTimeSeconds(),
            sevenDay?.ResetsAt?.ToUnixTimeSeconds(),
            account,
            modelKey,
            modelKey is null ? null : ModelBuckets.Label(modelKey),
            modelBucket?.Utilization,
            modelBucket?.ResetsAt?.ToUnixTimeSeconds(),
            // v3: the account's model-bucket families, seven_day_ stripped and
            // lower-cased. A list past the two-window guard above — empty when
            // the account has no model bucket, never null while a snapshot exists.
            ModelBuckets.Available(snapshot!)
                .Select(k => k.StartsWith("seven_day_", StringComparison.Ordinal) ? k["seven_day_".Length..] : k)
                .Select(s => s.ToLowerInvariant())
                .ToList(),
            plan);
    }

    /// Where this account's figures go, or null when the export is off. The
    /// account's own ExportPath wins; otherwise ExportDirectory plus
    /// <see cref="FileNameFor"/>.
    ///
    /// Two accounts must never share a file: they would overwrite each other's
    /// figures on every refresh. Final paths are compared, case-insensitively
    /// as Windows does. Every account's own ExportPath is taken first; then, in
    /// settings order, each account takes its plain name when it is free,
    /// which readers open by name, or `<name>-<id>` when it is not. An account
    /// missing from the list (removed mid-poll) counts as the last.
    ///
    /// The names come from the settings, the caller's
    /// <paramref name="displayName"/> only for an account missing from them:
    /// one source for every account, so a hand-edited name cannot slip past.
    public static string? ExportPathFor(WidgetSettingsData data, string accountId, string displayName)
    {
        var own = data.Account(accountId);
        if (!string.IsNullOrWhiteSpace(own?.ExportPath)) return own.ExportPath;
        if (string.IsNullOrWhiteSpace(data.ExportDirectory)) return null;

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in data.Accounts.Where(a => !string.IsNullOrWhiteSpace(a.ExportPath)))
            taken.Add(Normalize(account.ExportPath!));

        foreach (var account in data.Accounts.Where(a => string.IsNullOrWhiteSpace(a.ExportPath)))
        {
            var path = FreePath(data.ExportDirectory, account.DisplayName, account.Id, taken);
            if (account.Id == accountId) return path;
            taken.Add(Normalize(path));
        }

        return FreePath(data.ExportDirectory, displayName, accountId, taken);
    }

    /// The plain name, else the name with the id, else that with a counter:
    /// an account named like another's suffixed file must not land on it.
    private static string FreePath(string directory, string displayName, string id, HashSet<string> taken)
    {
        var name = FileNameFor(displayName, id);
        var path = Path.Combine(directory, name + ".widget.json");
        if (!taken.Contains(Normalize(path))) return path;

        var suffixed = name + "-" + FileNameFor(id, id);
        path = Path.Combine(directory, suffixed + ".widget.json");
        for (var n = 2; taken.Contains(Normalize(path)); n++)
            path = Path.Combine(directory, $"{suffixed}-{n.ToString(System.Globalization.CultureInfo.InvariantCulture)}.widget.json");
        return path;
    }

    /// True when an export to <paramref name="exportPath"/> would replace
    /// <paramref name="protectedPath"/> (settings.json): an ExportPath typed
    /// into the wrong field would write usage figures over every account.
    public static bool WritesOver(string exportPath, string protectedPath) =>
        string.Equals(Normalize(exportPath), Normalize(protectedPath), StringComparison.OrdinalIgnoreCase);

    /// A hand-written ExportPath may be relative or spelled with `..`; one
    /// that does not even parse cannot collide with a generated one.
    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    /// A display name turned into one path segment: trim, lowercase, anything
    /// outside `[a-z0-9_-]` becomes `-`, runs collapse, edges trimmed.
    ///
    /// The result is a FILE NAME, so this cannot be a "best effort" cleanup:
    /// a stray `\` or `:` left in would silently redirect the export into
    /// another directory or fail the write. Falls back to
    /// <paramref name="fallbackId"/> when nothing usable survives — two
    /// accounts named `--` would collide, their ids never do.
    public static string FileNameFor(string displayName, string fallbackId)
    {
        var slug = new System.Text.StringBuilder(displayName.Length);
        foreach (var raw in displayName.Trim().ToLowerInvariant())
        {
            var ch = raw is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' ? raw : '-';
            // Collapse runs as we go: a name like "work.shared@example" must not
            // become "work-shared--example" just because two specials were adjacent.
            if (ch == '-' && (slug.Length == 0 || slug[^1] == '-')) continue;
            slug.Append(ch);
        }

        while (slug.Length > 0 && slug[^1] == '-') slug.Length--;
        return slug.Length == 0 ? fallbackId : slug.ToString();
    }
}
