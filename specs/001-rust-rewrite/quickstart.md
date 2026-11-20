# Quickstart: build, test, run, validate (Rust rewrite)

Contracts and types: [contracts/](contracts/), [data-model.md](data-model.md). Stages:
[plan.md](plan.md#stages).

## Prerequisites

- Windows 11, or Windows 10 with the WebView2 Evergreen Runtime.
- Rust 1.99.1 with the MSVC target (`rustup show` inside `rust/` picks it from `rust-toolchain.toml`)
  and the Visual Studio Build Tools (MSVC linker, Windows SDK).
- `cargo-nextest` 0.9.152 and `cargo-mutants` 27.1.0 as prebuilt binaries (not `cargo install`).
- The C# build of the last release (for side-by-side checks) and its source (`src/`) for capturing
  fixtures.
- Quit the C# widget before running the Rust build (they share the single-instance mutex).

## Commands (from `rust/`)

| Purpose | Command |
|---|---|
| Check one crate (agents) | `cargo check -p cuw-settings` |
| Fast tests one crate (agents) | `cargo nextest run -p cuw-settings --profile fast` |
| Gates before a commit | `cargo fmt --check && cargo clippy --workspace --all-targets -- -D warnings && cargo nextest run --profile fast` |
| Full tests (coordinator, after merge) | `cargo nextest run` |
| Window/tray/WebView2 tests (owner only) | `cargo nextest run --run-ignored ignored-only` |
| Mutants for a wave | `cargo mutants --in-diff <diff> -p <crate>` |
| Debug run | `cargo run -p cuw-app` |
| Release exe | `$env:CUW_RELEASE_BUILD=1; cargo build -p cuw-app --release` -> `target\release\ClaudeUsageWidget.App.exe` (only when no agent is building) |
| Layout preview | `$env:CUW_RENDER_PREVIEW="$env:TEMP\cuw-prev"; cargo run -p cuw-app` |
| Menu preview | `$env:CUW_RENDER_MENU="$env:TEMP\cuw-menu"; cargo run -p cuw-app` |
| Band diagnostics | `$env:CLAUDE_BAND_DIAG="1"` before start; read `%TEMP%\claude-band-diag.log` |
| Pure crates have no Win32 dep | `cargo tree -p cuw-view -e normal | Select-String windows` returns nothing (CI step) |

## One-time fixture capture (S0/S1, from the C# build)

1. Copy the owner's current `%APPDATA%\ClaudeUsageWidget\settings.json` (scrub org ids) to
   `crates/cuw-settings/tests/fixtures/settings/csharp-current.json`; confirm CRLF, trailing newline
   and `DateTimeOffset` format against contracts/settings-json.md (**VERIFY** items there).
2. Run C# with `CUW_RENDER_MENU` and `CUW_RENDER_PREVIEW` into temp dirs; store `metrics.txt` as
   `crates/cuw-render/tests/fixtures/menu-metrics-csharp.txt` and the PNGs as
   `preview-csharp/` (reference only).
3. Save one real usage body and one organizations body from the fetch webview (DevTools of the login
   window), scrubbed, into `crates/cuw-usage/tests/fixtures/usage/` and
   `crates/cuw-claude/tests/fixtures/organizations/`.
4. On the C# build, check whether clicking an entry in the Accounts submenu or a Layout item closes the
   menu (research R-12 open item) and record the answer in `rust/docs/test-map.md` "Decisions" (task T034).

## Manual validation per story

Each check names the expected result; run on the owner's machine with the C# app quit.

### US1 tray + polling (S1)

1. Start the Rust exe on a machine signed in with the C# app: tray digit appears < 1 s, no sign-in
   window; within one poll tooltip equals what the C# showed (`Claude Usage Widget — 5H 42% · 7D 18%
   · FAB 8%` + status line if any).
2. Tray menu > Tray shows > 7D: icon switches to the 7-day figure; `TrayMetricKey` = `seven_day` in
   settings.json; open the file in the C# build afterwards: it reads it (SC-003).
3. Before data: placeholder ring in the taskbar ink; switch Windows taskbar light/dark: ink follows at
   once.
4. Start the Rust exe while the C# app runs (and vice versa): the second one exits silently.
5. Kill `explorer.exe` and restart it: the tray icon comes back (FR-024 fix 4).
6. Sleep and resume: one staggered refresh (account 0 at once) and a status reload; no overlapping
   fetches in `widget.log`.
7. 429 (test account or a fake via a hand-edited `RetryPausedUntil` in the future): no fetch until the
   deadline, figures stay dimmed/stale, the pause survives a restart, tooltip shows `rate limited ·
   retry in ...`.
8. Watch for any visible window during polls (none); `widget.log` lines match contracts/log-format.md.

### US2 desktop widget (S2)

1. With a C# settings file: panel opens at the same place and size, same layout, bottom of z-order,
   clicking it never takes focus.
2. Side-by-side screenshots (C# vs Rust `CUW_RENDER_PREVIEW`) for every preset in INV 2C §5, dark,
   light and 30 % opacity: same content and arrangement (FR-023).
3. Drag, resize from each edge and corner, lock/unlock (toolbar, header, tray); restart: position and
   size restored.
4. Edit mode: strip above/below, nine buttons cycle values, drag a cell onto another: swap saved;
   leaving keeps it.
5. Status dial click opens `https://status.claude.com` and logs `browser-open site=status-dial`.
6. At 150 % scale on a second monitor: panel restored inside that monitor's work area (FR-024 fix 1).
7. Transparent corners pass clicks to the desktop; 0 % opacity panel still draggable.

### US3 accounts (S3)

1. Add account > login window `Sign in to Claude.ai — account "New account"`; sign in to a second
   organization; both show usage; rename it; make it the tray account; restart: both still signed in.
2. Sign in to an organization another account already uses: notice `Account added twice` with Sign in.
3. Expire a session (sign out on claude.ai in the login window): notice `Session expired` with a
   Sign in button (FR-024 fix 3).
4. Four accounts: `Add account…` disabled with tooltip `The widget shows at most 4 accounts.`
5. Rename while a poll writes the file (keep the dialog open over a refresh): only the name changes
   (FR-024 fix 2).
6. Sign out and remove: account gone, its WebView2 profile data cleared.

### US4 taskbar band (S4)

1. Enable the band; switch Near tray / Left corner and Three metrics / All accounts: text matches
   `assets/taskbar-band.png`.
2. Fullscreen video (F11 and a game): band hides within ~150 ms and returns after leaving.
3. Maximize/restore windows with ShareX running: no blink (1200 ms slow hide + re-probe).
4. Show/hide Telegram from the tray, click the taskbar: band never stays buried.
5. Restart Explorer: band reattaches (immediately on TaskbarCreated, at most 5 s by the backstop).
6. `CLAUDE_BAND_DIAG=1`: every decision is in `%TEMP%\claude-band-diag.log` with the exact texts.

### US5 settings, autostart, updates, export, diagnostics (S5)

1. Theme System/Dark/Light and every opacity preset: panel and menu repaint, tick follows.
2. Launch at login: Run value `ClaudeUsageWidget` = `"<exe path>"`; sign out/in: the app starts.
3. Set `ExportDirectory`: after a refresh each account's `<slug>.widget.json` has the 12 keys
   (contracts/export-file.md); `ExportPath` = settings path logs `export skipped`.
4. Check for updates with an older local version against a test release: download, swap, restart;
   with a source build: "Open the release page?" prompt.
5. `widget.log` rotates past 1 MiB to `widget.log.1`.

### US6 switch (S6)

1. Fresh VM/user with the last C# release, signed in, autostart on, panel moved: run the C#
   "Check for updates and install…" against the first Rust release (draft release on a fork or the
   real one when approved): the Rust exe starts from the same folder with the same settings, accounts,
   sign-ins, autostart and position (SC-002).
2. Rust's own updater from that folder (C# leftovers present) updates to a newer Rust test build.
3. Quit Rust, start the old C# exe on the Rust-written settings.json: it reads it (SC-003).
4. Idle memory (Task Manager, private working set) <= half of the C# app; tray icon < 1 s (SC-004);
   zip size (SC-005).

### US7 bot bridge (S7, `personal` branch only)

Validation steps live with the personal-branch plan; acceptance as in spec US7.
