import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("ServiceStatus")
struct ServiceStatusTests {
    @Test("component statuses map onto the widget's vocabulary")
    func mapsComponentStatuses() {
        #expect(ServiceStatus.component("operational") == .operational)
        #expect(ServiceStatus.component("degraded_performance") == .degraded)
        #expect(ServiceStatus.component("partial_outage") == .partialOutage)
        #expect(ServiceStatus.component("major_outage") == .majorOutage)
        #expect(ServiceStatus.component("under_maintenance") == .maintenance)
    }

    @Test("page indicators map onto the same vocabulary")
    func mapsIndicators() {
        #expect(ServiceStatus.indicator("none") == .operational)
        #expect(ServiceStatus.indicator("minor") == .degraded)
        #expect(ServiceStatus.indicator("major") == .partialOutage)
        #expect(ServiceStatus.indicator("critical") == .majorOutage)
        #expect(ServiceStatus.indicator("maintenance") == .maintenance)
    }

    @Test("an unrecognised value is unknown, not a guess")
    func unknownValues() {
        #expect(ServiceStatus.component("teleported") == .unknown)
        #expect(ServiceStatus.indicator("") == .unknown)
    }

    @Test("every case has a short label that fits the dial")
    func labels() {
        #expect(ServiceStatus.operational.label == "OK")
        #expect(ServiceStatus.degraded.label == "SLOW")
        #expect(ServiceStatus.partialOutage.label == "PARTIAL")
        #expect(ServiceStatus.majorOutage.label == "DOWN")
        #expect(ServiceStatus.maintenance.label == "MAINT")
        #expect(ServiceStatus.unknown.label == "—")
    }
}

@Suite("StatusDecoder")
struct StatusDecoderTests {
    static let payload = Data("""
    {
      "status": { "indicator": "none", "description": "All Systems Operational" },
      "components": [
        { "name": "claude.ai", "status": "operational" },
        { "name": "Claude Code", "status": "degraded_performance" },
        { "name": "Claude Cowork", "status": "operational" }
      ]
    }
    """.utf8)

    @Test("the Claude Code component wins over the page roll-up")
    func prefersClaudeCode() throws {
        #expect(try StatusDecoder.status(from: Self.payload) == .degraded)
    }

    @Test("the page indicator is the fallback when the component is absent")
    func fallsBackToIndicator() throws {
        let data = Data("""
        {
          "status": { "indicator": "critical" },
          "components": [ { "name": "claude.ai", "status": "operational" } ]
        }
        """.utf8)
        #expect(try StatusDecoder.status(from: data) == .majorOutage)
    }

    @Test("a body with neither component nor indicator is malformed")
    func rejectsUselessBody() {
        #expect(throws: UsageError.malformedResponse) {
            try StatusDecoder.status(from: Data(#"{"components": []}"#.utf8))
        }
    }

    @Test("a non-JSON body is malformed")
    func rejectsGarbage() {
        #expect(throws: UsageError.malformedResponse) {
            try StatusDecoder.status(from: Data("<html>nope</html>".utf8))
        }
    }
}
