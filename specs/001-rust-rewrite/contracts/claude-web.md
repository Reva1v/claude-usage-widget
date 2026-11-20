# Contract: claude.ai through WebView2

Rules owner: `cuw-claude` (pure). Transport owner: `cuw-webview`. Source: INV 2B §1-§5.
No HTTP client ever talks to claude.ai; no custom headers, no UA override, no auth header.

## Environment and profiles

| Item | Value |
|---|---|
| UDF | `%LOCALAPPDATA%\ClaudeUsageWidget\profiles\default` (created before use) |
| Environment | one per process; `CreateCoreWebView2EnvironmentWithOptions(null, UDF, None)`; failed creation not cached |
| Account `default` | controller without options -> implicit profile `<UDF>\EBWebView\Default` |
| Other accounts | `ControllerOptions.ProfileName = <account id>` -> `<UDF>\EBWebView\WV2Profile_<id>` |
| Verify | named profile: `Profile.ProfileName` equals requested, ordinal-ignore-case, else error `WebView2 gave profile '<actual>' for account '<label>', expected '<name>'.` |
| Invariant | login and fetch controllers of one account are built by the same helper |

## Requests

| Purpose | Mechanism | URL |
|---|---|---|
| Organizations | top-level `Navigate` in the hidden fetch webview | `https://claude.ai/api/organizations` |
| Usage | same | `https://claude.ai/api/organizations/{orgId}/usage` (id inserted raw) |
| Sign-in | visible login window | `https://claude.ai/login` and any pasted `https://` URL |
| Cookie read | `CookieManager.GetCookies("https://claude.ai")` | - |

Body read after a successful navigation: `ExecuteScript("document.body.innerText ||
document.body.textContent || ''")`; result is a JSON string literal: `serde_json::from_str::<
Option<String>>` -> `None` = `MalformedResponse`, parse error = `Network(<serde message>)`.

## Navigation outcome (`cuw-claude::nav::classify`)

Input `(http_status: u16, is_success: bool, web_error: WebErrorStatus)`, evaluated in this order:

1. `http != 0` and not 2xx: log `nav-http path=<abs path> http=<code> success=<True|False>
   status=<name>`; 401/403 -> `Unauthorized{None}`; 429 -> `RateLimited{None}`; other ->
   `Network("Claude.ai returned HTTP <code>.")`.
2. `!is_success` (http 0 or 2xx) -> `PageError::NavigationFailed{status, http}` whose user text is
   `Claude.ai navigation failed: <name>.`.
3. success -> read body.

30 s timeout per navigation -> `Network("Claude.ai request timed out.")`, never retried.

`WebErrorStatus` integer -> .NET name (used in logs, retry policy and user text):
0 Unknown, 1 CertificateCommonNameIsIncorrect, 2 CertificateExpired, 3 ClientCertificateContainsErrors,
4 CertificateRevoked, 5 CertificateIsInvalid, 6 ServerUnreachable, 7 Timeout,
8 ErrorHttpInvalidServerResponse, 9 ConnectionAborted, 10 ConnectionReset, 11 Disconnected,
12 CannotConnect, 13 HostNameNotResolved, 14 OperationCanceled, 15 RedirectFailed,
16 UnexpectedError, 17 ValidAuthenticationCredentialsRequired, 18 ValidProxyAuthenticationRequired;
other n -> `n`. **VERIFY S1** against `webview2-com-sys` `COREWEBVIEW2_WEB_ERROR_STATUS_*`.

## Page fetch retry (`fetch_page`)

- Per-account gate (one navigation at a time), held across both attempts; outer idle-lease hold.
- Attempt 1..2: on `NavigationFailed` log `nav-failed path=<p> status=<name> http=<code>
  attempt=<n>`; retry once after 3 s iff `name` is exactly one of `Unknown, ConnectionAborted,
  ConnectionReset, OperationCanceled, Timeout`. Success on attempt 2 logs `retry-ok path=<p> attempt=2`.
- Separately, a dead webview (HRESULT `0x8007139F` or `0x80010108`, or the session's own closed
  state) on the first try of an action: log `webview-reset reason=fetch-webview-dead`, close, recreate,
  re-run the action once.

## Session cookie (`cuw-claude::cookie::is_session_cookie`)

`name == "sessionKey"` (ordinal) && `domain` ends with `claude.ai` (ignore case) && `value` non-empty.

## Usage fetch sequence (`cuw-claude::fetch_usage`)

1. No session cookie -> `NoCredentials`.
2. Stored org id empty -> fetch organizations; `pick`; none -> `MalformedResponse`; another account
   stores that org -> log `organization duplicate-of <name>`, return `duplicate_account(name)` and do
   not save; else save org id (save failure: log `settings <what> not saved: <Type>: <msg>`, continue)
   and record subscription fields from the same body.
3. Else if not latched and profile exists and `should_refresh(SubscriptionFetchedAt, now)`: fetch
   organizations again and record; `PageError::Usage(_)` / `NavigationFailed` -> latch, log
   `organization refresh-failed <Kind>`, continue; `Platform` error -> latch and fail the poll.
4. Fetch usage; decode; `Unauthorized{None}` -> clear stored org id (subscription fields stay), return
   the error.

Record subscription: `fields(body, org)`; nothing learned -> log `organization refresh-unparsed
<describe>`; else log `organization <describe> tier=<label>`; one save: `merge(existing, fetched)`
plus `SubscriptionFetchedAt = now` (always stamped).

### Organizations body (fields used)

JSON array of objects: `uuid` (fallback `id`) string; `capabilities` string array (`"chat"` required to
be picked); `raven_type` string (`"team"` preferred); `rate_limit_tier` string. Everything else is
only named in `describe` logs. Fixtures: `organizations/max-20x.json`, `team-raven.json`, `max-5x.json`
(INV 2B §5.6 sample bodies).

### Usage body (fields used)

JSON object. Bucket = any member that is an object with a numeric `utilization`; `resets_at` ISO
string or epoch-seconds number (else None). `limits[]` entries with `kind == "weekly_scoped"`,
numeric `percent`, `scope.model.display_name` string add `seven_day_<slug>` unless that key exists.
No bucket -> `MalformedResponse`; non-JSON (Cloudflare HTML) -> `MalformedResponse`.

## Login window contract

Title `Sign in to Claude.ai — account "<label>"`, 1000x720, centred. Hint text verbatim from INV 2B
§4.2. Paste box + `Open`: https only (`That is not an https link.` tooltip), no host allow-list. On
open: verify profile, clear the whole profile's browsing data, navigate to `/login`. Success = cookie
poll (2 s timer + after each NavigationCompleted): raise SignedIn, then close. Logs: `login-window
open`, `login-window closed signedIn=<True|False>`, `login-window reuse existing`, `login-window
still opening`, `login-window create-failed <Type>: <msg>`. At most one window per account; a pending
creation is shared, a finished one is not.

## Diagnostics logged by the fetch webview

`webview-process-failed kind=<name>` (every kind; reset for BrowserProcessExited,
RenderProcessExited, RenderProcessUnresponsive), `nav-download uri=<u> mime=<m> disposition=<d>`
(download cancelled + handled), `resource path=<p> http=<code> type=<ct|-> disposition=<cd|->` (only
`/api/organizations` responses that are not 200 + `application/json*`), `nav-redirect to=<uri>`,
`webview-idle low-memory-skipped dead|<Type>`.
