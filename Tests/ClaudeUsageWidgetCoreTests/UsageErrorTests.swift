import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("UsageError")
struct UsageErrorTests {
    @Test("every case reads as a sentence, not as Swift syntax")
    func readableMessages() {
        #expect(UsageError.noCredentials.localizedDescription
            == "No Claude.ai web session was found.")
        #expect(UsageError.unauthorized.localizedDescription
            == "The Claude.ai session expired. Sign in again from the widget menu.")
        #expect(UsageError.malformedResponse.localizedDescription
            == "The server returned something unexpected.")
    }

    @Test("a rate limit reads the same with and without a Retry-After")
    func rateLimitedMessage() {
        #expect(UsageError.rateLimited(retryAfterSeconds: 1378).localizedDescription
            == "The API is rate limited. The widget retries on its own.")
        #expect(UsageError.rateLimited(retryAfterSeconds: nil).localizedDescription
            == "The API is rate limited. The widget retries on its own.")
    }

    @Test("a network failure carries its own detail through")
    func networkDetail() {
        #expect(UsageError.network("The Internet connection appears to be offline.").localizedDescription
            == "The Internet connection appears to be offline.")
    }

    @Test("no message leaks Swift enum syntax")
    func noEnumSyntax() {
        let messages = [
            UsageError.noCredentials,
            .unauthorized,
            .malformedResponse,
            .rateLimited(retryAfterSeconds: 60),
            .network("offline"),
        ].map(\.localizedDescription)

        for message in messages {
            #expect(!message.contains("("))
            #expect(!message.contains("\""))
        }
    }
}
