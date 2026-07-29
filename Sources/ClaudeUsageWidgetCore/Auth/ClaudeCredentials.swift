import Foundation
import Security

/// Reads the Claude Code OAuth access token from the login keychain.
///
/// Read-only by design: Claude Code owns these credentials and refreshes them
/// itself. The widget never writes, deletes, or refreshes the item — when the
/// token has expired the fetch fails with `.unauthorized` and the next Claude
/// Code session puts a fresh one in place.
public enum ClaudeCredentials {
    /// Generic-password service under which Claude Code stores its credentials.
    public static let service = "Claude Code-credentials"

    public static func accessToken() -> String? {
        guard let data = itemData() else { return nil }
        return accessToken(fromItemData: data)
    }

    private static func itemData() -> Data? {
        // Queried by service alone: the account is the macOS user name, and
        // matching on it adds a failure mode without adding precision — the
        // service is already unique to Claude Code.
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var result: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess else { return nil }
        return result as? Data
    }

    /// Pulls the access token out of the stored JSON blob. Handles both the
    /// nested `claudeAiOauth` shape Claude Code writes today and a flat blob.
    static func accessToken(fromItemData data: Data) -> String? {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        if let oauth = root["claudeAiOauth"] as? [String: Any],
           let token = oauth["accessToken"] as? String {
            return token
        }
        return root["accessToken"] as? String
    }
}
