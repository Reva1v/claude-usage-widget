# Implementation Plan: Rust rewrite with full behavior parity

**Branch**: `rust-port` | **Date**: 2026-11-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-rust-rewrite/spec.md`; behavior reference
`specs/_inventory/behavior-inventory.md` (cited as INV).

## Summary

Hand-port the C#/.NET 10 WPF widget to a native Win32 Rust app in one Cargo workspace under `rust/`.
All business rules (settings schema and migration, usage decoding, 429 ladder, poll schedule,
formatting, layout math, band visibility state machine, release parsing, export) live in pure crates
with no `windows` dependency; Win32, Direct2D/DirectWrite, WebView2 (`webview2-com`) and
`Shell_NotifyIconW` live in thin platform crates; one binary named `ClaudeUsageWidget.App.exe`
wires them on a single STA UI thread with a small re-entrant local executor. Status page and GitHub
go through `ureq` on worker threads; claude.ai stays WebView2-navigation-only. Delivery follows
User Stories 1-7, each a runnable build (see Stages).

## Technical Context

**Language/Version**: Rust stable 1.99.1 (installed, released 2026-10-13), edition 2024, resolver 3,
`rust-version = "1.99"` pinned via `rust/rust-toolchain.toml`.

**Primary Dependencies** (versions checked on crates.io API on 2026-11-21; "max stable" at that date):

| Crate | Pin | Where | Note |
|---|---|---|---|
| `windows` | 0.62.2 | platform crates | must equal what `webview2-com` 0.39.1 requires (`^0.62`); do NOT take `windows-core` 0.100.0 (2026-09-03) until `webview2-com` moves |
| `windows-core` | 0.62.2 | platform crates | same reason |
| `webview2-com` | 0.39.1 (2026-03-11) | `cuw-webview` | depends on `windows ^0.62`, `webview2-com-sys ^0.39.1` |
| `serde` / `serde_json` | 1.0.229 / 1.0.154 | pure + platform | `serde_json` with `preserve_order` (key order on write) |
| `thiserror` | 2.0.21 | library crates | |
| `anyhow` | 1.0.104 | binary only | |
| `tracing` / `tracing-subscriber` | 0.1.44 / 0.3.23 | all / binary | widget.log is a custom `Layer` (contracts/log-format.md) |
| `jiff` | 1.0.1 (2026-11-18) | pure crates | `Timestamp`, `Offset`, local offset via `tz-system` |
| `ureq` | 3.5.2 (2026-11-12) | `cuw-http` | `default-features = false`, features `rustls`, `platform-verifier`, `win-system-proxy`, `gzip` |
| `zip` | 9.8.1 (2026-11-20) | `cuw-updater` | `default-features = false`, feature `deflate` |
| `sha2` | 0.11.1 | `cuw-settings` | bad-copy content hash |
| `uuid` | 1.29.3 | `cuw-app-core` | `v4`, dashed lowercase like `Guid.ToString()` |
| `futures-util` | 0.3.34 (2026-08-11) | `cuw-app-core` (`LocalBoxFuture` in `AppPorts::spawn`), `cuw-win-core`, `cuw-app` | `Shared`, `LocalBoxFuture`; `default-features = false`, features `std`, `alloc` |
| `embed-resource` | 3.0.12 | `cuw-app` build.rs | manifest (PerMonitorV2, comctl32 v6), icon, version info |
| dev: `tempfile` 3.27.0, `insta` 1.49.0, `proptest` 1.12.0, `pollster` 1.0.3 (2026-11-18) | | tests | `pollster` drives async ports in pure-crate tests |
| tools: `cargo-nextest` 0.9.152 (installed), `cargo-mutants` 27.1.0 | | dev box | install prebuilt binaries |

**Storage**: files only. `%APPDATA%\ClaudeUsageWidget\settings.json` (shared with the C# build),
`%LOCALAPPDATA%\ClaudeUsageWidget\widget.log(.1)`, WebView2 UDF
`%LOCALAPPDATA%\ClaudeUsageWidget\profiles\default`, export files, `%TEMP%` diag/update dirs, HKCU Run
value `ClaudeUsageWidget`.

**Testing**: `cargo nextest run` (profiles `fast` for agents, `default` full), unit tests next to code,
integration tests in `crates/<c>/tests/`, recorded bodies in `crates/<c>/tests/fixtures/`, `insta`
for text snapshots, `proptest` for math invariants, `cargo-mutants` per crate. Window/tray/sound tests
are `#[ignore]` (Principle III).

**Target Platform**: Windows 10 (WebView2 Runtime installed) and 11, x64, `x86_64-pc-windows-msvc`,
PerMonitorV2 DPI aware.

**Project Type**: desktop tray app (single portable exe).

**Performance Goals**: SC-004 idle memory at most half of the C# app, tray icon visible < 1 s after
launch; SC-005 exe < 75 MB zip (expected single-digit MB). Idle CPU: no periodic work besides the
5 s band backstop tick and the 300 s poll.

**Constraints**: same mutex name, settings path/format, UDF/profile scheme, Run value name, exe name
`ClaudeUsageWidget.App.exe`, zip name `ClaudeUsageWidget-vX.Y.Z-win-x64.zip`, version strings parseable
by `System.Version` (no pre-release suffix; first Rust release > last C# version, e.g. `0.3.0`). No
crash from any handler (FR-020): `panic = "unwind"` and `catch_unwind` at every callback boundary.

**Scale/Scope**: 21 crates, 36 C# test classes (~490 tests) to mirror, 4 accounts max, 7 stages.

All former unknowns are resolved in [research.md](research.md); items that need a live check are
marked **VERIFY** there and in the stage where they are checked.

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 (below).*

| Principle | Gate | Pre-design | Post-design |
|---|---|---|---|
| I Parity | Every INV subsystem owned by one crate; every C# test class mapped (Test strategy) | PASS: ownership table below | PASS: crate-interfaces.md covers every subsystem; test map lists all 36 classes |
| II Pure core | Business rules in crates without `windows`/WebView2/GUI deps; each external service behind one module | PASS | PASS (after /speckit-analyze moved menu navigation, digit fit, widget persist/hover rules, login texts and the band diag switch into pure crates; the platform-side helpers left are justified in Complexity Tracking): claude.ai = `cuw-claude` (rules) + `cuw-webview` (transport); status = `cuw-status`; GitHub = `cuw-release` + `cuw-updater`. Pure crates carry `#![forbid(unsafe_code)]` and no `windows` dep (checked by a `cargo tree` step in CI) |
| III Tests | Every ported behavior tested incl. edges; fixtures in `tests/fixtures/`; headless only; windows/tray `#[ignore]` | PASS | PASS: Test strategy; nextest `fast` profile; mutants config |
| IV Data compat | Same settings path/format, migrations, profiles, Run value; never drop unknown fields | PASS | PASS: contracts/settings-json.md (unknown fields preserved at top level, in accounts and in `Layout`); WebView2 rules in contracts/claude-web.md |
| V Safe Rust | stable, 2024, one workspace, no `unwrap` outside tests, thiserror/anyhow split, tracing, `unsafe` only in platform crates with `// SAFETY:` | PASS | PASS: lint table in research R-20; `clippy::undocumented_unsafe_blocks = "deny"` |

Deviations are listed in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/001-rust-rewrite/
├── plan.md            # this file
├── research.md        # Phase 0 decisions
├── data-model.md      # Phase 1 domain types
├── quickstart.md      # build/test/run + manual validation per story
├── contracts/
│   ├── settings-json.md
│   ├── claude-web.md
│   ├── status-api.md
│   ├── github-release.md
│   ├── export-file.md
│   ├── log-format.md
│   └── crate-interfaces.md   # signatures parallel agents code against
└── tasks.md           # /speckit-tasks output (not created here)
```

### Source Code

The workspace lives in `rust/` (not the repo root) so the C# solution (`src/`, `Tests/`,
`*.sln`) and the Rust workspace never share tool config while both ship; `dotnet` and `cargo` each
see only their own tree, CI jobs filter by path, and the cutover (US6) can move or delete either side
in one commit. Crates are prefixed `cuw-`.

```text
rust/
├── Cargo.toml              # [workspace] members = ["crates/*"], [workspace.dependencies], [workspace.lints]
├── rust-toolchain.toml     # 1.99.1
├── rustfmt.toml
├── .config/nextest.toml    # profiles default, fast; test-groups render
├── .cargo/config.toml      # rust-lld for dev, target-dir shared by worktrees
├── .cargo/mutants.toml
└── crates/
    ├── cuw-log/            # pure  LogLine format, widget.log writer + rotation, tracing Layer
    ├── cuw-layout/         # pure  layout types, ThemeKind, Sanitize, FlowGrid, PanelMetrics, toolbar, hit tests, drag/resize/clamp math, menu placement, tray digit fit, widget persist/hover rules
    ├── cuw-settings/       # pure  settings schema, tolerant JSON read/write w/ unknown fields, migration, SettingsStore, FileReplace
    ├── cuw-usage/          # pure  usage types, UsageError, decoder, math, thresholds, model buckets, HttpValidation
    ├── cuw-status/         # pure  status.claude.com decoding, ServiceStatus labels/words
    ├── cuw-claude/         # pure  claude.ai rules: org pick, subscription refresh/tier, nav outcome, retry policy, cookie predicate, idle lease, login/profile texts, fetch_usage orchestration (traits)
    ├── cuw-store/          # pure  UsageStore + StatusStore state machines (sans-IO), PollSchedule
    ├── cuw-release/        # pure  LatestRelease parse, IsNewer, version parse, self-install decision + texts
    ├── cuw-export/         # pure  export payload, path selection, slug, WritesOver
    ├── cuw-view/           # pure  DialModel, AccountRow, TrayText, BandText, StatusLine, notice, tooltip, palette, theme/opacity, tray MenuModel, menu navigation state
    ├── cuw-band-logic/     # pure  band geometry, probe verdict, visibility hysteresis machine, burial decision, diag switch and line texts
    ├── cuw-app-core/       # pure  commands -> settings mutations + effects, refresh coordinator (generic over ports), account lifecycle
    ├── cuw-http/           # io    blocking GET/download via ureq (worker threads only)
    ├── cuw-win-core/       # win   UI thread executor/message loop/timers, panic guard, mutex, known folders, shell open, MessageBox, registry (theme, autostart), system-events window, monitors/DPI
    ├── cuw-render/         # win   D2D/DWrite factories, TextMeasure impl, layered surface (ULW), dial/ring/glyph/text primitives, tray HICON, offscreen PNG
    ├── cuw-webview/        # win   WebView2 environment, per-account session (hidden fetch webview, navigate+read, cookies, profiles, idle close, death recovery), login window
    ├── cuw-tray/           # win   Shell_NotifyIconW + TaskbarCreated, popup menu window (owner-drawn, keep-open, submenus, tooltips, placement), rename dialog
    ├── cuw-widget/         # win   desktop widget window (layered, bottom z-order, no-activate, drag/resize/lock, edit mode, hover header, ghost)
    ├── cuw-band/           # win   taskbar band window (owned by Shell_TrayWnd, WinEvent hooks, probes, burial walk, timers, diag file)
    ├── cuw-updater/        # win   GitHub check, download, unzip, rename-swap, relaunch
    └── cuw-app/            # bin   [[bin]] name = "ClaudeUsageWidget.App"; composition root, build.rs resources
```

**Dependency direction** (arrows = "depends on"; no cycles; pure crates never depend on platform
crates):

```text
pure:     cuw-log   cuw-layout   cuw-usage   cuw-release        (no workspace deps)
          cuw-settings   -> cuw-layout
          cuw-status     -> cuw-usage, cuw-layout
          cuw-claude     -> cuw-usage, cuw-log
          cuw-store      -> cuw-usage, cuw-settings (DateTimeOffset in RetryState), cuw-status (ServiceStatus in StatusStore)
          cuw-band-logic -> cuw-layout (RectI)
          cuw-export     -> cuw-settings, cuw-usage, cuw-claude
          cuw-view       -> cuw-usage, cuw-status, cuw-claude, cuw-settings, cuw-layout
          cuw-app-core   -> all pure crates above (+ futures-util, uuid)
platform: cuw-http (ureq)
          cuw-win-core -> cuw-log, cuw-layout (MonitorInfo, ThemeKind)   (+ windows; never cuw-view)
          cuw-render   -> cuw-win-core, cuw-layout, cuw-status, cuw-view
          cuw-webview  -> cuw-win-core, cuw-claude, cuw-usage, cuw-log
          cuw-tray     -> cuw-win-core, cuw-render, cuw-view, cuw-layout
          cuw-widget   -> cuw-win-core, cuw-render, cuw-view, cuw-layout
          cuw-band     -> cuw-win-core, cuw-render, cuw-band-logic, cuw-view, cuw-layout
          cuw-updater  -> cuw-win-core, cuw-http, cuw-release, cuw-log
binary:   cuw-app      -> everything
```

`ThemeKind` lives in `cuw-layout` (the lowest crate both `cuw-win-core` and `cuw-view` already use)
and `cuw-view::theme` re-exports it, so the platform helper crate never depends on the view crate.
The graph is acyclic: every pure crate depends only on pure crates listed above it, and the same graph
is repeated at the top of contracts/crate-interfaces.md.

**Ownership of inventory sections** (each INV subsystem has exactly one owner crate; other crates
only call it):

| INV subsystem | Owner crate |
|---|---|
| Startup order, wiring, shutdown, handler policy (2A §1, 2D §0) | `cuw-app` (wiring) with rules in `cuw-app-core` |
| Single-instance mutex (2A §1.1) | `cuw-win-core::instance` |
| Power resume, theme change events (2A §1.6, 2C §3.2) | `cuw-win-core::system_events` |
| Settings store, FileReplace, `.bad-*`, migration, cap, schema (2A §3, §3) | `cuw-settings` |
| Accounts add/rename/remove/tray account (2A §1.8, 2D §2.5) | `cuw-app-core::accounts` |
| WebView2 environment, profiles, fetch webview, death, idle close, login window (2B §1-§4 transport) | `cuw-webview` |
| claude.ai rules: org pick, plan label, subscription refresh, nav classification, retry policy, cookie predicate, idle lease, fetch sequence (2B §2.2, §3.2-3.4 rules, §4.1, §5) | `cuw-claude` |
| Usage decoding, math, buckets, thresholds, error types, HttpValidation (2B §6, 2A §7) | `cuw-usage` |
| UsageStore, 429 ladder, StatusStore, PollSchedule (2B §7, §8 store, §9.2) | `cuw-store` |
| Service status decoding and words (2B §8, 2C §4.5) | `cuw-status` |
| Scheduling: RefreshAll, stagger, triggers (2B §9) | `cuw-app-core::refresh` |
| Formatting, dial derivation, StatusLine, notice, tooltip, palette, theme/opacity resolve (2C §3-§4) | `cuw-view` |
| Layout types and math, panel metrics, toolbar, hit tests, drag/resize/clamp, edit-strip math (2C §1.3-1.4, §1.7-1.9 math) | `cuw-layout` |
| Desktop widget window mechanics (2C §1.1-1.2, §1.5-1.10 platform) | `cuw-widget` |
| Dial/ring/status-dial/text drawing, tray icon pixels, glyphs (2C §2, 2D §1.4, §2.9 drawing) | `cuw-render` |
| Rename window (2C §6) | `cuw-tray::rename_dialog` |
| Layout preview / menu preview dev switches (2C §5, 2D §2.11) | `cuw-app::preview` (renders via `cuw-render` offscreen) |
| Tray icon lifecycle, tooltip, menu window, placement (2D §1-§2 platform) | `cuw-tray` |
| Tray menu content, ticks, enable rules, captions (2D §2.1-2.8 model) | `cuw-view::menu` |
| Menu placement math `Within` (2D §2.10) | `cuw-layout::placement` |
| Menu keyboard/mouse/keep-open state (2D §2.8 rules) | `cuw-view::menu_nav` (driven by `cuw-tray`) |
| Tray digit fit and ring geometry (2D §1.4 math) | `cuw-layout::icon` (drawn by `cuw-render`) |
| Widget persistence debounce, hover header, lock rule (2C §1.7, §1.9 rules) | `cuw-layout::widget_state` (used by `cuw-widget`) |
| Login window and profile texts, https paste check (2B §1.2, §4.2 rules) | `cuw-claude::login` (used by `cuw-webview`) |
| Band diag switch and line format (2D §3.8) | `cuw-band-logic::diag` (file append in `cuw-band`) |
| Taskbar band rules: geometry, probe verdict, hysteresis, burial decision (2D §3) | `cuw-band-logic` |
| Taskbar band window, hooks, timers, diag file (2D §3 platform) | `cuw-band` |
| Win32 inventory (2D §4) | split: generic helpers `cuw-win-core`, band calls `cuw-band` |
| Autostart (2A §4) | `cuw-win-core::autostart` (one module; too small for a crate) |
| Updater flow (2A §5.1-5.2) | `cuw-updater`; parsing/decisions (2A §5.3) `cuw-release` |
| Usage export (2A §6) | `cuw-export` (writes through `cuw-settings::file_replace`) |
| Logging, LogLine, widget.log (2A §8.1, 2B §10) | `cuw-log` |
| CoreInfo version (2A §8.2) | `cuw-release::version` |
| Error strings (2B §7.6-7.7) | `cuw-usage` (descriptions), `cuw-view` (status line, notice) |

**Structure Decision**: one workspace in `rust/` with the crates above. The binary is thin:
`main.rs` sets up logging, panic hook, COM, the mutex and the message loop, and `wiring.rs` connects
platform objects to `cuw-app-core`.

## Stages

Each stage ends with a runnable `ClaudeUsageWidget.App.exe` built from `rust/` that the owner can
run daily (with the C# app quit). "+" = crate gains the named parts in that stage.

| Stage | Story | Crates touched | Runnable deliverable |
|---|---|---|---|
| S0 | (setup) | workspace, all crate skeletons with the signatures of contracts/crate-interfaces.md, CI workflow `rust.yml`, nextest/mutants config | `cargo nextest run --profile fast` green on empty crates; exe starts and exits |
| S1 | US1 tray + polling | `cuw-log`, `cuw-settings`, `cuw-usage`, `cuw-status`, `cuw-claude`, `cuw-store`, `cuw-release` (version parse only), `cuw-layout` (types from S0; `placement::{within, to_px, to_dip}`, `BlockLayout::laid_out` for the settings writer, `icon` digit fit), `cuw-view` (dials, tray text, tooltip, palettes, menu model: GitHub/Issues links, Refresh now, Sign in, Tray shows, Model limit, Quit; items of later stages hidden via `MenuModel::without`; `menu_nav`), `cuw-app-core` (refresh coordinator, tray commands, `tray::on_system_event`), `cuw-http`, `cuw-win-core`, `cuw-render` (tray icon + menu paint), `cuw-webview` (environment, fetch session, login window: the tray account must be able to sign in again from S1), `cuw-tray` (icon + menu window), `cuw-app` | tray-only app: digit icon, tooltip, status line, 300 s poll, 429 ladder, resume refresh, mutex shared with C# |
| S2 | US2 widget | `cuw-layout` (+layout math, metrics, geometry, interact, clamp/restore, `widget_state`), `cuw-view` (+theme resolve, opacity, notice incl. FR-024 fix 3, panel frame, panel status line), `cuw-render` (+layered surface, dials, panel scene), `cuw-widget`, `cuw-app-core` (+panel commands, Layout/Show/Lock menu), `cuw-tray` (+Layout, Show on desktop, Lock position items), `cuw-app::preview` (`CUW_RENDER_PREVIEW`) | panel at saved place/size/layout, drag/resize/lock/edit mode, status dial click |
| S3 | US3 accounts | `cuw-webview` (+clear browsing data on remove, per-account profiles in use), `cuw-app-core` (+accounts add/rename/remove/select, OnSignedIn), `cuw-view` (+Accounts submenu), `cuw-tray` (+rename dialog) | multi-account with sign-in, duplicate-account notice, session-expired Sign in button (FR-024) |
| S4 | US4 band | `cuw-band-logic`, `cuw-band`, `cuw-render` (+band text with shadow), `cuw-view` (+Band items), `cuw-app-core` (+band commands) | band at left/tray, fullscreen hiding, burial recovery, Explorer restart, `CLAUDE_BAND_DIAG` |
| S5 | US5 settings/updates/export | `cuw-view` (+Theme, Opacity, Launch at login, Check for updates), `cuw-win-core::autostart`, `cuw-release`, `cuw-updater`, `cuw-export`, `cuw-app` (`CUW_RENDER_MENU`) | full menu parity, autostart, Rust-to-Rust self-update, export files |
| S6 | US6 switch | release packaging (`.claude/release.md` Rust section), `rust/` CI on `main`, README, C#-to-Rust updater dry run, leftover-file check | first Rust release installed by the last C# build |
| S7 | US7 personal bot bridge | personal branch only: crate `cuw-bot` + wiring hook in `cuw-app` | personal build starts the bot and writes its snapshot (contract kept on the personal branch, not in this public spec) |

**FR traceability**

| FR | Stage | FR | Stage | FR | Stage |
|---|---|---|---|---|---|
| FR-001 | S1-S5 (per INV part) | FR-010 | S1 | FR-019 | S1 |
| FR-002 | S1 | FR-011 | S1 (core items) .. S5 (all) | FR-020 | S1 (policy), every stage |
| FR-003 | S1 | FR-012 | S2 | FR-021 | all; S6 closes |
| FR-004 | S1 | FR-013 | S4 | FR-022 | S6 (naming fixed from S0) |
| FR-005 | every stage (test map) | FR-014 | S2 (panel), S1 (tray ink), S4 (band ink) | FR-023 | S2, S4, S5 (visual checks) |
| FR-006 | S1 | FR-015 | S3 | FR-024 | clamp S2, rename S3, session-expired S2 (implemented) / S3 (wired), TaskbarCreated S1 |
| FR-007 | S1 | FR-016 | S5 (verified again S6) | FR-025 | S7 |
| FR-008 | S1 | FR-017 | S5 | | |
| FR-009 | S1 (tooltip) / S2 (dial/line) | FR-018 | S5 | | |

FR-024 fix 3 (Session expired gets Sign in) is implemented and unit-tested in S2 (`cuw-view::notice`,
T140/T144) and wired and validated in S3 (T175/T177); owner-approved.

## Parallelization

Interfaces are fixed up front in [contracts/crate-interfaces.md](contracts/crate-interfaces.md);
S0 commits them as compiling skeletons (bodies `unimplemented!()`), so every agent codes against a
fixed API in its own crate(s) and worktree with a shared `CARGO_TARGET_DIR`. An interface change
needs the coordinator's approval and a contract edit first. Each agent gets at most ~10 tasks.

| Wave | Agents in parallel (crates, disjoint) | Depends on |
|---|---|---|
| W0 | 1 agent: workspace, lints, CI, skeletons, `cuw-log` | - |
| W1 (S1 pure) | A `cuw-settings`; B `cuw-usage` + `cuw-status`; C `cuw-claude`; D `cuw-store` + `cuw-release`(version parse only); E `cuw-view` (S1 parts) | W0 |
| W1 (S1 platform, same time) | F `cuw-win-core`; G `cuw-webview` (environment, fetch session, login window); H `cuw-render` (tray icon, menu paint) + `cuw-tray` (icon + menu window over a fixed `MenuModel`) + `cuw-view::menu_nav` + `cuw-layout::icon`; I `cuw-http` + `cuw-layout` (placement, `laid_out`) | W0 (code against skeletons of pure crates) |
| W2 | J `cuw-app-core` (refresh, tray commands) then K `cuw-app` wiring (coordinator or one agent) | W1 merged |
| W3 (S2) | L `cuw-layout`; M `cuw-view` panel parts + `cuw-render` panel scene; N `cuw-widget` (+ `cuw-layout::widget_state`); O `cuw-app-core` panel commands | W2 |
| W4 (S3+S4 can overlap) | O `cuw-webview` clear-data + `cuw-tray` rename dialog; P `cuw-app-core` accounts; Q `cuw-band-logic`; R `cuw-band` + band rendering | W3 (Q, R only need W2) |
| W5 (S5) | S `cuw-release` + `cuw-updater`; T `cuw-export`; U menu completion (`cuw-view` + autostart) + `CUW_RENDER_MENU` | W2 |

**Merge order inside a wave.** Agents of one wave code in parallel against the S0 skeletons, but a
skeleton body is `unimplemented!()`, so a test that calls a sibling batch's function panics until that
batch is merged. The coordinator therefore merges in dependency order, and an agent whose tests call
another batch's functions rebases on it before its final `nextest` run: W1 = (B, D, F, H, I) first,
then (A, C), then (E, G); W3 = L first, then M, N, O; W4 = Q before R. tasks.md states this per batch.

Rules for agents: `cargo check -p <crate>` while editing, `cargo nextest run -p <crate> --profile
fast` before reporting; workspace clippy and full nextest only by the coordinator after each merge;
no agent runs `#[ignore]` tests or release builds.

## Test strategy

- **Location**: unit tests in `#[cfg(test)] mod tests` beside the code for private helpers;
  behavior tests ported from C# go to `crates/<crate>/tests/<csharp_class_snake>.rs` (one file per C#
  class, test names keep the C# meaning in snake case, e.g. `a_429_pauses_until_now_plus_3600_plus_300`),
  so FR-005 coverage is checked by file name.
- **Fixtures**: recorded bodies in `crates/<crate>/tests/fixtures/`: `usage/*.json` (the bodies
  inlined in `UsageDecoderTests`, plus one real body captured on the owner's account with values
  scrubbed), `organizations/*.json` (the three documented org bodies), `status/summary-*.json`,
  `github/release-*.json`, `settings/*.json` (legacy single-account, current C#-written file captured
  in S1 incl. `DateTimeOffset` values, unknown fields, trailing commas, BOM, bad enums, unparseable),
  `menu-metrics-csharp.txt` and `preview-csharp/*.png` (captured once from the C# build with
  `CUW_RENDER_MENU` / `CUW_RENDER_PREVIEW`, used for manual and tolerance comparisons). Tests never
  touch the network, the real `%APPDATA%`, the registry or the user's WebView2 folder (all paths and
  clocks are injected).
- **C# class -> Rust location**

| C# test class | Rust test file |
|---|---|
| CoreInfoTests | `cuw-release/tests/core_info.rs` |
| HttpValidationTests | `cuw-usage/tests/http_validation.rs` |
| LatestReleaseTests | `cuw-release/tests/latest_release.rs` |
| LogLineTests | `cuw-log/tests/log_line.rs` (+ rotation tests, new) |
| SettingsMigrationTests | `cuw-settings/tests/settings_migration.rs` (+ legacy text fixtures, new) |
| SmokeTests | not ported: placeholder; reason recorded in the test map |
| UsageExportTests | `cuw-export/tests/usage_export.rs` |
| WidgetSettingsAccountTests | `cuw-settings/tests/widget_settings_account.rs` (DuplicateAccount part in `cuw-usage/tests/usage_types.rs`) |
| WidgetSettingsTests | `cuw-settings/tests/widget_settings.rs` (Windows-only file tests `#[cfg(windows)]`; symlink test skips without privilege 1314; icacls test in nextest group `fs-acl`) |
| AccountRowTests | `cuw-view/tests/account_row.rs` |
| FetchIdleLeaseTests | `cuw-claude/tests/fetch_idle_lease.rs` |
| NavigationRetryPolicyTests | `cuw-claude/tests/navigation_retry_policy.rs` |
| OrganizationPickerTests | `cuw-claude/tests/organization_picker.rs` |
| SubscriptionRefreshTests | `cuw-claude/tests/subscription_refresh.rs` |
| SubscriptionTierTests | `cuw-claude/tests/subscription_tier.rs` |
| ModelBucketsTests | `cuw-usage/tests/model_buckets.rs` |
| ThresholdLevelTests | `cuw-usage/tests/threshold_level.rs` |
| UsageDecoderTests | `cuw-usage/tests/usage_decoder.rs` |
| UsageMathTests | `cuw-usage/tests/usage_math.rs` |
| UsageTypesTests | `cuw-usage/tests/usage_types.rs` |
| PollScheduleTests | `cuw-store/tests/poll_schedule.rs` |
| StatusStoreTests | `cuw-store/tests/status_store.rs` (two-thread variants become "begin while in flight" cases: the sans-IO store is single-threaded by design; reason recorded) |
| UsageStoreTests | `cuw-store/tests/usage_store.rs` (same note for the thread variant) |
| ServiceStatusTests + StatusDecoderTests | `cuw-status/tests/service_status.rs` |
| BandTextTests | `cuw-view/tests/band_text.rs` |
| BandViewTests | `cuw-view/tests/band_view.rs` |
| DialGeometryTests | `cuw-layout/tests/dial_geometry.rs` |
| ThemeChoiceTests | `cuw-view/tests/theme_choice.rs` (round-trip part in `cuw-settings`) |
| PanelOpacityTests | `cuw-view/tests/panel_opacity.rs` (round-trip part in `cuw-settings`) |
| TrayTextTests | `cuw-view/tests/tray_text.rs` |
| DialModelTests + StatusLineTests | `cuw-view/tests/dial_model.rs`, `cuw-view/tests/status_line.rs` |
| LayoutTests (FlowGrid, GridTracks, LayoutSettings, StatusMode, LayoutCycle, PanelMetrics) | `cuw-layout/tests/layout.rs` |
| PanelViewTests | `cuw-layout/tests/panel_view.rs` |
| App: DesktopWidgetWindowTests | pure part (clamp before redraw, rebuild on account count change) in `cuw-layout/tests/clamp.rs` + `cuw-widget/tests/window.rs` `#[ignore]` |
| App: MenuPlacementTests | `cuw-layout/tests/menu_placement.rs` |
| App: WebViewDeathTests | `cuw-claude/tests/webview_death.rs` (raw HRESULT predicate; .NET-wrapper cases map to the HRESULT; `ObjectDisposed` maps to the session's own `Closed` state) |

- **New tests for previously untested behavior** (Principle III): app-core wiring (startup retry
  2 s/30 s, stagger, resume, Refresh-now scope, OnSignedIn), autostart value format (registry calls
  behind a trait, fake in tests), updater decisions, log rotation, export writer, tolerant enum
  fallback values, FR-024 fixes (physical-pixel clamp at 125/150/200 %, rename re-read, session
  expired button, TaskbarCreated re-add via a fake shell port), band hysteresis and burial decision
  tables, menu model per state.
- **Headless vs ignored**: everything in pure crates is headless. Platform crates put logic behind
  pure functions; tests that create windows, a tray icon, WebView2 or play sound are `#[ignore]` and run
  only by the owner. Offscreen Direct2D renders (WIC software target, no window) are headless but
  font-dependent: they run in nextest group `render`, excluded from `fast`.
- **nextest** (`rust/.config/nextest.toml`): profile `default` = all non-ignored; profile `fast` =
  `default-filter = "not (test-group(render) | test-group(fs-acl))"`, `fail-fast = true`;
  `[test-groups] fs-acl = { max-threads = 1 }`, `render = { max-threads = 2 }`. Timer behavior is
  tested on injected clocks (`now` parameters, sans-IO stores), never with real sleeps.
- **cargo-mutants** (`rust/.cargo/mutants.toml`): `test_tool = "nextest"`, `additional_cargo_test_args
  = ["--profile", "fast"]`, `test_workspace = false`, `exclude_globs = ["crates/cuw-win-core/**",
  "crates/cuw-render/**", "crates/cuw-webview/**", "crates/cuw-tray/**", "crates/cuw-widget/**",
  "crates/cuw-band/**", "crates/cuw-updater/**", "crates/cuw-http/**", "crates/cuw-app/**"]`. Run per
  wave with `--in-diff` by the coordinator after merge; agents add a test per surviving mutant in
  `mutants.out/missed.txt` or record why it is equivalent.

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
|---|---|---|
| 21 crates | agents work in parallel on disjoint crates; pure crates build/test in seconds (Principle II, best-practices Rust) | one core crate: every agent rebuilds and conflicts in one crate; 10-task cap per agent cannot be kept |
| Own re-entrant UI-thread executor in `cuw-win-core` instead of tokio | WebView2 COM objects are `!Send` and STA-bound; callbacks arrive through the message loop; modal loops (MessageBoxW, menu) must keep polling tasks | tokio `LocalSet` needs its own blocking loop and cannot be driven from inside a Win32 message loop without a second thread |
| `cuw-settings`, `cuw-log`, `cuw-export` do file I/O in "pure" crates | the store state machine and FileReplace semantics are test-pinned with real temp files (WidgetSettingsTests); they use only `std` (`std::os::windows` share modes), no `windows` crate | moving them to a platform crate would put the most test-heavy code behind the slow build |
| Unknown JSON fields preserved (C# drops them) | constitution IV "a write never drops fields it does not understand" | matching C# would violate IV |
| Updater swaps by renaming the running exe and relaunching with `--after-update <pid>` instead of a PowerShell script | single exe, no script host or execution policy dependency; same user-visible prompts and restart | keeping PowerShell works too and stays the fallback if the rename swap fails in S5 testing (research R-15) |
| Some headless-tested helpers stay in platform crates: `WM_WINDOWPOSCHANGING` rewrites (`cuw-webview`, `cuw-widget`), registry adapters behind `RegistryRead`/`RunKey` fakes (`cuw-win-core` theme, autostart), the window-free executor core (`cuw-win-core::ui`), updater flow sequencing and the copy-retry swap (`cuw-updater`), the band diag file append (`cuw-band`) | each works on Win32 types (`WINDOWPOS`, `SWP_*`, registry value kinds, HWND/process handles) or is IO orchestration whose decisions and texts already live in pure crates (`cuw-release`, `cuw-band-logic::diag`); constitution II "no business rules in platform code" still holds | moving them would mirror Win32 types in pure crates or split one IO step across two crates; all other Windows-free logic (menu navigation, digit fit, widget debounce/hover/lock, login texts, diag switch) was moved to pure crates during /speckit-analyze |
| Staggered poll keeps an account matched by id (C# drops one poll if the account is renamed during the wait) | natural in Rust; the C# quirk is an inferred accident | needs owner OK as a tiny behavior change (research R-21); otherwise port the quirk |
