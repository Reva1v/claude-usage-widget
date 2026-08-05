using System.Text.Json;

namespace ClaudeUsageWidget.Core;

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
