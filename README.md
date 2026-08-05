# Claude Usage Widget (Windows)

[![CI](https://github.com/Reva1v/claude-usage-widget/actions/workflows/ci.yml/badge.svg)](https://github.com/Reva1v/claude-usage-widget/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A Windows desktop widget showing Claude Code subscription limits and Claude's
own service status, as four dials in a square panel pinned to the bottom of
the desktop.

This is a Windows port (C#/.NET 8 + WPF) of
[TadelUnso/claude-usage-widget](https://github.com/TadelUnso/claude-usage-widget),
the original macOS app. It reads the same figures through the same
authenticated claude.ai web session; the desktop widget, tray icon, sign-in
flow, refresh cycle and settings persistence are all reimplemented for
Windows.

TODO: screenshot of the widget on Windows.

## The panel

A 2x2 grid:

- **SESSION** — the 5-hour session limit
- **WEEK** — the 7-day weekly limit
- One per-model weekly limit — whichever the server returns; pick a specific
  one from the tray menu when more than one applies
- **STATUS** — Claude's own service status; click it to open
  [status.claude.com](https://status.claude.com)

Each usage dial fills its arc with the share of the limit already spent:
green below 60%, amber below 85%, red above. The centre reads the percentage
and the time left until the window resets. The status dial fills its ring
solid rather than to a fraction — there is no percentage for "operational" or
"down", only a state.

The panel is bottom-pinned on the desktop, sits behind application windows,
and can be dragged, resized and locked in place; position, size and lock
state are all remembered across launches. An optional taskbar band can show
the same live figures docked to the taskbar (see Known limitations below).

## Tray icon

The tray icon shows a live figure and opens a menu with:

- **Claude Usage Widget vX.Y.Z — GitHub** — opens the repository
- **Report an Issue**
- **Refresh now** — fetch immediately instead of waiting out the 5-minute
  cycle
- **Sign in to Claude.ai…**
- **Tray shows** — pick which figure (5H, 7D or MODEL) the tray icon itself
  displays
- **Model limit** — pick which per-model weekly limit the dial shows
  (appears once the server returns more than one)
- **Show on desktop** — hide the panel while keeping the tray icon
- **Taskbar band** — toggle the optional taskbar-docked figure display
- **Band position** — dock the band near the tray icons or in the taskbar's
  left corner
- **Lock position**
- **Launch at login**
- **Quit Claude Usage Widget**

## How it reads your usage

The widget reads your usage through an authenticated claude.ai session. On
first launch it opens a sign-in window (a WebView2 view of claude.ai); once
you are signed in, the session cookie lives in the widget's own per-profile
cookie store under `%LOCALAPPDATA%\ClaudeUsageWidget\profiles\default`, and
the widget asks `claude.ai/api/organizations/<id>/usage` for the same
figures the web app shows you. The sign-in window's cookie store is isolated
from your regular browsers — signing in here does not touch Chrome, Edge or
any other browser, and signing out of one does not sign out the other. Sign
in again from the tray menu whenever the session expires.

It deliberately does not use `api.anthropic.com/api/oauth/usage`. That
endpoint carries a Cloudflare rate limit strict enough that a widget polling
every five minutes trips it, and the resulting block is renewed by each
further request rather than expiring — the widget could then never recover
on its own. Requests back off on HTTP 429 responses instead.

## Service status

The fourth dial reads `https://status.claude.com`, preferring the "Claude
Code" component's status; if that component is ever renamed or retired, it
falls back to the page-wide status instead.

## Known limitations

**A Claude subscription is required.** The usage endpoint this widget reads
is subscription-only. On an account billed per token there is no session or
weekly limit to show — the widget will say so rather than show empty dials,
and there is nothing to configure.

**The usage API is undocumented.** It is the same call claude.ai makes for
its own usage screen, so it can change without notice and take the dials
with it.

**The per-model dial depends on your plan.** A separate weekly limit for a
specific model is a Max and Team Premium arrangement. On Pro and Team
Standard that model is billed from usage credits instead, so the dial shows
whichever per-model limit your account does have, or `n/a` if it has none.

**Taskbar band is a transparent window docked above the taskbar, not a true
embed.** Windows 11's Mica compositing over the taskbar makes genuinely
embedded (`WS_CHILD` of `Shell_TrayWnd`) content illegible — confirmed by
direct pixel measurement — so the band is instead a normal top-level window
*owned* by the taskbar (it always stays above it, without the Mica dimming a
`WS_CHILD` gets). It doesn't steal focus or show up in Alt-Tab, renders
white text with a subtle shadow directly over your wallpaper/taskbar color,
and hides itself automatically whenever a fullscreen app (e.g. a game)
covers the screen, reappearing once you leave it. Pick where it docks —
next to the tray icons (default) or the taskbar's left corner — from the
tray menu's "Band position" submenu.

**Signing in happens in the widget's own window.** The sign-in window is a
WebView2 view of claude.ai with its own cookie store, isolated from your
regular browsers.

## Requirements

- Windows 11 (WebView2 Runtime is preinstalled). On Windows 10, install the
  [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
  first.
- A Claude.ai account on a subscription plan
- .NET 8 SDK, to build from source

## Run

```
dotnet run --project src/ClaudeUsageWidget.App
```

## Publish

```
dotnet publish src/ClaudeUsageWidget.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Development

```
dotnet test
```

149 tests across `Tests/ClaudeUsageWidget.Core.Tests`.

## Architecture

```
src/ClaudeUsageWidget.Core/   — decoding, pure math, thresholds, bucket
                                 selection, settings, the HTTP calls
  Usage/    — usage snapshot decoding and math
  Status/   — Claude's own service status: fetch and decode
  Store/    — observable stores, rate-limit backoff
  Settings/ — settings.json persistence
  Formatting/, Views/, Web/ — shared formatting and view/session helpers
src/ClaudeUsageWidget.App/    — WPF app shell: desktop widget window, tray
                                 icon, taskbar band, sign-in window (WebView2),
                                 launch-at-login, 5-minute refresh timer
Tests/ClaudeUsageWidget.Core.Tests/ — xUnit tests for ClaudeUsageWidget.Core
```

All computation is pure functions over a decoded snapshot and is unit-tested;
the session reader and the HTTP clients are thin wrappers with smoke tests.

## Credits

This project is a Windows port of
[TadelUnso/claude-usage-widget](https://github.com/TadelUnso/claude-usage-widget).
All credit for the original design and concept goes to the upstream project.
Licensed under the [MIT License](LICENSE).
