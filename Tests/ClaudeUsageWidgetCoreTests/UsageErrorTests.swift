import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("UsageError")
struct UsageErrorTests {
    @Test("every case reads as a sentence, not as Swift syntax")
    func readableMessages() {
        #expect(UsageError.noCredentials.localizedDescription
            == "No Claude Code credentials were found in the keychain.")
        #expect(UsageError.unauthorized.localizedDescription
            == "The token was rejected. Sign in to Claude Code again.")
        #expect(UsageError.malformedResponse.localizedDescription
            == "The server returned something unexpected.")
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
            .network("offline"),
        ].map(\.localizedDescription)

        for message in messages {
            #expect(!message.contains("("))
            #expect(!message.contains("\""))
        }
    }
}
