using System.Text.Json;

namespace ClaudeUsageWidget.Core;

/// How Claude's own status page describes the service, reduced to the handful
/// of states worth showing on a dial.
public enum ServiceStatus
{
    Operational,
    Degraded,
    PartialOutage,
    MajorOutage,
    Maintenance,
    Unknown,
}

public static class ServiceStatusText
{
    /// Short enough to sit inside a 68 pt dial.
    public static string Label(ServiceStatus status) => status switch
    {
        ServiceStatus.Operational => "OK",
        ServiceStatus.Degraded => "SLOW",
        ServiceStatus.PartialOutage => "PARTIAL",
        ServiceStatus.MajorOutage => "DOWN",
        ServiceStatus.Maintenance => "MAINT",
        ServiceStatus.Unknown => "—",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}

public static class ServiceStatusParser
{
    /// A per-component `status` string from the status page.
    public static ServiceStatus Component(string raw) => raw switch
    {
        "operational" => ServiceStatus.Operational,
        "degraded_performance" => ServiceStatus.Degraded,
        "partial_outage" => ServiceStatus.PartialOutage,
        "major_outage" => ServiceStatus.MajorOutage,
        "under_maintenance" => ServiceStatus.Maintenance,
        _ => ServiceStatus.Unknown,
    };

    /// The page-wide `status.indicator` string.
    public static ServiceStatus Indicator(string raw) => raw switch
    {
        "none" => ServiceStatus.Operational,
        "minor" => ServiceStatus.Degraded,
        "major" => ServiceStatus.PartialOutage,
        "critical" => ServiceStatus.MajorOutage,
        "maintenance" => ServiceStatus.Maintenance,
        _ => ServiceStatus.Unknown,
    };
}

/// Reads a Statuspage summary body.
public static class StatusDecoder
{
    /// The component this widget cares about. Everything else on the page is
    /// about other surfaces.
    private const string ComponentName = "Claude Code";

    public static ServiceStatus Status(string json)
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

            if (root.TryGetProperty("components", out var components) &&
                components.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in components.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object) continue;
                    if (!entry.TryGetProperty("name", out var name) ||
                        name.ValueKind != JsonValueKind.String ||
                        name.GetString() != ComponentName) continue;
                    if (!entry.TryGetProperty("status", out var statusProperty) ||
                        statusProperty.ValueKind != JsonValueKind.String) continue;

                    var status = ServiceStatusParser.Component(statusProperty.GetString()!);
                    if (status != ServiceStatus.Unknown) return status;
                }
            }

            // The named component may be renamed or retired; the page-wide roll-up
            // still says something useful.
            if (root.TryGetProperty("status", out var page) && page.ValueKind == JsonValueKind.Object &&
                page.TryGetProperty("indicator", out var indicator) &&
                indicator.ValueKind == JsonValueKind.String)
            {
                var status = ServiceStatusParser.Indicator(indicator.GetString()!);
                if (status != ServiceStatus.Unknown) return status;
            }

            throw new UsageException(UsageError.MalformedResponse);
        }
    }
}
