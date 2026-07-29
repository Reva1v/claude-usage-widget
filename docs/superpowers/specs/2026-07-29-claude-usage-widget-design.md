# Claude Usage Widget — Design

Date: 2026-07-29

## Purpose

A macOS desktop widget that shows Claude Code subscription limits as three
dials: the 5-hour session limit, the 7-day weekly limit, and one model-specific
weekly limit (Fable by default).

The widget behaves like [mole-widget](../../../../mole-widget): a borderless
window living at desktop level, above the wallpaper and below application
windows, with a menu bar extra for settings.

## Data source

`GET https://api.anthropic.com/api/oauth/usage`

Authorization is the Claude Code OAuth access token read from the macOS
Keychain. The endpoint and its response field names were confirmed by
inspecting the strings of the installed Claude Code binary
(`~/.local/share/claude/versions/2.1.220`).

Response shape — a JSON object whose values are usage buckets:

```json
{
  "five_hour":            { "utilization": 42, "resets_at": "..." },
  "seven_day":            { "utilization": 17, "resets_at": "..." },
  "seven_day_opus":       { "utilization":  3, "resets_at": "..." },
  "seven_day_sonnet":     { "utilization":  9, "resets_at": "..." },
  "seven_day_oauth_apps": { "utilization":  0, "resets_at": "..." },
  "extra_usage":          { "utilization":  0, "resets_at": "..." }
}
```

Parsing is deliberately generic — a `[String: Bucket]` dictionary rather than a
struct with fixed properties. A new `seven_day_fable` key is picked up without a
code change, and a key that disappears server-side breaks nothing.

`seven_day_fable` is absent in Claude Code 2.1.220; only `opus` and `sonnet`
model buckets exist there. The third dial therefore auto-selects (see below).

### Rejected alternative

The reference project [SlavomirDurej/claude-usage-widget](https://github.com/SlavomirDurej/claude-usage-widget)
reads the `sessionKey` cookie from claude.ai and fetches
`https://claude.ai/api/organizations/{id}/usage` through a hidden Electron
BrowserWindow, because plain requests are blocked by Cloudflare. That approach
requires the user to copy a session key out of browser dev tools, and
reproducing the Cloudflare bypass in Swift would mean driving a `WKWebView`.
The OAuth endpoint needs neither.

## Structure

A Swift Package targeting macOS 14+, Swift 6 toolchain in language mode v5, no
Xcode project — mirroring mole-widget so both projects build the same way.

```
Package.swift                      — library ClaudeUsageWidgetCore + executable ClaudeUsageWidget
Makefile                           — run / test / app
Resources/Info.plist               — LSUIElement, com.sbezbabnykh.claude-usage-widget
Sources/ClaudeUsageWidget/         — app shell: desktop window, MenuBarExtra
Sources/ClaudeUsageWidgetCore/
  Auth/ClaudeCredentials.swift     — reads the Claude Code OAuth token from the Keychain
  Usage/UsageAPI.swift             — URLSession request to /api/oauth/usage
  Usage/UsageTypes.swift           — Bucket, UsageSnapshot, error states
  Usage/UsageMath.swift            — pure functions: window progress, bucket selection, formatting
  Store/UsageStore.swift           — @Observable store with the refresh timer
  Views/DialView.swift             — one dial
  Views/WidgetRootView.swift       — the three-dial panel
  Views/Theme.swift                — colors, fonts, threshold colors
  WidgetSettings.swift             — UserDefaults keys and bounds
Tests/ClaudeUsageWidgetCoreTests/
```

`Makefile test` passes the `-F /Library/Developer/CommandLineTools/Library/Developer/Frameworks`
flags that Swift Testing needs when only Command Line Tools are installed —
without them `swift test` silently runs zero tests and exits 0.

Sparkle is out of scope for the first version. It exists in mole-widget to ship
DMG updates; here it belongs to a later packaging step, not to the working
widget.

## App shell behaviour

Taken from mole-widget:

- borderless `NSWindow` one level above the Finder desktop icon window
- `canJoinAllSpaces`, `stationary`, `ignoresCycle`
- draggable with the mouse, position saved via `setFrameAutosaveName`
- lock position toggle, hide/show toggle, both persisted
- `LSUIElement` — no Dock icon
- launch at login via `SMAppService`
- `MenuBarExtra` holding settings, a manual refresh, and quit

## Refresh and state

The store refreshes every 5 minutes. Server-side usage figures are coarse, so a
faster interval would only add requests. It also refreshes on demand from the
menu and when the Mac wakes from sleep.

Store state is one of:

- `loading` — first fetch in flight
- `ok(snapshot, fetchedAt)` — dials live
- `noCredentials` — no Keychain item found
- `unauthorized` — the endpoint returned 401
- `networkError` — request failed

The last three dim the dials and show a status line; the previous snapshot, if
any, stays on screen rather than being cleared. Nothing about a failed fetch
crashes the widget.

## The dial

Three dials in a row inside a rounded dark panel.

- **Outer arc** — `utilization` from 0 to 100%, starting at the 12 o'clock
  position and running clockwise. Color follows thresholds: green below 60%,
  amber below 85%, red above — the same `Theme.barColor` mapping mole-widget
  uses.
- **Hand** — progress through the current window, one full revolution per
  window. The API gives only `resets_at`, not the window length, so the length
  is a constant per bucket kind: `five_hour` → 5 hours, every `seven_day_*` →
  7 days. `elapsed = window - (resetsAt - now)`, clamped to [0, 1].
- **Center** — the label (SESSION / WEEK / FABLE), the percentage, and the time
  remaining below it (`2h 14m`, `3d 6h`).

When `resets_at` is missing or already in the past, the hand is hidden rather
than drawn at a guessed angle.

### Third dial selection

Default: `seven_day_fable`. If that key is absent, the first available model
bucket in a fixed preference order (`fable`, `opus`, `sonnet`). The menu bar
offers a picker listing every model bucket the server actually returned, and the
choice is persisted. If the response has no model bucket at all, the dial reads
`n/a`.

## Testing

All computation lives in pure functions over decoded snapshots and is tested
without network or Keychain access:

- decoding fixtures, including unknown keys, missing keys, and a malformed body
- third-dial selection: fable present, fable absent, no model buckets at all
- `elapsedFraction`: mid-window, `resetsAt` in the past, `resetsAt` nil, clamping
- remaining-time formatting at boundaries (59 s, exactly 1 h, more than a day)
- threshold color mapping at 59/60/84/85/100

`ClaudeCredentials` and `UsageAPI` are thin wrappers over the Keychain and
URLSession and get smoke tests only, as the collectors do in mole-widget.

## Known unknown

The exact Keychain item that Claude Code stores its OAuth token under could not
be verified while writing this spec — the permission classifier blocked reading
credentials. It must be confirmed as the first implementation step. If the
storage format differs from the expectation, only `ClaudeCredentials.swift`
changes; the rest of the design is unaffected.
