import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("UsageAPI")
struct UsageAPITests {
    @Test("builds the request the endpoint expects")
    func buildsRequest() {
        let request = UsageAPI.request(token: "sk-ant-oat01-example")
        #expect(request.url?.absoluteString == "https://api.anthropic.com/api/oauth/usage")
        #expect(request.httpMethod == "GET")
        #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer sk-ant-oat01-example")
        #expect(request.value(forHTTPHeaderField: "anthropic-beta") == "oauth-2025-04-20")
        #expect(request.value(forHTTPHeaderField: "Accept") == "application/json")
    }

    @Test("a 2xx status passes")
    func acceptsSuccess() throws {
        try UsageAPI.validate(statusCode: 200)
        try UsageAPI.validate(statusCode: 204)
    }

    @Test("401 and 403 mean the token is no longer good")
    func rejectsUnauthorized() {
        #expect(throws: UsageError.unauthorized) { try UsageAPI.validate(statusCode: 401) }
        #expect(throws: UsageError.unauthorized) { try UsageAPI.validate(statusCode: 403) }
    }

    @Test("429 carries the server's Retry-After through")
    func rejectsRateLimited() {
        #expect(throws: UsageError.rateLimited(retryAfterSeconds: 1378)) {
            try UsageAPI.validate(statusCode: 429, retryAfterSeconds: 1378)
        }
        #expect(throws: UsageError.rateLimited(retryAfterSeconds: nil)) {
            try UsageAPI.validate(statusCode: 429)
        }
    }

    @Test("any other status is a network error naming the code")
    func rejectsOtherStatuses() {
        #expect(throws: UsageError.network("HTTP 500")) { try UsageAPI.validate(statusCode: 500) }
    }
}
