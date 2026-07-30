import Foundation

/// How Claude's own status page describes the service, reduced to the handful
/// of states worth showing on a dial.
public enum ServiceStatus: Equatable, Sendable {
    case operational
    case degraded
    case partialOutage
    case majorOutage
    case maintenance
    case unknown

    /// Short enough to sit inside a 68 pt dial.
    public var label: String {
        switch self {
        case .operational: "OK"
        case .degraded: "SLOW"
        case .partialOutage: "PARTIAL"
        case .majorOutage: "DOWN"
        case .maintenance: "MAINT"
        case .unknown: "—"
        }
    }

    /// A per-component `status` string from the status page.
    public static func component(_ raw: String) -> ServiceStatus {
        switch raw {
        case "operational": .operational
        case "degraded_performance": .degraded
        case "partial_outage": .partialOutage
        case "major_outage": .majorOutage
        case "under_maintenance": .maintenance
        default: .unknown
        }
    }

    /// The page-wide `status.indicator` string.
    public static func indicator(_ raw: String) -> ServiceStatus {
        switch raw {
        case "none": .operational
        case "minor": .degraded
        case "major": .partialOutage
        case "critical": .majorOutage
        case "maintenance": .maintenance
        default: .unknown
        }
    }
}

/// Reads a Statuspage summary body.
public enum StatusDecoder {
    /// The component this widget cares about. Everything else on the page is
    /// about other surfaces.
    static let componentName = "Claude Code"

    public static func status(from data: Data) throws -> ServiceStatus {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw UsageError.malformedResponse
        }

        if let components = root["components"] as? [Any] {
            for entry in components {
                guard let component = entry as? [String: Any],
                      component["name"] as? String == componentName,
                      let raw = component["status"] as? String
                else { continue }
                let status = ServiceStatus.component(raw)
                if status != .unknown { return status }
            }
        }

        // The named component may be renamed or retired; the page-wide roll-up
        // still says something useful.
        if let page = root["status"] as? [String: Any],
           let indicator = page["indicator"] as? String {
            let status = ServiceStatus.indicator(indicator)
            if status != .unknown { return status }
        }

        throw UsageError.malformedResponse
    }
}
