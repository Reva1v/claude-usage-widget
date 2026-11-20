# Specification Analysis Report: Rust rewrite (001-rust-rewrite)

**Date**: 2026-11-21 | **Branch**: `rust-port` | **Command**: `/speckit-analyze`

**Inputs**: spec.md, plan.md, tasks.md, research.md, data-model.md, contracts/*.md, quickstart.md,
checked against `.specify/memory/constitution.md` (v1.0.0) and
`specs/_inventory/behavior-inventory.md` (INV). The prerequisite script passed; `.specify/extensions.yml`
has no hooks.

**Remediation**: the owner approved fixing every CRITICAL and HIGH finding and the known gaps before
implementation. Only the documents were edited.

## Findings

| ID | Category | Severity | Location(s) | Summary | Status |
|----|----------|----------|-------------|---------|--------|
| C1 | Constitution (II) | CRITICAL | tasks T020, T092, T100, T102, T108, T148, T153, T189; plan structure | Logic that needs no Windows sat in platform crates: menu navigation (cuw-tray), tray digit fit (cuw-render), widget debounce/hover/lock (cuw-widget), login title / https check / profile-mismatch text (cuw-webview), band diag switch and line format (cuw-band). | FIXED: moved to `cuw-view::menu_nav` (+`tray_text::truncate_tooltip`), `cuw-layout::icon`, `cuw-layout::widget_state`, `cuw-claude::login`, `cuw-band-logic::diag`. Remaining Win32-bound helpers are justified in plan Complexity Tracking (G-15). |
| I1 | Inconsistency | HIGH | export-file.md; crate-interfaces.md; T217 | `export_path_for` had 4 parameters in one contract and 3 in the other (G-01). | FIXED: 3 parameters, as C# `ExportPathFor(data, accountId, displayName)`; the caller checks `writes_over`. |
| I2 | Inconsistency | HIGH | plan dependency direction; crate-interfaces.md | Missing graph edges (G-02, G-03, cuw-render->cuw-status, cuw-band->ThemeKind, futures-util in cuw-app-core); S0 skeletons would not compile. | FIXED: one acyclic graph in both files; T012, T014, T018, T022 name the deps. |
| I3 | Inconsistency | HIGH | cuw-win-core; data-model §8 | cuw-win-core depended on cuw-view for `ThemeKind` (G-04). | FIXED: `ThemeKind` lives in cuw-layout, re-exported by cuw-view. |
| U1 | Underspecification | HIGH | plan test strategy; T117, T120 | The FR-024 fix 4 test needed a shell port missing from the contract (G-05). | FIXED (approved): `cuw-app-core::tray` port (`SystemEventKind`, `TrayHost`, `on_system_event`). |
| U2 | Underspecification | HIGH | cuw-render, cuw-view contracts; data-model §8, §11 | Undefined types: `Rgb`, `Rgba`, `Align`, `MenuLayout`, `EditOverlay`, Command `Source`/`Site`. | FIXED: all defined (`SignInSource`, `Site` in data-model §11). |
| I4 | Inconsistency (ordering) | HIGH | waves W1, W3, W4 | Concurrent batches call functions that sibling batches implement; `unimplemented!()` skeletons panic until merged. | FIXED (G-16): merge order W1 = (B, D, F, H, I) then (A, C) then (E, G); W3 = L first; W4 = Q before R; per-batch "rebase on" notes. |
| I5 | Inconsistency (ordering) | HIGH | settings-json.md writer; T041/T043 vs T133 | The S1 settings writer needed `BlockLayout::laid_out`, scheduled for S2. | FIXED: T111/T113 add it to S1-I; S1-A rebases on S1-I. |
| I6 | Inconsistency | MEDIUM | plan Stages S1, W1 | S1 did not list cuw-layout (G-06). | FIXED (placement is T113). |
| I7 | Inconsistency | MEDIUM | plan Stages S1/S2 | S1 omitted cuw-release version parse and menu paint; S2 claimed dials and palette built in S1. | FIXED |
| I8 | Inconsistency | MEDIUM | plan FR table | Session-expired Sign-in fix listed under S3, tasks implement it in S2 (G-10). | FIXED (approved): implemented S2, wired and validated S3. |
| U3 | Underspecification | MEDIUM | plan Technical Context; T002 | futures-util and pollster unpinned (G-14). | FIXED: futures-util 0.3.34, pollster 1.0.3 (crates.io, 2026-11-21). |
| I9 | Inconsistency | MEDIUM | S0-E, T034, R-12, quickstart | T034 (observe the C# submenu behavior) was not ordered before the menu work (G-12). | FIXED: T034 before S1-E (T077, T081) and S1-H (T102, T108). |
| E1 | Coverage gap | MEDIUM | INV 2A §1.11 | Shutdown order had no task. | FIXED: T122, T157, T201. |
| I10 | Inconsistency | LOW | T035 | Asked for sign-off already given. | FIXED |
| U4 | Underspecification | LOW | crate-interfaces.md | Error enums named but not spelled out. | FIXED (S0 skeleton defines them) |
| E2 | Coverage gap | LOW | spec edge "clock jumps" | No wall-clock jump test; stores take an injected `now`. | ACCEPTED |
| C2 | Constitution (III) | LOW | T109 | cuw-http test uses a loopback TcpListener. | ACCEPTED |
| C3 | Constitution (II) | LOW | Constitution Check II | Rules and transport crates split per service. | ACCEPTED |
| D1 | Duplication | LOW | FR-001 vs FR-010..014 | FR-001 is an umbrella; T236 audits it. | ACCEPTED |
| A1 | Ambiguity | LOW | SC-006 | Relative to the C# app; measured by the 7-day watch (T204, T242). | ACCEPTED |
| G1 | Gap G-13 | LOW | T248 | `personal` carries a permanent diff in shared files. | ACCEPTED |
| G2 | Gap G-11 | LOW | T067 | PollScheduleTests "index -1" not expressible with usize. | ACCEPTED (recorded) |

## Coverage summary

| Requirement | Task IDs |
|---|---|
| FR-001 | all phases; T236, T252 |
| FR-002 | T031, T037-T046, T127, T239 |
| FR-003 | T094, T095, T123, T237 |
| FR-004 | T085, T089, T121, T122 |
| FR-005 | T007, ported test tasks, checkpoints, T236, T252 |
| FR-006 | T056-T064, T096, T097, T123 |
| FR-007 | T067, T071, T115, T119, T125 |
| FR-008 | T065, T069, T074, T080, T123 |
| FR-009 | T051, T055, T066, T070, T125, T141, T146 |
| FR-010 | T075, T080, T100, T101, T105, T107, T124 |
| FR-011 | T077, T081, T102, T106, T108, T170, T221, T227 |
| FR-012 | T128-T137, T142, T145-T155, T157, T158 |
| FR-013 | T178-T195, T201 |
| FR-014 | T078, T082, T086, T090, T124, T138, T143, T157, T191, T201 |
| FR-015 | T162-T175 |
| FR-016 | T219, T222, T226, T237 |
| FR-017 | T205-T213, T226 |
| FR-018 | T214-T218, T227 |
| FR-019 | T025-T030, T122 |
| FR-020 | T084, T088, T090, T108, T122, T123, T193 |
| FR-021 | T036, T127, T161, T177, T204, T229, T242 |
| FR-022 | T024, T230-T232, T237 |
| FR-023 | T032, T142, T159, T161, T204, T224, T229 |
| FR-024 fixes 1-4 | fix 1: T130/T137/T150/T161; fix 2: T163/T165/T174/T177; fix 3: T140/T144/T175/T177; fix 4: T103/T117/T120/T124/T127 |
| FR-025 | T243-T249 |
| SC-001..SC-006 | T236/T252; T237; T127/T239; T240; T231/T240; T204/T242 |

## Constitution alignment

- I Parity: aligned; 36 C# test classes mapped; shutdown gap closed.
- II Pure core: violation C1 fixed; remaining helpers justified in Complexity Tracking.
- III Tests: aligned (C2 accepted).
- IV Data compatibility: aligned.
- V Safe Rust: aligned; dependencies complete and pinned.

## Metrics

- Requirements: 31 (25 FR + 6 SC). Tasks: 253. Coverage: 100%.
- Before remediation: CRITICAL 1, HIGH 7, MEDIUM 6, LOW 9. After: 0 CRITICAL, 0 HIGH, 0 MEDIUM
  open; 7 LOW accepted.

## Next actions

- `/speckit-implement` starts with Phase 1 (S0-A). S0-E (T031-T035) needs the owner: fixture
  capture from the C# build and observing the tray submenu behavior.
