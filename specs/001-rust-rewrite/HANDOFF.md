# Handoff: Rust rewrite, ready for implementation

**Date**: 2026-11-21 | **Branch**: `rust-port` (from `main` at v0.2.12) | **Next step**: `/speckit-implement`

## Where things stand

Spec Kit flow done up to and including analyze. No Rust code exists yet.

| Artifact | What it is |
|---|---|
| `.specify/memory/constitution.md` | 5 principles: parity, pure core / thin platform, tests for every behavior, user data compatibility, safe Rust |
| `specs/_inventory/behavior-inventory.md` | ~2150-line behavior reference of the C# app with file:line refs (static read, not run) |
| `spec.md` | 7 user stories = 7 stages; FR-001..FR-025; owner answers: look visually equivalent, 4 clear bugs fixed (FR-024), bot bridge later on `personal` only |
| `plan.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md` | Native Win32 stack (`windows` 0.62.2, Direct2D/DirectWrite, `webview2-com` 0.39.1, Shell_NotifyIconW, ureq), 21 crates in `rust/`, stages S0-S7, waves W0-W5 |
| `contracts/crate-interfaces.md` | Rust signatures between crates; parallel agents code against it |
| `tasks.md` | 253 tasks (T001-T253) in 36 batches of at most 10, each marked medium / complex / owner, with traceability tables |
| `analysis.md` | 23 findings; all CRITICAL/HIGH/MEDIUM fixed in the docs, 7 LOW accepted |

## How to start tomorrow

1. `git switch rust-port` and pull.
2. Owner glance (two items analyze flagged): the new Complexity Tracking row in `plan.md` (G-15:
   which Win32-bound helpers stay in platform crates) and the merge-order rule in `tasks.md`
   (G-16: W1 = B, D, F, H, I, then A, C, then E, G; W3 = L first; W4 = Q before R).
3. Run `/speckit-implement`, phase by phase, through the `parallel-agents` skill (worktrees, shared
   `CARGO_TARGET_DIR`, merge one batch at a time, gates after each merge).
   - S0-A alone first (workspace, CI, test map), then S0-B/C/D in parallel.
   - Model per batch (owner rules): "complex" batches -> `sonnet-5.5-high`, "medium" ->
     `sonnet-5.5-medium`, reviews -> `opus-5.5-medium`. Briefs in English, tests required for new
     behavior including edge cases, headless tests only.
4. Owner-only batch S0-E (T031-T035) needs you at the machine:
   - capture fixtures from the running C# build (real `settings.json` copies, `CUW_RENDER_MENU` and
     `CUW_RENDER_PREVIEW` renders, recorded claude.ai and status responses);
   - T034: watch whether clicking an item in the Accounts / Layout submenus closes the C# tray
     menu; this must be answered before batches S1-E and S1-H.
5. MVP = phases 1-3 (S0 + US1): a tray-only Rust build that reads your existing settings and
   sign-in. Check it with the US1 steps in `quickstart.md`.

## Gates

`cargo fmt --check`, `cargo clippy --workspace --all-targets -- -D warnings`,
`cargo nextest run --profile fast` per batch; full run and `cargo mutants --in-diff` after merges.

## Rules to keep

- The C# app keeps shipping from `main` until stage S6; releases from `main` stay as in
  `.claude/release.md`.
- `personal` is local-only and never pushed; US7 (bot bridge) lands there only.
- Tell the other contributor to the repo before the switch reaches `main`.
- VERIFY items in `research.md` are checked live in the stage they name (WebView2 environment
  sharing with the C# app, System.Text.Json date/newline format, tray digit padding, C#->Rust
  updater dry run).
