---

description: "Task list for the Rust rewrite with full behavior parity"
---

# Tasks: Rust rewrite with full behavior parity

**Input**: Design documents from `specs/001-rust-rewrite/` (plan.md, spec.md, research.md, data-model.md,
contracts/*.md, quickstart.md). Behavior reference: `specs/_inventory/behavior-inventory.md` (cited as
INV). C# tests: `Tests/ClaudeUsageWidget.Core.Tests/*`, `Tests/ClaudeUsageWidget.App.Tests/*`.

**Prerequisites**: plan.md (stages S0-S7, crate layout, waves W0-W5, test strategy),
contracts/crate-interfaces.md (fixed public API; a signature change needs the coordinator's approval
and an edit of that file first).

**Tests**: REQUIRED (constitution Principle III, spec FR-005). Every behavior task has a test task that
comes first in its batch. Tests that port a C# class go to `rust/crates/<crate>/tests/<csharp_class_snake>.rs`
(one file per C# class, test names keep the C# meaning in snake case); recorded bodies go to
`rust/crates/<crate>/tests/fixtures/`. Edge cases named in the task are mandatory. Tests that create a
window, a tray icon, WebView2 or play sound are `#[ignore]`; offscreen Direct2D renders are in nextest
test group `render`; ACL tests in group `fs-acl` (both excluded from profile `fast`). Timers are tested on
injected clocks, never with real sleeps. Tests never touch the network, the real `%APPDATA%`, the registry
or the user's WebView2 folder.

**Organization**: phases follow plan stages: Phase 1-2 = S0, Phase 3-9 = S1-S7 (= US1-US7), final phase =
polish. Inside a phase, tasks are grouped into **batches** (one agent each, at most 10 tasks). Each batch
header states its difficulty and what may run at the same time.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel with other [P] tasks of the same batch (touches disjoint files, no
  dependency on an unfinished task). Tests of a batch are written before its implementation tasks.
- **[Story]**: US1..US7 (spec.md user stories); Setup/Foundational/Polish tasks carry no story label.
- **Batch difficulty**: `complex` = Win32 / COM / WebView2 / rendering / band (coordinator assigns
  `sonnet-5.5-high` or Opus); `medium` = pure core logic (coordinator assigns `sonnet-5.5-medium` or
  `-high`).
- Paths are relative to the repository root; the workspace is `rust/` and crates are
  `rust/crates/cuw-*/`.

## Agent rules (every batch)

- Work in your own worktree on your own crate(s) only; shared `CARGO_TARGET_DIR` (rust/.cargo/config.toml).
- While editing: `cargo check -p <crate>`; before reporting: `cargo nextest run -p <crate> --profile fast`
  green for every crate the batch touched. Do not run `#[ignore]` tests, release builds or workspace-wide
  clippy (coordinator only).
- No `unwrap` outside tests, `thiserror` in libraries, `anyhow` only in `cuw-app`, `// SAFETY:` on every
  `unsafe` block, pure crates keep `#![forbid(unsafe_code)]` and no `windows` dependency.
- Code comments in English. Do not commit; the coordinator merges.

## Path Conventions

```text
rust/Cargo.toml, rust/rust-toolchain.toml, rust/rustfmt.toml, rust/clippy.toml
rust/.config/nextest.toml, rust/.cargo/config.toml, rust/.cargo/mutants.toml
rust/docs/test-map.md                       # C# test class -> Rust test file / reason (FR-005)
rust/crates/<crate>/src/*.rs                # code + #[cfg(test)] unit tests for private helpers
rust/crates/<crate>/tests/*.rs              # behavior tests (ported C# classes + new ones)
rust/crates/<crate>/tests/fixtures/**       # recorded bodies, settings files, reference renders
.github/workflows/rust.yml                  # Rust CI (path filter rust/**)
```

---

## Phase 1: Setup (S0, wave W0)

**Purpose**: workspace, lints, test tooling and CI, so every later batch builds and tests the same way.

### Batch S0-A: workspace and tooling (medium) - runs alone, first

- [ ] T001 Create `rust/Cargo.toml`: `[workspace] members = ["crates/*"]`, `resolver = "3"`, `[workspace.package] edition = "2024"`, `rust-version = "1.99"`; `[workspace.dependencies]` with the exact pins of plan.md Technical Context (`windows`/`windows-core` 0.62.2, `webview2-com` 0.39.1, `serde` 1.0.229, `serde_json` 1.0.154 with `preserve_order`, `thiserror` 2.0.21, `anyhow` 1.0.104, `tracing` 0.1.44, `tracing-subscriber` 0.3.23, `jiff` 1.0.1 with `tz-system`, `ureq` 3.5.2 `default-features = false` + `rustls`,`platform-verifier`,`win-system-proxy`,`gzip`, `zip` 9.8.1 `default-features = false` + `deflate`, `sha2` 0.11.1, `uuid` 1.29.3 `v4`, `embed-resource` 3.0.12, dev `tempfile` 3.27.0, `insta` 1.49.0, `proptest` 1.12.0); `[workspace.lints]` exactly as research R-20; `[profile.release]` and `[profile.dev]` as R-20 (`panic = "unwind"` in both)
- [ ] T002 [P] Check the current versions of `futures-util` (0.3.x) and `pollster` on crates.io (plan.md marks them "not checked"), pin them in `rust/Cargo.toml` `[workspace.dependencies]` with a comment naming the check date
- [ ] T003 [P] Create `rust/rust-toolchain.toml` (channel 1.99.1, target `x86_64-pc-windows-msvc`, components rustfmt, clippy), `rust/rustfmt.toml` (edition 2024), `rust/clippy.toml` (`allow-unwrap-in-tests = true`, `allow-expect-in-tests = true`)
- [ ] T004 [P] Create `rust/.config/nextest.toml`: profile `default` (all non-ignored), profile `fast` with `default-filter = "not (test-group(render) | test-group(fs-acl))"` and `fail-fast = true`; `[test-groups] fs-acl = { max-threads = 1 }`, `render = { max-threads = 2 }`; overrides that put tests named `render_*` / files `*_render.rs` into `render` and tests named `acl_*` into `fs-acl` (naming rule documented in a comment)
- [ ] T005 [P] Create `rust/.cargo/config.toml` (`rust-lld` linker for the dev profile on `x86_64-pc-windows-msvc`, shared `target-dir` for worktrees) and `rust/.cargo/mutants.toml` exactly as plan.md Test strategy (`test_tool = "nextest"`, `additional_cargo_test_args = ["--profile", "fast"]`, `test_workspace = false`, the 9 platform crates in `exclude_globs`)
- [ ] T006 [P] Create `.github/workflows/rust.yml`: `windows-latest`, triggers push/pull_request on `rust-port` and `main` with `paths: ["rust/**", ".github/workflows/rust.yml"]`, working directory `rust/`; steps: toolchain from `rust-toolchain.toml`, install prebuilt `cargo-nextest`, `cargo fmt --check`, `cargo clippy --workspace --all-targets -- -D warnings`, `cargo nextest run --profile fast`, then `cargo nextest run` (full, non-ignored), and a purity step that fails if `cargo tree -p <crate> -e normal` contains `windows` for each pure crate (`cuw-log cuw-layout cuw-settings cuw-usage cuw-status cuw-claude cuw-store cuw-release cuw-export cuw-view cuw-band-logic cuw-app-core`)
- [ ] T007 [P] Create `rust/docs/test-map.md`: table of all 36 C# test classes (plan.md Test strategy "C# class -> Rust location", LayoutTests sub-classes listed, ServiceStatusTests+StatusDecoderTests, DialModelTests+StatusLineTests) with columns `C# class | Rust test file | task ID | status | reason if not ported`; pre-fill the recorded reasons: SmokeTests not ported (placeholder `Assert.True(true)`); StatusStore/UsageStore two-thread variants become "begin while in flight" cases (sans-IO stores are single-threaded); WebViewDeathTests .NET-wrapper cases map to raw HRESULTs and `ObjectDisposed` maps to the session's `Closed` state (FR-005, INV §5)

---

## Phase 2: Foundational (S0, wave W0)

**Purpose**: compiling skeletons of every crate with the exact public API of
contracts/crate-interfaces.md and the types of data-model.md, plus `cuw-log` (every crate logs through
it), so all S1 batches can start in parallel.

**CRITICAL**: no user story batch starts before the Phase 2 checkpoint passes.

### Batch S0-B: pure crate skeletons (medium) - after S0-A; concurrent with S0-C, S0-D

Each task: `Cargo.toml` (workspace deps/lints, dependency direction of plan.md "Dependency direction"),
`src/lib.rs` with `#![forbid(unsafe_code)]`, module files named below, every public item of
contracts/crate-interfaces.md with `unimplemented!()` bodies, every type of data-model.md fully defined
(fields, derives `Debug, Clone, PartialEq` where possible), error enums with `thiserror`.

- [ ] T008 [P] Skeleton `rust/crates/cuw-layout/` (`src/lib.rs`, `model.rs` types of data-model §3 incl. `RectD`/`RectI`/`Edge`/`CellHit`/`ToolbarButton`/`HeaderButton`, `flow.rs`, `cycle.rs`, `resolve.rs`, `metrics.rs`, `geometry.rs`, `interact.rs`, `placement.rs`, `text.rs` with `TextMeasure`/`TextStyle`/`FontRole`); constants `CAPTION_LINE = 8.0 * 1.1708984375`, `EDGE_BAND = 8.0`, `DEFAULT_ORDER`
- [ ] T009 [P] Skeleton `rust/crates/cuw-settings/` (`src/lib.rs`, `model.rs` `Settings`/`LegacyFields`/`AccountProfile`/`ThemeChoice`/`BandView` exactly as data-model §2 incl. `extra: serde_json::Map` fields and `Default` values, `time.rs` `DateTimeOffset` (Eq/Ord by instant), `json.rs`, `migrate.rs`, `file_replace.rs`, `store.rs`, `error.rs`); constants `ACCOUNT_LIMIT = 4`, `LEGACY_ACCOUNT_ID = "default"`, `DEFAULT_SIDE`/`MIN_SIDE`/`MAX_SIDE`
- [ ] T010 [P] Skeletons `rust/crates/cuw-usage/` (`src/lib.rs`, `types.rs` data-model §4, `decode.rs`, `math.rs`, `buckets.rs`, `http_validation.rs`) and `rust/crates/cuw-status/` (`src/lib.rs` with `ServiceStatus`, `ENDPOINT`, `PAGE_URL`)
- [ ] T011 [P] Skeleton `rust/crates/cuw-claude/` (`src/lib.rs` URLs/`BODY_SCRIPT`, `types.rs` data-model §6, `org.rs`, `subscription.rs`, `tier.rs`, `nav.rs`, `cookie.rs`, `lease.rs`, `fetch.rs` with traits `PageFetcher` and `AccountSettings` (async fn in traits, no `dyn`))
- [ ] T012 [P] Skeletons `rust/crates/cuw-store/` (`src/lib.rs`, `usage_store.rs`, `status_store.rs`, `schedule.rs`, types of data-model §7) and `rust/crates/cuw-release/` (`src/lib.rs` constants, `version.rs`, `latest.rs`, `install.rs`, `texts.rs`, types of data-model §10). Note: `cuw-store` needs `cuw-settings` (`DateTimeOffset` in `RetryState`) and `cuw-status` (`ServiceStatus` in `StatusStore`) in addition to `cuw-usage` (see Gaps G-02)
- [ ] T013 [P] Skeletons `rust/crates/cuw-export/` (`src/lib.rs`, `payload.rs`, `path.rs`, `write.rs`) and `rust/crates/cuw-view/` (`src/lib.rs`, `dials.rs`, `tray_text.rs`, `band_text.rs`, `status_line.rs`, `notice.rs`, `theme.rs`, `opacity.rs`, `panel.rs`, `menu.rs`, types of data-model §8 incl. full `MenuId`)
- [ ] T014 [P] Skeletons `rust/crates/cuw-band-logic/` (`src/lib.rs` constants, `geometry.rs`, `columns.rs`, `probe.rs`, `visibility.rs`, `burial.rs`, `diag.rs`, types of data-model §9; uses `cuw_layout::RectI`, see Gaps G-03) and `rust/crates/cuw-app-core/` (`src/lib.rs`, `commands.rs`, `startup.rs`, `accounts.rs`, `refresh.rs`, types of data-model §11 incl. full `Command` and `Effects`)
- [ ] T015 Run `cargo check --workspace` on the merged S0-B output and the purity check of T006 locally (`cargo tree -p <crate> -e normal | Select-String windows` empty for all 12 pure crates); fix skeleton compile errors only inside `rust/crates/cuw-*` pure crates

### Batch S0-C: platform and binary skeletons (complex) - after S0-A; concurrent with S0-B, S0-D

Each task: `Cargo.toml` with the dependencies of plan.md "Dependency direction", public items of
contracts/crate-interfaces.md with `unimplemented!()` bodies, `!Send` UI-thread types. They compile
only after S0-B is merged; write against the contract, then `cargo check` after the merge.

- [ ] T016 [P] Skeleton `rust/crates/cuw-http/` (`src/lib.rs`: `Response`, `get_text`, `download_to_file`, `HttpError`)
- [ ] T017 [P] Skeleton `rust/crates/cuw-win-core/` (`src/lib.rs`, `ui.rs`, `guard.rs`, `instance.rs` with `MUTEX_NAME = "ClaudeUsageWidget.SingleInstance"`, `paths.rs`, `shell.rs`, `dialogs.rs`, `theme.rs`, `autostart.rs` with `VALUE_NAME = "ClaudeUsageWidget"`, `system_events.rs`, `monitors.rs`, `hresult.rs`, `error.rs` `WinError`); depends on `cuw-layout` (`MonitorInfo`) and `cuw-view` (`ThemeKind`) besides `windows` and `cuw-log` (see Gaps G-04)
- [ ] T018 [P] Skeleton `rust/crates/cuw-render/` (`src/lib.rs`, `renderer.rs`, `surface.rs` `LayeredSurface`/`Canvas`, `dials.rs`, `panel.rs`, `band.rs`, `tray_icon.rs`, `menu_paint.rs`, `offscreen.rs`, `error.rs`)
- [ ] T019 [P] Skeleton `rust/crates/cuw-webview/` (`src/lib.rs`, `environment.rs`, `profile.rs`, `fetch_host.rs`, `session.rs` with `impl cuw_claude::PageFetcher for Session`, `cookies.rs`, `login_window.rs`, `error.rs` `WebError`)
- [ ] T020 [P] Skeleton `rust/crates/cuw-tray/` (`src/lib.rs`, `icon.rs` `TrayIcon`, `menu_window.rs` `MenuWindow`, `menu_nav.rs` (pure keyboard/keep-open logic), `rename_dialog.rs`)
- [ ] T021 [P] Skeleton `rust/crates/cuw-widget/` (`src/lib.rs` `WidgetEvent`/`WidgetInit`, `window.rs`, `input.rs`, `edit_mode.rs`, `persist.rs`)
- [ ] T022 [P] Skeleton `rust/crates/cuw-band/` (`src/lib.rs` `BandWindow`, `window.rs`, `hooks.rs`, `probe.rs`, `lifecycle.rs`, `diag.rs`)
- [ ] T023 [P] Skeleton `rust/crates/cuw-updater/` (`src/lib.rs` `UpdaterConfig`, `check_and_install`, `after_update`; `check.rs`, `swap.rs`, `after_update.rs`)
- [ ] T024 Skeleton `rust/crates/cuw-app/`: `Cargo.toml` with `[[bin]] name = "ClaudeUsageWidget.App"` (exe name fixed for FR-022), `build.rs` using `embed-resource` with `rust/crates/cuw-app/res/app.rc`, `res/app.manifest` (PerMonitorV2 DPI awareness, comctl32 v6), `res/app.ico` copied from the C# project icon, version info from `CARGO_PKG_VERSION`; `src/main.rs` that initialises nothing yet and exits 0; empty `src/wiring.rs`, `src/preview.rs` (INV 2A §2)

### Batch S0-D: cuw-log (medium) - after S0-A; concurrent with S0-B, S0-C

- [ ] T025 [P] Write `rust/crates/cuw-log/tests/log_line.rs` porting **LogLineTests** (INV 2A §8.1): exact line `yyyy-MM-ddTHH:mm:ss.fff+03:00 <account> <event> <details>` with milliseconds and a `+03:00` offset (`jiff::Zoned` with a fixed offset), newline in details folded to one space, `\r` and `\n` each folded separately in ALL three arguments (`"wo\r\nrk"` -> `"wo  rk"`); edge cases: empty details, negative offset, offset `+00:00` (never `Z`)
- [ ] T026 [P] Write `rust/crates/cuw-log/tests/rotation.rs` (new, contracts/log-format.md): file > 1 048 576 bytes before an append moves to `widget.log.1` (overwriting an existing `.1`), exactly 1 048 576 does not rotate, rotation failure (`.1` held open without delete share) still appends, missing directory is created, unwritable path never panics or returns an error, lines end in CRLF, UTF-8 without BOM; uses `tempfile`
- [ ] T027 [P] Write `rust/crates/cuw-log/tests/layer.rs` (new): with a `tracing_subscriber::registry().with(WidgetLogLayer)`, `cuw_log::event("a", "e", "d")` writes one line, events on any other target are not written, fields `account`/`event`/`details` map to the line; `type_name_of` returns the short type name used in `<Type>: <msg>` details
- [ ] T028 Implement `rust/crates/cuw-log/src/line.rs` `format_line` and `rust/crates/cuw-log/src/lib.rs` constants (INV 2A §8.1, research R-18) so T025 passes
- [ ] T029 Implement `rust/crates/cuw-log/src/file.rs` `WidgetLogFile` (process-wide lock, rotation rule, never fails) so T026 passes
- [ ] T030 Implement `rust/crates/cuw-log/src/layer.rs` `WidgetLogLayer`, `event()`, `type_name_of` so T027 passes

### Batch S0-E: fixture capture and open decisions (owner + coordinator, no agent) - any time during S0, must finish before S1 batches that use the fixtures

- [ ] T031 [P] Capture the owner's `%APPDATA%\ClaudeUsageWidget\settings.json` written by the C# build (with a 429 pause and a subscription stamp present), scrub org ids, save as `rust/crates/cuw-settings/tests/fixtures/settings/csharp-current.json`; record CRLF / trailing newline / `DateTimeOffset` format answers for contracts/settings-json.md **VERIFY S1** (a) (quickstart "One-time fixture capture" step 1, research R-16)
- [ ] T032 [P] Run the C# build with `CUW_RENDER_MENU` and `CUW_RENDER_PREVIEW`; store `metrics.txt` as `rust/crates/cuw-render/tests/fixtures/menu-metrics-csharp.txt` and the PNGs under `rust/crates/cuw-render/tests/fixtures/preview-csharp/` (quickstart step 2; INV 2C §5, 2D §2.11)
- [ ] T033 [P] Save one real usage body and one organizations body from the C# login window DevTools, scrubbed, as `rust/crates/cuw-usage/tests/fixtures/usage/real-scrubbed.json` and `rust/crates/cuw-claude/tests/fixtures/organizations/real-scrubbed.json` (quickstart step 3)
- [ ] T034 [P] On the C# build, check whether clicking an Accounts submenu entry or a Layout item closes the menu (research R-12 open item, INV 2D §2.8, §9); record the observed rule in `rust/docs/test-map.md` "Decisions" section for T077 and T102
- [ ] T035 [P] Get the owner's sign-off on research R-21 items (stagger matched by id, updater swap R-15, `resets_at` narrowing R-17, tray icon size from taskbar DPI R-11, D2D preview renders) and the FR-024 notice change landing in S2; record answers in `rust/docs/test-map.md` "Decisions"

### Checkpoint S0

- [ ] T036 Coordinator gates on the merged S0 tree from `rust/`: `cargo fmt --check`, `cargo clippy --workspace --all-targets -- -D warnings`, `cargo nextest run --profile fast` (cuw-log tests green, other crates compile with zero tests), full `cargo nextest run`; `cargo run -p cuw-app` builds `ClaudeUsageWidget.App.exe` that starts and exits 0; `rust.yml` passes on the branch (plan S0 deliverable)

---

## Phase 3: User Story 1 - See my usage in the tray with my existing sign-in (Priority: P1) MVP (S1)

**Goal**: tray-only Rust app that reads the C# settings and sign-ins, polls claude.ai every 300 s with
stagger, 429 ladder and resume refresh, shows the digit icon, tooltip and status line, and shares the
single-instance mutex with the C# app.

**Independent Test**: on a machine signed in with the C# app, start the Rust build alone: within one poll
the tray digit and tooltip match the C# app, and `settings.json` is still readable by the C# app
(quickstart US1 1-8).

**Concurrency (wave W1)**: batches S1-A, S1-B, S1-C, S1-D, S1-E, S1-F, S1-G, S1-H, S1-I run at the same
time (disjoint crates; each codes against the S0 skeletons of the other crates). S1-J starts after W1 is
merged; S1-K after S1-J.

### Batch S1-A: cuw-settings (medium) - wave W1, concurrent with S1-B..S1-I

- [ ] T037 [P] [US1] Write `rust/crates/cuw-settings/tests/settings_migration.rs` porting **SettingsMigrationTests** (INV 2A §3.6): legacy single-account fields (org id, `RetryPausedUntil`, `ConsecutiveRateLimits`) carried into account `default` with `display_name = "default"` and `TrayAccountId = "default"`; legacy fields cleared; migrates with no org id; cap 7 -> 4 keeps the first four (`a0` first); already-migrated returns `Migrated::Unchanged`; new: the same through real legacy JSON text fixtures `tests/fixtures/settings/legacy-single-account.json`, `legacy-no-org.json`, `seven-accounts.json`; existing `TrayAccountId` kept when migrating; `TrayAccountId` not cleaned when truncating (INV §7 oddity, ported as is)
- [ ] T038 [P] [US1] Write `rust/crates/cuw-settings/tests/widget_settings_account.rs` porting **WidgetSettingsAccountTests** (all but the DuplicateAccount part, which goes to T050): `account(id)` finder, `account_with_organization` ignores the excepted id, `with_account` rewrites only that account, unknown id changes nothing (never resurrects), `tray_account` = `TrayAccountId` match else first, none when no accounts
- [ ] T039 [P] [US1] Write `rust/crates/cuw-settings/tests/widget_settings.rs` part 1 porting **WidgetSettingsTests** store and JSON cases (INV 2A §3.2-3.3, data-model §2 state table): `clamp_side` 100->150, 170, 500->340; missing file -> defaults (visible, 170, `five_hour`), not remembered; round trip of many fields incl. two accounts; layout with Status cell survives; `StatusMode`/`ModelDial`/`PlanLine` survive, absent -> None (resolve Shown/Hidden elsewhere); subscription fields and fetch stamp survive, old account JSON lacking them -> None; `ExportDirectory` survives / absent None; round-trip parts of **ThemeChoiceTests** (`"Theme": "Light"`, old file None) and **PanelOpacityTests** (`"PanelOpacity": 0.4`); corrupt file first load -> defaults with ONE `s.json.bad-*` copy (content preserved) even after two loads and a save; file turning unparseable after a good load -> `Unparseable`, file untouched, `current()` returns last good; uncopiable unparseable file (held `.bad-held`) not loaded as defaults; file vanishing after a good load -> last good; `current()` last good while held, error when no read ever succeeded; all with `tempfile` paths
- [ ] T040 [US1] Write `rust/crates/cuw-settings/tests/widget_settings.rs` part 2 (same file, after T039) porting the **WidgetSettingsTests** FileReplace cases, `#[cfg(windows)]` (INV 2A §3.4, research R-14): save replaces the file (hard link keeps old bytes, no leftover temp); save waits out a reader with ReadWrite share for 150 ms; NaN save fails, old file kept, no temp; held (no-share) file during load waited out (100 ms vs 750 ms budget) and not clobbered; unreleasable hold -> `Err(Io)` and nothing saved; read-only file -> immediate error, no retry; symlinked settings keeps the link and updates the target (skip when `CreateSymbolicLink` fails with 1314); failed replace deletes temp; stale read-only `.tmp` does not block save; reader sharing only Read is waited out; ACL deny (icacls deny W,D file + DC dir) fails at once, test named `acl_*` (group `fs-acl`); failed write with undeletable temp returns the ORIGINAL io error with `temp_cleanup_failed: Some(..)`
- [ ] T041 [P] [US1] Write `rust/crates/cuw-settings/tests/json_compat.rs` (new, contracts/settings-json.md, research R-14/R-16) with fixtures under `tests/fixtures/settings/`: UTF-8 BOM accepted; trailing commas accepted; comments rejected (unparseable); tolerant enums for every settings enum (`Theme`, `BandView`, `StatusMode`, `ModelDial`, `PlanLine`, layout enums): name case-insensitive, defined ordinal, numeric string, unknown -> first variant (value asserted), `null` -> None for nullable, `null`/bool/object/array for a non-nullable enum -> whole file unparseable; unknown keys preserved at top level, in accounts, in `Layout` and `Block` and written back after known keys; write = C# declaration order, nulls written, enum names, 2-space indent, CRLF, no BOM, integral doubles without `.0`, `LaidOut` written and ignored on read; `DateTimeOffset` lenient read forms (`T`/space, 0-9 fraction digits, `Z`/`±HH:MM`, offset-less = UTC) and STJ write (7-digit truncation, trailing zeros trimmed, `+00:00` never `Z`); byte-compare re-write of `csharp-current.json` (T031) for the stamp values; `json::number` integral -> integer
- [ ] T042 [P] [US1] Implement `rust/crates/cuw-settings/src/time.rs` `DateTimeOffset::{parse_lenient, format_stj, from_timestamp_local}` (research R-16) so the time cases of T041 pass
- [ ] T043 [P] [US1] Implement `rust/crates/cuw-settings/src/json.rs` `parse`, `to_string`, `number` with a manual `serde_json::Value` walk (research R-14, contracts/settings-json.md Reading/Writing; `SaveError::NonFinite` for NaN/inf) so T041 passes
- [ ] T044 [P] [US1] Implement `rust/crates/cuw-settings/src/migrate.rs` `migrate` and the helpers in `rust/crates/cuw-settings/src/model.rs` (`account`, `account_with_organization`, `with_account`, `tray_account`, `clamp_side`; INV 2A §3.6, data-model §2 validation rules) so T037, T038 pass
- [ ] T045 [P] [US1] Implement `rust/crates/cuw-settings/src/file_replace.rs` (temp `<target>.<32 hex>.tmp`, `sync_all`, rename-replace, `BACKOFF_MS = [50, 100, 200, 400]`, transient = raw os error 32/33/5 + read-only/probe-open with share modes, symlink target resolved; `read_to_string` retries sharing violations only; research R-14, INV 2A §3.4) so T040 passes
- [ ] T046 [US1] Implement `rust/crates/cuw-settings/src/store.rs` `SettingsStore` (`load`, `current`, `save`, `update`; migration on every successful load, never on save; bad copy `<path>.bad-<UTC yyyyMMddHHmmss>` skipped when an existing `.bad-*` has the same SHA-256; `SettingsError::is_io_class`; data-model §2 state table, INV 2A §3.2-3.3) so T039 passes (after T043-T045)

### Batch S1-B: cuw-usage + cuw-status (medium) - wave W1

- [ ] T047 [P] [US1] Write `rust/crates/cuw-usage/tests/usage_decoder.rs` porting **UsageDecoderTests** (INV 2B §6.1) with bodies as files in `tests/fixtures/usage/*.json` (the inlined C# bodies + `real-scrubbed.json` from T033): 4 buckets decoded, scalar `currency` ignored, unknown keys kept; ISO `Z` and fractional seconds; epoch-seconds numeric `resets_at`; missing/boolean reset -> None; bucketless body, HTML sign-in page, boolean utilization -> `MalformedResponse`; `weekly_scoped` limits folded into `seven_day_<slug>` with reset parsed, `kind:five_hour` ignored; slug `"Claude Opus 4.5"` -> `seven_day_claude_opus_4_5`; top-level bucket beats a limits entry; incomplete scoped entries skipped; limits alone suffice; malformed limit entries tolerated; unsluggable `!!!` skipped; new edges (research R-17): out-of-range epoch -> `Network(..)`, non-ISO date string -> None, bucket at 0 and 100, unknown model bucket kept
- [ ] T048 [P] [US1] Write `rust/crates/cuw-usage/tests/usage_math.rs` porting **UsageMathTests** (INV 2B §6.2, 2C §4.1-4.2: `remaining_text` 59s/10m/1h 0m/1d 1h, past or None -> None; `fraction` scales and clamps 140 -> 1, -5 -> 0; `percent_text` 0.575 -> `58%`, 0.574 -> `57%`, 0 -> `0%`, 1 -> `100%`) and `rust/crates/cuw-usage/tests/threshold_level.rs` porting **ThresholdLevelTests** (0 and 0.59 Ok, 0.6 and 0.84 Warning, 0.85 and 1 Danger)
- [ ] T049 [P] [US1] Write `rust/crates/cuw-usage/tests/model_buckets.rs` porting **ModelBucketsTests** (INV 2B §6.4): non-model keys filtered (`five_hour`, `seven_day`, `seven_day_oauth_apps`, `seven_day_overage_included`, `extra_usage`); order fable/opus/sonnet then unknown alphabetically (`seven_day_aardvark` before `seven_day_zebra`, after opus); `resolve(None)` fable else first; present preference honoured, stale dropped; None without model buckets; `label` strips prefix and ASCII-uppercases; `is_model_key` theory
- [ ] T050 [P] [US1] Write `rust/crates/cuw-usage/tests/usage_types.rs` porting **UsageTypesTests** (snapshot equality by contents; `get` None for missing; three fixed descriptions; rate-limited description equal with/without Retry-After; Network description = message; `RateLimited(600) == RateLimited(600)` and `!= RateLimited(None)`; no description contains `(` or `"`) plus the DuplicateAccount part of **WidgetSettingsAccountTests** (`duplicate_account` is Unauthorized kind and names the other account; default Unauthorized text contains "expired"), and `rust/crates/cuw-usage/tests/http_validation.rs` porting **HttpValidationTests** (200/204 ok; 401, 403 Unauthorized; 429 + Retry-After 600 -> RateLimited(600); 429 without -> RateLimited(None); 500 -> Network("HTTP 500"); INV 2A §7)
- [ ] T051 [P] [US1] Write `rust/crates/cuw-status/tests/service_status.rs` porting **ServiceStatusTests** and **StatusDecoderTests** (INV 2B §8, 2C §4.5, contracts/status-api.md) with `tests/fixtures/status/summary-*.json`: component and indicator mapping, unknown strings (`teleported`, `""`) -> Unknown; labels `OK SLOW PARTIAL DOWN MAINT —`; `line` speaks in Line mode when operational (`service operational`), None in Cell mode when operational, `service major outage` when not; `line_label` words; decoder: Claude Code component beats roll-up `none`, operational component beats `critical` indicator and another component's `major_outage`, indicator fallback, neither -> `MalformedResponse`, non-JSON -> `MalformedResponse`
- [ ] T052 [P] [US1] Implement `rust/crates/cuw-usage/src/types.rs` (`UsageError::{kind, description, duplicate_account, network}` with the INV 2B §7.7 texts, `UsageSnapshot::{new, get}`, `ThresholdLevel`) so T050 (types part) passes
- [ ] T053 [P] [US1] Implement `rust/crates/cuw-usage/src/decode.rs` (`snapshot`, `slug`, `reset_date`; research R-17) so T047 passes
- [ ] T054 [P] [US1] Implement `rust/crates/cuw-usage/src/math.rs`, `src/buckets.rs`, `src/http_validation.rs` (INV 2B §6.2-6.4, 2A §7) so T048, T049, T050 (validation part) pass
- [ ] T055 [P] [US1] Implement `rust/crates/cuw-status/src/lib.rs` `decode`, `component`, `indicator`, `label`, `line_label`, `line` so T051 passes

### Batch S1-C: cuw-claude (medium) - wave W1

- [ ] T056 [P] [US1] Write `rust/crates/cuw-claude/tests/organization_picker.rs` porting **OrganizationPickerTests** (INV 2B §5.1-5.3) with bodies in `tests/fixtures/organizations/*.json` (the three documented bodies + `real-scrubbed.json`): team+chat preferred over earlier personal and non-chat; first chat-capable fallback; `id` when `uuid` missing; `describe` lists every key in order, quotes tier-like values, not `name`; `describe("nope", None) == "not-json"`; `fields` raw capabilities/tier/raven of the picked org; empty (not None) capabilities for a found org with none; None capabilities when the picked org is absent or the body is not JSON; non-string capabilities skipped, non-string `rate_limit_tier` -> None; `pick` None for `[]` and garbage
- [ ] T057 [P] [US1] Write `rust/crates/cuw-claude/tests/subscription_refresh.rs` porting **SubscriptionRefreshTests** (INV 2B §5.5: 1 h cadence, None stamp refreshes, 59 min waits, exactly 1 h refreshes, `learned_nothing` only for all-None, empty capability list is an answer, `merge` keeps/replaces, nothing+nothing, stamp 5 min in the future refreshes) and `rust/crates/cuw-claude/tests/subscription_tier.rs` porting **SubscriptionTierTests** (INV 2B §5.6, every case listed in INV §5 2B §12: Ultra/Team/Team Premium/Max/Pro/Free/Max or Ultra/Max 40x/Enterprise/prettified unknown tiers, `has_five_hour_limit` theory incl. None -> true, `label_or_null` AND guard, `label_for` incl. Team + `seven_day_fable` -> Team Premium and the five non-Premium variants, Ultra with fable stays Ultra, never-fetched -> None)
- [ ] T058 [P] [US1] Write `rust/crates/cuw-claude/tests/navigation_retry_policy.rs` porting **NavigationRetryPolicyTests** (5 transient names true; `HostNameNotResolved`, `"Unknown "`, `"unknown"`, `""`, `CertificateCommonNameIsIncorrect` false; `RETRY_DELAY == 3 s`), `rust/crates/cuw-claude/tests/webview_death.rs` porting **WebViewDeathTests** (dead for 0x8007139F and 0x80010108, not dead for E_FAIL 0x80004005 and E_NOINTERFACE 0x80004002 (the `NotImplementedException` case); .NET wrapper/`ObjectDisposed` cases recorded in `rust/docs/test-map.md`), and `rust/crates/cuw-claude/tests/nav_classify.rs` (new, contracts/claude-web.md "Navigation outcome"): HTTP status first (401/403/429/5xx mapped via `http_validation`), success, `web_error_from_raw` for every listed value and `Other(n)` name = number, `unwrap_script_result` JSON string unwrap and malformed -> `MalformedResponse`, `process_failed_resets` true for 0,1,2 only, `NAVIGATION_TIMEOUT == 30 s`
- [ ] T059 [P] [US1] Write `rust/crates/cuw-claude/tests/fetch_idle_lease.rs` porting **FetchIdleLeaseTests** (INV 2B §2.2: last exit returns a token and `should_release` true; no token while another user holds; a new enter invalidates the old token; token not honoured while in use; exit without enter -> `Err(ExitWithoutEnter)`; `DELAY > 0` and `<= 30 s`) and `rust/crates/cuw-claude/tests/cookie.rs` (new, INV 2B §4.1: session cookie predicate on name/domain/value incl. empty value, other domain, case rules)
- [ ] T060 [P] [US1] Write `rust/crates/cuw-claude/tests/fetch_usage.rs` (new, contracts/claude-web.md "Usage fetch sequence", INV 2B §5.4) over a fake `PageFetcher` and fake `AccountSettings` driven with `pollster`: no cached org -> organizations fetched, picked, saved, then usage URL; cached org -> usage only; org already used by another account -> DuplicateAccount error + log `organization duplicate-of <name>`; subscription refresh only when `should_refresh`, merge rules, `refresh-failed`/`refresh-unparsed` logs, per-run `subscription_fetch_failed` latch; account gone (`subscription` None) handled; `PageError::into_usage_error` mapping (`Claude.ai navigation failed: <name>.`, Platform -> Network(message)); `clear_cached_organization` saves None
- [ ] T061 [P] [US1] Implement `rust/crates/cuw-claude/src/org.rs` (`pick`, `fields`, `describe`; INV 2B §5.1-5.3) so T056 passes
- [ ] T062 [P] [US1] Implement `rust/crates/cuw-claude/src/subscription.rs` and `rust/crates/cuw-claude/src/tier.rs` (INV 2B §5.5-5.6) so T057 passes
- [ ] T063 [P] [US1] Implement `rust/crates/cuw-claude/src/nav.rs`, `src/cookie.rs`, `src/lease.rs` (INV 2B §2.2-2.3, §3.2-3.4, §4.1) so T058, T059 pass
- [ ] T064 [US1] Implement `rust/crates/cuw-claude/src/fetch.rs` (`fetch_usage`, `clear_cached_organization`, `PageError::into_usage_error`; logs via `cuw_log::event`) so T060 passes (after T061-T063)

### Batch S1-D: cuw-store + cuw-release version (medium) - wave W1

- [ ] T065 [P] [US1] Write `rust/crates/cuw-store/tests/usage_store.rs` porting **UsageStoreTests** against the sans-IO API (INV 2B §7, data-model §7 state machine): starts Loading; `finish_load(Ok)` -> `Ok(snapshot, source_updated_at ?? now)` + last snapshot; `begin_load(.., has_credentials=false)` -> `Failed(NoCredentials)` with no ticket; Unauthorized surfaced; platform errors -> `Failed(Network)`; last snapshot kept on failure (`snapshot_for_display` returns it); 429 pauses until `now + 3600 + 300` and the next `begin_load` returns `Paused`; resumes after the deadline; 429 without Retry-After uses the ladder; 1h -> 6h -> 24h escalation (+300 s each) and Retry-After larger than the ladder wins; success resets escalation; pause survives a rebuilt store (`new(Some(state))` -> `Failed(RateLimited(None))`, no fetch); coalescing = `JoinInFlight` while in flight (replaces the two-thread variants, reason in test-map); Changed (`changed` flag) once on success and not while paused; expired pause at construction requests a clear; `PersistedRetry::Unreadable` starts unpaused and requests no write; a failed persist does not fail a good fetch; unchanged retry state not re-requested (exactly 2 persist requests: pause, clear); a clear that failed at construction is re-requested on the next success and then skipped (`[None, None]`); the "throwing Changed subscriber" case is covered by T084 (reason in test-map)
- [ ] T066 [P] [US1] Write `rust/crates/cuw-store/tests/status_store.rs` porting **StatusStoreTests** (INV 2B §8 store): starts Unknown; publishes on success; keeps last on a later failure; failing first load stays Unknown; overlapping loads -> `JoinInFlight` (replaces gated and two-thread variants); changed once per successful load (also when equal), never on failure
- [ ] T067 [P] [US1] Write `rust/crates/cuw-store/tests/poll_schedule.rs` porting **PollScheduleTests** (INV 2B §9.2: 4 accounts -> 0/75/150/225 s; single 0; error for index == count and count 0 (the C# index -1 case is unrepresentable with `usize`, reason in test-map); proptest for 1..=8 accounts: offsets within [0, 299] s and distinct)
- [ ] T068 [P] [US1] Write `rust/crates/cuw-release/tests/core_info.rs` porting **CoreInfoTests** (INV 2A §8.2: `parse_version(Some("1.2.3+abcdef1234")) == "1.2.3"`; None, `""`, `"+abcdef"` -> `"unknown"`; `env!("CARGO_PKG_VERSION")` result never empty)
- [ ] T069 [P] [US1] Implement `rust/crates/cuw-store/src/usage_store.rs` (`REFRESH_INTERVAL`, `RETRY_MARGIN`, `LADDER`, `UsageStore` per data-model §7) so T065 passes
- [ ] T070 [P] [US1] Implement `rust/crates/cuw-store/src/status_store.rs` so T066 passes
- [ ] T071 [P] [US1] Implement `rust/crates/cuw-store/src/schedule.rs` `offset_for` (`cycle * i / n`, `ScheduleError`) so T067 passes
- [ ] T072 [P] [US1] Implement `rust/crates/cuw-release/src/version.rs` `parse_version` so T068 passes

### Batch S1-E: cuw-view S1 parts (medium) - wave W1

- [ ] T073 [P] [US1] Write `rust/crates/cuw-view/tests/dial_model.rs` porting **DialModelTests** (INV 2C §4.6): dial from bucket (`2h 0m`); missing bucket/snapshot -> empty dial; three dials in fixed order `5H, 7D, FABLE`, `MODEL` title without model bucket; pinned model bucket honoured; spent week moves the 5H countdown to the week (`3d 0h`) with percent kept, 99% does not, spent per-model does not; Ultra week-only = `no_limit` with `"no limit"`; other/unknown plans n/a; unrecognised Max tier keeps no_limit; neither window -> not no_limit; Ultra with a five-hour bucket shows the number; None snapshot not no_limit
- [ ] T074 [P] [US1] Write `rust/crates/cuw-view/tests/status_line.rs` porting **StatusLineTests** (INV 2B §7.6, 2C §1.6): silent when healthy; `updated 16m ago` at >= 15 min; `unexpected response from the API`; `HTTP 500`; None for NoCredentials/Unauthorized; `loading…`; rate limited with deadline `rate limited · retry in 10m`, without / expired `rate limited`
- [ ] T075 [P] [US1] Write `rust/crates/cuw-view/tests/tray_text.rs` porting **TrayTextTests** (INV 2C §4.3, 2D §1.3): metrics `5H 57% / 7D 38% / FAB 8%`; 0.575 -> 58%; missing fraction -> `—` with label `MOD`; no_limit -> `∞`; `icon_index` fallback theory (chosen 0 with no_limit -> 1; others stay; no fallback when the week has no figure); label cut rules (`OPUS`, `W`, `FIVE` kept; `SON`, `FIV` cut); empty title -> no metric; no models -> none; new: `tooltip` text `Claude Usage Widget — 5H 42% · 7D 18% · FAB 8%` + status line on a new line, the three tooltip constants, `metric_index` (`seven_day` -> 1, `model` -> 2, other -> 0), `icon_digits` digits-only (`"42%"` -> `"42"`, `"—"`/`"∞"` -> None, `"100%"` -> `"100"`)
- [ ] T076 [P] [US1] Write `rust/crates/cuw-view/tests/account_row.rs` porting **AccountRowTests** (INV 2C §4.6, all 12 cases of INV §5 2C §10: one row per account in settings order with 3 dials, fraction 0.62, `1h 12m`; band time follows 5H into a spent week `3d 0h`; null/missing snapshot keeps the row; single; none -> no rows; plan labels Ultra/Team/Max and Team Premium from its own snapshot; Ultra no_limit vs Max; unfetched plan -> None; chats-only -> `Free`; uses DisplayName not Id)
- [ ] T077 [P] [US1] Write `rust/crates/cuw-view/tests/menu_model.rs` (new, INV 2D §2.1-2.8): `build` returns the full item list in INV order with texts, glyphs (`glyph_for`), separators and submenus; Tray shows radio ticks from `TrayMetricKey` (unknown key -> 5H); Model limit `Auto` + one item per available bucket with label, tick on resolved/pinned; Sign in visibility rule; Accounts submenu: one entry per account with tray-account tick, Rename/Remove, `Add account…` disabled at 4 with tooltip `The widget shows at most 4 accounts.`; `closes_menu` per item per INV 2D §2.8 and the decision recorded by T034; `without(&[ids])` removes items and empty submenus; `find`; version caption uses `MenuState.version`
- [ ] T078 [P] [US1] Write `rust/crates/cuw-view/tests/taskbar_ink.rs` (new, INV 2D §1.4, 2C §3.4): `taskbar_ink(Light) == #1B1B1B`, `taskbar_ink(Dark) == white`; `DARK`/`LIGHT` palette constants equal INV 2C §3.4 hex values (Light `Track` = `#B7BECC`, INV §7 2C §0 item 4); `alpha_byte` rounds half to even (0.5/255 cases)
- [ ] T079 [P] [US1] Implement `rust/crates/cuw-view/src/dials.rs` (`all`, `is_spent`, `account_rows`, `subscription_of`; INV 2C §4.6) so T073, T076 pass
- [ ] T080 [P] [US1] Implement `rust/crates/cuw-view/src/status_line.rs` (`text`; `panel_line` may stay `unimplemented!()` until S2) and `rust/crates/cuw-view/src/tray_text.rs` so T074, T075 pass
- [ ] T081 [P] [US1] Implement `rust/crates/cuw-view/src/menu.rs` (`MenuModel`, `MenuItem`, `build`, `glyph_for`, `without`, `find`; INV 2D §2.1-2.8) so T077 passes
- [ ] T082 [P] [US1] Implement in `rust/crates/cuw-view/src/theme.rs` the `DARK`/`LIGHT` palettes, `taskbar_ink`, `alpha_byte`, `PANEL_ALPHA`, `OVERLAY_ALPHA` (INV 2C §3.3-3.4) so T078 passes

### Batch S1-F: cuw-win-core (complex) - wave W1

- [ ] T083 [P] [US1] Write unit tests in `rust/crates/cuw-win-core/src/ui.rs` `#[cfg(test)]` for the window-free executor core (run queue, a task is never polled while it is being polled, a task woken during its own poll is re-queued, re-entrant drain from a nested loop polls other tasks, `callback()` oneshot resolves, dropped sender) and `rust/crates/cuw-win-core/tests/executor_live.rs` `#[ignore]` for the real hidden-window loop (`spawn_local`, `post` from another thread, `sleep`, `UiTimer` start/stop, `run_blocking`, nested `MessageBoxW` keeps tasks running) (research R-02)
- [ ] T084 [P] [US1] Write `rust/crates/cuw-win-core/tests/guard.rs` (new, FR-020, research R-19): `guarded` returns the closure value; a panic returns the fallback and writes `unhandled panic <label>: <message>` through `cuw_log` (captured with a test layer); a second guarded call after a panic still works; a sequence of handlers where one panics still runs the others (the C# "throwing Changed subscriber" case of UsageStoreTests)
- [ ] T085 [P] [US1] Write `rust/crates/cuw-win-core/tests/instance.rs` (new, FR-004, INV 2A §1.1): `acquire` with a unique test mutex name returns `Some` first and `None` while held, `Some` again after drop; `MUTEX_NAME == "ClaudeUsageWidget.SingleInstance"`
- [ ] T086 [P] [US1] Write `rust/crates/cuw-win-core/tests/theme_read.rs` (new, INV 2C §3.2): theme kind from registry DWORDs through an internal `RegistryRead` trait with a fake: `AppsUseLightTheme`/`SystemUsesLightTheme` 1 -> Light, 0 -> Dark, missing/failed read -> Dark; change detection reports only when a value changed
- [ ] T087 [US1] Implement `rust/crates/cuw-win-core/src/ui.rs` (STA init, hidden wake window `WM_APP_WAKE`, re-entrant executor, `SetTimer`-based `sleep`/`UiTimer`, `run_blocking` on `std::thread`, `post`, `callback`; research R-02) so T083 passes
- [ ] T088 [P] [US1] Implement `rust/crates/cuw-win-core/src/guard.rs` and the panic hook in `ui::init` (file:line in the log line; research R-19) so T084 passes
- [ ] T089 [P] [US1] Implement `rust/crates/cuw-win-core/src/instance.rs` (`CreateMutexW(None, true, name)`, `ERROR_ALREADY_EXISTS` -> `None`; research R-22), `src/paths.rs` (`SHGetKnownFolderPath`, `GetTempPathW`, `GetModuleFileNameW`), `src/shell.rs` (`ShellExecuteW` open), `src/dialogs.rs` (`MessageBoxW` with `MB_DEFAULT_DESKTOP_ONLY`), `src/hresult.rs` so T085 passes
- [ ] T090 [US1] Implement `rust/crates/cuw-win-core/src/theme.rs` (`apps_kind`, `taskbar_kind`) and `rust/crates/cuw-win-core/src/system_events.rs` (hidden top-level `CuwSystemEvents` window: `WM_SETTINGCHANGE` -> `ThemeChanged` only on change, `WM_POWERBROADCAST`/`PBT_APMRESUMEAUTOMATIC` -> `Resumed`, registered `TaskbarCreated`, `WM_DISPLAYCHANGE`, tray callback message; every wndproc body inside `guarded`; research R-13, INV 2A §1.6) so T086 passes
- [ ] T091 [P] [US1] Implement `rust/crates/cuw-win-core/src/monitors.rs` (`all` via `EnumDisplayMonitors` + `GetMonitorInfoW` + `GetDpiForMonitor`, `from_point` with `MONITOR_DEFAULTTONEAREST`, `drag_threshold_px` from `SM_CXDRAG`/`SM_CYDRAG`; INV 2C §1.9)

### Batch S1-G: cuw-webview (complex) - wave W1

- [ ] T092 [P] [US1] Write `rust/crates/cuw-webview/tests/webview_logic.rs` (headless, new) for the pure helpers exposed by the crate: fetch-host `WM_WINDOWPOSCHANGING` rewrite keeps `SWP_HIDEWINDOW` and the (-32000,-32000) position (research R-06); profile check ordinal-ignore-case and the exact mismatch text `WebView2 gave profile '<actual>' for account '<label>', expected '<name>'.` (research R-05); login window title `Sign in to Claude.ai — account "<label>"`; paste-box https-only check (`That is not an https link.`); `WebError::is_dead` for `Com{0x8007139F}`, `Com{0x80010108}`, `Closed`
- [ ] T093 [P] [US1] Write `rust/crates/cuw-webview/tests/live.rs` `#[ignore]` (owner only): environment on a temp UDF (failure not cached: second call retries), `default` account uses the implicit profile and a named id gets `ProfileName == id`, fetch session navigates a local `data:`/file page and reads the body, no visible window appears, cookies read, idle lease closes after 15 s, killing the browser process triggers reset + one retry, login window opens and closes
- [ ] T094 [P] [US1] Implement `rust/crates/cuw-webview/src/environment.rs` (`Environment::shared`: create UDF dir, `CreateCoreWebView2EnvironmentWithOptions(None, udf, None)`, failure not cached, `0x8007139F` -> wait 2 s and retry once then `Network(<message>)`; research R-04, INV 2B §1.1, §1.3; FR-003)
- [ ] T095 [P] [US1] Implement `rust/crates/cuw-webview/src/profile.rs`: one controller-creation helper used by login and fetch (`default` -> `CreateCoreWebView2Controller`, other ids -> `CreateCoreWebView2ControllerOptions` + `put_ProfileName(id)`), verify `ICoreWebView2_13::Profile().ProfileName()` (research R-05, INV 2B §1.2; FR-003)
- [ ] T096 [US1] Implement `rust/crates/cuw-webview/src/fetch_host.rs`: hidden `CuwFetchHost` window (`WS_POPUP`, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, never shown, `WM_SHOWWINDOW` swallowed, position rewrite), controller `put_IsVisible(false)`, event hooks ProcessFailed (reset kinds via `cuw_claude::nav::process_failed_resets`), DownloadStarting cancel+handled (`nav-download`), WebResourceResponseReceived diagnostics for `/api/organizations` (`resource`), NavigationStarting redirect log (`nav-redirect`), tokens owned and removed on close (research R-06, INV 2B §2.1, contracts/claude-web.md Diagnostics)
- [ ] T097 [US1] Implement `rust/crates/cuw-webview/src/session.rs` `Session` + `impl cuw_claude::PageFetcher`: serialization gate (INV 2B §2.4), navigate + `NavigationCompleted` with 30 s UI-timer race, `cuw_claude::nav::classify`, `ExecuteScript(BODY_SCRIPT)` + `unwrap_script_result`, retry once after `RETRY_DELAY` on transient (`retry-ok`, `nav-failed` logs), dead detection -> `webview-reset` + one retry (INV 2B §2.3), idle lease with `put_MemoryUsageTargetLevel` Low/Normal (`E_NOINTERFACE` skipped, `webview-idle`), `close()` (after T095, T096)
- [ ] T098 [P] [US1] Implement `rust/crates/cuw-webview/src/cookies.rs` (`CookieManager().GetCookies("https://claude.ai")` -> `cuw_claude::cookie::Cookie` list; INV 2B §4.1)
- [ ] T099 [US1] Implement `rust/crates/cuw-webview/src/login_window.rs` and `Session::{open_login_window, on_signed_in, set_live_label}`: 1000x720 DIP centred window, STATIC hint, EDIT paste box (Enter subclass), `Open` button, controller fills the rest (`put_Bounds` on `WM_SIZE`, `MoveFocus`, `NotifyParentWindowPositionChanged`), init = controller -> verify profile -> `ClearBrowsingDataAll` -> navigate `LOGIN_URL` -> cookie poll every 2 s and after each NavigationCompleted -> SignedIn then close; reuse existing / still opening; `closed` flag after every await; logs `login-window open|closed signedIn=<True|False>|reuse existing|still opening|create-failed <Type>: <msg>` (research R-07, INV 2B §4.2-4.3)

### Batch S1-H: cuw-render tray icon + menu paint, cuw-tray icon + menu window (complex) - wave W1

- [ ] T100 [P] [US1] Write `rust/crates/cuw-render/tests/tray_icon_fit.rs` (headless, new, research R-11, INV 2D §1.4) for the pure digit-fit function with a fake `TextMeasure`: em starts at `0.95*H`, steps down 0.5 px to a floor of 6, first size with `width + em/3 <= W - 1` wins; `"5"`, `"42"`, `"100"` at 16, 20, 24, 32 px; floor reached for very wide text; ring geometry (2 px pen inset 2)
- [ ] T101 [P] [US1] Write `rust/crates/cuw-render/tests/tray_menu_render.rs` (test group `render`, tests named `render_*`): tray icon for `None`/`"5"`/`"42"`/`"100"` at 16/24/32 px yields non-empty alpha with the requested ink and the ring when `None`; `layout_menu` row heights/paddings at 96/144 dpi equal the values in `tests/fixtures/menu-metrics-csharp.txt` (T032) within 1 px
- [ ] T102 [P] [US1] Write `rust/crates/cuw-tray/tests/menu_nav.rs` (headless, new, INV 2D §2.8, research R-12) for `src/menu_nav.rs`: Up/Down skip separators, disabled and hidden items and wrap; Right/Enter on a submenu opens it, Left closes it; Esc closes the innermost; Enter on a `closes_menu` item closes all, on a toggle keeps open; hover opens a submenu after 400 ms (injected clock) or at once on click; and the tooltip truncation helper (127 UTF-16 units, never splits a surrogate pair)
- [ ] T103 [P] [US1] Write `rust/crates/cuw-tray/tests/live.rs` `#[ignore]`: `TrayIcon::create` adds an icon, `set_icon`/`set_tooltip` modify it, `readd` after a simulated TaskbarCreated, `Drop` deletes it; `MenuWindow::show_at_cursor`, `update` while open, `close`
- [ ] T104 [US1] Implement `rust/crates/cuw-render/src/renderer.rs` (D2D/DWrite/WIC factories, `TextMeasure` impl via `IDWriteTextLayout` with fonts Consolas, Segoe UI, Segoe UI Variable Text -> Segoe UI fallback, Segoe MDL2 Assets and weights 600/500/700; research R-10) and `rust/crates/cuw-render/src/offscreen.rs` (`render_png` on a WIC software target)
- [ ] T105 [US1] Implement `rust/crates/cuw-render/src/tray_icon.rs` (`render(digits, ink, size_px)` -> `Hicon` via `CreateIconIndirect`, grayscale AA, `Drop` = `DestroyIcon`; digit fit of T100; INV 2D §1.2, §1.4, research R-11) so T100, T101 (icon part) pass
- [ ] T106 [P] [US1] Implement `rust/crates/cuw-render/src/menu_paint.rs` (`layout_menu`, `draw_menu`: Panel fill, 1 px Track border, hover rounded rect radius 4 alpha 153 inset 3, tick U+E73E in Accent, separators, submenu arrows, glyphs at `0.72*px`; INV 2D §2.9, research R-12) so T101 (menu part) passes
- [ ] T107 [US1] Implement `rust/crates/cuw-tray/src/icon.rs` `TrayIcon` (`Shell_NotifyIconW` `NIM_ADD` uID 1 without GUID, `NIF_MESSAGE|NIF_ICON|NIF_TIP|NIF_SHOWTIP`, `NIM_SETVERSION` 4, placeholder ring, `set_icon` destroys the previous HICON after `NIM_MODIFY`, `set_tooltip` truncates to 127, `readd` = `NIM_ADD` again, `icon_size_px` = `GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForWindow(Shell_TrayWnd))` fallback `GetDpiForSystem`, `Drop` = `NIM_DELETE`; INV 2D §1.1-1.3, research R-11)
- [ ] T108 [US1] Implement `rust/crates/cuw-tray/src/menu_nav.rs` (pure, so T102 passes) and `rust/crates/cuw-tray/src/menu_window.rs` (`CuwMenu` popup per (sub)menu, `WS_EX_TOOLWINDOW|WS_EX_TOPMOST`, `CS_DROPSHADOW`, Win11 round corners via `DwmSetWindowAttribute(33, 2)` ignoring the result, `SetForegroundWindow` on open, bottom edge at the cursor then `cuw_layout::placement::within`, keyboard/mouse, submenus off the right edge (left if no room), close on Esc / outside click / close actions, keep-open re-sync via `on_opening` and `update`, comctl32 tracking tooltips, `set_palette`; every wndproc inside `guarded`; INV 2D §2.8-2.10, research R-12)

### Batch S1-I: cuw-http + cuw-layout placement (medium) - wave W1

- [ ] T109 [P] [US1] Write `rust/crates/cuw-http/tests/http.rs` (new) against a loopback `std::net::TcpListener` server in the test (no external network): `get_text` returns status and body, sends the given headers (`User-Agent`), follows redirects, decodes gzip, times out with `HttpError`; non-2xx returns `Response` (not an error); `download_to_file` writes the body to `dest` and returns the status
- [ ] T110 [P] [US1] Write `rust/crates/cuw-layout/tests/menu_placement.rs` porting **MenuPlacementTests** (INV 2D §2.10, §6, 1920x1080 with a 48 px bottom taskbar): menu 260x600 at (1700,1060) -> (1440, 432); inside stays; top taskbar (work top 48, y 20) -> 48; side taskbar (work width 1872, x 1700) -> 1612; 1200-tall menu -> y 0; second monitor at x -1920, y 480 -> 432
- [ ] T111 [P] [US1] Write `rust/crates/cuw-layout/tests/placement_units.rs` (new, research R-09): `to_px`/`to_dip` at 96/120/144/192 dpi with round-half-away-from-zero (e.g. 0.5 px cases, negative coordinates)
- [ ] T112 [US1] Implement `rust/crates/cuw-http/src/lib.rs` (`ureq` agent: rustls + platform verifier, WinINET system proxy, gzip, per-call timeout; status 100 s and GitHub 5 min are passed by callers; research R-03) so T109 passes
- [ ] T113 [US1] Implement `rust/crates/cuw-layout/src/placement.rs` `within`, `to_dip`, `to_px`, `MonitorInfo` (`clamp_to_work_area` and `restore_position` stay `unimplemented!()` until T137) so T110, T111 pass

### Batch S1-J: cuw-app-core S1 parts (medium) - wave W2, after W1 is merged

- [ ] T114 [P] [US1] Write `rust/crates/cuw-app-core/tests/startup.rs` (new, INV 2A §1.2-1.4): `retry_interval` 2 s during the first minute of `WaitingForSettings`, 30 s after 1 min (boundary at exactly 60 s); `first_run_account()` = id `default`, display name `Claude` (INV §7 2A §12 oddity kept)
- [ ] T115 [P] [US1] Write `rust/crates/cuw-app-core/tests/refresh.rs` (new, INV 2B §9.2-9.4, FR-007) with fake `AppPorts`/`AccountPort`/`Clock` (virtual time, `pollster`/manual executor): `refresh_all` with 4 accounts sleeps 0/75/150/225 s and loads each once; account matched by id after a rename during the wait (R-21 decision of T035; if the owner rejects, port the C# skip instead); account removed during the wait is skipped (`account_alive` false); status loaded last; `refresh_account`: cookie check Err -> `set_webview_failing(true)` and no load, Ok(false) -> `set_has_cookie(false)` then load (store yields NoCredentials), Ok(true) -> load then export; overlapping `refresh_all` calls do not start duplicate loads (the store coalesces; assert one load per account)
- [ ] T116 [P] [US1] Write `rust/crates/cuw-app-core/tests/commands_tray.rs` (new, INV 2A §1.7, 2D §2.2-2.4): `apply` for `TrayMetric(SevenDay)` sets `tray_metric_key = "seven_day"` with effects `tray_icon`+`tooltip`+`menu`; `ModelBucket(None|Some)` sets `model_bucket` with render effects; `RefreshNow` -> `refresh = Some(TrayAccountAndStatus)` per INV 2D §2.2; `SignIn(Tray)` -> `open_login = Some(tray account id)` and log `sign-in-requested source=tray`; `OpenUrl(Tray, url)` logs `browser-open site=tray url=<u>`; `Quit` -> `quit`; each command mutates only its own field (other fields and `extra` unchanged)
- [ ] T117 [P] [US1] Write `rust/crates/cuw-app-core/tests/system_events.rs` (new, FR-024 fix 4 regression, INV 2D §1.1, research R-11) against a fake `TrayHost` port: `TaskbarCreated` -> `readd()` then icon and tooltip re-render, also when it arrives twice; `ThemeChanged` with a changed taskbar kind -> icon re-render, unchanged -> nothing; `DisplayChanged` -> icon re-render; `Resumed` -> `RefreshScope::All`. Requires the `tray` module of T120 (contract addition, see Gaps G-05)
- [ ] T118 [US1] Implement `rust/crates/cuw-app-core/src/commands.rs` `apply` + `Effects` for the S1 commands (`TrayMetric`, `ModelBucket`, `RefreshNow`, `SignIn`, `OpenUrl`, `Quit`) so T116 passes; other commands return `CommandError::NotYet` until their stage
- [ ] T119 [P] [US1] Implement `rust/crates/cuw-app-core/src/startup.rs` and `rust/crates/cuw-app-core/src/refresh.rs` (`refresh_account`, `refresh_all` using `cuw_store::schedule::offset_for`; INV 2B §9.2-9.4) so T114, T115 pass
- [ ] T120 [P] [US1] First add to contracts/crate-interfaces.md (coordinator approval) a `cuw-app-core::tray` module: `trait TrayHost { fn readd(&mut self); fn render_icon(&mut self); fn render_tooltip(&mut self); }` and `fn on_system_event(e: &SystemEventKind, host: &mut impl TrayHost) -> Option<RefreshScope>` with a pure `SystemEventKind` mirror of `cuw_win_core::system_events::SystemEvent`; then implement it in `rust/crates/cuw-app-core/src/tray.rs` so T117 passes

### Batch S1-K: cuw-app S1 wiring (complex) - wave W2, after S1-J

- [ ] T121 [P] [US1] Write `rust/crates/cuw-app/tests/smoke.rs` `#[ignore]` (owner only): the built exe (`env!("CARGO_BIN_EXE_ClaudeUsageWidget.App")`) with a temp `APPDATA`/`LOCALAPPDATA` starts, a second instance exits 0 within 2 s, the first creates `widget.log` only on events
- [ ] T122 [US1] Implement `rust/crates/cuw-app/src/main.rs`: parse args (`--after-update <pid>` accepted and ignored until S5), `ui::init`, tracing = `WidgetLogLayer` on `%LOCALAPPDATA%\ClaudeUsageWidget\widget.log` + debug layer to `OutputDebugStringW` in debug builds, panic hook, `instance::acquire(MUTEX_NAME)` -> exit 0 silently when taken (FR-004), build `App`, `ui::run()` (INV 2A §1.1, contracts/crate-interfaces.md cuw-app)
- [ ] T123 [US1] Implement `rust/crates/cuw-app/src/wiring.rs` `App`: `SettingsStore` at `%APPDATA%\ClaudeUsageWidget\settings.json`; startup retry loop on `is_io_class` errors with `startup::retry_interval`, tooltip `TOOLTIP_SETTINGS_RETRY` and log `settings unreadable at startup, retrying: <Type>: <msg>` (INV 2A §1.2); `BuildAccounts` with `first_run_account` when the file had no accounts (INV 2A §1.3); one runtime per account = `Session` (UDF `%LOCALAPPDATA%\ClaudeUsageWidget\profiles\default`, profile name per `accounts::webview_profile_name`) + `UsageStore` with persisted retry state written through `SettingsStore::update` (quiet, logged `settings <what> not saved`); `impl AccountSettings`, `impl Clock`, `impl AppPorts`; every store-change subscriber called inside `guarded` (FR-020)
- [ ] T124 [US1] Implement `rust/crates/cuw-app/src/tray.rs`: `SystemEvents` + `TrayIcon` + `MenuWindow`; icon = `tray_text::icon_digits` of the tray account's `icon_index` metric rendered with `theme::taskbar_ink(taskbar_kind())` at `TrayIcon::icon_size_px()`, placeholder ring before data (US1 scenario 4); tooltip via `tray_text::tooltip` (+ `TOOLTIP_WEBVIEW_ERROR` / `TOOLTIP_DEFAULT` cases, INV 2D §1.3); `impl cuw_app_core::tray::TrayHost` and route `SystemEvent`s through `on_system_event` so TaskbarCreated re-adds the icon (FR-024 fix 4); menu from `menu::build` with every not-yet-implemented `MenuId` removed by `MenuModel::without`; menu ids -> `Command` -> `apply` -> `Effects` executor (render, refresh, open login, open URL, quit); menu handler errors logged `unhandled tray <Type>: <msg>`
- [ ] T125 [US1] Implement `rust/crates/cuw-app/src/poll.rs`: `UiTimer` every `REFRESH_INTERVAL` (300 s) -> `refresh_all`; triggers at start, `Resumed`, sign-in (`on_signed_in` -> `RefreshScope::Account(id)`) and Refresh now (INV 2B §9.1, §9.4); status load = `run_blocking(cuw_http::get_text(cuw_status::ENDPOINT, UA, 100 s))` -> `http_validation::validate` -> `cuw_status::decode` -> `StatusStore` (INV 2B §8)

### Checkpoint S1 (US1)

- [ ] T126 [US1] Coordinator gates after merging each S1 batch: `cargo fmt --check`, `cargo clippy --workspace --all-targets -- -D warnings`, `cargo nextest run --profile fast`; after the last merge the full `cargo nextest run` and `cargo mutants --in-diff` for `cuw-settings cuw-usage cuw-status cuw-claude cuw-store cuw-view cuw-app-core cuw-layout cuw-log` (each surviving mutant gets a test or an "equivalent" note); update `rust/docs/test-map.md` status for every class ported in S1
- [ ] T127 [US1] Owner manual validation: quickstart "US1 tray + polling" steps 1-8 (incl. FR-024 fix 4 Explorer restart, step 5), plus the live checks research R-03 (status fetch behind the owner's network), R-04 (Rust start within 1 s of quitting C#, no 0x8007139F), R-06 (no visible window during polls; `WebErrorStatus` integers vs `webview2-com-sys`), R-11 (icon digit size vs C# for "5"/"42"/"100" at 100 % and 150 %), R-16 / settings-json VERIFY S1 (C# build reads a Rust-written settings.json with a pause and a stamp); run the `#[ignore]` suites of `cuw-win-core`, `cuw-webview`, `cuw-tray`, `cuw-app`; record results in `rust/docs/test-map.md` "Decisions"

---

## Phase 4: User Story 2 - The desktop widget looks and behaves as before (Priority: P2) (S2)

**Goal**: the desktop panel with every layout, dial, ring, status dial/line, plan line, opacity, hover
buttons, drag/resize/lock and edit mode, at the saved place and size, bottom of the z-order, never
focused.

**Independent Test**: with C# settings the Rust panel opens at the same place, size and layout;
side-by-side `CUW_RENDER_PREVIEW` renders match per preset and theme (quickstart US2 1-7).

**Concurrency (wave W3)**: S2-L, S2-M, S2-N and S2-O run at the same time (disjoint crates; S2-M and
S2-N code against the `cuw-layout` contract). S2-P starts after all four are merged.

### Batch S2-L: cuw-layout (medium) - wave W3, concurrent with S2-M, S2-N, S2-O

- [ ] T128 [P] [US2] Write `rust/crates/cuw-layout/tests/layout.rs` porting **LayoutTests** (INV 2C §1.3-1.4) - all six classes: `FlowGridTests` (grid 2 columns: 1->(1,1), 2->(1,2), 3->(2,2), 4->(2,2); row/column; 0 -> (0,0)); `GridTracksTests` (gap tracks, sums, `track_of`); `LayoutSettingsTests` (sanitize None/duplicates/missing required/permutation kept/status+model rules/idempotent/fallback gets mode/`toggle`/`laid_out`/`swap_cells` by identity incl. name Above, out-of-range no-op, swaps survive sanitize; missing `block`/`order` = default instead of the C# NullReference); `StatusModeTests` (saved wins, read from order, order None no crash); `LayoutCycleTests` (flows, 4 name placements, 2-value toggles, out-of-range restarts); `PanelMetricsTests` (every case of INV §5 2C §10: 170 square, 3==4 accounts, row/column growth, name-above cost, 248 wide, aspect, side clamped, zero accounts, block gap = 2*gap, gap 10, top == bottom gap for every name placement and status mode, height formula, status dial drops caption band, edit mode grows by reserve only, nine buttons one row at Min/Default/Max side, nine 16 pt would overflow, plan line reserves one caption line for Above/Below only, top==bottom with plan, narrow panel wraps to 2x5, reserve = rows+gap)
- [ ] T129 [P] [US2] Write `rust/crates/cuw-layout/tests/panel_view.rs` porting **PanelViewTests** (Classic layout/mode, Accounts == default, `panel_view_default` 0,1 -> Classic and 2,4 -> Accounts, `current_view` recognition, file without layout resolves by account count, saved layout kept, model-hidden respected, apply writes layout and status mode and keeps other switches, classic has no caption band) and `rust/crates/cuw-layout/tests/dial_geometry.rs` porting **DialGeometryTests** (0 -> -90, 0.25 -> 0, 1 -> 270)
- [ ] T130 [P] [US2] Write `rust/crates/cuw-layout/tests/clamp.rs` (pure part of **DesktopWidgetWindowTests** + FR-024 fix 1 regression, INV 2C §1.9, research R-09): rebuilding 4 -> 5 accounts with the fixture layout (`Row`/`Column`/`Above`, side 222) keeps the window inside the work area (left edge always, right edge when it fits) and grows; 5 -> 4 shrinks; `clamp_to_work_area` in physical pixels with left/top winning; at 125 %, 150 % and 200 % a panel saved at the right/bottom edge of a scaled monitor is clamped by its pixel size, not its DIP size (the C# bug); `restore_position` picks the monitor whose rect holds `(x*scale_m, y*scale_m)`, else the primary's scale; result identical to C# at 100 %; tolerance 0.5 px
- [ ] T131 [P] [US2] Write `rust/crates/cuw-layout/tests/interact.rs` (new, INV 2C §1.7-1.8): `hit_edge` corners first and `EDGE_BAND` 8 DIP unscaled; `resize_delta` per edge sign; `apply_side` None when |delta| < 0.5 and clamped to [150, 340]; `drag_started` vs `SM_CXDRAG`/`SM_CYDRAG`; `enter_strip` above iff `panel_top - reserve >= work_top`, first run below; `reconsider_strip` flips only near the work-area top and reports the anchor shift; proptest: `apply_side` result always within [150, 340]
- [ ] T132 [P] [US2] Write `rust/crates/cuw-layout/tests/geometry.rs` (new, INV 2C §1.3, §1.5, §1.8): `panel_geometry` block, name and cell rects for each preset of INV 2C §5; toolbar = 9 buttons in `ToolbarButton` order with 14*scale boxes and 2*scale gaps; header Hide/Edit/Lock rects; `hit_cell`/`hit_toolbar`/`hit_header` inside, on the boundary and outside; strip rect above/below
- [ ] T133 [P] [US2] Implement `rust/crates/cuw-layout/src/flow.rs`, `src/cycle.rs`, `src/model.rs` methods (`BlockLayout::{laid_out, toggle, swap_cells, default_block}`, `WidgetLayout::{default_layout, sanitize}`, `clamp_side`; INV 2C §1.4) so T128 (flow, settings, cycle, status mode parts) passes
- [ ] T134 [US2] Implement `rust/crates/cuw-layout/src/resolve.rs` and `rust/crates/cuw-layout/src/metrics.rs` (`panel_metrics`, INV 2C §1.3 formulas) so T128 (metrics part), T129 pass (after T133)
- [ ] T135 [US2] Implement `rust/crates/cuw-layout/src/geometry.rs` (`panel_geometry`, hit tests, `dial_angle_degrees`) so T132 and T129 (dial geometry) pass (after T134)
- [ ] T136 [P] [US2] Implement `rust/crates/cuw-layout/src/interact.rs` so T131 passes
- [ ] T137 [P] [US2] FR-024 fix 1: implement `rust/crates/cuw-layout/src/placement.rs` `clamp_to_work_area` and `restore_position` in physical pixels (research R-09) so T130 passes

### Batch S2-M: cuw-view panel parts + cuw-render panel scene (complex) - wave W3

- [ ] T138 [P] [US2] Write `rust/crates/cuw-view/tests/theme_choice.rs` porting **ThemeChoiceTests** resolve part (6 combinations: follows the system only for `System`/None; INV 2C §3.1) plus `threshold_color`/`status_color` per palette (INV 2C §3.4)
- [ ] T139 [P] [US2] Write `rust/crates/cuw-view/tests/panel_opacity.rs` porting **PanelOpacityTests** (INV 2C §3.5): `resolve` None/NaN -> 0.82 and clamp [0, 1]; `painted` never below 1/255 (`0 -> 1/255`, `0.001 -> 1/255`, `0.2`, `1.0`); presets `[1.0, 0.82, 0.6, 0.4, 0.2, 0.0]` descending and containing 0.82; `is_preset` tick within 0.005; `caption` texts
- [ ] T140 [P] [US2] Write `rust/crates/cuw-view/tests/notice.rs` (new, INV 2C §1.6, data-model §8) incl. the FR-024 fix 3 regression: NoCredentials -> `Not signed in` + Sign in button; Unauthorized(None) -> `Session expired` + Sign in button (C# had none); Unauthorized(Some(text)) -> `Account added twice` + button with that detail; Loading/Ok/RateLimited/Network/Malformed -> None
- [ ] T141 [P] [US2] Write `rust/crates/cuw-view/tests/panel_frame.rs` (new, INV 2C §1.5-1.6): `panel::frame` keeps rows, dims when the tray account is not Ok but has a last snapshot, `status_line::panel_line` combines service status (Line mode) and usage line, `plan_line` copied from settings, notice from the tray account state
- [ ] T142 [P] [US2] Write `rust/crates/cuw-render/tests/panel_render.rs` (group `render`, tests `render_*`): each preset of INV 2C §5 in dark and light and at 30 % opacity renders offscreen to a PNG of size `PanelMetrics * dpi/96` at 96 and 144 dpi; pixels outside the rounded corners have alpha 0, panel pixels alpha >= 1; usage dial arc ends at `dial_angle_degrees(f)`; output PNGs written to a temp dir for manual comparison with `tests/fixtures/preview-csharp/`
- [ ] T143 [P] [US2] Implement in `rust/crates/cuw-view/src/theme.rs` `resolve`, `palette`, `threshold_color`, `status_color`, and `rust/crates/cuw-view/src/opacity.rs` (INV 2C §3.1-3.5) so T138, T139 pass
- [ ] T144 [P] [US2] Implement `rust/crates/cuw-view/src/notice.rs`, `rust/crates/cuw-view/src/panel.rs` and `status_line::panel_line` in `rust/crates/cuw-view/src/status_line.rs` so T140, T141 pass
- [ ] T145 [US2] Implement `rust/crates/cuw-render/src/surface.rs` (`LayeredSurface`: 32 bpp premultiplied top-down DIB, `ID2D1DCRenderTarget` bound to the memory DC, `UpdateLayeredWindow(ULW_ALPHA)`; `Canvas` primitives clear, rounded_rect, text (grayscale AA), glyph, dashed_rect, rect_stroke; research R-08, R-10)
- [ ] T146 [US2] Implement `rust/crates/cuw-render/src/dials.rs` (`usage_dial`, `status_dial` with label shrink-to-fit, `name_ring`; dimmed variants; INV 2C §2) (after T145)
- [ ] T147 [US2] Implement `rust/crates/cuw-render/src/panel.rs` `PanelScene::draw` (blocks, names with ellipsis, plan line, status line and notice overlays, hover header, edit overlay with toolbar and dashed cells, drag ghost at 0.6 opacity; INV 2C §1.5-1.6, §1.8) so T142 passes (after T146)

### Batch S2-N: cuw-widget (complex) - wave W3

- [ ] T148 [P] [US2] Write `rust/crates/cuw-widget/tests/window_logic.rs` (headless, new) for the crate's pure helpers: `WM_WINDOWPOSCHANGING` rewrite sets `HWND_BOTTOM` and clears `SWP_NOZORDER` (INV 2C §1.1); persistence debounce emits one `PersistGeometry` 500 ms after the last move on an injected clock and flushes at once on close (INV 2C §1.9); hover header state shows on enter and hides on leave, not in edit mode; locked panel ignores drag/resize but keeps buttons
- [ ] T149 [P] [US2] Write `rust/crates/cuw-widget/tests/window.rs` `#[ignore]` (window part of **DesktopWidgetWindowTests**, needs a desktop session): `rebuild(5, ..)` after a stale 4-row frame keeps the window inside the work area and grows; `rebuild(4, ..)` shrinks without error; `WS_EX_NOACTIVATE` window never becomes foreground on click
- [ ] T150 [US2] Implement `rust/crates/cuw-widget/src/window.rs` `WidgetWindow::{create, render, rebuild, show, hide, close}`: `WS_POPUP` + `WS_EX_LAYERED|WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW`, `MA_NOACTIVATE`, bottom z-order rewrite, first-run position and restore via `placement::restore_position` + `clamp_to_work_area` (clamp before redraw on rebuild), `WM_DPICHANGED` with the suggested rect, present through `LayeredSurface` (INV 2C §1.1-1.2, §1.9-1.10, research R-08/R-09)
- [ ] T151 [US2] Implement `rust/crates/cuw-widget/src/input.rs`: drag (threshold via `interact::drag_started`), resize from edges/corners (`hit_edge`, `resize_delta`, `apply_side`), `SetCapture`, `TrackMouseEvent(TME_LEAVE)` hover, header buttons Hide/Edit/Lock -> `WidgetEvent`, status dial click -> `OpenStatusPage`, Sign in button on the notice -> `SignIn`; transparent corners pass clicks through (INV 2C §1.7, §1.6) (after T150)
- [ ] T152 [US2] Implement `rust/crates/cuw-widget/src/edit_mode.rs`: strip above/below via `enter_strip`/`reconsider_strip` with window-top shift so the panel does not move, nine toolbar buttons -> `WidgetEvent`s (`EditDone`, `LayoutEdited`, `StatusMode`, `ModelDial`, `PlanLine`, `LockToggled`, `HideRequested`), cell drag with ghost and swap via `BlockLayout::swap_cells` on drop (INV 2C §1.8) (after T151)
- [ ] T153 [P] [US2] Implement `rust/crates/cuw-widget/src/persist.rs` (500 ms debounce -> `PersistGeometry{x_dip, y_dip, side}`, flush on close; INV 2C §1.9) so T148 (debounce part) passes

### Batch S2-O: cuw-app-core panel commands (medium) - wave W3

- [ ] T154 [P] [US2] Write `rust/crates/cuw-app-core/tests/commands_panel.rs` (new, INV 2A §1.7, 2D §2.6-2.7): `ShowOnDesktop(b)` -> `widget_visible` + effect; `LockPosition(b)` -> `position_locked`; `PanelView(v)` writes layout and status mode and keeps model dial / plan line; `LayoutEdited`, `StatusMode`, `ModelDial`, `PlanLine` write their field with `rebuild_layout`; `ToggleEditLayout` / `SetEditLayout` produce `edit_mode` only when `widget_exists`, and showing the widget when hidden; `Hide` -> `widget_visible = false`; `OpenUrl(StatusDial, url)` logs `browser-open site=status-dial url=<u>`; every command leaves other fields and `extra` untouched
- [ ] T155 [US2] Implement the panel commands in `rust/crates/cuw-app-core/src/commands.rs` so T154 passes

### Batch S2-P: cuw-app S2 wiring + layout preview (complex) - after S2-L, S2-M, S2-N, S2-O are merged

- [ ] T156 [P] [US2] Write `rust/crates/cuw-app/tests/preview.rs` (group `render`, test `render_preview_*`): running the exe with `CUW_RENDER_PREVIEW=<tempdir>` exits 0 before taking the mutex and writes one PNG per INV 2C §5 preset and theme
- [ ] T157 [US2] Implement `rust/crates/cuw-app/src/widget.rs`: create `WidgetWindow` from settings (`WidgetInit`), render `panel::frame` on every store, theme, opacity and settings change, `rebuild` on account count change, `WidgetEvent` -> `Command` -> `apply`, `PersistGeometry` -> `SettingsStore::update` (failure logged `settings widget position not saved: <Type>: <msg>`); apps-theme `ThemeChanged` repaints panel and menu palette (FR-014)
- [ ] T158 [US2] In `rust/crates/cuw-app/src/tray.rs` stop hiding the S2 menu ids (`PanelView`, `EditLayout`, `ShowOnDesktop`, `LockPosition`) and route them to the S2 commands
- [ ] T159 [US2] Implement `rust/crates/cuw-app/src/preview.rs` `CUW_RENDER_PREVIEW`: render every INV 2C §5 preset (dark, light, 30 % opacity) through `offscreen::render_png`, before `ui::init` and the mutex, exit 0 (spec Assumption on dev switches)

### Checkpoint S2 (US2)

- [ ] T160 [US2] Coordinator gates after each merge (`cargo fmt --check`, `cargo clippy --workspace --all-targets -- -D warnings`, `cargo nextest run --profile fast`); after the last merge full `cargo nextest run` (incl. `render` group) and `cargo mutants --in-diff` for `cuw-layout`, `cuw-view`, `cuw-app-core`; update `rust/docs/test-map.md`
- [ ] T161 [US2] Owner manual validation: quickstart "US2 desktop widget" steps 1-7 (incl. FR-024 fix 1 at 150 % on a second monitor, step 6) and research VERIFY R-09 (Rust panel opens where the C# panel was at 150 %) and R-10 (C# vs Rust preview PNGs side by side per preset, FR-023); run `#[ignore]` suites of `cuw-widget`

---

## Phase 5: User Story 3 - Manage accounts and sign in (Priority: P3) (S3)

**Goal**: add (up to 4), sign in, rename, remove and choose the tray account, each with its own
claude.ai session; Not signed in / Session expired / duplicate-account notices offer Sign in.

**Independent Test**: add a second account, sign in to another organization, rename it, make it the
tray account, restart: both show their own usage and stay signed in (quickstart US3 1-6).

**Concurrency (wave W4)**: S3-A and S3-B run at the same time, and also at the same time as S4-Q and
S4-R (disjoint crates). S3-C starts after S3-A and S3-B are merged.

### Batch S3-A: cuw-app-core accounts (medium) - wave W4, concurrent with S3-B, S4-Q, S4-R

- [ ] T162 [P] [US3] Write `rust/crates/cuw-app-core/tests/accounts.rs` (new, INV 2A §1.8, data-model §11): `add` refuses at 4 (`AccountError`), new id is a dashed lowercase UUIDv4, name `New account`; `rename` trims, blank or unknown id -> None, changes only `display_name`; `remove` drops the account and clears `tray_account_id` if it pointed there, unknown id -> false; `webview_profile_name("default") == None`, other ids -> `Some(id)`
- [ ] T163 [P] [US3] Write `rust/crates/cuw-app-core/tests/rename_reread.rs` (new, FR-024 fix 2 regression) with a real `SettingsStore` in a temp dir: load settings, then (dialog open) another writer saves a changed theme, opacity and a 429 pause for another account, then the rename is applied with `store.update(|s| accounts::rename(s, id, "  Work "))`: file has `display_name == "Work"` and every concurrent change kept; renaming an account removed meanwhile changes nothing
- [ ] T164 [P] [US3] Write `rust/crates/cuw-app-core/tests/commands_accounts.rs` (new, INV 2D §2.5, 2B §4.4): `AddAccount` -> new account + `open_login = Some(new id)` + log `sign-in-requested source=add-account`; at 4 accounts -> error, no change; `SelectAccount(id)` -> `tray_account_id` + `tray_icon`/`tooltip`/`menu` effects; `RemoveAccount(id)` effects rebuild + render; `SignIn(Panel)` -> `open_login` for the account of the notice + log `source=panel`; on signed in -> `refresh = Some(Account(id))` (INV 2B §9.4)
- [ ] T165 [US3] Implement `rust/crates/cuw-app-core/src/accounts.rs` (`add`, `rename`, `remove`, `webview_profile_name`; ids via `uuid` v4) so T162, T163 pass
- [ ] T166 [US3] Implement the account commands and the on-signed-in rule in `rust/crates/cuw-app-core/src/commands.rs` so T164 passes (after T165)

### Batch S3-B: cuw-webview clear data + cuw-tray rename dialog + accounts menu check (complex) - wave W4

- [ ] T167 [P] [US3] Write `rust/crates/cuw-tray/tests/rename_logic.rs` (headless, new, INV 2C §6, §3.6): pure helpers of the dialog: typed text trimmed, Rename disabled for blank, initial text = current name selected, colours from the palette per INV 2C §3.6
- [ ] T168 [P] [US3] Write `rust/crates/cuw-tray/tests/rename_live.rs` `#[ignore]`: `rename_dialog::ask` resolves `Some(text)` on Rename/Enter, `None` on Cancel/Esc/close, stays modeless (other windows keep working)
- [ ] T169 [P] [US3] Extend `rust/crates/cuw-webview/tests/live.rs` `#[ignore]`: two named sessions keep separate cookies; `clear_browsing_data` on a named profile removes its cookies only; `default` profile untouched by another account's clear
- [ ] T170 [P] [US3] Write `rust/crates/cuw-view/tests/menu_accounts.rs` (new, US3 scenario 3, INV 2D §2.5): with 1..4 accounts the Accounts submenu lists them in settings order with the tray-account tick; `Add account…` enabled below 4 and disabled at 4 with tooltip `The widget shows at most 4 accounts.`; Rename/Remove ids carry the account id; fix `rust/crates/cuw-view/src/menu.rs` if a case fails
- [ ] T171 [P] [US3] Implement `Session::clear_browsing_data` in `rust/crates/cuw-webview/src/session.rs` (`ICoreWebView2Profile2::ClearBrowsingDataAll` on the account's profile; INV 2A §1.8) so T169 passes
- [ ] T172 [P] [US3] Implement `rust/crates/cuw-tray/src/rename_dialog.rs` `ask` (modeless window, EDIT + Rename/Cancel, palette colours, resolves once; INV 2C §6) so T167 passes

### Batch S3-C: cuw-app S3 wiring (complex) - after S3-A, S3-B are merged

- [ ] T173 [US3] Implement `rust/crates/cuw-app/src/accounts.rs`: Add (create runtime + `Session` with profile name = id, open login, `set_live_label`), Select, Remove (`clear_browsing_data` first, then `SettingsStore::update(remove)`, drop the runtime, rebuild widget), duplicate-account notice from the fetch error (INV 2A §1.8, 2B §4.4)
- [ ] T174 [US3] FR-024 fix 2 wiring in `rust/crates/cuw-app/src/accounts.rs`: Rename = `rename_dialog::ask(current)` then `SettingsStore::update(|s| accounts::rename(s, id, &typed))` (re-read after the dialog, never a settings copy taken before it); failure logged `settings rename not saved: <Type>: <msg>`
- [ ] T175 [US3] In `rust/crates/cuw-app/src/widget.rs` and `src/tray.rs`: the notice Sign in button (Not signed in, Session expired - FR-024 fix 3 -, Account added twice) opens the login window for that account (`SignIn(Panel)`); stop hiding Accounts menu ids; on signed in -> refresh that account

### Checkpoint S3 (US3)

- [ ] T176 [US3] Coordinator gates after each merge (fmt, workspace clippy `-D warnings`, `cargo nextest run --profile fast`), full `cargo nextest run` after the last merge, `cargo mutants --in-diff` for `cuw-app-core`, `cuw-view`; update `rust/docs/test-map.md`
- [ ] T177 [US3] Owner manual validation: quickstart "US3 accounts" steps 1-6 (incl. FR-024 fix 2, step 5, and fix 3, step 3); run `#[ignore]` suites of `cuw-webview` and `cuw-tray`

---

## Phase 6: User Story 4 - Taskbar band (Priority: P4) (S4)

**Goal**: the opt-in transparent band owned by the taskbar, left or by the tray, metrics or one row per
account, hiding over real fullscreen apps without flicker, never buried, back after Explorer restarts,
with the opt-in diagnostic log.

**Independent Test**: enable the band, switch positions and views, run fullscreen video, restart
Explorer: every INV 2D §3 rule holds and `CLAUDE_BAND_DIAG=1` logs each decision (quickstart US4 1-6).

**Concurrency (wave W4)**: S4-Q, S4-R and S4-S run at the same time as each other and as S3-A/S3-B (they
need only W2). S4-T starts after S4-Q, S4-R, S4-S are merged (and after S3-C if both touch
`rust/crates/cuw-app/src/tray.rs`; merge order S3-C then S4-T).

### Batch S4-Q: cuw-band-logic (medium) - wave W4, concurrent with S4-R, S4-S, S3-A, S3-B

- [ ] T178 [P] [US4] Write `rust/crates/cuw-band-logic/tests/geometry.rs` (new, INV 2D §3.4, §3.2): `geometry` for `left` true/false, with and without a notify-area rect, at 96/120/144 dpi, content width rounding; `columns::{width, height, content_width}` with +14 gaps between columns and +5 inside a pair; empty column list
- [ ] T179 [P] [US4] Write `rust/crates/cuw-band-logic/tests/verdict.rs` (new, INV 2D §3.5): `probe_points` at fractions 0.35/0.55/0.8 of the tray rect; `verdict`: hit None -> Visible (short-circuit), root = tray or own -> Visible, DWM-cloaked root -> Visible, all points foreign and root == foreground -> `ObscuredByForeground`, all foreign otherwise -> `Obscured`, first visible sample stops evaluation
- [ ] T180 [P] [US4] Write `rust/crates/cuw-band-logic/tests/visibility.rs` (new, INV 2D §3.5, data-model §9 machine): first request after `dock` -> `ApplyNow`; request equal to applied -> `CancelTimer`; same pending -> `None` (no re-timing); show -> `StartTimer{150}`, hide by foreground -> 150, other hide -> 1200; `timer_fired` with a flipped fresh verdict cancels, `None` probe applies pending; `dock` resets; proptest: any overlay sequence shorter than 1200 ms that is not by-foreground never produces `ApplyNow(true)` (no flicker, US4 scenario 2)
- [ ] T181 [P] [US4] Write `rust/crates/cuw-band-logic/tests/burial.rs` (new, INV 2D §3.7): nearest-first z-order above the band: the tray itself overlapping -> `BuriedUnderTray`; a visible overlapping window of another class -> `BuriedUnder{hwnd, class}`; invisible or rect-less entries ignored; non-overlapping ignored; at most 64 entries examined
- [ ] T182 [P] [US4] Write `rust/crates/cuw-band-logic/tests/diag.rs` (new, contracts/log-format.md "Band diagnostic log"): every message template produced byte-exact (`probe f=0.35 pt=(10,20) hit=0 -> visible`, ..., `buried under {hwnd} cls={cls} — re-asserting topmost`), HWNDs decimal, `f` always with `.` decimal separator
- [ ] T183 [P] [US4] Implement `rust/crates/cuw-band-logic/src/geometry.rs` and `src/columns.rs` so T178 passes
- [ ] T184 [P] [US4] Implement `rust/crates/cuw-band-logic/src/probe.rs` (`probe_points`, `verdict`) so T179 passes
- [ ] T185 [P] [US4] Implement `rust/crates/cuw-band-logic/src/visibility.rs` (`Visibility`, constants `SHOW_MS`, `FAST_HIDE_MS`, `HIDE_MS`, `TICK` 5 s, `LOCATION_DEBOUNCE` 100 ms) so T180 passes
- [ ] T186 [P] [US4] Implement `rust/crates/cuw-band-logic/src/burial.rs` so T181 passes
- [ ] T187 [P] [US4] Implement `rust/crates/cuw-band-logic/src/diag.rs` (one fn per template) so T182 passes

### Batch S4-R: cuw-band + band rendering (complex) - wave W4

- [ ] T188 [P] [US4] Write `rust/crates/cuw-render/tests/band_render.rs` (group `render`, tests `render_band_*`): `band::draw` with 1-4 entries in light and dark taskbar ink produces text pixels with a shadow; `band::content_width` equals `columns::content_width` of the measured columns within 1 DIP
- [ ] T189 [P] [US4] Write `rust/crates/cuw-band/tests/diag_file.rs` (headless, new, INV 2D §3.8): the diag writer is on only when the injected env value is exactly `"1"` (`"true"`, `"01"`, empty -> off), read once; line `HH:mm:ss.fff <message>` + newline appended to the injected path; an unwritable path is swallowed
- [ ] T190 [P] [US4] Write `rust/crates/cuw-band/tests/live.rs` `#[ignore]` (owner only): band docks under `Shell_TrayWnd` at left and tray positions, hides for a fullscreen foreground window, survives `taskbar_created`, `close` unhooks
- [ ] T191 [P] [US4] Implement `rust/crates/cuw-render/src/band.rs` (`draw` with shadowed Consolas text in the taskbar ink, `content_width`; INV 2D §3.2) so T188 passes
- [ ] T192 [US4] Implement `rust/crates/cuw-band/src/window.rs`: layered ULW window owned via `SetWindowLongPtrW(GWLP_HWNDPARENT, Shell_TrayWnd)`, `HWND_TOPMOST` once per dock and on burial only, `set_position`, `render` repositions when docked, `close` (INV 2D §3.3-3.4, research R-13)
- [ ] T193 [US4] Implement `rust/crates/cuw-band/src/hooks.rs`: four `SetWinEventHook(.., WINEVENT_OUTOFCONTEXT)` hooks on the UI thread, callback through a thread-local `Weak` inside `guarded`, REORDER runs the burial check synchronously, others post to the executor (INV 2D §3.6) (after T192)
- [ ] T194 [US4] Implement `rust/crates/cuw-band/src/probe.rs`: sampling `WindowFromPoint`, `GetAncestor(GA_ROOT)`, `DwmGetWindowAttribute(DWMWA_CLOAKED)`, class names, foreground window and the z-order walk (max 64) into `cuw-band-logic` inputs; timers: 5 s backstop tick, 100 ms location debounce, hysteresis timers from `VisibilityAction` (INV 2D §3.5-3.7) (after T192)
- [ ] T195 [US4] Implement `rust/crates/cuw-band/src/lifecycle.rs` (`dock`, `detach`, `on_lost`, `taskbar_created` re-docks at once plus the backstop; INV 2D §3.9) and `rust/crates/cuw-band/src/diag.rs` (file writer of T189 fed by `cuw_band_logic::diag`) (after T193, T194)

### Batch S4-S: band view + band commands (medium) - wave W4

- [ ] T196 [P] [US4] Write `rust/crates/cuw-view/tests/band_text.rs` porting **BandTextTests** (INV 2C §4.4, 2D §3.1; 7 facts): five-hour percent and reset per account (`62%`, `1h 12m`); `"personal-low"` -> `"personal"` (8 chars); `"default"` kept; no data -> `—` not `0%`; three separate fields; no reset -> None not `""`; no accounts -> empty
- [ ] T197 [P] [US4] Write `rust/crates/cuw-view/tests/band_view.rs` porting **BandViewTests**: `resolve_view` (None+1 Metrics, None+2 Accounts, saved Accounts+1 Accounts, saved Metrics+3 Metrics); `metric_entries` `5H/7D/FAB` with `3%/68%/58%` and None resets; Ultra without five-hour shows `∞` with no reset in both views; Ultra spent week shows `100%` and `2d 0h` in the Accounts band while `dials[0].no_limit` stays true
- [ ] T198 [P] [US4] Write `rust/crates/cuw-app-core/tests/commands_band.rs` (new, INV 2D §2.7, §3.10): `TaskbarBand(b)` -> `taskbar_band_enabled` + `band_visibility = Some(b)`; `BandPosition(Left|Tray)` -> `band_position` `"left"`/`"tray"` + effect; `BandView(v)` -> `band_view` + `band` effect; unknown saved `band_position` string treated as tray
- [ ] T199 [P] [US4] Implement `rust/crates/cuw-view/src/band_text.rs` (`MAX_NAME = 8`, `entries`, `metric_entries`, `resolve_view`) so T196, T197 pass
- [ ] T200 [P] [US4] Implement the band commands in `rust/crates/cuw-app-core/src/commands.rs` so T198 passes

### Batch S4-T: cuw-app S4 wiring (complex) - after S4-Q, S4-R, S4-S (and S3-C) are merged

- [ ] T201 [US4] Implement `rust/crates/cuw-app/src/band.rs`: create `BandWindow` when `taskbar_band_enabled`, destroy when turned off, `set_position` from `band_position`, render `band_text` entries for the resolved view on every store change and taskbar `ThemeChanged` (FR-014 band ink), `TaskbarCreated` -> `band.taskbar_created()` (and tray re-add from S1), `on_lost` re-dock; read `CLAUDE_BAND_DIAG` once at startup (INV 2D §3.9-3.10)
- [ ] T202 [US4] In `rust/crates/cuw-app/src/tray.rs` stop hiding the band menu ids (`TaskbarBand`, `BandPosition`, `BandView`) and route them to the band commands

### Checkpoint S4 (US4)

- [ ] T203 [US4] Coordinator gates after each merge (fmt, workspace clippy `-D warnings`, `cargo nextest run --profile fast`), full `cargo nextest run` after the last merge, `cargo mutants --in-diff` for `cuw-band-logic`, `cuw-view`, `cuw-app-core`; update `rust/docs/test-map.md`
- [ ] T204 [US4] Owner manual validation: quickstart "US4 taskbar band" steps 1-6 (text vs `assets/taskbar-band.png`, FR-023), 7-day band-flicker watch started for SC-006; run `#[ignore]` suites of `cuw-band`

---

## Phase 7: User Story 5 - Settings, autostart, updates, export, diagnostics (Priority: P5) (S5)

**Goal**: full tray-menu parity: theme and opacity, Launch at login (same Run value), Check for updates
and install (Rust-to-Rust), usage export files, widget log rotation, `CUW_RENDER_MENU`.

**Independent Test**: toggle each setting and check the persisted value, the Run value, the export
files and the log lines against INV 2A §4-§8 (quickstart US5 1-5).

**Concurrency (wave W5, needs only W2)**: S5-A, S5-C and S5-D run at the same time (disjoint crates) and
may overlap S2-S4; S5-B starts after S5-A is merged (uses `cuw-release`); S5-E after S5-A..S5-D and
after S4-T (shared `cuw-app` files).

### Batch S5-A: cuw-release (medium) - wave W5, concurrent with S5-C, S5-D

- [ ] T205 [P] [US5] Write `rust/crates/cuw-release/tests/latest_release.rs` porting **LatestReleaseTests** (INV 2A §5.3, contracts/github-release.md) with fixtures `tests/fixtures/github/release-with-assets.json`, `release-no-assets.json`, `not-found.json`, `empty-tag.json`, `no-html-url.json`, `array.json`: picks the `-win-x64.zip` asset among others, strips `v` from the tag, keeps the page URL; empty assets -> `asset_url` None; not-a-release payloads -> `Ok(None)`; `is_newer` numeric (0.2.10 > 0.2.9, `v0.3.0` > 0.2.10, equal and older false, `unknown`/`garbage` false)
- [ ] T206 [P] [US5] Write `rust/crates/cuw-release/tests/install_decision.rs` (new, data-model §10, contracts/github-release.md Flow): `System.Version` semantics (2-4 components, leading `v`/`V`, `1.2` < `1.2.0`, 5 components / negative / text -> unparseable -> false); `install_block` -> `NoAsset`, `SourceBuild` (when `release_build` false), `NotWritable{dir}` in that precedence, None otherwise; `texts::*` byte-exact (`You have the latest version (v<cur>).`, open-page prompt with each `<why>`, install prompt, `Update failed: <msg>`), `CAPTION`, `user_agent("0.3.0") == "ClaudeUsageWidget/0.3.0"`
- [ ] T207 [P] [US5] Implement `rust/crates/cuw-release/src/latest.rs` (`LatestRelease::parse`, `is_newer`) so T205 passes
- [ ] T208 [P] [US5] Implement `rust/crates/cuw-release/src/install.rs` and `rust/crates/cuw-release/src/texts.rs` (`install_block`, texts, `user_agent`) so T206 passes

### Batch S5-B: cuw-updater (complex) - after S5-A is merged

- [ ] T209 [P] [US5] Write `rust/crates/cuw-updater/tests/swap.rs` (headless, temp dirs, new, research R-15): extracting a test zip finds `ClaudeUsageWidget.App.exe` at any depth, missing -> error `ClaudeUsageWidget.App.exe is not in the release zip.`; swap renames the current exe to `.old`, copies every extracted file (20 attempts, 500 ms apart through an injected sleeper; a file locked for 3 attempts still succeeds), failure after all attempts renames `.old` back and returns the error; writable probe `.update-probe-<32 hex>` created and deleted, failure -> not writable; `after_update` cleanup deletes `*.old` and the work dir, waits for the pid at most 60 s (injected)
- [ ] T210 [P] [US5] Write `rust/crates/cuw-updater/tests/flow.rs` (headless, new, contracts/github-release.md Flow 1-7) over internal ports (HTTP, dialogs, process start, quit) with fakes: single flight (second call while busy returns at once); not newer -> log `update up-to-date latest=<v>` + info box; each block reason -> Yes/No box, Yes logs `browser-open site=update url=<page>` and opens it; install prompt No -> nothing; Yes -> logs `update check current=<v>`, `update download <url>`, `update install v<v> into <dir>`, starts the new exe with `--after-update <pid>`, quits; any error -> `update failed <Type>: <msg>` + error box `Update failed: <msg>`
- [ ] T211 [US5] Implement `rust/crates/cuw-updater/src/check.rs` `check_and_install` (GitHub request with `user_agent` and 5 min timeout via `cuw_http` on `run_blocking`, decisions from `cuw-release`, `MessageBoxW` through `cuw_win_core::dialogs`, work dir `%TEMP%\ClaudeUsageWidget-update-<latest>`; INV 2A §5.1-5.2) so T210 passes
- [ ] T212 [P] [US5] Implement `rust/crates/cuw-updater/src/swap.rs` (zip extract with `zip`, rename-swap, copy retries, rollback; research R-15) so T209 (swap part) passes
- [ ] T213 [P] [US5] Implement `rust/crates/cuw-updater/src/after_update.rs` `after_update(pid)` (wait max 60 s, delete `*.old` and the work dir) so T209 (cleanup part) passes

### Batch S5-C: cuw-export (medium) - wave W5

- [ ] T214 [P] [US5] Write `rust/crates/cuw-export/tests/usage_export.rs` porting **UsageExportTests** (INV 2A §6, every case listed in INV §5 2A §10: both windows + reset stamps, units `atMs == fiveResetAt*1000`, None snapshot -> None, neither window -> None, one window enough, Ultra exports `"fiveHour":null` and `"fiveResetAt":null` never 0, model bucket = dial's (fable first, preferred wins, unavailable preferred -> 4 nulls), `availableModels` order and `[]` not null, all 12 camelCase keys, round trip, `"plan":null` present, plan from raw fields (`Team`, `Ultra`), `file_name_for` slug cases (`personal`, `" Work "`, `work.shared@example`, `--` -> id, `""` -> id), every `export_path_for` collision case incl. `-gone`, id matching, names from settings, case-insensitive explicit path, later account's own path reserves, `personal-id-2-id-3`; `writes_over` refuses settings.json in several spellings (case, `..`); no override and no directory -> None)
- [ ] T215 [P] [US5] Write `rust/crates/cuw-export/tests/writer.rs` (new, contracts/export-file.md): `write` creates the directory, replaces atomically through `cuw_settings::file_replace` (hard link keeps old bytes), output is the compact single line with keys in contract order and integral numbers without `.0`; a held target returns `ReplaceError` (caller only traces)
- [ ] T216 [P] [US5] Implement `rust/crates/cuw-export/src/payload.rs` (`payload`, `to_json` using `cuw_settings::json::number`) so the payload/JSON cases of T214 pass
- [ ] T217 [P] [US5] Implement `rust/crates/cuw-export/src/path.rs` (`export_path_for`, `file_name_for`, `writes_over`; lexical full-path normalization, case-insensitive) so the path cases of T214 pass; resolve the signature question of Gaps G-01 with the coordinator first
- [ ] T218 [P] [US5] Implement `rust/crates/cuw-export/src/write.rs` so T215 passes

### Batch S5-D: autostart + settings commands + full menu model (medium) - wave W5

- [ ] T219 [P] [US5] Write `rust/crates/cuw-win-core/tests/autostart.rs` (new, INV 2A §4, research R-22) over an internal `RunKey` trait with a fake: `VALUE_NAME == "ClaudeUsageWidget"`; `value_for(exe)` = `"\"<exe>\""`; enabled iff the value exists and is REG_SZ or REG_EXPAND_SZ (DWORD -> false, missing -> false); `set_enabled(true)` writes the quoted path, `set_enabled(false)` deletes and tolerates a missing value
- [ ] T220 [P] [US5] Write `rust/crates/cuw-app-core/tests/commands_settings.rs` (new, INV 2A §1.7, 2D §2.7): `Theme(choice)` -> `theme` + `apply_theme`; `PanelOpacity(p)` -> `panel_opacity` + `render_widget`; `CheckUpdates` effect; Launch at login handled as an effect (registry, not settings); other fields untouched
- [ ] T221 [P] [US5] Write `rust/crates/cuw-view/tests/menu_model_full.rs` (new, FR-011, INV 2D §2.1, §2.7): full menu in INV order with nothing removed; Theme radio ticks (None -> System); Opacity preset captions and tick within 0.005; Launch at login tick from `MenuState.autostart`; Check for updates caption; Show on desktop / Lock position ticks; Taskbar band submenu ticks
- [ ] T222 [P] [US5] Implement `rust/crates/cuw-win-core/src/autostart.rs` (HKCU `Software\Microsoft\Windows\CurrentVersion\Run`, REG_SZ quoted `GetModuleFileNameW` path; FR-016) so T219 passes
- [ ] T223 [P] [US5] Implement the settings commands in `rust/crates/cuw-app-core/src/commands.rs` so T220 passes; fix `rust/crates/cuw-view/src/menu.rs` for any T221 failure

### Batch S5-E: cuw-app S5 wiring + menu preview (complex) - after S5-A..S5-D and S4-T are merged

- [ ] T224 [P] [US5] Write `rust/crates/cuw-app/tests/menu_preview.rs` (group `render`, test `render_menu_preview_*`): `CUW_RENDER_MENU=<tempdir>` exits 0 before the mutex and writes menu PNGs and `metrics.txt`; `metrics.txt` matches `rust/crates/cuw-render/tests/fixtures/menu-metrics-csharp.txt` line by line within 1 px (INV 2D §2.11)
- [ ] T225 [US5] Implement `CUW_RENDER_MENU` in `rust/crates/cuw-app/src/preview.rs` (menus for dark and light at 96/144 dpi through `offscreen::render_png`, `metrics.txt` in the C# format)
- [ ] T226 [US5] In `rust/crates/cuw-app/src/main.rs` and `rust/crates/cuw-app/src/wiring.rs`: `--after-update <pid>` -> `cuw_updater::after_update(pid)` before the mutex; `CheckUpdates` effect -> `check_and_install(UpdaterConfig{current_version: CARGO_PKG_VERSION, release_build: option_env!("CUW_RELEASE_BUILD") == Some("1"), exe_path}, quit)`; Launch at login toggle -> `autostart::set_enabled`, tick from `is_enabled`; theme and opacity apply to panel and menu
- [ ] T227 [US5] Implement export wiring in `rust/crates/cuw-app/src/export.rs`: after every account refresh that produced `UsageState::Ok`, `SettingsStore::current()` -> `export_path_for` -> if `writes_over(path, settings path)` log `<name> export skipped: ExportPath is settings.json` every refresh, else `payload` + `write` (failure only traced; INV 2A §1.10, contracts/export-file.md); then remove every remaining id from the `MenuModel::without` list in `rust/crates/cuw-app/src/tray.rs` (full menu parity, FR-011)

### Checkpoint S5 (US5)

- [ ] T228 [US5] Coordinator gates after each merge (fmt, workspace clippy `-D warnings`, `cargo nextest run --profile fast`), full `cargo nextest run` after the last merge, `cargo mutants --in-diff` for `cuw-release`, `cuw-export`, `cuw-view`, `cuw-app-core`; update `rust/docs/test-map.md`
- [ ] T229 [US5] Owner manual validation: quickstart "US5 settings, autostart, updates, export, diagnostics" steps 1-5 (Rust-to-Rust update against a test release; source build shows the "Open the release page?" prompt; `widget.log` rotation); C# vs Rust `CUW_RENDER_MENU` PNGs side by side (FR-023)

---

## Phase 8: User Story 6 - Switch existing users to the Rust build (Priority: P6) (S6)

**Goal**: the first Rust release installs through the last C# build's own updater and keeps settings,
sign-ins, autostart and widget position.

**Independent Test**: on a C# install of the last C# version, the built-in updater installs the first
Rust release; the Rust app starts in its place with everything kept (quickstart US6 1-4).

**Concurrency**: S6-A is one agent after all of S1-S5 are merged; S6-B is owner/coordinator work after S6-A.

### Batch S6-A: packaging and release flow (medium)

- [ ] T230 [P] [US6] Write `rust/scripts/tests/package.Tests.ps1` (Pester, new) for the packaging script: zip name `ClaudeUsageWidget-v<version>-win-x64.zip`, `ClaudeUsageWidget.App.exe` at the zip root, no file name that collides with the C# publish output (`*.dll`, `ClaudeUsageWidget.App.dll`), version from `rust/crates/cuw-app/Cargo.toml`, script refuses a version with a pre-release suffix (FR-022, contracts/github-release.md "Release asset")
- [ ] T231 [US6] Implement `rust/scripts/package.ps1`: `$env:CUW_RELEASE_BUILD=1; cargo build -p cuw-app --release`, copy `target\release\ClaudeUsageWidget.App.exe`, zip as above into `rust/dist/` so T230 passes
- [ ] T232 [P] [US6] Set the version to `0.3.0` (greater than the last C# version 0.2.12, parseable by `System.Version`, no suffix) in `rust/crates/cuw-app/Cargo.toml` and check the version resource of `rust/crates/cuw-app/build.rs` shows it (INV 2A §2)
- [ ] T233 [P] [US6] Add the Rust section to the local `.claude/release.md` (gitignored): version source `rust/crates/cuw-app/Cargo.toml`, `rust/scripts/package.ps1`, asset name, `personal` branch sync unchanged
- [ ] T234 [P] [US6] CI on `main`: keep `.github/workflows/rust.yml` triggers for `main`; add `paths-ignore: ["rust/**"]` to `.github/workflows/ci.yml` so C# CI skips Rust-only changes; decide with the owner whether C# CI stays after the switch
- [ ] T235 [P] [US6] Update `README.md` (and its language variants if present) for the Rust build: build/test commands from quickstart, Windows 10 WebView2 note, release asset name; no feature changes

### Batch S6-B: switch validation (owner + coordinator, no agent)

- [ ] T236 [US6] Parity audit (SC-001, FR-001, FR-005): walk the INV checklist sections 2A-2D and `rust/docs/test-map.md`; every behavior is implemented or recorded as intentionally changed (spec FR-024, research R-17/R-21, plan Complexity Tracking), every C# test class has a Rust test file or a reason; list any gap as a follow-up task before continuing
- [ ] T237 [US6] C#-to-Rust updater dry run (quickstart US6 step 1, research VERIFY R-15, FR-022, US6 scenarios 1-2): fresh user with the last C# release, signed in, autostart on, panel moved; C# "Check for updates and install…" against a draft Rust release on a fork; Rust exe starts from the same folder with the same settings, accounts, sign-ins, autostart (Run value unchanged, FR-016) and position (SC-002)
- [ ] T238 [US6] Rust updater from that folder with C# leftovers present updates to a newer Rust test build (quickstart US6 step 2)
- [ ] T239 [US6] Quit Rust, start the old C# exe on the Rust-written `settings.json`: it reads it, including unknown fields Rust kept (quickstart US6 step 3, SC-003)
- [ ] T240 [US6] Measure SC-004 (idle private working set <= half of C#, tray icon < 1 s after launch) and SC-005 (zip < 75 MB); record numbers in `rust/docs/test-map.md` "Decisions"
- [ ] T241 [US6] Tell the other public-repo contributor about the switch before it lands on `main` (spec Assumptions); record the date in `rust/docs/test-map.md` "Decisions"; then merge `rust-port` into `main` with the owner's approval and release with the `release` skill

### Checkpoint S6 (US6)

- [ ] T242 [US6] Coordinator gates on the release commit (fmt, workspace clippy `-D warnings`, `cargo nextest run --profile fast`, full `cargo nextest run`, `#[ignore]` suites by the owner), packaging script tests, and quickstart "US6 switch" steps 1-4 all passed; SC-006 7-day watch started on the release build

---

## Phase 9: User Story 7 - Telegram bot bridge on the personal branch (Priority: P7) (S7) - personal branch only

**Goal**: on the local `personal` branch only, the Rust app writes the bot snapshot file, offers the
"Telegram bot" tray submenu and starts the bot at launch, as the C# personal build does. NEVER pushed;
never merged into `main` or `rust-port`.

**Independent Test**: on `personal`, start the Rust app: the bot starts once, the snapshot file updates
on every refresh, and the bot reports the same limits as with the C# build.

**Concurrency**: one agent (S7-A) after S6 (or after S5 for a local preview), working on the
`personal` branch in its own worktree.

### Batch S7-A: cuw-bot (medium) - personal branch only

- [ ] T243 [US7] personal branch only: write the bridge contract `rust/crates/cuw-bot/CONTRACT.md` from the C# personal build source (`BotSupervisor`, snapshot writer): `widget-snapshot.json` in `%LOCALAPPDATA%\claude-limits-observer` (collector snapshot format + `model_windows`, field names, time format), `bot.pid` supervision contract (written by the bot, read by the widget), venv `pythonw.exe` redirector (two processes = one bot), bot keeps running after the widget quits; capture one C#-written snapshot as `rust/crates/cuw-bot/tests/fixtures/widget-snapshot-csharp.json`
- [ ] T244 [P] [US7] personal branch only: write `rust/crates/cuw-bot/tests/snapshot.rs`: snapshot JSON for a given set of account snapshots equals the C# fixture's shape and values; written atomically through `cuw_settings::file_replace`; directory created; no snapshot -> no write
- [ ] T245 [P] [US7] personal branch only: write `rust/crates/cuw-bot/tests/supervisor.rs` with a fake process table: autostart on and `bot.pid` process alive -> not started; dead or missing pid -> started once; app quit does not stop the bot; menu actions start/stop/toggle autostart per the C# submenu
- [ ] T246 [P] [US7] personal branch only: implement `rust/crates/cuw-bot/src/snapshot.rs` so T244 passes
- [ ] T247 [P] [US7] personal branch only: implement `rust/crates/cuw-bot/src/supervisor.rs` (process check via a trait; real impl with `OpenProcess`/image name in `rust/crates/cuw-bot/src/process_win.rs`) so T245 passes
- [ ] T248 [US7] personal branch only: add the "Telegram bot" submenu ids (personal-branch edit of `MenuId` in `rust/crates/cuw-view/src/menu.rs` and of contracts/crate-interfaces.md on that branch) and the wiring hook `rust/crates/cuw-app/src/bot.rs`: start at launch unless alive, write the snapshot on every refresh, route menu ids

### Checkpoint S7 (US7) - personal branch only

- [ ] T249 [US7] personal branch only: gates from `rust/` (fmt, workspace clippy `-D warnings`, `cargo nextest run --profile fast`, full run) on `personal`; manual check of spec US7 scenarios 1-2 against the bot; confirm `git log origin/personal` does not exist (never pushed)

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: closing sweeps after S6 on `main` (independent of S7).

- [ ] T250 [P] Full `cargo mutants` run (not `--in-diff`) for every pure crate; add a test per surviving mutant or an "equivalent" note in `rust/docs/test-map.md`
- [ ] T251 [P] Triage `clippy::pedantic` warnings across `rust/crates/*` (fix or `#[allow]` with a reason); keep `-D warnings` green
- [ ] T252 [P] Final `rust/docs/test-map.md`: every C# class row has status "ported" with task IDs or a recorded reason; every FR row of the traceability table below checked off
- [ ] T253 Re-run every quickstart manual validation section US1-US6 on the released build and record the date in `rust/docs/test-map.md` "Decisions"

---

## Dependencies & Execution Order

### Phase dependencies

- Phase 1 (S0-A) -> Phase 2 (S0-B, S0-C, S0-D in parallel; S0-E any time) -> Checkpoint S0.
- Phase 3 (US1) needs Checkpoint S0. Inside: W1 batches S1-A..S1-I in parallel -> S1-J -> S1-K -> Checkpoint S1.
- Phase 4 (US2) needs Checkpoint S1 (W2 merged). S2-L/M/N/O in parallel -> S2-P.
- Phase 5 (US3) needs S2 merged (wave W4 per plan). S3-A, S3-B in parallel -> S3-C.
- Phase 6 (US4): S4-Q, S4-R, S4-S need only W2 (S1) and may run with S3; S4-T after S3-C (shared `cuw-app` files).
- Phase 7 (US5): S5-A, S5-C, S5-D need only W2 and may overlap S2-S4; S5-B after S5-A; S5-E after S4-T.
- Phase 8 (US6) needs every earlier checkpoint. Phase 9 (US7) after S6 on `personal` only. Phase 10 after S6.

### Story dependencies

- US1: foundation for all; MVP.
- US2: uses US1 stores, view and tray; independently testable with US1 running.
- US3: uses the US2 panel for notices (tray-only Sign in works from US1).
- US4: uses US1 only (band reads the same stores).
- US5: uses US1; menu completion touches US2-US4 items.
- US6: needs US1-US5. US7: needs US6 (or US5 locally).

### Batch list and concurrency

| Batch | Phase | Crates / files | Tasks | Difficulty | May run concurrently with |
|---|---|---|---|---|---|
| S0-A | 1 | rust/ config, CI, test-map | 7 | medium | nothing (first) |
| S0-B | 2 | 12 pure crate skeletons | 8 | medium | S0-C, S0-D, S0-E |
| S0-C | 2 | 9 platform/bin skeletons | 9 | complex | S0-B, S0-D, S0-E |
| S0-D | 2 | cuw-log | 6 | medium | S0-B, S0-C, S0-E |
| S0-E | 2 | fixtures, decisions (owner) | 5 | owner | any S0 batch |
| S1-A | 3 | cuw-settings | 10 | medium | S1-B..S1-I |
| S1-B | 3 | cuw-usage, cuw-status | 9 | medium | S1-A, S1-C..S1-I |
| S1-C | 3 | cuw-claude | 9 | medium | S1-A, S1-B, S1-D..S1-I |
| S1-D | 3 | cuw-store, cuw-release (version) | 8 | medium | S1-A..S1-C, S1-E..S1-I |
| S1-E | 3 | cuw-view (S1) | 10 | medium | S1-A..S1-D, S1-F..S1-I |
| S1-F | 3 | cuw-win-core | 9 | complex | S1-A..S1-E, S1-G..S1-I |
| S1-G | 3 | cuw-webview | 8 | complex | S1-A..S1-F, S1-H, S1-I |
| S1-H | 3 | cuw-render, cuw-tray | 9 | complex | S1-A..S1-G, S1-I |
| S1-I | 3 | cuw-http, cuw-layout (placement) | 5 | medium | S1-A..S1-H |
| S1-J | 3 | cuw-app-core (S1) | 7 | medium | nothing (after W1 merge) |
| S1-K | 3 | cuw-app | 5 | complex | nothing (after S1-J) |
| S2-L | 4 | cuw-layout | 10 | medium | S2-M, S2-N, S2-O, S5-A, S5-C, S5-D |
| S2-M | 4 | cuw-view (panel), cuw-render (panel) | 10 | complex | S2-L, S2-N, S2-O, S5-A, S5-C |
| S2-N | 4 | cuw-widget | 6 | complex | S2-L, S2-M, S2-O, S5-A, S5-C, S5-D |
| S2-O | 4 | cuw-app-core (panel commands) | 2 | medium | S2-L, S2-M, S2-N, S5-A, S5-C |
| S2-P | 4 | cuw-app | 4 | complex | S4-Q, S5-A, S5-C (disjoint crates) |
| S3-A | 5 | cuw-app-core (accounts) | 5 | medium | S3-B, S4-Q, S4-R, S5-A, S5-C |
| S3-B | 5 | cuw-webview, cuw-tray, cuw-view test | 6 | complex | S3-A, S4-Q, S4-R, S5-A, S5-C |
| S3-C | 5 | cuw-app | 3 | complex | S4-Q, S4-R, S5-A, S5-C |
| S4-Q | 6 | cuw-band-logic | 10 | medium | S3-A, S3-B, S3-C, S4-R, S4-S, S5-A, S5-C, S5-D |
| S4-R | 6 | cuw-band, cuw-render (band) | 8 | complex | S3-A, S3-B, S3-C, S4-Q, S4-S, S5-A, S5-C, S5-D |
| S4-S | 6 | cuw-view (band), cuw-app-core (band) | 5 | medium | S4-Q, S4-R, S5-A, S5-C (not with S3-A or S5-D: same app-core / view files) |
| S4-T | 6 | cuw-app | 2 | complex | S5-A, S5-B, S5-C |
| S5-A | 7 | cuw-release | 4 | medium | any batch not touching cuw-release |
| S5-B | 7 | cuw-updater | 5 | complex | S5-C, S5-D, S4-* |
| S5-C | 7 | cuw-export | 5 | medium | any batch not touching cuw-export |
| S5-D | 7 | cuw-win-core (autostart), cuw-app-core, cuw-view | 5 | medium | S5-A, S5-B, S5-C, S4-Q, S4-R (not with other app-core/view batches) |
| S5-E | 7 | cuw-app | 4 | complex | nothing touching cuw-app |
| S6-A | 8 | rust/scripts, CI, README, release notes | 6 | medium | nothing |
| S6-B | 8 | validation (owner) | 6 | owner | nothing |
| S7-A | 9 | cuw-bot (personal branch only) | 6 | medium | nothing |

Rules: two batches may run together only if they touch disjoint crates (or, for `cuw-app-core`,
`cuw-view` and `cuw-app`, disjoint files; `commands.rs`, `menu.rs`, `tray.rs` and `wiring.rs` are
single-writer: one batch at a time). Interfaces between concurrent batches are fixed by
contracts/crate-interfaces.md; a needed change stops the batch until the coordinator edits the contract.

### Within each batch

- Test tasks first (they must compile against the skeleton and fail), then implementation in the order
  given; [P] marks tasks with disjoint files.
- Checkpoints run after merge; the next phase starts only when its checkpoint is green.

---

## Parallel Example: User Story 1 (wave W1)

```text
# Nine agents at once, one per batch, each in its own worktree:
Agent 1: Batch S1-A cuw-settings        (medium)
Agent 2: Batch S1-B cuw-usage+status    (medium)
Agent 3: Batch S1-C cuw-claude          (medium)
Agent 4: Batch S1-D cuw-store+release   (medium)
Agent 5: Batch S1-E cuw-view            (medium)
Agent 6: Batch S1-F cuw-win-core        (complex)
Agent 7: Batch S1-G cuw-webview         (complex)
Agent 8: Batch S1-H cuw-render+tray     (complex)
Agent 9: Batch S1-I cuw-http+placement  (medium)
# Inside Agent 2, the five test tasks are [P] and can be written in any order:
"usage_decoder.rs", "usage_math.rs + threshold_level.rs", "model_buckets.rs", "usage_types.rs + http_validation.rs", "service_status.rs"
```

## Parallel Example: User Story 4 with User Story 3 (wave W4)

```text
Agent 1: Batch S3-A cuw-app-core accounts   (medium)
Agent 2: Batch S3-B webview+tray rename     (complex)
Agent 3: Batch S4-Q cuw-band-logic          (medium)
Agent 4: Batch S4-R cuw-band+render band    (complex)
# S4-S waits for S3-A (both edit cuw-app-core/src/commands.rs)
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 + Phase 2 (S0) -> Checkpoint S0.
2. Phase 3 (S1) -> Checkpoint S1: the owner runs the tray-only Rust build daily with the C# app quit.
3. Stop and validate with quickstart US1 before starting S2.

### Incremental delivery

Each checkpoint leaves a runnable `ClaudeUsageWidget.App.exe` (FR-021): S1 tray, S2 + panel, S3 +
accounts, S4 + band, S5 full parity, S6 switch on `main`, S7 personal. The C# app keeps shipping from
`main` until S6.

### Parallel team strategy

The coordinator runs waves W0-W5 as in plan.md Parallelization with the batch table above, merges
batch branches one by one, runs the gates after each merge and the full suite and mutants after the
last merge of a phase.

---

## Gaps found in the plan (resolve before or during the named batch)

- **G-01** contracts/export-file.md names `export_path_for(settings, account_id, display_name, settings_path)` (4 parameters) while contracts/crate-interfaces.md has 3 and keeps `writes_over` separate. Coordinator decides before T217 (proposed: keep 3 parameters, the caller checks `writes_over`, fix export-file.md).
- **G-02** plan.md "Dependency direction" lists `cuw-store -> cuw-usage` only, but `RetryState.until: DateTimeOffset` (cuw-settings) and `StatusStore` uses `cuw_status::ServiceStatus`; `cuw-store` must also depend on `cuw-settings` and `cuw-status` (T012).
- **G-03** `cuw-band-logic` is listed without dependencies but its API uses `RectI` (cuw-layout); either depend on `cuw-layout` or define its own rect (T014, proposed: depend on cuw-layout).
- **G-04** `cuw-win-core` returns `cuw_layout::placement::MonitorInfo` and `ThemeKind` (cuw-view) but the plan lists only `windows` and `cuw-log`; add `cuw-layout` and `cuw-view` as dependencies (or move `ThemeKind` to a lower crate) (T017).
- **G-05** The plan's test strategy wants the FR-024 TaskbarCreated fix tested "via a fake shell port", but no port exists in crate-interfaces.md; T120 adds `cuw-app-core::tray` (`TrayHost`, `on_system_event`) after a contract edit.
- **G-06** `cuw-layout` is scheduled for S2 (wave W3), but S1 needs its types (via `cuw-settings` and `cuw-status`) and `placement::within` (menu placement, MenuPlacementTests). Types come from the S0 skeleton; `within`/`to_px`/`to_dip` are pulled into S1 (T113); `cuw-layout` is then edited in S1 and S2 by different batches, never concurrently.
- **G-07** The plan says "reason recorded in the test map" but names no file; this task list uses `rust/docs/test-map.md` (T007).
- **G-08** No packaging script is named for S6 (the plan points to the gitignored `.claude/release.md`); T231 adds `rust/scripts/package.ps1` with Pester tests.
- **G-09** The UsageStoreTests case "a throwing Changed subscriber is logged and others still run" has no home in the sans-IO store; it maps to the `guarded` tests (T084) plus wiring (T123).
- **G-10** FR-024 fix 3 (Session expired gets a Sign in button) lives in `cuw-view::notice`, which the plan builds in S2 while its FR table says S3; this list implements and tests it in S2 (T140) and wires/validates it in S3 (T175, T177). Owner sign-off requested in T035.
- **G-11** PollScheduleTests "throws for index -1" cannot be expressed with `usize`; recorded as not applicable (T067).
- **G-12** Accounts/Layout submenu keep-open behavior is still an open item (research R-12, INV 2D §9); T034 must record the observed C# rule before T077.
- **G-13** US7: the plan keeps the bot contract on the personal branch only; the `MenuId` enum (cuw-view) and crate-interfaces.md need personal-branch-only edits (T248), so `personal` will carry a permanent diff in shared files.
- **G-14** `futures-util` and `pollster` versions are unchecked in the plan (T002).

---

## Traceability

### FR -> tasks

| FR | Tasks |
|---|---|
| FR-001 | every phase; audit T236, T252 |
| FR-002 | T037-T046, T031, T127, T239 |
| FR-003 | T094, T095, T123, T237 |
| FR-004 | T085, T089, T122, T121 |
| FR-005 | T007, every ported test task, T126, T160, T176, T203, T228, T236, T252 |
| FR-006 | T056-T064, T096, T097, T123 |
| FR-007 | T067, T071, T115, T119, T125 |
| FR-008 | T065, T069, T074, T080, T123 |
| FR-009 | T051, T055, T066, T070, T125, T146, T141 |
| FR-010 | T075, T080, T100, T101, T105, T107, T124 |
| FR-011 | T077, T081, T102, T106, T108, T170, T221, T227 |
| FR-012 | T128-T137, T142, T145-T147, T148-T153, T154, T155, T157, T158 |
| FR-013 | T178-T187, T188-T195, T201 |
| FR-014 | T078, T082, T086, T090, T124, T138, T143, T157, T191, T201 |
| FR-015 | T162-T166, T167-T172, T173-T175 |
| FR-016 | T219, T222, T226, T237 |
| FR-017 | T205-T208, T209-T213, T226 |
| FR-018 | T214-T218, T227 |
| FR-019 | T025-T030, T122 |
| FR-020 | T084, T088, T090, T108, T122, T123, T193 |
| FR-021 | T036, T127, T161, T177, T204, T229, T242 |
| FR-022 | T024, T230, T231, T232, T237 |
| FR-023 | T032, T142, T159, T161, T204, T224, T229 |
| FR-024 fix 1 (physical-pixel clamp) | T130, T137, T150, T161 |
| FR-024 fix 2 (rename re-read) | T163, T165, T174, T177 |
| FR-024 fix 3 (Session expired Sign in) | T140, T144, T175, T177 |
| FR-024 fix 4 (tray re-add on TaskbarCreated) | T117, T120, T124, T103, T127 |
| FR-025 | T243-T248, T249 |

### C# test class -> tasks

| C# test class | Rust test file | Tasks |
|---|---|---|
| CoreInfoTests | cuw-release/tests/core_info.rs | T068, T072 |
| HttpValidationTests | cuw-usage/tests/http_validation.rs | T050, T054 |
| LatestReleaseTests | cuw-release/tests/latest_release.rs | T205, T207 |
| LogLineTests | cuw-log/tests/log_line.rs | T025, T028 |
| SettingsMigrationTests | cuw-settings/tests/settings_migration.rs | T037, T044 |
| SmokeTests | not ported (reason in test-map) | T007 |
| UsageExportTests | cuw-export/tests/usage_export.rs | T214, T216, T217 |
| WidgetSettingsAccountTests | cuw-settings/tests/widget_settings_account.rs; cuw-usage/tests/usage_types.rs | T038, T044, T050, T052 |
| WidgetSettingsTests | cuw-settings/tests/widget_settings.rs | T039, T040, T045, T046 |
| AccountRowTests | cuw-view/tests/account_row.rs | T076, T079 |
| FetchIdleLeaseTests | cuw-claude/tests/fetch_idle_lease.rs | T059, T063 |
| NavigationRetryPolicyTests | cuw-claude/tests/navigation_retry_policy.rs | T058, T063 |
| OrganizationPickerTests | cuw-claude/tests/organization_picker.rs | T056, T061 |
| SubscriptionRefreshTests | cuw-claude/tests/subscription_refresh.rs | T057, T062 |
| SubscriptionTierTests | cuw-claude/tests/subscription_tier.rs | T057, T062 |
| ModelBucketsTests | cuw-usage/tests/model_buckets.rs | T049, T054 |
| ThresholdLevelTests | cuw-usage/tests/threshold_level.rs | T048, T054 |
| UsageDecoderTests | cuw-usage/tests/usage_decoder.rs | T047, T053 |
| UsageMathTests | cuw-usage/tests/usage_math.rs | T048, T054 |
| UsageTypesTests | cuw-usage/tests/usage_types.rs | T050, T052 |
| PollScheduleTests | cuw-store/tests/poll_schedule.rs | T067, T071 |
| StatusStoreTests | cuw-store/tests/status_store.rs | T066, T070 |
| UsageStoreTests | cuw-store/tests/usage_store.rs (+ guard tests) | T065, T069, T084 |
| ServiceStatusTests (+ StatusDecoderTests) | cuw-status/tests/service_status.rs | T051, T055 |
| BandTextTests | cuw-view/tests/band_text.rs | T196, T199 |
| BandViewTests | cuw-view/tests/band_view.rs | T197, T199 |
| DialGeometryTests | cuw-layout/tests/dial_geometry.rs | T129, T135 |
| ThemeChoiceTests | cuw-view/tests/theme_choice.rs; round trip in cuw-settings/tests/widget_settings.rs | T138, T143, T039 |
| PanelOpacityTests | cuw-view/tests/panel_opacity.rs; round trip in cuw-settings/tests/widget_settings.rs | T139, T143, T039 |
| TrayTextTests | cuw-view/tests/tray_text.rs | T075, T080 |
| DialModelTests (+ StatusLineTests) | cuw-view/tests/dial_model.rs, status_line.rs | T073, T074, T079, T080 |
| LayoutTests (FlowGrid, GridTracks, LayoutSettings, StatusMode, LayoutCycle, PanelMetrics) | cuw-layout/tests/layout.rs | T128, T133, T134 |
| PanelViewTests | cuw-layout/tests/panel_view.rs | T129, T134 |
| App: DesktopWidgetWindowTests | cuw-layout/tests/clamp.rs + cuw-widget/tests/window.rs (#[ignore]) | T130, T137, T149, T150 |
| App: MenuPlacementTests | cuw-layout/tests/menu_placement.rs | T110, T113 |
| App: WebViewDeathTests | cuw-claude/tests/webview_death.rs | T058, T063 |

---

## Notes

- [P] tasks = different files, no dependency on an unfinished task.
- Every C# behavior test keeps its meaning in the Rust test name; new tests cover the gaps INV §5 lists
  (app wiring, autostart, updater, log rotation, export writer, tolerant enum fallback values, FR-024
  fixes, band tables, menu model).
- Verify tests fail before implementing; the coordinator commits after each merged batch (agents never
  commit).
- US7 tasks live only on the local `personal` branch and are never pushed.
