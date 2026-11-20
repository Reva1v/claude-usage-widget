# Feature Specification: Rust rewrite with full behavior parity

**Feature Branch**: `rust-port`

**Created**: 2026-11-21

**Status**: Draft

**Input**: User description: "Rewrite the whole Claude Usage Widget (today a C#/.NET 10 WPF app) as a Rust app with full behavior parity, as the constitution requires. The reference for every behavior is specs/_inventory/behavior-inventory.md and the existing C# tests. Scope: desktop widget, tray icon and menu, taskbar band, accounts and sign-in, usage polling, service status, themes, autostart, updater, usage export, diagnostics. Existing users switch without losing settings or sign-ins. The rewrite ships in stages, and each stage leaves a runnable app. The C# app keeps shipping from main until parity."

**Reference**: every behavior named below is specified in detail (rules, constants, timings, texts,
file formats) in `specs/_inventory/behavior-inventory.md`, cited here as "INV <section>". Where
this spec and the inventory differ, this spec wins; where the inventory and the C# code differ, the
code wins unless this spec says otherwise.

## User Scenarios & Testing *(mandatory)*

The stories are ordered as delivery stages. Each one leaves a runnable app that a user can keep
running every day.

### User Story 1 - See my usage in the tray with my existing sign-in (Priority: P1)

A current user quits the C# widget and starts the Rust build. Without signing in again or touching
any setting, the tray shows the same live number (5H, 7D or model, as chosen), the tooltip lists the
same figures and service status line, and the numbers refresh on the same schedule, including the
429 pause ladder and resume after sleep.

**Why this priority**: it proves the hardest invisible parts (reading the existing settings and
sign-in, fetching from claude.ai, decoding usage) and is already useful on its own.

**Independent Test**: on a machine signed in with the C# app, start the Rust build alone: within
one poll the tray digit and tooltip match what the C# app showed, and `settings.json` is still
readable by the C# app afterwards.

**Acceptance Scenarios**:

1. **Given** a signed-in C# install, **When** the Rust build starts, **Then** it shows usage for the
   tray account without a sign-in prompt (INV 2B §1-§3).
2. **Given** usage data, **When** the user picks "Tray shows: 7D" in the tray menu, **Then** the
   icon digit switches to the 7-day figure and the choice is saved (INV 2D §1-§2).
3. **Given** claude.ai answers 429, **When** polling continues, **Then** the account pauses on the
   1h / 6h / 24h ladder, the pause survives a restart, and the last good figures stay visible as
   stale (INV 2B §7).
4. **Given** no data yet, **When** the icon is drawn, **Then** the placeholder ring is shown in the
   taskbar's own light or dark ink, and the ink follows a taskbar theme change at once.
5. **Given** the C# app is already running, **When** the Rust build starts, **Then** the
   single-instance rule applies across both builds (INV 2A §1.1).

---

### User Story 2 - The desktop widget looks and behaves as before (Priority: P2)

The user sees the same desktop panel: dials or account rows, the service status dial or line, the
plan line, the chosen layout and opacity, at the saved position and size. Hover buttons (hide, edit
layout, lock), drag, resize, edit mode with cell swapping, and every tray-menu setting that changes
the panel work as before; the panel stays at the bottom of the z-order and never takes focus.

**Why this priority**: the panel is the main surface most users look at.

**Independent Test**: with saved settings from the C# app, the Rust panel opens at the same place
and size with the same layout; screenshots of both builds side by side show the same content and
arrangement for each layout preset and theme.

**Acceptance Scenarios**:

1. **Given** saved `WidgetX`, `WidgetY`, `WidgetSide` and `Layout`, **When** the app starts,
   **Then** the panel appears at that place, size and layout, clamped to a visible work area
   (INV 2C §1).
2. **Given** the panel is unlocked, **When** the user drags or resizes it, **Then** the new position
   and size are saved and restored on the next start.
3. **Given** edit mode, **When** the user drags one cell onto another, **Then** the cells swap and
   the layout is saved; leaving edit mode keeps it.
4. **Given** a click on the status dial, **When** released, **Then** the status page opens in the
   default browser.

---

### User Story 3 - Manage accounts and sign in (Priority: P3)

The user adds an account (up to 4), signs in to it in a visible sign-in window, renames it, picks
which account the tray describes, and removes one. Each account keeps its own separate claude.ai
session. "Not signed in" and "Session expired" states offer a way to sign in again.

**Why this priority**: multi-account users depend on it; single-account users only need sign-in
recovery.

**Independent Test**: add a second account, sign in to a different organization, rename it, make
it the tray account, restart: both accounts show their own usage and the second is still signed in.

**Acceptance Scenarios**:

1. **Given** one account, **When** the user adds one and signs in, **Then** both show usage, and
   signing in to an organization another account already uses shows the duplicate-account error
   (INV 2B §4-§5).
2. **Given** an account whose session expired, **When** the panel shows the notice, **Then** the
   user can open the sign-in window from it.
3. **Given** four accounts, **When** the user opens the Accounts menu, **Then** adding is disabled
   with the explanation tooltip.

---

### User Story 4 - Taskbar band (Priority: P4)

A user who turned on the taskbar band sees the transparent "numbers only" strip on the taskbar at the
chosen place (left or by the tray), showing metrics or one row per account. It hides over real
fullscreen apps, survives Explorer restarts, and does not vanish under the taskbar.

**Why this priority**: an opt-in feature with the most fragile platform behavior; it can come after
the main surfaces.

**Independent Test**: enable the band, switch between positions and views, run a fullscreen video,
restart Explorer: the band follows every rule in INV 2D §3, and the diagnostic log records each
visibility decision when its environment variable is set.

**Acceptance Scenarios**:

1. **Given** the band is on, **When** an active window goes fullscreen over the taskbar, **Then** the
   band hides within the documented delay and comes back when it leaves.
2. **Given** an invisible overlay briefly covers the taskbar, **When** it lasts less than the
   inactive-window delay, **Then** the band does not flicker.
3. **Given** Explorer restarts, **When** the taskbar comes back, **Then** the band reattaches and the
   tray icon is re-added.

---

### User Story 5 - Settings, autostart, updates, export, diagnostics (Priority: P5)

Theme choice (system, dark, light) and panel opacity, "Launch at login", "Check for updates and
install", usage export files and the widget log all work as before.

**Why this priority**: each is small, and none blocks daily use of the earlier stages.

**Independent Test**: toggle each tray setting and check the persisted value, the Run-key entry,
the export file contents and the log lines against INV 2A §4-§8.

**Acceptance Scenarios**:

1. **Given** "Launch at login" is ticked, **When** the user signs in to Windows, **Then** the app
   starts, and the Run value has the same name as the C# app's.
2. **Given** an export directory, **When** usage refreshes, **Then** each account's export file is
   rewritten atomically with the same JSON keys as today.
3. **Given** a newer release exists, **When** the user runs "Check for updates and install", **Then**
   the app downloads, replaces itself and restarts, or explains why it cannot.

---

### User Story 6 - Switch existing users to the Rust build (Priority: P6)

When the Rust build reaches parity, an existing C# user gets it through the C# app's own "Check for
updates and install" and keeps settings, sign-ins, autostart and widget position.

**Why this priority**: it is the last step and needs every other story.

**Independent Test**: on a C# install of the last C# version, run the built-in updater against the
first Rust release: the Rust app starts in its place with the same settings, accounts, sign-ins and
autostart.

**Acceptance Scenarios**:

1. **Given** the last C# version, **When** it installs the first Rust release, **Then** the Rust app
   starts from the same folder, and files left over from the C# build do not break it or its own
   updater.
2. **Given** autostart was on in the C# app, **When** the Rust app replaces it, **Then** autostart
   still launches the Rust app.

---

### User Story 7 - Telegram bot bridge on the personal branch (Priority: P7)

On the owner's local `personal` branch only, the Rust app writes the snapshot file the bot reads,
offers the "Telegram bot" tray submenu and starts the bot at launch, as the C# personal build does
today.

**Why this priority**: personal and local; it comes after the public switch.

**Independent Test**: on the `personal` branch, start the Rust app: the bot starts once, the
snapshot file updates on every refresh, and the bot reports the same limits it did with the C#
build.

**Acceptance Scenarios**:

1. **Given** bot autostart is on, **When** the app starts, **Then** the bot is started unless its
   recorded process is already alive, and it keeps running after the app quits.
2. **Given** a refresh, **When** usage changes, **Then** the snapshot file has the same format and
   location as the C# personal build writes.

### Edge Cases

- `settings.json` missing, empty, malformed or with unknown fields or enum values: same tolerant
  reading, `.bad-*` copy, retry timing and last-good fallback as INV 2A §3; unknown fields survive
  a save.
- Usage response with no buckets, a bucket at 0% or 100%, a missing `resets_at`, an unknown model
  bucket, or a non-JSON body (an HTML sign-in page): same decoding outcomes as INV 2B §6.
- WebView2 Runtime missing, or its process dies mid-fetch: the same error states and recovery as
  INV 2B §1.3 and §2.3, never a crash.
- Monitor unplugged, DPI changed, taskbar on another edge, or panel saved off-screen: panel
  clamped into a visible work area at the correct scale.
- Clock jumps, sleep and resume, network loss: polling resumes per INV 2B §9 without duplicate
  overlapping fetches.
- Two accounts that resolve to the same organization: duplicate-account error, no data mix-up.
- Explorer restarts: tray icon and band come back.

## Requirements *(mandatory)*

### Functional Requirements

**Parity and data**

- **FR-001**: The Rust app MUST implement every user-visible behavior listed in INV sections 2A to
  2D, except items this spec marks as intentionally changed.
- **FR-002**: The Rust app MUST read and write `settings.json` in the same location and format as
  the C# app (key names, enum spellings, defaults, clamps, account cap, legacy-field migration;
  INV §3), so either build can read the other's file.
- **FR-003**: The Rust app MUST reuse the existing per-account sign-in sessions, including the legacy
  `default` account's session location (INV 2B §1.2), so no user has to sign in again.
- **FR-004**: The Rust app MUST keep the single-instance rule, shared with the C# app while both
  exist.
- **FR-005**: Each C# test MUST have a Rust test that pins the same behavior, or a recorded reason it
  no longer applies (INV §5).

**Usage and status**

- **FR-006**: The app MUST fetch the organization list, subscription fields and usage from claude.ai
  for each account with the same requests, organization pick, plan-label rules and refresh intervals
  as INV 2B §3-§5.
- **FR-007**: The app MUST poll on the same schedule with the same per-account stagger, triggers
  (start, resume, sign-in, manual refresh) and merging of overlapping refreshes as INV 2B §9.
- **FR-008**: The app MUST apply the same 429 pause ladder, persisted per account, and show the last
  good figures as stale while an account is failing (INV 2B §7).
- **FR-009**: The app MUST read the service status as INV 2B §8 describes and show it as a dial or a
  line.

**Surfaces**

- **FR-010**: The tray icon MUST show the selected metric's number, or the placeholder ring, drawn
  for the taskbar's light or dark mode at the tray's pixel size, and a tooltip with the same text.
- **FR-011**: The tray menu MUST offer every item, submenu, checkmark and stay-open behavior of
  INV 2D §2, styled to match the app theme.
- **FR-012**: The desktop panel MUST support every layout, panel view, dial, ring, opacity preset,
  hover button, drag, resize, lock and edit-mode behavior of INV 2C §1-§2, never take focus, and
  stay at the bottom of the z-order.
- **FR-013**: The taskbar band MUST follow INV 2D §3: owned by the taskbar, positions left or tray,
  visibility probes, hysteresis delays, burial recovery, fullscreen hiding, Explorer-restart
  recovery and the opt-in diagnostic log.
- **FR-014**: Themes MUST follow INV 2C §3: the panel follows the chosen or system app theme, while
  the tray icon and band follow the taskbar theme; palette colors are the same.

**Accounts and sign-in**

- **FR-015**: Users MUST be able to add (up to 4), sign in, rename, remove and choose the tray
  account, with sessions kept apart per account (INV 2A §1.8, 2B §4).

**Settings, autostart, updates, export, diagnostics**

- **FR-016**: "Launch at login" MUST use the same Run value name and keep working across the switch
  from the C# app.
- **FR-017**: The updater MUST check the latest GitHub release, install it in place and restart, and
  explain the cases where it cannot (INV 2A §5).
- **FR-018**: Usage export MUST write the same per-account files with the same keys and the same
  atomic replace (INV 2A §6).
- **FR-019**: The widget log MUST keep the same location, rotation and line format (INV 2A §8).
- **FR-020**: No failure in a handler, fetch or system event may end the process; it is logged and
  the app keeps running.

**Delivery**

- **FR-021**: The rewrite MUST ship in the stages of User Stories 1 to 6, each a runnable build; the
  C# app keeps shipping from `main` until User Story 6. User Story 7 follows on `personal` only.
- **FR-022**: The last C# version's updater MUST be able to install the first Rust release (INV 2A
  §5.2: it looks for the running exe's file name in a `*-win-x64.zip` asset).
- **FR-023**: The look MUST stay visually equivalent to the C# app: the same layout, colors, fonts
  and sizes, with small anti-aliasing differences allowed. Pixel-identical output is not required,
  and the look is not redesigned in this feature.
- **FR-024**: Known C# oddities (INV §7) are ported as they are, except these clear bugs, which the
  port fixes:
  - the panel position clamp works in physical pixels at every scale above 100% (no DIP and pixel
    mix-up);
  - renaming an account changes only that account's name and never overwrites settings changed while
    the rename window was open;
  - the "Session expired" notice offers the same Sign in action as "Not signed in";
  - the tray icon is re-added after Explorer restarts, handled explicitly.
- **FR-025**: The local-only Telegram bot bridge is ported as a separate stage after parity (User
  Story 7), on the local `personal` branch only, never pushed.

### Key Entities *(include if feature involves data)*

- **Settings**: the persisted `settings.json`: panel placement, layout, theme, opacity, tray and
  band choices, export directory, accounts, legacy fields (INV §3).
- **Account profile**: id (also the sign-in session name), display name, organization, 429 pause
  state, subscription fields and their fetch time, optional export path.
- **Usage snapshot**: per account, the usage buckets (5-hour, 7-day, per-model) with utilization and
  reset time, plus fetch time and error state.
- **Service status**: overall indicator and the "Claude Code" component status.
- **Release**: latest version, page link and Windows asset link.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the behaviors in the inventory's checklist are either working in the Rust app
  or recorded as intentionally changed, and every C# test has a Rust counterpart or a recorded
  reason.
- **SC-002**: An existing user switches to the Rust build with zero sign-ins and zero settings
  changes: the first refresh after the switch shows usage for every account.
- **SC-003**: After the switch, the C# app can still read the settings file the Rust app wrote.
- **SC-004**: The Rust app uses at most half the memory at idle of the C# app and starts to a
  visible tray icon in under 1 second on the owner's machine.
- **SC-005**: The release is one portable executable, smaller than the current 75 MB zip.
- **SC-006**: Over 7 days of daily use the Rust app does not crash and the band shows no flicker or
  disappearance the C# app does not have.

## Assumptions

- The UI text stays in English, as today.
- Windows 10 users still install the WebView2 Runtime themselves, as today; Windows 11 has it built
  in.
- claude.ai is still read through a signed-in embedded browser session, as today; no official API
  replaces it.
- The release asset keeps the `ClaudeUsageWidget-vX.Y.Z-win-x64.zip` name and contains an exe named
  `ClaudeUsageWidget.App.exe` until users are on the Rust updater, so the C# updater can install it.
- Developer-only render modes (`CUW_RENDER_PREVIEW`, `CUW_RENDER_MENU`) may be replaced by
  equivalent test tooling.
- The other contributor to the public repo is told before the switch lands on `main`.
