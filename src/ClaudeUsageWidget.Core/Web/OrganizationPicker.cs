using System.Text.Json;

namespace ClaudeUsageWidget.Core;

/// What the picked organization says about its subscription, exactly as the API
/// wrote it. Stored raw on <see cref="AccountProfile"/> so a mapping fix in
/// <see cref="SubscriptionTier"/> reaches an existing install without another
/// round trip to claude.ai.
/// <param name="Capabilities">Null ONLY when the organization was not found (or
/// the body was not JSON) — a found one with no `capabilities` array gives an
/// empty list. That distinction is the sentinel the session backfills on: null
/// means never asked, empty means asked and told nothing.</param>
public sealed record OrganizationFields(
    IReadOnlyList<string>? Capabilities,
    string? RateLimitTier,
    string? RavenType)
{
    public static readonly OrganizationFields Unknown = new(null, null, null);
}

/// Chooses which Claude.ai organization to read usage for, from the raw
/// `/api/organizations` response body.
///
/// Ports the selection rule in `fetchOrganizationID` in
/// `Sources/ClaudeUsageWidget/ClaudeWebSession.swift:58-74`: only
/// organizations with `chat` in their capabilities are candidates, a "team"
/// one is preferred over any other kind, and the id comes from `uuid`,
/// falling back to `id`.
public static class OrganizationPicker
{
    public static string? Pick(string organizationsJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(organizationsJson);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;

            JsonElement? firstChatCapable = null;
            JsonElement? team = null;

            foreach (var organization in document.RootElement.EnumerateArray())
            {
                if (organization.ValueKind != JsonValueKind.Object) continue;
                if (!HasChatCapability(organization)) continue;

                firstChatCapable ??= organization;
                if (team is null && StringProperty(organization, "raven_type") == "team") team = organization;
            }

            var selected = team ?? firstChatCapable;
            return selected is null ? null : Id(selected.Value);
        }
    }

    /// One log line's worth of what the picked organization says about its
    /// subscription: every property NAME (the shape of the object, so a new
    /// field is noticed), and the VALUE of anything tier-like — a name
    /// containing `tier`, `plan`, `billing`, `subscription`, `raven_type` or
    /// `capabilities`. Written for the 2026-09-04 ask to show the plan under the
    /// account name: the fixture this picker was written against carries none
    /// of it, and the live shape is what decides the field to read.
    public static string Describe(string organizationsJson, string? pickedId)
    {
        try
        {
            using var document = JsonDocument.Parse(organizationsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return "not-an-array";
            if (Picked(document.RootElement, pickedId) is not { } organization)
                return "picked-organization-not-found";

            var names = new List<string>();
            var tierish = new List<string>();
            foreach (var property in organization.EnumerateObject())
            {
                names.Add(property.Name);
                var lower = property.Name.ToLowerInvariant();
                if (lower.Contains("tier") || lower.Contains("plan") || lower.Contains("billing") ||
                    lower.Contains("subscription") || lower == "raven_type" || lower == "capabilities")
                {
                    tierish.Add($"{property.Name}={property.Value.GetRawText()}");
                }
            }
            return $"keys={string.Join(',', names)} {string.Join(' ', tierish)}";
        }
        catch (JsonException)
        {
            return "not-json";
        }
    }

    /// The subscription-shaped fields of the picked organization, RAW — the
    /// same walk <see cref="Describe"/> takes, reading three named properties
    /// instead of logging all of them.
    ///
    /// Never throws: a body that will not parse, an id that is no longer in it
    /// and a property of the wrong JSON type all come back as "unknown" rather
    /// than as an exception in the middle of a poll.
    public static OrganizationFields Fields(string organizationsJson, string? pickedId)
    {
        try
        {
            using var document = JsonDocument.Parse(organizationsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return OrganizationFields.Unknown;
            if (Picked(document.RootElement, pickedId) is not { } organization)
                return OrganizationFields.Unknown;

            return new OrganizationFields(
                Capabilities(organization),
                StringProperty(organization, "rate_limit_tier"),
                StringProperty(organization, "raven_type"));
        }
        catch (JsonException)
        {
            return OrganizationFields.Unknown;
        }
    }

    /// The first organization matching `pickedId`, or the first object at all
    /// when no id is given. One walk for both readers, so the log line and the
    /// stored fields cannot come from different organizations.
    private static JsonElement? Picked(JsonElement root, string? pickedId)
    {
        foreach (var organization in root.EnumerateArray())
        {
            if (organization.ValueKind != JsonValueKind.Object) continue;
            if (pickedId is not null && Id(organization) != pickedId) continue;
            return organization;
        }

        return null;
    }

    /// Non-string entries are skipped rather than stringified: the list feeds
    /// name comparisons, and a `7` in it can only ever be noise. The list is
    /// empty, never null, for an organization that was found — that is what
    /// makes null mean "never fetched" everywhere else.
    private static IReadOnlyList<string> Capabilities(JsonElement organization)
    {
        if (!organization.TryGetProperty("capabilities", out var capabilities) ||
            capabilities.ValueKind != JsonValueKind.Array) return [];

        return capabilities.EnumerateArray()
            .Where(capability => capability.ValueKind == JsonValueKind.String)
            .Select(capability => capability.GetString()!)
            .ToList();
    }

    private static bool HasChatCapability(JsonElement organization)
    {
        if (!organization.TryGetProperty("capabilities", out var capabilities) ||
            capabilities.ValueKind != JsonValueKind.Array) return false;

        foreach (var capability in capabilities.EnumerateArray())
        {
            if (capability.ValueKind == JsonValueKind.String && capability.GetString() == "chat") return true;
        }
        return false;
    }

    private static string? Id(JsonElement organization)
    {
        var id = StringProperty(organization, "uuid") ?? StringProperty(organization, "id");
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
