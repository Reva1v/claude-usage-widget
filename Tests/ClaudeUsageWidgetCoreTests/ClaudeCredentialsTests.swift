import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("ClaudeCredentials")
struct ClaudeCredentialsTests {
    @Test("reads the token out of the claudeAiOauth blob")
    func readsNestedToken() {
        let data = Data("""
        {
          "claudeAiOauth": {
            "accessToken": "sk-ant-oat01-example",
            "refreshToken": "sk-ant-ort01-example",
            "expiresAt": 1785348000
          }
        }
        """.utf8)
        #expect(ClaudeCredentials.accessToken(fromItemData: data) == "sk-ant-oat01-example")
    }

    @Test("accepts a flat blob that carries the token at the top level")
    func readsFlatToken() {
        let data = Data(#"{"accessToken": "sk-ant-oat01-flat"}"#.utf8)
        #expect(ClaudeCredentials.accessToken(fromItemData: data) == "sk-ant-oat01-flat")
    }

    @Test("returns nil for a blob without a token")
    func rejectsTokenlessBlob() {
        #expect(ClaudeCredentials.accessToken(fromItemData: Data(#"{"claudeAiOauth": {}}"#.utf8)) == nil)
    }

    @Test("returns nil for a blob that is not JSON")
    func rejectsGarbage() {
        #expect(ClaudeCredentials.accessToken(fromItemData: Data("not json".utf8)) == nil)
    }
}
