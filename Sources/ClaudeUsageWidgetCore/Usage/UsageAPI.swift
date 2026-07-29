import Foundation

/// The one HTTP call this widget makes.
public struct UsageAPI: Sendable {
    public static let endpoint = URL(string: "https://api.anthropic.com/api/oauth/usage")!

    /// The beta opt-in Claude Code itself sends with oauth-authenticated calls.
    static let betaHeader = "oauth-2025-04-20"

    private let session: URLSession

    public init(session: URLSession = .shared) {
        self.session = session
    }

    public func fetch(token: String) async throws -> UsageSnapshot {
        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: Self.request(token: token))
        } catch {
            throw UsageError.network(error.localizedDescription)
        }

        guard let http = response as? HTTPURLResponse else { throw UsageError.malformedResponse }
        try Self.validate(statusCode: http.statusCode)
        return try UsageDecoder.snapshot(from: data)
    }

    /// Split out from `fetch` so the header set is assertable without a network
    /// round trip.
    static func request(token: String) -> URLRequest {
        var request = URLRequest(url: endpoint)
        request.httpMethod = "GET"
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        request.setValue(betaHeader, forHTTPHeaderField: "anthropic-beta")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        return request
    }

    /// Maps a status code onto the widget's error vocabulary.
    static func validate(statusCode: Int) throws {
        switch statusCode {
        case 200..<300: return
        case 401, 403: throw UsageError.unauthorized
        default: throw UsageError.network("HTTP \(statusCode)")
        }
    }
}
