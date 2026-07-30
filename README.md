# Claude Usage Widget

A macOS desktop widget showing Claude Code subscription limits as three dials:
the 5-hour session limit, the 7-day weekly limit, and one per-model weekly
limit.

Each dial fills its arc with the share of the limit already spent — green,
amber past 60%, red past 85% — while the hand shows how far the current window
has run. The centre reads the percentage and the time left until the window
resets.

## How it reads your usage

The widget calls `https://api.anthropic.com/api/oauth/usage` with the OAuth
token Claude Code already stores in your login keychain. It reads that item and
nothing else, and never writes to it — Claude Code owns and refreshes those
credentials. macOS asks for permission the first time; if the token has expired,
the widget says so and the next Claude Code session refreshes it.

## Requirements

- macOS 14+
- Claude Code, signed in
- Swift 6 toolchain — Command Line Tools are enough (`xcode-select --install`)

## Build

```bash
make app
open "dist/Claude Usage Widget.app"
```

## Development

```bash
make run    # run a dev build
make test   # run the test suite
```

> **Important:** run tests only via `make test`. On a machine without full Xcode
> a bare `swift test` silently runs zero tests and exits 0 — the Makefile passes
> the toolchain flags Swift Testing needs from Command Line Tools.

## Menu bar

- **Refresh now** — fetch immediately instead of waiting out the 5-minute cycle
- **Model limit** — pick which per-model weekly limit the third dial shows
  (appears once the server returns more than one)
- **Lock position** — pin the panel so it cannot be dragged
- **Show on desktop** — hide the panel while keeping the menu bar item
- **Launch at login**

## Architecture

```
Sources/ClaudeUsageWidgetCore/
  Auth/          — keychain read
  Usage/         — decoding, pure math, bucket selection, the HTTP call
  Store/         — @Observable store, 5-minute refresh
  Views/         — SwiftUI dials and panel
Sources/ClaudeUsageWidget/  — app shell: desktop window, MenuBarExtra
```

All computation is pure functions over a decoded snapshot and is unit-tested;
the keychain reader and the HTTP client are thin wrappers with smoke tests.
