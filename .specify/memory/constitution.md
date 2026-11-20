# Claude Usage Widget Constitution

## Core Principles

### I. Behavior Parity With the C# App

The C#/.NET WPF app on `main` is the reference for the Rust rewrite. Every user-visible behavior it
has (widget, tray icon and menu, taskbar band, accounts, login, polling, themes, autostart, updater,
export, error states) MUST exist in the Rust app before the Rust app replaces it. A behavior change
is allowed only when the spec records it as intentional and the owner approved it. The behavior
inventory in `specs/_inventory/` and the existing C# tests are the checklist: each C# test MUST
have a Rust test that pins the same behavior, or a written reason why it no longer applies.

### II. Pure Core, Thin Platform Shell

Logic that does not need Windows (usage decoding, formatting, thresholds, dial math, poll schedule,
retry and backoff, settings schema and migrations, layout math) MUST live in crates with no Win32,
WebView2 or GUI dependency, so its tests build and run in seconds. Win32, WebView2, tray and window
code lives in platform crates behind small interfaces and holds no business rules. Each external
service (claude.ai, the status page, GitHub releases) sits behind one module with its own domain
types.

### III. Tests for Every Behavior (NON-NEGOTIABLE)

Every new or ported behavior MUST come with tests, including edge cases (missing data, zero, 100%,
limits reached, malformed responses, 429s, clock edges, multi-monitor and DPI math). Recorded HTTP
responses live in `tests/fixtures/`; tests never touch the network or the user's real settings.
Agents run only headless tests; tests that open windows, show the tray or play sound are marked
`#[ignore]` and run only with the owner's consent.

### IV. User Data Compatibility

Switching from the C# build to the Rust build MUST NOT lose user data or sessions: the Rust app
reads the existing `%APPDATA%\ClaudeUsageWidget\settings.json` (with every known migration), reuses
the per-account WebView2 profiles under `%LOCALAPPDATA%\ClaudeUsageWidget\profiles\`, and keeps the
same autostart Run value name. Any format the Rust app writes MUST stay readable by it after an
upgrade; a write never drops fields it does not understand.

### V. Safe, Plain Rust

Stable Rust, edition 2024, one Cargo workspace. No `unwrap()` outside tests; `thiserror` in library
crates, `anyhow` only in the binary. Logging through `tracing`, never `println!`. `unsafe` only in
platform crates, each block with a `// SAFETY:` comment. Start simple: no new feature, abstraction
or dependency the spec does not need.

## Constraints

- Target: Windows 10 (with WebView2 Runtime) and Windows 11, x64; per-monitor-v2 DPI aware.
- Deliverable: a single portable executable, no installer and no runtime to install.
- Code comments, commit messages, logs and docs are in English; the UI text is English as today.
- Commits follow the existing conventional style (`feat(app): ...`, `fix(core): ...`) with the
  required `Co-Authored-By` trailer.

## Development Workflow

- The rewrite happens on the `rust-port` branch; the C# app keeps shipping from `main` until the
  Rust app reaches parity and the owner approves the switch. The local `personal` branch is never
  pushed.
- Work goes through Spec Kit: specify, clarify, plan, tasks, analyze, implement, in stages that each
  leave a runnable app.
- Gates before every commit that touches Rust: `cargo fmt --check`, `cargo clippy --workspace
  --all-targets -- -D warnings`, `cargo nextest run` (fast profile for agents, full after merges).
- Releases use the `release` skill; release specifics live in `.claude/release.md` and change when
  the Rust app takes over.

## Governance

This constitution overrides other project practices for the Rust rewrite. Amendments are made with
`/speckit-constitution`, recorded with a version bump (MAJOR: a principle removed or redefined;
MINOR: a principle or section added; PATCH: wording), and approved by the owner. Plans and reviews
MUST check work against Principles I to V and justify any deviation in the plan's complexity table.

**Version**: 1.0.0 | **Ratified**: 2026-11-21 | **Last Amended**: 2026-11-21
