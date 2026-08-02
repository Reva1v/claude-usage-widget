import AppKit
import ClaudeUsageWidgetCore
import Foundation
import WebKit

// Keep the claimed browser aligned with the actual engine. Cloudflare's
// Turnstile compares more than the UA string; claiming Chrome from WKWebView
// produces a fingerprint mismatch and an endless “verify you are human” loop.
private let claudeBrowserUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) "
    + "AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Safari/605.1.15"

/// Reads Claude subscription usage through the authenticated claude.ai web
/// session. This deliberately avoids api.anthropic.com/api/oauth/usage: that
/// OAuth endpoint has a separate, sticky Cloudflare rate limit.
@MainActor
final class ClaudeWebSession {
    private static let organizationKey = "claudeAiOrganizationID"

    private let dataStore = WKWebsiteDataStore.default()
    private var activeFetcher: WebPageJSONFetcher?

    func fetchUsage() async throws -> UsageSnapshot {
        guard await hasSessionCookie() else { throw UsageError.noCredentials }

        let organizationID: String
        if let saved = UserDefaults.standard.string(forKey: Self.organizationKey), !saved.isEmpty {
            organizationID = saved
        } else {
            organizationID = try await fetchOrganizationID()
            UserDefaults.standard.set(organizationID, forKey: Self.organizationKey)
        }

        do {
            let data = try await fetchJSON(
                URL(string: "https://claude.ai/api/organizations/\(organizationID)/usage")!
            )
            return try UsageDecoder.snapshot(from: data)
        } catch UsageError.unauthorized {
            UserDefaults.standard.removeObject(forKey: Self.organizationKey)
            throw UsageError.unauthorized
        }
    }

    func clearCachedOrganization() {
        UserDefaults.standard.removeObject(forKey: Self.organizationKey)
    }

    func hasSessionCookie() async -> Bool {
        await withCheckedContinuation { continuation in
            dataStore.httpCookieStore.getAllCookies { cookies in
                continuation.resume(returning: cookies.contains {
                    $0.name == "sessionKey" && $0.domain.hasSuffix("claude.ai") && !$0.value.isEmpty
                })
            }
        }
    }

    private func fetchOrganizationID() async throws -> String {
        let data = try await fetchJSON(URL(string: "https://claude.ai/api/organizations")!)
        guard let organizations = try? JSONSerialization.jsonObject(with: data) as? [[String: Any]] else {
            throw UsageError.malformedResponse
        }

        let chatOrganizations = organizations.filter { organization in
            (organization["capabilities"] as? [String])?.contains("chat") == true
        }
        let selected = chatOrganizations.first(where: { $0["raven_type"] as? String == "team" })
            ?? chatOrganizations.first
        guard let selected,
              let id = (selected["uuid"] as? String) ?? (selected["id"] as? String),
              !id.isEmpty
        else { throw UsageError.malformedResponse }
        return id
    }

    private func fetchJSON(_ url: URL) async throws -> Data {
        let fetcher = WebPageJSONFetcher(dataStore: dataStore)
        activeFetcher = fetcher
        defer { activeFetcher = nil }
        return try await fetcher.fetch(url)
    }
}

/// Loads an API URL as a browser page and extracts its rendered JSON body.
/// Cloudflare therefore sees WebKit navigation with the same cookie jar used
/// for login, rather than a headless URLSession request.
@MainActor
private final class WebPageJSONFetcher: NSObject, WKNavigationDelegate {
    private let webView: WKWebView
    private var continuation: CheckedContinuation<Data, Error>?
    private var responseStatus: Int?

    init(dataStore: WKWebsiteDataStore) {
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = dataStore
        webView = WKWebView(frame: NSRect(x: 0, y: 0, width: 800, height: 600), configuration: configuration)
        super.init()
        webView.customUserAgent = claudeBrowserUserAgent
        webView.navigationDelegate = self
    }

    func fetch(_ url: URL) async throws -> Data {
        try await withCheckedThrowingContinuation { continuation in
            self.continuation = continuation
            webView.load(URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 30))
        }
    }

    func webView(
        _ webView: WKWebView,
        decidePolicyFor navigationResponse: WKNavigationResponse,
        decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void
    ) {
        responseStatus = (navigationResponse.response as? HTTPURLResponse)?.statusCode
        decisionHandler(.allow)
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        webView.evaluateJavaScript("document.body.innerText || document.body.textContent || ''") { [weak self] value, error in
            MainActor.assumeIsolated {
                guard let self else { return }
                if let error {
                    self.finish(throwing: UsageError.network(error.localizedDescription))
                    return
                }
                switch self.responseStatus {
                case 401, 403:
                    self.finish(throwing: UsageError.unauthorized)
                case 429:
                    self.finish(throwing: UsageError.rateLimited(retryAfterSeconds: nil))
                case let status? where !(200...299).contains(status):
                    self.finish(throwing: UsageError.network("Claude.ai returned HTTP \(status)."))
                default:
                    guard let text = value as? String else {
                        self.finish(throwing: UsageError.malformedResponse)
                        return
                    }
                    self.finish(returning: Data(text.utf8))
                }
            }
        }
    }

    func webView(
        _ webView: WKWebView,
        didFail navigation: WKNavigation!,
        withError error: Error
    ) {
        finish(throwing: UsageError.network(error.localizedDescription))
    }

    func webView(
        _ webView: WKWebView,
        didFailProvisionalNavigation navigation: WKNavigation!,
        withError error: Error
    ) {
        finish(throwing: UsageError.network(error.localizedDescription))
    }

    private func finish(returning data: Data) {
        continuation?.resume(returning: data)
        continuation = nil
        webView.stopLoading()
    }

    private func finish(throwing error: Error) {
        continuation?.resume(throwing: error)
        continuation = nil
        webView.stopLoading()
    }
}

@MainActor
final class ClaudeLoginWindowController: NSWindowController, WKHTTPCookieStoreObserver, WKNavigationDelegate {
    private let cookieStore: WKHTTPCookieStore
    private let onSignedIn: () -> Void
    private var completed = false

    init(dataStore: WKWebsiteDataStore, onSignedIn: @escaping () -> Void) {
        self.cookieStore = dataStore.httpCookieStore
        self.onSignedIn = onSignedIn

        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = dataStore
        let webView = WKWebView(frame: NSRect(x: 0, y: 0, width: 1000, height: 720), configuration: configuration)
        let window = NSWindow(
            contentRect: webView.frame,
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "Sign in to Claude.ai"
        window.contentView = webView

        super.init(window: window)
        cookieStore.add(self)
        webView.customUserAgent = claudeBrowserUserAgent
        webView.navigationDelegate = self

        // This data store belongs only to the widget. Clear failed Turnstile
        // state as well as caches; otherwise a rejected challenge survives the
        // rebuild and immediately resumes the same loop.
        let websiteDataTypes = WKWebsiteDataStore.allWebsiteDataTypes()
        dataStore.removeData(ofTypes: websiteDataTypes, modifiedSince: .distantPast) {
            webView.load(URLRequest(
                url: URL(string: "https://claude.ai/login")!,
                cachePolicy: .reloadIgnoringLocalCacheData
            ))
        }
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { nil }

    override func close() {
        cookieStore.remove(self)
        super.close()
    }

    func cookiesDidChange(in cookieStore: WKHTTPCookieStore) {
        cookieStore.getAllCookies { [weak self] cookies in
            guard cookies.contains(where: {
                $0.name == "sessionKey" && $0.domain.hasSuffix("claude.ai") && !$0.value.isEmpty
            }) else { return }
            Task { @MainActor [weak self] in
                guard let self, !completed else { return }
                completed = true
                close()
                onSignedIn()
            }
        }
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        window?.title = "Sign in to Claude.ai — \(webView.url?.host ?? "loaded")"
    }

    func webView(
        _ webView: WKWebView,
        didFail navigation: WKNavigation!,
        withError error: Error
    ) {
        showLoadError(error)
    }

    func webView(
        _ webView: WKWebView,
        didFailProvisionalNavigation navigation: WKNavigation!,
        withError error: Error
    ) {
        showLoadError(error)
    }

    private func showLoadError(_ error: Error) {
        guard let webView = window?.contentView as? WKWebView else { return }
        let escaped = error.localizedDescription
            .replacingOccurrences(of: "&", with: "&amp;")
            .replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;")
        webView.loadHTMLString(
            "<main style='font:16px -apple-system;padding:32px'>Claude.ai failed to load:<br><br>\(escaped)</main>",
            baseURL: nil
        )
    }
}
