using System.Globalization;
using System.Text.Json;

namespace ClaudeUsageWidget.Core;

/// Turns a raw /api/oauth/usage body into a snapshot.
///
/// Parses with JsonDocument rather than a typed model because the response is
/// an open map: unknown keys must survive, and members that are not buckets (a
/// plain currency string, for instance) must be ignored instead of failing the
/// parse.
public static class UsageDecoder
{
    public static UsageSnapshot Snapshot(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new UsageException(UsageError.MalformedResponse);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new UsageException(UsageError.MalformedResponse);

            var buckets = new Dictionary<string, UsageBucket>();
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object) continue;
                // JsonValueKind.Number automatically excludes true/false: JSON
                // booleans get their own ValueKind, so this also rejects a
                // boolean utilization without a separate check.
                if (!property.Value.TryGetProperty("utilization", out var utilization) ||
                    utilization.ValueKind != JsonValueKind.Number) continue;

                buckets[property.Name] = new UsageBucket(
                    utilization.GetDouble(),
                    ResetDate(property.Value.TryGetProperty("resets_at", out var resetsAt) ? resetsAt : default));
            }

            FoldScopedLimits(root, buckets);

            if (buckets.Count == 0) throw new UsageException(UsageError.MalformedResponse);
            return new UsageSnapshot(buckets);
        }
    }

    /// Accepts both an ISO 8601 string and a unix timestamp. The exact wire
    /// format could not be observed while writing this, so both are handled.
    private static DateTimeOffset? ResetDate(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (text is not null &&
                DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            var seconds = value.GetDouble();
            if (seconds > 0) return DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
        }

        return null;
    }

    /// Folds per-model weekly limits from the `limits` array into synthetic
    /// `seven_day_<model>` buckets.
    ///
    /// Since the Fable launch the server leaves the legacy top-level
    /// `seven_day_<model>` fields null and reports scoped models only here, so
    /// without this the per-model dial has nothing to show. Synthesising the
    /// same key shape the flat fields used means every consumer downstream —
    /// bucket selection, the dial, the menu picker — keeps working unchanged,
    /// and a per-model limit added later appears on its own.
    private static void FoldScopedLimits(JsonElement root, Dictionary<string, UsageBucket> buckets)
    {
        if (!root.TryGetProperty("limits", out var limits) || limits.ValueKind != JsonValueKind.Array) return;

        foreach (var entry in limits.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            if (!entry.TryGetProperty("kind", out var kind) ||
                kind.ValueKind != JsonValueKind.String ||
                kind.GetString() != "weekly_scoped") continue;
            if (!entry.TryGetProperty("percent", out var percent) ||
                percent.ValueKind != JsonValueKind.Number) continue;
            if (!entry.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object) continue;
            if (!scope.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.Object) continue;
            if (!model.TryGetProperty("display_name", out var displayName) ||
                displayName.ValueKind != JsonValueKind.String) continue;

            var name = Slug(displayName.GetString() ?? string.Empty);
            if (name.Length == 0) continue;
            var key = "seven_day_" + name;
            // A real top-level bucket is authoritative; this only fills gaps.
            if (buckets.ContainsKey(key)) continue;

            buckets[key] = new UsageBucket(
                percent.GetDouble(),
                ResetDate(entry.TryGetProperty("resets_at", out var resetsAt) ? resetsAt : default));
        }
    }

    /// "Claude Opus 4.5" -> "claude_opus_4_5"
    private static string Slug(string displayName)
    {
        var lowered = displayName.ToLowerInvariant();
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var c in lowered)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
                continue;
            }
            if (current.Length > 0) parts.Add(current.ToString());
            current.Clear();
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return string.Join("_", parts);
    }
}
