namespace ClaudeUsageWidget.Core.Tests;

public class ServiceStatusTests
{
    [Fact]
    public void MapsComponentStatuses()
    {
        Assert.Equal(ServiceStatus.Operational, ServiceStatusParser.Component("operational"));
        Assert.Equal(ServiceStatus.Degraded, ServiceStatusParser.Component("degraded_performance"));
        Assert.Equal(ServiceStatus.PartialOutage, ServiceStatusParser.Component("partial_outage"));
        Assert.Equal(ServiceStatus.MajorOutage, ServiceStatusParser.Component("major_outage"));
        Assert.Equal(ServiceStatus.Maintenance, ServiceStatusParser.Component("under_maintenance"));
    }

    [Fact]
    public void MapsIndicators()
    {
        Assert.Equal(ServiceStatus.Operational, ServiceStatusParser.Indicator("none"));
        Assert.Equal(ServiceStatus.Degraded, ServiceStatusParser.Indicator("minor"));
        Assert.Equal(ServiceStatus.PartialOutage, ServiceStatusParser.Indicator("major"));
        Assert.Equal(ServiceStatus.MajorOutage, ServiceStatusParser.Indicator("critical"));
        Assert.Equal(ServiceStatus.Maintenance, ServiceStatusParser.Indicator("maintenance"));
    }

    [Fact]
    public void UnknownValuesAreUnknownNotAGuess()
    {
        Assert.Equal(ServiceStatus.Unknown, ServiceStatusParser.Component("teleported"));
        Assert.Equal(ServiceStatus.Unknown, ServiceStatusParser.Indicator(""));
    }

    [Fact]
    public void EveryCaseHasAShortLabelThatFitsTheDial()
    {
        Assert.Equal("OK", ServiceStatusText.Label(ServiceStatus.Operational));
        Assert.Equal("SLOW", ServiceStatusText.Label(ServiceStatus.Degraded));
        Assert.Equal("PARTIAL", ServiceStatusText.Label(ServiceStatus.PartialOutage));
        Assert.Equal("DOWN", ServiceStatusText.Label(ServiceStatus.MajorOutage));
        Assert.Equal("MAINT", ServiceStatusText.Label(ServiceStatus.Maintenance));
        Assert.Equal("—", ServiceStatusText.Label(ServiceStatus.Unknown));
    }
}

public class StatusDecoderTests
{
    private const string Payload = """
    {
      "status": { "indicator": "none", "description": "All Systems Operational" },
      "components": [
        { "name": "claude.ai", "status": "operational" },
        { "name": "Claude Code", "status": "degraded_performance" },
        { "name": "Claude Cowork", "status": "operational" }
      ]
    }
    """;

    [Fact]
    public void TheClaudeCodeComponentWinsOverThePageRollUp() =>
        Assert.Equal(ServiceStatus.Degraded, StatusDecoder.Status(Payload));

    [Fact]
    public void PrefersClaudeCodeComponent()
    {
        var status = StatusDecoder.Status("""
        {
          "components": [
            { "name": "Other", "status": "major_outage" },
            { "name": "Claude Code", "status": "operational" }
          ],
          "status": { "indicator": "critical" }
        }
        """);
        Assert.Equal(ServiceStatus.Operational, status);
    }

    [Fact]
    public void ThePageIndicatorIsTheFallbackWhenTheComponentIsAbsent()
    {
        const string json = """
        {
          "status": { "indicator": "critical" },
          "components": [ { "name": "claude.ai", "status": "operational" } ]
        }
        """;
        Assert.Equal(ServiceStatus.MajorOutage, StatusDecoder.Status(json));
    }

    [Fact]
    public void ABodyWithNeitherComponentNorIndicatorIsMalformed() =>
        Assert.Equal(UsageError.MalformedResponse,
            Assert.Throws<UsageException>(() => StatusDecoder.Status("""{"components": []}""")).Error);

    [Fact]
    public void ANonJsonBodyIsMalformed() =>
        Assert.Equal(UsageError.MalformedResponse,
            Assert.Throws<UsageException>(() => StatusDecoder.Status("<html>nope</html>")).Error);
}
