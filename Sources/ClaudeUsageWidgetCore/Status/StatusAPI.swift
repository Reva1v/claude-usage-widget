import Foundation

/// Reads Claude's public status page. No credentials: the endpoint is open.
public struct StatusAPI: Sendable {
    public static let endpoint = URL(string: "https://status.claude.com/api/v2/summary.json")!

    private let session: URLSession

    public init(session: URLSession = UsageAPI.connectivityAwareSession) {
        self.session = session
    }

    public func fetch() async throws -> ServiceStatus {
        var request = URLRequest(url: Self.endpoint)
        request.httpMethod = "GET"
        request.setValue("application/json", forHTTPHeaderField: "Accept")

        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch {
            throw UsageError.network(error.localizedDescription)
        }

        guard let http = response as? HTTPURLResponse else { throw UsageError.malformedResponse }
        try UsageAPI.validate(statusCode: http.statusCode)
        return try StatusDecoder.status(from: data)
    }
}
