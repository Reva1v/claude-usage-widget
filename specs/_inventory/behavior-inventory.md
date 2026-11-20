# Behavior inventory: claude-usage-widget (C#/.NET 10 WPF) for the Rust rewrite

Source: `C:\Users\Reva1v\IdeaProjects\claude-usage-widget`, branch `rust-port` (identical to `main`, HEAD `d73a93a`, version 0.2.12). This document is the reference the Rust spec is written from. It was produced by a static read of the code, tests, README and `docs/superpowers/*`; nothing was executed. Statements marked **(inferred)** are deductions, not something the code says outright. The Telegram bot bridge (`src/ClaudeUsageWidget.App/Bot`) lives only on the local `personal` branch and is out of scope.

How it is organised:

- **Section 1** is the overview, written for this document.
- **Section 2** is the per-subsystem inventory in four parts (2A to 2D), each with its own file-reference prefixes, listed in the legend below. The index right after the legend maps each subsystem to the part and subsection that covers it.
- **Sections 3 to 6** collect the settings schema, the external contracts, the tests and the porting hazards from all four parts.
- **Section 7** lists the places where the code, the specs and the README disagree, plus questions left open.

Some facts appear in more than one part; where two parts overlap, both kept their own `file:line` references.

## Legend of file-reference prefixes

All paths are relative to the repo root.

- Part 2A: `ASC` = `src/ClaudeUsageWidget.App`, `CORE` = `src/ClaudeUsageWidget.Core`. A bare `App.xaml.cs:N` means `src/ClaudeUsageWidget.App/App.xaml.cs`.
- Part 2B: `src/App` = `src/ClaudeUsageWidget.App`, `src/Core` = `src/ClaudeUsageWidget.Core`, `Tests` = `Tests/ClaudeUsageWidget.Core.Tests`. Bare file names such as `ClaudeWebSession.cs:N` refer to the files in those folders.
- Part 2C:

| Prefix | Path |
|---|---|
| `DWW` | `src/ClaudeUsageWidget.App/Windows/DesktopWidgetWindow.cs` |
| `RNW` | `src/ClaudeUsageWidget.App/Windows/RenameWindow.cs` |
| `SYS` | `src/ClaudeUsageWidget.App/Windows/SystemTheme.cs` |
| `WRV` | `src/ClaudeUsageWidget.App/Views/WidgetRootView.xaml.cs` |
| `WRX` | `src/ClaudeUsageWidget.App/Views/WidgetRootView.xaml` |
| `DIAL` / `DBASE` / `DTEXT` / `RING` / `PAL` / `SDIAL` / `THEME` | `src/ClaudeUsageWidget.App/Views/{DialControl,DialControlBase,DialText,NameRingControl,Palette,StatusDialControl,Theme}.cs` |
| `PREV` | `src/ClaudeUsageWidget.App/LayoutPreview.cs` |
| `APP` | `src/ClaudeUsageWidget.App/App.xaml.cs` |
| `C/Views/*` | `src/ClaudeUsageWidget.Core/Views/*.cs` |
| `TT` | `src/ClaudeUsageWidget.Core/Formatting/TrayText.cs` |
| `THR` | `src/ClaudeUsageWidget.Core/Usage/ThresholdLevel.cs` |
| `UM` | `src/ClaudeUsageWidget.Core/Usage/UsageMath.cs` |
| `SS` | `src/ClaudeUsageWidget.Core/Status/ServiceStatus.cs` |
| `WS` | `src/ClaudeUsageWidget.Core/Settings/WidgetSettings.cs` |

  In 2C, WPF lengths are DIPs (1/96 inch) unless stated otherwise, and "scale" = `ClampSide(side) / 170`.
- Part 2D (paths relative to `src/ClaudeUsageWidget.App/`): `TI` = `Tray/TrayIcon.cs`, `TIR` = `Tray/TrayIconRenderer.cs`, `MC` = `Tray/MenuChrome.cs`, `MG` = `Tray/MenuGlyphs.cs`, `MP` = `Tray/MenuPlacement.cs`, `WMR` = `Tray/WidgetMenuRenderer.cs`, `MPV` = `Tray/MenuPreview.cs`, `TBW` = `Windows/TaskbarBandWindow.cs`, `W32` = `Windows/Win32.cs`, `APP` = `App.xaml.cs`.

## Subsystem index

| Subsystem | Where |
|---|---|
| Process, startup order, wiring, shutdown, unhandled exceptions | 2A §1, 2D §0 |
| Single-instance mutex | 2A §1.1, 2D §0 |
| Power / sleep (resume refresh) | 2A §1.6, 2B §9.4 |
| Settings store, atomic replace, unparseable file handling, file paths | 2A §3 |
| Migrations, account cap, settings schema | 2A §3.6; Section 3 |
| Accounts / profiles (data model, add, rename, remove, tray account) | 2A §1.3, §1.8; 2B §1.2; 2D §2.5 |
| Per-profile WebView2 sessions, environment, runtime missing, crash recovery | 2B §1, §2 |
| How claude.ai is fetched (hidden WebView2 navigation, injected JS, retries) | 2B §3 |
| Login flow, session cookie, login window | 2B §4 |
| Organization pick, subscription tier / plan names | 2B §5 |
| Usage decoding, math, model buckets, thresholds | 2B §6; 2C §4 |
| UsageStore state machine, 429 ladder, Retry-After, error states | 2B §7 |
| Idle lease of the fetch WebView | 2B §2.2 |
| Poll schedule, per-account stagger, triggers | 2A §1.5; 2B §9 |
| Service status (status.claude.com) | 2B §8 |
| Formatting: TrayText, BandText, durations, percents, dial derivation | 2C §4 |
| Desktop widget window: styles, z-order, layouts, geometry, drag, resize, lock, edit mode, positioning, monitors, DPI, show/hide | 2C §1 |
| Dial rendering, name ring, status dial | 2C §2 |
| Themes: system/dark/light, apps vs taskbar theme, palette hex values, opacity | 2C §3 |
| Rename window | 2C §6 |
| Layout preview (`CUW_RENDER_PREVIEW`) | 2C §5 |
| Tray icon: creation, icon drawing, tooltip | 2D §1 |
| Tray menu: items, submenus, checkmarks, handlers, custom rendering, glyphs, placement, `CUW_RENDER_MENU` | 2D §2 |
| Taskbar band: owned window, positioning, probes, hysteresis, burial, fullscreen hiding, timers, WinEvent hooks, `CLAUDE_BAND_DIAG` | 2D §3 |
| Win32 P/Invoke inventory | 2D §4 |
| Autostart (Run key) | 2A §4 |
| Updater (GitHub latest release, self-install script) | 2A §5 |
| Usage export (`*.widget.json`) | 2A §6 |
| HttpValidation, CoreInfo | 2A §7, §8.2 |
| Logging / diagnostics (`widget.log`) | 2A §8; 2B §10 |
| Error states shown to the user (complete string list) | 2B §7.6-7.7; 2C §1.6; 2A §1.2 |

---

# 1. Overview

## 1.1 Projects

- `src/ClaudeUsageWidget.Core` (net10.0 class library): pure logic. It holds usage decoding and math (`Usage/`), the status page fetch and decode (`Status/`), the observable stores and the 429 backoff (`Store/`), settings persistence and migration (`Settings/`), formatting (`Formatting/TrayText.cs`), view models and layout math (`Views/`), the organization and plan helpers and the idle lease (`Web/`), the GitHub release parsing (`Update/`), and log line formatting (`Diagnostics/`). It has no WPF dependency and is unit-tested.
- `src/ClaudeUsageWidget.App` (net10.0-windows, WPF and WinForms): the shell. It holds `App.xaml.cs` (the composition root and all scheduling), the desktop widget window and its views, the tray icon and owner-drawn menu (WinForms `NotifyIcon` and `ContextMenuStrip`), the taskbar band window, the WebView2 session and login window, autostart, the updater, the export writer and the log. The one dependency is the `Microsoft.Web.WebView2` NuGet package.
- `Tests/ClaudeUsageWidget.Core.Tests` (xUnit, 33 test classes) and `Tests/ClaudeUsageWidget.App.Tests` (STA-thread WPF tests: widget window, menu placement, WebView death). CI is `.github/workflows/ci.yml`.

## 1.2 Process and threads

- One process with no main window and `ShutdownMode=OnExplicitShutdown`. It lives until Quit, or until the updater calls `Shutdown()`.
- Single instance: named mutex `ClaudeUsageWidget.SingleInstance`, initially owned. A second instance exits silently. The dev render switches `CUW_RENDER_PREVIEW` and `CUW_RENDER_MENU` run before the mutex check, render PNGs and exit.
- Threads:
  - **Most work happens on the WPF UI thread (Dispatcher):** every window, the tray menu (WinForms on the same thread), the WebView2 controllers, the `DispatcherTimer`s, and the store `Changed` handlers, which are marshalled back with `Dispatcher.Invoke`.
  - **Background:** `SystemEvents` (power and theme changes) arrive on a system thread and are marshalled to the UI thread. HttpClient awaits (status page, GitHub) continue on the thread pool or the UI context.
  - **Band:** `SetWinEventHook` callbacks (out-of-context, on the UI thread's message loop) drive the band's visibility probe.
- Unhandled exceptions on the UI thread, in WinForms menu handlers and in fire-and-forget tasks are logged to `widget.log` and swallowed. The process never dies from a handler failure.

## 1.3 External dependencies

| Dependency | Use |
|---|---|
| WebView2 Runtime (Evergreen) | One shared environment. The user-data folder is `%LOCALAPPDATA%\ClaudeUsageWidget\profiles\default`, with one WebView2 *profile* per account; the first, migrated account uses the implicit default profile. A visible login window loads `https://claude.ai/login`. Each account has a hidden off-screen fetch WebView that *navigates* to the JSON API URLs and reads `document.body.innerText`. |
| claude.ai (undocumented) | `GET https://claude.ai/api/organizations` (organization pick and plan fields) and `GET https://claude.ai/api/organizations/{orgId}/usage` (the usage buckets). These are browser navigations carrying the session cookie (`sessionKey`), with no custom headers and the native user agent. |
| status.claude.com | `GET https://status.claude.com/api/v2/summary.json` through a plain HttpClient. Uses the status of the "Claude Code" component, falling back to the page-wide `status.indicator`. |
| GitHub | `GET https://api.github.com/repos/Reva1v/claude-usage-widget/releases/latest`, only when the user clicks the manual update menu item. The `*-win-x64.zip` asset is downloaded and applied by a hidden PowerShell script. The tray also opens the repository page and its issues page in the default browser. |
| Registry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ClaudeUsageWidget` (autostart). `HKCU\...\Themes\Personalize` values `AppsUseLightTheme` (panel and menu theme) and `SystemUsesLightTheme` (tray digits and band text). |
| Files | `%APPDATA%\ClaudeUsageWidget\settings.json` (plus a `.tmp` file and `.bad-*` copies). `%LOCALAPPDATA%\ClaudeUsageWidget\widget.log` (rotates at 1 MB to `.log.1`). Optional export files `*.widget.json`. `%TEMP%\claude-band-diag.log` when `CLAUDE_BAND_DIAG=1`. `%TEMP%\ClaudeUsageWidget-update-<v>\` during an update. |
| Shell / Win32 | `Shell_TrayWnd` as owner of the band window. WinEvent hooks, `WindowFromPoint` probes, `SetWindowPos`, the work area per monitor, `SystemEvents.PowerModeChanged` and `UserPreferenceChanged`. |

## 1.4 Data flow: from fetch to every UI surface

1. **Triggers:**
   - Startup, after settings load. If the tray account has no session cookie, its login window opens first.
   - The 300 s `DispatcherTimer`, and resume from sleep (`PowerModes.Resume`).
   - Tray "Refresh now", which refreshes the tray account and the status only.
   - A successful sign-in, which refreshes that account only after clearing its cached organization.
2. **`RefreshAllAsync`:**
   - Account *i* of *n* is refreshed after `PollSchedule.OffsetFor(i, n) = 300*i/n` seconds. Account 0 is awaited inline; the others are scheduled with `Task.Delay` and skipped if the account was removed while it waited.
   - `StatusStore.LoadAsync` runs right after account 0 and never waits for the delayed accounts.
3. **Per account:**
   - `HasSessionCookieAsync`. If WebView2 itself throws, a `_webViewFailing` flag is set, which changes the tray tooltip.
   - `UsageStore.LoadAsync` coalesces concurrent calls and respects the 429 pause, which is persisted per account in settings.json.
   - Its fetch delegate, `ClaudeWebSession.FetchUsageAsync`, runs: the organization pick (cached; refreshed hourly for the plan label), then the usage navigation, then `UsageDecoder.Snapshot`.
   - The store moves to Ok, Failed or RateLimited and raises `Changed`.
   - `ExportUsage` writes `*.widget.json` on Ok when an export is configured.
4. **`OnStoresChanged`** (marshalled to the UI thread) redraws every surface from the same inputs: settings, plus each account's `Snapshot`, which falls back to the last good snapshot while the account is failing:
   - **Desktop widget:** `AccountRow.ForAll(...)` builds the rows, `AccountDials`/`DialModel` builds the dials, `StatusLine.Text` gives the status line, and `NoticeFor` gives the sign-in and error notices. Everything is rendered by `WidgetRootView`.
   - **Tray tooltip:** `TrayText.Metrics` plus `StatusLine`, for the tray account.
   - **Tray icon digits:** `TrayIconRenderer.Render(metric, SystemTheme.TaskbarKind)`, showing the metric picked in "Tray shows".
   - **Tray menu state:** `SyncMenuState(TrayMenuState)` with checkmarks, available model buckets, accounts, views, theme and opacity.
   - **Taskbar band:** `BandText.MetricEntries(trayRow)` or `BandText.Entries(rows)`, depending on `BandView`.
5. **Settings writes** come from menu handlers (read-modify-write through `SettingsStore.Load`/`Save`), from window move and resize (a debounced 500 ms persist timer), from rename, add and remove, from the 429 retry state, and from the cached organization and plan fields written by the web session.
6. **Theme changes:** `SystemTheme` (registry plus `UserPreferenceChanged`) calls `ApplyTheme`, which repaints the panel and menu and redraws the tray icon and band.

---

# 2. Subsystems

## Part 2A: app shell, startup and wiring, settings store, autostart, updater, export, logging

### 2A §0. Quick map of the process (what exists at runtime)

- One WPF process, no main window. `ShutdownMode=OnExplicitShutdown` (ASC/App.xaml:5). The process lives as long as nobody calls `Shutdown()`.
- Objects owned by `App` (App.xaml.cs:21-66): `TrayIcon` (WinForms NotifyIcon + custom menu), `DesktopWidgetWindow`, `TaskbarBandWindow` (lazy), `SettingsStore`, `StatusStore`, `DispatcherTimer _refreshTimer`, `IReadOnlyList<AccountRuntime> _accounts`, `_editingLayout` (session-only flag), `_hasSessionCookie` / `_webViewFailing` (ConcurrentDictionary<accountId,bool>), named mutex, `_startupRetry` timer.
- `AccountRuntime` (ASC/AccountRuntime.cs:9) = record `(AccountProfile Profile, ClaudeWebSession Session, UsageStore Store)`. One per account. The only shared thing between accounts is the WebView2 environment (comment line 4-6). Computed property `Snapshot` (lines 14-15): `Store.CurrentState is Ok(snapshot,_) ? snapshot : Store.LastSnapshot` -> the last good snapshot is drawn (dimmed dials) while a refresh fails.
- Timers (all UI-thread `DispatcherTimer`/`Task.Delay`): refresh timer 300 s (App.xaml.cs:244); startup retry 2 s then 30 s (App.xaml.cs:173-185); per-account staggered `Task.Delay` (App.xaml.cs:348); DesktopWidgetWindow persist timer 500 ms (DesktopWidgetWindow.cs, out of scope but writes settings).
- No auto update check anywhere: `Updater.CheckAndInstallAsync` is only called from the tray item (App.xaml.cs:121; TrayIcon.cs:390-393).

### 2A §1. App startup, wiring, shutdown (`App.xaml.cs`)

#### 1.1 Startup order in `OnStartup` (App.xaml.cs:68-161)
1. `base.OnStartup(e)` (70).
2. `Theme.PublishResources()` (74): the XAML binds the palette through DynamicResource; keys must exist before the first view loads.
3. `LayoutPreview.RunIfRequested()` (76) and `Tray.MenuPreview.RunIfRequested()` (77): dev-only render modes controlled by env vars
   `CUW_RENDER_PREVIEW=<dir>` and `CUW_RENDER_MENU=<dir>` (LayoutPreview.cs:30; MenuPreview.cs:24). When set (non-blank) they render PNGs
   (+ `metrics.txt` for the menu) into the dir and call `Environment.Exit(0)` (LayoutPreview.cs ~line 100, MenuPreview.cs:35), i.e. the app exits instead of starting.
   They run BEFORE the single-instance check, so they work even when a widget is already running.
4. Single-instance mutex (85-91): `new Mutex(initiallyOwned: true, "ClaudeUsageWidget.SingleInstance", out isFirstInstance)`.
   - Name has no `Local\`/`Global\` prefix -> per-session (Windows default for unprefixed names is the session namespace).
   - If not first: `Shutdown(); return;` immediately (no message, no focus of the old instance, nothing logged).
   - Mutex is stored in a field so GC does not release it (64-66); it is never explicitly released (ReleaseMutex never called); it dies with the process.
   - Rationale in comment (79-84): on 2026-08-06 two zombie instances fought over band/widget position.
5. Error handlers (100-113):
   - `DispatcherUnhandledException` -> `Debug.WriteLine`, `WidgetLog.Write("-", "unhandled", "dispatcher <ExceptionTypeName>: <Message>")`, `args.Handled = true` (the process survives; no dialog).
   - `System.Windows.Forms.Application.ThreadException` -> `WidgetLog.Write("-", "unhandled", "tray <Type>: <Message>")` (tray menu clicks run under WinForms callbacks that would otherwise show a crash dialog).
   - There is NO `AppDomain.UnhandledException` and NO `TaskScheduler.UnobservedTaskException` handler. Async fire-and-forget is covered by `FireAndForget` (see 1.6).
6. `_trayIcon = new TrayIcon()` and event wiring (119-152). Events and what they do:
   - `QuitRequested` -> `Shutdown()`.
   - `UpdateRequested` -> `FireAndForget(() => Updater.CheckAndInstallAsync(Shutdown))`.
   - `RefreshRequested` ("Refresh now"): no-op until `_statusStore` exists (127); refreshes ONLY the tray account (`RefreshAccountAsync`) then `_statusStore.LoadAsync()` (comment 122-123: refreshing all accounts manually is what the endpoint punishes with a rate limit).
   - `SignInRequested` -> logs `WidgetLog.Write(<tray account DisplayName or "-">, "sign-in-requested", "source=tray")` then `account.Session.OpenLoginWindowAsync()` for the tray account.
   - `TrayMetricSelected`, `ModelBucketSelected`, `ShowOnDesktopToggled`, `TaskbarBandToggled`, `BandPositionSelected`, `LockPositionToggled`, `MenuOpening` (-> `RefreshTrayMenuState`), `AccountSelected`, `EditLayoutToggled`, `PanelViewSelected`, `BandViewSelected`, `ThemeSelected`, `PanelOpacitySelected`, `AccountAddRequested`, `AccountRenameRequested`, `AccountRemoveRequested` (async). Handlers described in section 1.7.
7. Settings path (154-157): `%APPDATA%\ClaudeUsageWidget\settings.json` (`Environment.SpecialFolder.ApplicationData` = Roaming). `new SettingsStore(path)`.
8. `if (TryBuildAccounts()) CompleteStartup(); else RetryStartupUntilSettingsLoad();` (159-160).

#### 1.2 Settings unreadable at startup (163-202)
- `TryBuildAccounts` calls `BuildAccounts()` and catches ONLY `IOException` or `UnauthorizedAccessException` (197). `SettingsUnparseableException` is an IOException (SettingsUnparseableException.cs:8) so it is retried too. Any other exception type propagates (and would hit DispatcherUnhandledException -> swallowed, app half-started: inferred).
- On failure: `WidgetLog.Write("-", "settings", "unreadable at startup, retrying: <Type>: <Message>")` (199).
- `RetryStartupUntilSettingsLoad`: tray tooltip set to `"Claude Usage Widget — settings.json unreadable, retrying"` (171, em dash). `DispatcherTimer` interval 2 s; on each tick `TryBuildAccounts()`; if it succeeds the timer stops and `CompleteStartup()` runs; once more than 1 minute has elapsed since the first failure the interval becomes 30 s (184-185). Retries forever (no upper bound).
- Rationale: no half-started widget; never write defaults over a file that is only held (166).
- While in this state: no widget window, no polling, no status store; tray menu still opens (RefreshTrayMenuState returns early when `_widgetWindow is null`, 528; Refresh does nothing, 127; `ShowOnDesktop`/`LockPosition` handlers return early, 589/607). Quit works. Some handlers use `_settings!` and `_widgetWindow!` without null check (e.g. `OnPanelViewSelected` 672) -> would throw NullReference if clicked in this window (inferred; swallowed by the WinForms ThreadException handler).

#### 1.3 `BuildAccounts` (827-845) and account runtime creation (847-869)
- `data = _settings.Load()` (throws on unreadable file; see 3.x).
- If `data.Accounts.Count == 0` (first run: missing file, or an unparseable file for which defaults were returned): create ONE account `new AccountProfile(Id: "default", DisplayName: "Claude", OrganizationId: null, RetryPausedUntil: null, ConsecutiveRateLimits: 0)` and set `TrayAccountId = "default"`, then `_settings.Save(data)` (this writes a settings.json on first run).
  NOTE the display name differs by path: an existing file without `Accounts` goes through SettingsMigration and gets DisplayName `"default"` (SettingsMigration.cs:40-46) while a missing/blank-defaults path gets `"Claude"` here.
- `_accounts = data.Accounts.Select(CreateAccountRuntime).ToList()`.
- `CreateAccountRuntime(profile)`:
  - WebView2 profile name = `null` (implicit default profile) when `profile.Id == "default"` (SettingsMigration.LegacyAccountId), else `profile.Id` (855). Rationale: the pre-multi-account sign-in lives in the implicit profile; see WebViewEnvironment.cs:38-45 INVARIANT (login and fetch controllers must use the same profile name).
  - `new ClaudeWebSession(profileName, profile.Id, profile.DisplayName, _settings)`; `session.SignedIn += () => OnSignedIn(profile.Id)`.
  - `new UsageStore(fetch: session.FetchUsageAsync, hasCredentials: () => _hasSessionCookie.TryGetValue(id) && value, now: () => DateTimeOffset.Now, loadRetryState: () => LoadRetryState(id), saveRetryState: s => SaveRetryState(id, s), log: msg => WidgetLog.Write(profile.DisplayName, "unhandled", msg))` (859-865). `store.Changed += OnStoresChanged`.
  - NB: the `log` lambda captures the display name AT CREATION; a later rename is not reflected in this log label (inferred; `ReloadAccountProfiles` replaces `Profile` in the record but the lambda keeps the old name).
- Retry-state persistence per account (808-825): `LoadRetryState(id)` reads `settings.Current().Account(id)`; returns `UsageRetryState(until, ConsecutiveRateLimits)` only if `RetryPausedUntil` is non-null (else null). `SaveRetryState(id, state)` does `Save(Load().WithAccount(id, a => a with { RetryPausedUntil = state?.Until, ConsecutiveRateLimits = state?.ConsecutiveRateLimits ?? 0 }))`. Rate-limit pause is per account (comment 808-809).

#### 1.4 `CompleteStartup` (205-269)
Order:
1. `ApplyTheme()` (209): `Theme.Apply(ThemeChoices.Resolve(data.Theme, SystemTheme.AppsKind), PanelOpacities.Resolve(data.PanelOpacity))` (637-641).
2. `SystemTheme.Changed += OnSystemThemeChanged; SystemTheme.Start(Dispatcher)` (210-211). SystemTheme (ASC/Windows/SystemTheme.cs) reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` values `AppsUseLightTheme` (panel/menu palette under Theme=System) and `SystemUsesLightTheme` (taskbar colour: tray icon digits and band text ALWAYS follow this); missing or any read failure = Dark; non-zero int = Light; listens to `SystemEvents.UserPreferenceChanged`, category General only, re-reads both values, hops to dispatcher only if either changed.
3. `_statusStore = new StatusStore(new StatusApi().FetchAsync); _statusStore.Changed += OnStoresChanged` (213-214).
4. `_widgetWindow = new DesktopWidgetWindow(_settings)` (217) and subscribe: `HideRequested` -> `OnWidgetHideRequested` (= `SetEditingLayout(false)`); `LayoutEdited` -> `OnLayoutSelected` (same path as tray Layout menu); `EditDoneRequested` -> `SetEditingLayout(false)`; `EditToggleRequested` -> toggle; `StatusModeSelected`, `ModelDialSelected`, `PlanLineSelected`; `SignInRequested` (panel button) -> log `sign-in-requested source=panel` then `OpenLoginWindowAsync` for the tray account.
5. Refresh timer (244-246): `DispatcherTimer { Interval = 300 s }` (`UsageStore.RefreshIntervalSeconds` = 300, CORE/Store/UsageStore.cs:20; StatusStore has its own const = 300, StatusStore.cs:14). Tick -> `FireAndForget(RefreshAllAsync)`. NOTE: the first tick occurs 300 s after startup; the initial refresh is done by `StartupAsync`.
6. `SystemEvents.PowerModeChanged += OnPowerModeChanged` (251).
7. First render: `RenderWidget(); UpdateTrayTooltip(); RefreshTrayIcon(); RefreshTrayMenuState();` (253-256).
8. `if (settings.Current().WidgetVisible) _widgetWindow.Show();` (262): hidden state persists across runs.
9. `if (settings.Current().TaskbarBandEnabled) SetTaskbarBandVisible(true);` (266).
10. `FireAndForget(StartupAsync)` (268).

#### 1.5 `StartupAsync` (280-320) and refresh logic (322-394)
- `StartupAsync`: tray account only (`TrayAccount()` null -> return). `HasSessionCookieAsync()` in try/catch (exception swallowed, hasSession stays false). If no session: `OpenLoginWindowAsync()` in try/catch (exception -> `Debug.WriteLine` only, nothing in widget.log). Then `await RefreshAllAsync()`. Only the tray account gets an auto login window; other signed-out accounts show empty dials until the user signs in from the Accounts submenu (comment 282-285).
- `RefreshAllAsync` (322-361): takes snapshot of `_accounts`. For i in 0..n-1: `offset = PollSchedule.OffsetFor(i, n)` = `300 s * i / n` (CORE/Store/PollSchedule.cs:13-22; throws if n<=0 or index out of range). Offset zero (account index 0) -> `await RefreshAccountAsync(account)` inline (so the first account's fetch delays the scheduling of the others by its duration); other accounts -> fire `Task.Delay(offset).ContinueWith(..., TaskScheduler.FromCurrentSynchronizationContext())` NOT awaited; in the continuation it skips if the account was removed meanwhile (`!_accounts.Contains(account)`) else `FireAndForget(() => RefreshAccountAsync(account))`. Example with 4 accounts: 0 s, 75 s, 150 s, 225 s. Finally `await _statusStore.LoadAsync()` (status is shared; not delayed).
  - Needs a UI SynchronizationContext at call time (called from the UI thread via FireAndForget).
- `RefreshAccountAsync` (363-394): `_hasSessionCookie[id] = await Session.HasSessionCookieAsync(); _webViewFailing[id] = false`. On exception: `_hasSessionCookie[id] = true` (deliberately NOT false, so UsageStore proceeds to `fetch` and reports an honest Network error instead of "NoCredentials"), `_webViewFailing[id] = true`, `Debug.WriteLine` only. Then `await account.Store.LoadAsync()` and `ExportUsage(account)`.
- `UsageStore.LoadAsync` coalesces concurrent calls (CORE/Store/UsageStore.cs:~150) and skips while `RetryPausedUntil` is in the future; RefreshAccountAsync still calls ExportUsage afterwards but ExportUsage only writes when state is `Ok`.

#### 1.6 Other wiring
- `FireAndForget(Func<Task>)` (1067-1078): `async void`, awaits the action, catches everything -> `Debug.WriteLine` + `WidgetLog.Write("-", "unhandled", "fire-and-forget <Type>: <Message>")`.
- `OnSignedIn(accountId)` (440-453): finds account; `Session.ClearCachedOrganization()` (an account may have signed in via a different login; saved org id must not survive); `FireAndForget(RefreshAccountAsync(account))` for that account only.
- `OnStoresChanged` (489-502): `Dispatcher.Invoke(() => { RenderWidget(); UpdateTrayTooltip(); RefreshTrayIcon(); RefreshTrayMenuState(); RenderTaskbarBand(); })`. Both `UsageStore.Changed` and `StatusStore.Changed` route here; may arrive off the UI thread.
- `OnPowerModeChanged` (478-487): ONLY `PowerModes.Resume` matters (Suspend/StatusChange ignored). Marshals with `Dispatcher.Invoke(() => FireAndForget(RefreshAllAsync))` (SystemEvents fires on a separate system thread). So on resume ALL accounts refresh (staggered per 1.5, account 0 at once) plus status; no delay for network readiness; no debounce other than UsageStore coalescing + rate-limit pause.
- `SetEditingLayout(bool)` (470-476): sets `_editingLayout` (NOT persisted: "a widget that starts in edit mode because it was closed in edit mode would be a trap", 28-30), `_trayIcon.EditingLayout`, `_widgetWindow.EditMode`, then `RefreshTrayMenuState`. Called on hide (`OnWidgetHideRequested`), on "Show on desktop" turned off, on Done.
- `TrayAccount()` (873-877): account whose id == `settings.Current().TrayAccountId`, else the first account; null only when no accounts. Used for the tray icon, tooltip, "Refresh now", status/error shown on panel, startup login window, panel Sign in button.
- `SnapshotsById()` (879-880): dictionary accountId -> `account.Snapshot` (last good).
- `ReloadAccountProfiles()` (1053-1060): after a rename, replaces `Profile` in live `AccountRuntime` records (matching by id, dropping runtimes whose id vanished from settings) without recreating sessions.

#### 1.7 Tray/panel settings handlers (each does read-modify-write: `_settings.Save(_settings.Load() with {...})`)
All use `Load()` (not `Current()`), so an unreadable/unparseable file makes the handler throw (IOException) and NOTHING is saved (by design; exception is swallowed by WinForms ThreadException handler -> logged `unhandled tray ...`).
| Handler | Setting written | Side effects |
|---|---|---|
| OnTrayMetricSelected(key) 563 | `TrayMetricKey` = "five_hour" / "seven_day" / "model" (menu strings; any unknown value is treated as index 0 by `TrayIcon.MetricIndex`, TrayIcon.cs:286-291) | RefreshTrayIcon, RefreshTrayMenuState |
| OnModelBucketSelected(key?) 571 | `ModelBucket` (null = auto) | RenderWidget, tooltip, icon, menu |
| OnShowOnDesktopToggled(bool) 585 | `WidgetVisible` | returns if no window; Show()/Hide(); if hidden -> SetEditingLayout(false) |
| OnLockPositionToggled 602 | via `_widgetWindow.PositionLocked` setter (window itself saves `PositionLocked`) | menu refresh |
| OnTaskbarBandToggled 612 | `TaskbarBandEnabled` | `SetTaskbarBandVisible` |
| OnBandPositionSelected 620 | `BandPosition` ("tray" or "left") | `_bandWindow?.SetPosition` |
| OnBandViewSelected 628 | `BandView` | RenderTaskbarBand |
| OnThemeSelected 643 | `Theme` | ApplyTheme |
| OnPanelOpacitySelected 650 | `PanelOpacity` | ApplyTheme |
| OnPanelViewSelected 669 | `LayoutResolution.Apply(settings, view)` sets `Layout`, `StatusMode`, `ModelDial` | `_widgetWindow.RebuildLayout(count)`, Render |
| OnLayoutSelected 882 | `Layout = WidgetLayout.Sanitize(layout, current.Status, current.ModelDial)`, `StatusMode = current.Status`, `ModelDial = current.ModelDial` (both written back so a legacy file stops being ambiguous) | RebuildLayout |
| OnStatusModeSelected 905 | `StatusMode`, `ModelDial`, sanitized `Layout` | RebuildLayout |
| OnModelDialSelected 928 | `ModelDial`, `StatusMode`, sanitized `Layout` | RebuildLayout |
| OnPlanLineSelected 949 | `PlanLine` | RebuildLayout (panel size depends on the line) |
| OnAccountSelected 958 | `TrayAccountId` | render widget, band, tooltip, icon, menu |
| DesktopWidgetWindow.PersistGeometry (out of scope; DesktopWidgetWindow.cs:624-629) | `WidgetX`, `WidgetY` (= Top + strip pad), `WidgetSide` (debounced by 500 ms timer; skipped when Left/Top is NaN) | |

#### 1.8 Accounts add / rename / remove
- Add (970-999): refuse if `Accounts.Count >= AccountLimits.Max` (4). New profile `Id = Guid.NewGuid().ToString()` (dashed GUID, satisfies profile-name alphabet and never equals "default"), `DisplayName = "New account"`, other fields null/0. Saved; new `AccountRuntime` appended; `RebuildLayout`, `RenderWidget`, `RenderTaskbarBand`, menu refresh; logs `WidgetLog.Write("New account", "sign-in-requested", "source=add-account")`; then `OpenLoginWindowAsync()` on the new session.
- Rename (1001-1015): `RenameWindow.Ask(currentName)` (modal WPF dialog, out of scope); null/whitespace -> cancel; saves `DisplayName = name.Trim()`; `ReloadAccountProfiles()`; render widget + band + menu. No uniqueness/length check in App code (inferred; RenameWindow may limit).
- Remove (1017-1048): async. Finds runtime; `await Session.ClearBrowsingDataAsync()` BEFORE changing settings (exception -> Debug only); then saves `Accounts` without it and `TrayAccountId = null` if it was the tray account; `_accounts` filtered; `RebuildLayout`, render widget/band, tooltip, tray icon, menu. No guard against removing the last account (inferred; TrayIcon.cs:315 adds the item for each account). With zero accounts `TrayAccount()` is null and Render* methods return early; on next launch BuildAccounts recreates `default`/"Claude". The Accounts submenu disables "Add account" at the cap with tooltip `"The widget shows at most 4 accounts."` (TrayIcon.cs:324-329).

#### 1.9 Rendering helpers
- `RenderWidget` (751-765): needs a tray account; `AccountRow.ForAll(data.Accounts, SnapshotsById(), data.ModelBucket, now)` -> `_widgetWindow.Render(rows, trayAccount.Store.CurrentState, _statusStore.Status, trayAccount.Store.RetryPausedUntil)`. The error/notice on the panel is the TRAY account's state only.
- `RenderTaskbarBand` (733-749): no-op if band window null. `BandViews.Resolve(data.BandView, data.Accounts.Count) == Metrics` and a tray row exists -> `BandText.MetricEntries(trayRow)` else `BandText.Entries(rows)`; `_bandWindow.Render(entries, SystemTheme.TaskbarKind)`.
- `SetTaskbarBandVisible` (682-702): window created lazily; `SetPosition(BandPosition)`; Render BEFORE `Dock()`; disable -> `Detach()` (window survives). `OnTaskbarBandLost` (715-728): on `Lost` event disposes the dead window (`Close()` guarded against InvalidOperationException) and re-creates if `TaskbarBandEnabled`.
- `RefreshTrayIcon` (507-519): `AccountDials.For(profile, snapshot, ModelBucket, now).Dials` -> `TrayText.Metrics(models)` -> `TrayText.IconIndex(TrayIcon.MetricIndex(TrayMetricKey), models)` -> `TrayIcon.SetIcon(TrayIconRenderer.Render(metrics[index].Value, SystemTheme.TaskbarKind))`. The icon is drawn dynamically (a number), not from app.ico (csproj comment 72-74).
- `UpdateTrayTooltip` (767-806): if `_webViewFailing[trayAccountId]` -> tooltip `"Claude Usage Widget — WebView2 error, see widget for details"`. Else `"Claude Usage Widget — 5H 42% · 7D 18% · FAB 8%"` = prefix + `" — "` + metrics `"{Label} {Value}"` joined by `" · "` (` · ` is U+00B7); empty metrics -> just `"Claude Usage Widget"`; if `StatusLine.Text(state, now, RetryPausedUntil)` is non-null it is appended after `\n`. (NotifyIcon text limit handling is in TrayIcon.SetTooltip; not read.)
- `RefreshTrayMenuState` (525-561): returns if no widget window; builds `TrayMenuState(TrayMetricKey, availableBuckets, ModelBucket, WidgetVisible, widgetWindow.PositionLocked, TaskbarBandEnabled, BandPosition, resolvedModelLabel, Accounts, TrayAccountId, PanelViews.Current(resolved layout, status), BandViews.Resolve(BandView, accounts), Theme ?? System, PanelOpacities.Resolve(PanelOpacity))`. Bucket list comes from the TRAY account's snapshot.

#### 1.10 Usage export call site (`ExportUsage`, 403-438)
Runs after every `RefreshAccountAsync` (even after failures; but only writes on `Ok`). Steps:
1. `data = settings.Current()`; `profile = data.Account(id)` (re-read each time so hand edits of `ExportPath`/`ExportDirectory` apply without restart).
2. `path = UsageExport.ExportPathFor(data, id, DisplayName)`; null -> return (export off).
3. If `UsageExport.WritesOver(path, settings.Path)` -> `WidgetLog.Write(DisplayName, "export", "skipped: ExportPath is settings.json")`; return. (Logged on EVERY refresh while misconfigured.)
4. Only if `account.Store.CurrentState is Ok(snapshot,_)` (a fresh success; the last-good snapshot is NOT re-exported so file "age" never lies).
5. `plan = SubscriptionTier.LabelFor(profile, snapshot)`.
6. `payload = UsageExport.Payload(snapshot, DateTimeOffset.Now, DisplayName, data.ModelBucket, plan)`; null -> return.
7. `UsageExportWriter.Write(path, payload)`.

#### 1.11 Shutdown / `OnExit` (1080-1105)
Order: unsubscribe `SystemEvents.PowerModeChanged`; `SystemTheme.Stop()`; stop refresh timer; `_trayIcon.Dispose()` (removes tray icon immediately; otherwise the icon lingers until mouse-over); `_widgetWindow.Close()`; `_bandWindow.Detach()` then `Close()` (band may still be owned by Shell_TrayWnd of another process); `base.OnExit`. Note: `_startupRetry` timer is not stopped (irrelevant since process ends), `UsageStore`s/WebView sessions are not explicitly disposed, settings are not flushed (every change is saved immediately). The mutex is not released explicitly. Also runs for the second instance (which only called `Shutdown()`; fields null-safe).
Shutdown triggers: tray Quit; Updater (`quit` callback = `Shutdown`).

### 2A §2. Assembly / build metadata

- `ASC/App.xaml:5` `ShutdownMode="OnExplicitShutdown"`; no startup URI, no resources.
- `ASC/AssemblyInfo.cs`: `[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]` (WPF boilerplate; no behaviour).
- `ASC/app.manifest`: assemblyIdentity `ClaudeUsageWidget.App` v1.0.0.0 (line 9); DPI: `<dpiAwareness>PerMonitorV2</dpiAwareness>` (12), `<dpiAware>true/PM</dpiAware>` (13), `<gdiScaling>true</gdiScaling>` (14); five `supportedOS` GUIDs (22-26): `{e2011457-...}`, `{35138b9a-...}`, `{4a2f28e3-...}`, `{1f676c76-...}`, `{8e0f7a12-...}` (these are, by Microsoft's published list, Vista, 7, 8, 8.1, 10/11; the file's own comment says "Windows 7 .. 11"; mapping from memory = inferred). Without Per-Monitor V2 WPF would be bitmap-scaled between monitors and the tray icon/dials (drawn pixel-for-pixel) would be wrong (comment 2-5).
- `ASC/ClaudeUsageWidget.App.csproj`: OutputType WinExe (16); TFM `net10.0-windows` (17); Nullable+ImplicitUsings; `UseWPF` + `UseWindowsForms` (WinForms only for NotifyIcon/ContextMenuStrip/Screen); ApplicationManifest `app.manifest`; ApplicationIcon `Resources\app.ico` (exe icon only); **`<Version>0.2.12</Version>`** (27); AssemblyTitle `Claude Usage Widget` (28); NoWarn WFO0003 (33); `PublishReadyToRun=true` (35); PackageReference `Microsoft.Web.WebView2` 1.0.4258.31 (8); ProjectReference to Core; InternalsVisibleTo `ClaudeUsageWidget.App.Tests`. No RuntimeIdentifier in csproj; publish command from README: `dotnet publish src/ClaudeUsageWidget.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`. `CoreInfo.Version` reads the entry assembly's InformationalVersion, so this csproj `Version` is THE version shown in UI and compared by the updater.
- `CORE/ClaudeUsageWidget.Core.csproj`: net10.0, Nullable, ImplicitUsings, InternalsVisibleTo `ClaudeUsageWidget.Core.Tests`; no package references (only BCL: System.Text.Json, System.Security.Cryptography). (bin/obj folders contain stale net8.0 outputs; irrelevant.)
- Stray tracked file at repo root literally named `NavigateToPastedLink()` (appears in `git ls-files`; not examined; likely accidental).

### 2A §3. Settings store

#### 3.1 Files and paths
| Path | Meaning |
|---|---|
| `%APPDATA%\ClaudeUsageWidget\settings.json` | main settings (Roaming). Set in App.xaml.cs:154-156. UTF-8 (no BOM written), indented JSON. |
| `<settings.json>.<32-hex-guid>.tmp` | temp file used by FileReplace.Write (unique per write; deleted on failure; moved over target on success) (FileReplace.cs:41) |
| `<settings.json>.bad-<yyyyMMddHHmmss UTC>` | copy of an unparseable settings file (SettingsStore.cs:166-167) |
| `%LOCALAPPDATA%\ClaudeUsageWidget\widget.log`, `widget.log.1` | event log, see section 8 |
| `%LOCALAPPDATA%\ClaudeUsageWidget\profiles\default` | WebView2 user data folder (`WebViewEnvironment.UserDataFolder`; other agent covers) |
| `%TEMP%\ClaudeUsageWidget-update-<version>\` | updater work dir |
| `%TEMP%\claude-band-diag.log` | band diagnostics when env `CLAUDE_BAND_DIAG=1` (TaskbarBandWindow.cs:890-895, out of scope) |

#### 3.2 `SettingsStore` behaviour (CORE/Settings/SettingsStore.cs)
JSON options (21-26): `WriteIndented=true`, `AllowTrailingCommas=true`, converter `TolerantEnumConverter`. NO naming policy -> property names are the C# PascalCase names exactly (`WidgetVisible`, ...). Default System.Text.Json behaviours that matter (inferred from defaults, not set): property-name matching is case-SENSITIVE on read, nulls are written (`"WidgetX": null`), comments in JSON are an error (not allowed), unknown members are silently dropped (stated in WidgetSettings.cs:131-133), `DateTimeOffset` is ISO 8601 round-trip, no BOM on write (FileStream + Utf8 serializer), indent 2 spaces, newline = Environment.NewLine (CRLF on Windows, inferred).

Locks: `_saveLock` (guards Load and Save: "a read's result cannot be remembered over a newer save's", 65-66) and `_lastGoodLock`.
`_lastGood` = last settings read or written successfully (in-memory cache; set by `Remember` on successful Load and Save).

`Load()` (67-110), in order, all under `_saveLock`:
1. Read text via `FileReplace.Read(() => File.ReadAllText(path))` -> retries on sharing violations (see 3.4).
2. `FileNotFoundException` or `DirectoryNotFoundException` -> returns `_lastGood ?? new WidgetSettingsData()` (does NOT call migration and does NOT Remember). Meaning: first run = defaults with empty account list (no phantom `default` account, 78-82); file deleted after a good read (sync tool deletes/recreates) = last good settings stand in.
3. Parse `JsonSerializer.Deserialize<WidgetSettingsData>(json, options) ?? new WidgetSettingsData()` (JSON `null` literal gives defaults), then `SettingsMigration.Apply(raw)`.
4. If parse throws `JsonException`, `NotSupportedException`, `InvalidOperationException` or `ArgumentException` (91): 
   - `KeepBadCopy(json)` first (see below).
   - If `_lastGood != null` -> throw `SettingsUnparseableException("<path> does not parse; keeping the last good settings", inner)`.
   - Else if the copy could not be made/does not exist -> throw `SettingsUnparseableException("<path> does not parse and could not be copied aside")`.
   - Else (first load, bad bytes safely kept) -> return `new WidgetSettingsData()` (defaults; empty accounts; NOT remembered, NOT migrated). The caller (BuildAccounts) then creates a `default`/"Claude" account and saves it, replacing the bad file (the bad copy preserves the original).
5. Success -> `Remember(data)` and return the MIGRATED data (so `Current()` returns post-migration shape, and legacy fields are cleared in memory; they leave the file on the next save).

`Current()` (116-130): `Load()`, but on `IOException`/`UnauthorizedAccessException` (incl. SettingsUnparseableException) returns `_lastGood` if any, else rethrows. Used for everything that only DRAWS or reads (render, tooltip, export, retry-state load, session). `Load()` is for read-modify-write only: a failure must abort the save (never write defaults over a held/broken file).

`Save(data)` (135-142): under `_saveLock`: `FileReplace.Write(path, stream => JsonSerializer.Serialize(stream, data, options))` then `Remember(data)`. Exceptions propagate to the caller (e.g. `PersistRetryState` in UsageStore catches all; tray handlers do not -> logged by ThreadException handler).

`KeepBadCopy(json)` (153-175): SHA-256 of the UTF-8 bytes of the bad text; enumerates `<settingsFileName>.bad-*` in the settings directory; if any existing file has identical content hash -> true (no new copy: "once per distinct content", so a 5-minute poll does not fill the folder). Else writes `<path>.bad-<UTC yyyyMMddHHmmss>` via `File.WriteAllText` and returns true. Catches `IOException`/`UnauthorizedAccessException` -> false. Note: same-second collisions with different content would overwrite the earlier stamp (inferred, `File.WriteAllText` truncates).

`SettingsUnparseableException` (SettingsUnparseableException.cs): sealed, subclasses `IOException` on purpose so every handler that already waits out a held file treats it the same (read-modify-write aborts, views draw last good, startup retry).

#### 3.3 Interaction summary (state table)
| File state | First Load ever | Load after a good read | Current() |
|---|---|---|---|
| missing | defaults (empty accounts; App then creates `default`/"Claude" and saves) | last good | same as Load |
| exists, parses | migrated data | migrated data | same |
| exists, unparseable | bad copy kept, then defaults (if copy ok) else throws | throws SettingsUnparseableException (bad copy still kept) | last good (or throws if none) |
| held exclusively > ~0.75 s / ACL denies read | IOException/UnauthorizedAccessException thrown | thrown | last good (or throws if none) |

#### 3.4 `FileReplace` (CORE/Settings/FileReplace.cs) - atomic write/read helper (shared with the usage export)
- Backoff schedule `[50, 100, 200, 400]` ms between attempts = ~0.75 s total, then the last exception is rethrown (17-18).
- `Write(path, writeAction)` (33-64): `target = ResolveLink(path)` (if the path is a symlink whose target exists, the FINAL TARGET is replaced and the link is kept, 125-131); creates the target directory if needed; temp name `target + "." + Guid.ToString("N") + ".tmp"`; opens it `FileMode.Create, FileAccess.Write, FileShare.None`; calls the writer; `stream.Flush(flushToDisk: true)` (fsync); then `Move(temp, target)`. On ANY exception: tries to delete the temp; if that deletion fails with IO/UnauthorizedAccess the message is stored in `ex.Data["TempCleanupFailed"]` ("<temp>: <Type>: <Message>") and the ORIGINAL exception is rethrown (`throw;`).
- `Move(temp, target)` (66-80): `File.Move(temp, target, overwrite: true)`; retries on exceptions where `IsTransient(ex, target)` while `attempt < 4`.
- `IsSharingViolation` (22-23): `IOException` with `(HResult & 0xFFFF)` 32 (ERROR_SHARING_VIOLATION) or 33 (ERROR_LOCK_VIOLATION).
- `IsTransient(ex, target)` (105-123): sharing violation -> true. `UnauthorizedAccessException` -> probe: if target has ReadOnly attribute -> false (permanent); else open target `FileMode.Open, FileAccess.Write, FileShare.ReadWrite|Delete` -> success -> true (a reader holding it, access denied for the move); probe fails with sharing violation or FileNotFoundException -> true (strict reader / antivirus on the temp / no target yet); any other probe failure (e.g. ACL denies write) -> false (fail at once). Anything else -> false.
- `Read<T>(func)` (84-97): retries ONLY on sharing violations with the same backoff; anything else rethrown immediately.
- Documented Windows facts: a move over a held file fails with ACCESS_DENIED (0x80070005), same as a read-only file (measured on Win11); a deny ACE on the FILE alone does not stop the replace (folder delete-child right permits it); new file takes the folder's inherited ACL, not the old file's.

#### 3.5 `TolerantEnumConverter` (CORE/Settings/TolerantEnumConverter.cs)
Factory for ALL enums (`typeToConvert.IsEnum`); nullable enum properties work through the framework's Nullable wrapper over it (inferred).
- Write: `value.ToString()` as JSON string (enum NAME, e.g. `"Dark"`).
- Read: string token: `Enum.TryParse<T>(text, ignoreCase: true)` AND `Enum.IsDefined(named)` -> value (case-insensitive; numeric strings like "1" also parse via Enum.TryParse and are accepted if defined, inferred from .NET). Number token: `TryGetInt32` and `IsDefined` -> that ordinal; other number -> `default(T)`. String/number that is unknown -> `default(T)` (the enum's FIRST member, ordinal 0), NOT a failure. Any other token (null, bool, object, array) -> throws `JsonException("Expected a <T> name, got <TokenType>.")` which makes the WHOLE file "unparseable" (for non-nullable enum slots; for a null token on a nullable enum property the framework yields null before this runs, inferred).
- Consequence for nullable settings: an unknown name in `BandView`, `Theme`, `StatusMode`, `ModelDial`, `PlanLine` reads as the first enum member (Metrics / System / Cell / Shown / Hidden), not null (inferred from converter semantics; test `AnUnknownEnumNameFallsBackForThatFieldAlone` only asserts that accounts survive and a good field is kept, it does not assert the fallback value of the bad field).
- Unknown name inside `Layout.Block.Order` reads as `BlockItem.Name` (first member) -> likely duplicates -> `WidgetLayout.Sanitize.IsUsable` fails (needs distinct items and both FiveHour and SevenDay) -> `BlockLayout.DefaultOrder` (inferred).

#### 3.6 Migration and caps (CORE/Settings/SettingsMigration.cs)
- `AccountLimits.Max = 4` (13).
- `SettingsMigration.LegacyAccountId = "default"` (26): the pre-multi-account WebView2 profile was literally named `default`.
- `Apply(raw)` (28-52), applied on every successful load (never on save):
  1. If `Accounts.Count > 4` -> keep the FIRST 4 (drop from the end), return (legacy fields untouched). `TrayAccountId` is not repaired (may point to a dropped account -> `TrayAccount()` falls back to first).
  2. Else if `Accounts.Count > 0` -> return `raw` unchanged (same instance; test `LeavesAnAlreadyMigratedFileAlone` uses `Assert.Same`). Legacy fields in such a file are ignored and not cleared.
  3. Else (no accounts, and the file EXISTS and parsed) -> synthesizes ONE account: `new AccountProfile(Id: "default", DisplayName: "default", OrganizationId: raw.OrganizationId, RetryPausedUntil: raw.RetryPausedUntil, ConsecutiveRateLimits: raw.ConsecutiveRateLimits)`; sets `TrayAccountId = raw.TrayAccountId ?? "default"`; clears legacy fields `OrganizationId=null, RetryPausedUntil=null, ConsecutiveRateLimits=0`. This fires even when no org id exists (a signed-in-but-never-fetched user keeps their cookie, test `MigratesASignedInAccountThatNeverResolvedItsOrganization`).
- Migration history (what the code reveals): v0 single-account fields at top level (`OrganizationId`, `RetryPausedUntil`, `ConsecutiveRateLimits`) -> v1 multi-account `Accounts` list (the "multi-account" release; README: "Upgrading from a single-account build keeps you signed in"). Later additive, nullable fields need no migration code (null means "file predates it"): `TrayAccountId`, `ExportDirectory`, `AccountProfile.ExportPath`, `StatusMode`, `ModelDial`, `PlanLine`, `Layout`, `BandView`, `Theme`, `PanelOpacity`, `Capabilities`/`RateLimitTier`/`RavenType`/`SubscriptionFetchedAt` (each pinned by tests in WidgetSettingsTests). WebViewEnvironment.cs:17-19 comment says the old user data folder is kept so the upgrade does not strand it, "though it does NOT preserve the sign-in, which was measured", while README:146 claims an upgrade keeps you signed in; the two statements disagree (other agent covers WebView details).
- The 4-account cap rationale (README "Four accounts is the cap"): rows unreadable above 4 at default scale, request-count politeness; single constant.

#### 3.7 settings.json schema

Moved to Section 3 of this document.

### 2A §4. Autostart (`ASC/Autostart.cs`)
- Registry: `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`, value name `ClaudeUsageWidget` (lines 15-16). No admin rights; no Task Scheduler/service.
- `IsEnabled()` (21-25): opens key read-only; true iff the value exists AND is a `string` (a missing value, a missing key, or a non-string type all read as off).
- `SetEnabled(enabled)` (33-56): opens key writable, else `CreateSubKey`, else throws InvalidOperationException. Enable: `exePath = Environment.ProcessPath` (throws InvalidOperationException if null) and writes REG_SZ `"<exePath>"` WITH surrounding double quotes. Disable: `DeleteValue(name, throwOnMissingValue: false)`. Registry exceptions (no rights, GPO) propagate.
- Caller (`TrayIcon.OnLaunchAtLoginClicked`, TrayIcon.cs:~611-629): `desired = !item.Checked`; try SetEnabled + set checkbox; on ANY exception the checkbox is re-read from the registry (`Autostart.IsEnabled()`, not rolled back blindly) and the error goes to `Debug.WriteLine` only. The checkbox is also re-synced from the registry on each menu opening (TrayIcon.cs:271). Autostart is NOT stored in settings.json; the registry is the source of truth.
- Quirk: the registered path is whatever exe is running (a `dotnet run` build registers the bin path; no arguments are passed on autostart, so autostart and manual launch are identical). Updater replaces files in place so the path stays valid.
- Not handled: StartupApproved key (Task Manager "disable") is not consulted (inferred: only the Run value is read).

### 2A §5. Updater (`ASC/Updater.cs`, `CORE/Update/LatestRelease.cs`)

#### 5.1 Entry and constants
- Trigger only from tray menu item "Check for updates and install…" (`UpdateCaption`, TrayIcon.cs:73, closes the menu then raises `UpdateRequested`). Manual only; no background check.
- `Caption = "Claude Usage Widget"` (message box title). Static `HttpClient` with `Timeout = 5 minutes` (38) and `User-Agent: ClaudeUsageWidget/<CoreInfo.Version>` (40; GitHub requires a UA). `_busy` static bool prevents a second concurrent run (34, 48-49); reset in `finally`.
- Any exception: `WidgetLog.Write("-", "update", "failed <ExceptionTypeName>: <Message>")` + MessageBox `"Update failed: <Message>"` (Error icon) (54-58).

#### 5.2 Flow (`RunAsync`, 65-147)
1. Log `update check current=<version>`.
2. `GET https://api.github.com/repos/Reva1v/claude-usage-widget/releases/latest` (`LatestRelease.Endpoint`, LatestRelease.cs:9-10) with `Accept: application/vnd.github+json`; `EnsureSuccessStatusCode` (HTTP error -> exception -> "Update failed: Response status code does not indicate success: 403 (rate limit exceeded)." etc.). Unauthenticated (inferred: GitHub's 60 req/h/IP limit applies).
3. `LatestRelease.Parse(body)`; null -> `InvalidDataException("GitHub returned no release.")`.
4. `LatestRelease.IsNewer(release.Version, CoreInfo.Version)`; if not newer: log `update up-to-date latest=<v>` and MessageBox (Information) `"You have the latest version (v<current>)."`; return.
5. `exePath = Environment.ProcessPath` (throw if null); `installDir = dirname(exePath)`.
6. "Cannot self-install" cases -> instead ask to open the release page: `release.AssetUrl is null` (message "The release has no Windows build attached."), OR `fromSource` = file `ClaudeUsageWidget.App.dll` exists beside the exe (dev build; "This copy runs from a source build, so it can't replace itself."), OR `!CanWrite(installDir)` (probe: write+delete a file `.update-probe-<guid N>` in installDir; "The app folder is not writable:\n<dir>"). Prompt (Yes/No, default Yes): `"Version v<latest> is available.\n\n<why>\n\nOpen the release page?"`; Yes -> log `browser-open site=update url=<url>` and `Process.Start(url, UseShellExecute=true)` on `release.PageUrl` (html_url).
7. Otherwise prompt (Yes/No, default Yes): `"Version v<latest> is available (you have v<current>).\n\nDownload and install it now? The widget will restart."`; No -> return (nothing downloaded).
8. Work dir `%TEMP%\ClaudeUsageWidget-update-<latest>`; deleted (recursive) if it exists, then created. Log `update download <assetUrl>`. Stream the asset (`Client.GetStreamAsync(AssetUrl)`, follows redirects by HttpClient default) to `<work>\release.zip`; `ZipFile.ExtractToDirectory(zip, <work>\files)`. Find the exe in the extracted tree: `Directory.EnumerateFiles(extracted, <running exe's file name>, AllDirectories).FirstOrDefault()`; missing -> `InvalidDataException("<exeName> is not in the release zip.")`. The directory that contains the found exe becomes `-Source` (so a flat zip or a folder-wrapped zip both work).
9. Write `<work>\install.ps1` (text of `InstallScript`), start `powershell.exe` (resolved via PATH) with `UseShellExecute=false, CreateNoWindow=true` and args: `-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File <script> -ProcId <current pid> -Source <dir of new exe> -Target <installDir> -Exe <exePath> -Work <work>` (arguments via ArgumentList). Log `update install v<ver> into <installDir>`. Call `quit()` (= `App.Shutdown`).
10. The script (153-166), verbatim logic: `Wait-Process -Id $ProcId -Timeout 60 -ErrorAction SilentlyContinue`; up to 20 attempts: `Copy-Item -Path (Join-Path $Source '*') -Destination $Target -Recurse -Force -ErrorAction Stop` then break, on error `Start-Sleep -Milliseconds 500`; then ALWAYS `Start-Process -FilePath $Exe` (even if copy failed -> old version restarts); then `Remove-Item -LiteralPath $Work -Recurse -Force -ErrorAction SilentlyContinue`. The script waits for the old PROCESS to exit, so the single-instance mutex is free when the new one starts. Not elevated (hence the writability probe).
- On any failure between step 8 and 9 the work dir is left behind (no cleanup in code; inferred) and the old exe keeps running.
- Message boxes use `MessageBoxOptions.DefaultDesktopOnly` and no owner window (so they do not land behind other windows, comment 183-184). They are modal and block the UI thread's message loop re-entrantly (WPF MessageBox).

#### 5.3 `LatestRelease` (CORE/Update/LatestRelease.cs)
- `record LatestRelease(string Version, Uri? AssetUrl, Uri PageUrl)`.
- `Parse(json)` (20-49): `JsonDocument.Parse` (throws JsonException on invalid JSON -> caught as update failure). Root not an object -> null. Requires `tag_name` (string, length>0) and `html_url` (absolute URI) else null. `assets` array: first asset whose `name` ends with `-win-x64.zip` (OrdinalIgnoreCase; `AssetSuffix`) and has an absolute `browser_download_url` -> `AssetUrl` (an asset with the suffix but a bad URL is skipped and the search continues). `Version = tag_name.TrimStart('v','V')`.
- `IsNewer(latest, current)` (55-58): both strings (after `TrimStart('v','V')`) must parse with `System.Version.TryParse` and `latest > current` strictly. "unknown" or garbage on either side = false. NOTE `System.Version` needs at least major.minor; a bare "1" is unparseable (inferred); a 4-part version compares properly; pre-release suffixes like `1.2.3-beta` do not parse -> never newer.
- Release asset name convention: `ClaudeUsageWidget-vX.Y.Z-win-x64.zip` (only suffix matched), built by the project's release skill (`.claude/release.md`).

### 2A §6. Usage export (`CORE/Usage/UsageExport.cs`, `ASC/UsageExportWriter.cs`)

#### 6.1 Output file
Off by default. Enabled by `settings.ExportDirectory` (all accounts) and/or per-account `ExportPath`. Written after each successful refresh of that account only (state `Ok`), never from the last-good snapshot. Written whole via `FileReplace.Write` (temp + fsync + move, retries, symlink-aware) so a polling reader never sees a half file. `UsageExportWriter.Write` NEVER throws: any exception -> `Debug.WriteLine("Usage export to '<path>' failed: ...")` only (nothing in widget.log).
JSON options `UsageExport.JsonOptions` (UsageExport.cs:65-68): `PropertyNamingPolicy = CamelCase`, NO ignore condition (nulls are written as `null`), not indented (compact single line), no converters.

#### 6.2 Payload `UsageExportPayload` (record, UsageExport.cs:37-49) -> JSON keys (camelCase)
| key | type | meaning | versions |
|---|---|---|---|
| `fiveHour` | double? | utilization % of `five_hour` bucket (null when no such bucket, e.g. Ultra) | v1 |
| `sevenDay` | double? | utilization % of `seven_day` bucket | v1 |
| `atMs` | long | `now.ToUnixTimeMilliseconds()` (epoch MILLISECONDS) | v1 |
| `fiveResetAt` | long? | `five_hour.ResetsAt` as epoch SECONDS | v1 |
| `sevenResetAt` | long? | epoch SECONDS | v1 |
| `account` | string? | account DisplayName | v2 |
| `modelKey` | string? | e.g. `seven_day_fable` (the bucket `ModelBuckets.Resolve(preferredBucket, snapshot)` picks = same as third dial) | v2 |
| `modelLabel` | string? | `ModelBuckets.Label(key)`: strip `seven_day_` prefix, upper-case invariant (`FABLE`) | v2 |
| `modelSevenDay` | double? | utilization of that bucket | v2 |
| `modelResetAt` | long? | epoch SECONDS | v2 |
| `availableModels` | string[]? | families from `ModelBuckets.Available(snapshot)`: known in order `seven_day_fable`, `seven_day_opus`, `seven_day_sonnet` then other model keys alphabetical ordinal (excluded: `seven_day_oauth_apps`, `seven_day_overage_included`); each with `seven_day_` stripped and lower-cased; empty list (never null) whenever a snapshot exists | v3 |
| `plan` | string? | `SubscriptionTier.LabelFor(profile, snapshot)`: "Team", "Team Premium", "Max", "Ultra", "Pro", "Free", "Enterprise", "Max or Ultra" ...; `null` (key present) when the three raw subscription fields were never fetched | v3 |
Order of keys in file = record parameter order above. All 12 keys always present.
- `Payload(...)` returns null (nothing is written) when snapshot is null or has NEITHER `five_hour` NOR `seven_day` (only one is enough; the other is null). Zeros are never invented.
- Example (README:172-178): `{ "fiveHour": 62, "sevenDay": 30, "atMs": 1787748230070, "fiveResetAt": 1787750400, "sevenResetAt": 1787860800, "account": "Personal", "modelKey": "seven_day_fable", "modelLabel": "FABLE", "modelSevenDay": 15, "modelResetAt": 1788278400, "availableModels": ["fable"], "plan": "Max" }`.
- README notes: older `plan` values were renamed ("Max 20x" -> "Ultra", "Max 5x" -> "Max", Premium seat "Team" -> "Team Premium"); a Team seat whose snapshot has a `seven_day_fable` bucket (any utilization) is labelled "Team Premium".

#### 6.3 Path selection `ExportPathFor(data, accountId, displayName)` (UsageExport.cs:129-147)
1. If the account (by id) has a non-blank `ExportPath` -> that path as is (any spelling; may be relative; directory is created by FileReplace.Write).
2. Else if `ExportDirectory` blank -> null (off).
3. Else compute a collision-free file name. `taken` (case-insensitive set of normalized full paths, `Path.GetFullPath`, falling back to the raw string on ArgumentException/NotSupported/PathTooLong) starts with every account's own ExportPath. Then iterate accounts WITHOUT their own ExportPath in settings order: `FreePath(dir, account.DisplayName, account.Id, taken)`; if this is the requested account return it, else add it to `taken`. The names come from the SETTINGS (not the caller's displayName) so hand edits cannot slip past; if the requested account id is not in the settings (removed mid-poll) it is processed last using the caller's displayName.
4. `FreePath`: `<dir>\<FileNameFor(displayName,id)>.widget.json` if free; else `<dir>\<name>-<FileNameFor(id,id)>.widget.json`; else `...-<n>.widget.json` for n=2,3,... until free (n invariant culture).
5. `FileNameFor(displayName, fallbackId)` (186-200): trim, `ToLowerInvariant`, each char outside `[a-z0-9_-]` becomes `-` (non-ASCII letters too), runs of `-` collapse (and leading dashes dropped), trailing dashes trimmed; empty result -> `fallbackId` (unsanitized id). Examples: "personal"->personal, " Work "->work, "work.shared@example"->work-shared-example, "--"->id, ""->id.
6. `WritesOver(exportPath, protectedPath)`: normalized full-path equality ordinal-ignore-case (so `C:\App\sub\..\settings.json` and `c:\app\SETTINGS.JSON` count). If true, App skips the export and logs `export skipped: ExportPath is settings.json` (every refresh).
- Behavioural contract: the plain name belongs to the first account in settings order that wants it; others get `-<id>`.

### 2A §7. HttpValidation (`CORE/HttpValidation.cs`)
`Validate(int statusCode, int? retryAfterSeconds = null)`: 2xx -> return; 401 or 403 -> throw `UsageException(UsageError.Unauthorized)`; 429 -> throw `UsageException(UsageError.RateLimited(retryAfterSeconds))`; anything else (incl. 1xx, 3xx, 404, 500) -> `UsageException(UsageError.Network("HTTP <code>"))`. Used by every API client (comment: 401 from usage and from status mean the same).
`UsageError` messages (UsageTypes.cs:97-105, not my scope but referenced): NoCredentials "No Claude.ai web session was found."; Unauthorized (default) "The Claude.ai session expired. Sign in again from the widget menu."; MalformedResponse "The server returned something unexpected."; RateLimited "The API is rate limited. The widget retries on its own."; Network = the message (e.g. "HTTP 500"). `DuplicateAccount(otherName)` = Unauthorized kind with message `This Claude.ai login is already added as "<name>". Sign in again with another login.`

### 2A §8. Logging / diagnostics

#### 8.1 `WidgetLog` (ASC/WidgetLog.cs) and `LogLine` (CORE/Diagnostics/LogLine.cs)
- File: `%LOCALAPPDATA%\ClaudeUsageWidget\widget.log` (Local, not Roaming; machine-local). `Directory.CreateDirectory` on every write.
- Always on; one line per event; NOTHING is written on healthy 5-minute refreshes.
- Line format: `<yyyy-MM-ddTHH:mm:ss.fff zzz local time with offset> <account> <event> <details>` e.g. `2026-09-04T11:39:45.123+03:00 low nav-failed path=/x status=Unknown http=0 attempt=1` (LogLine.cs:9, 19-24). Culture: `InvariantCulture`. Single spaces between the four fields; no quoting (so account names with spaces make fields ambiguous: inferred). Every one of account/event/details has `\r` and `\n` each replaced by a single space (per char: "\r\n" -> two spaces) (LogLine.cs:30). Lines end with `Environment.NewLine` (CRLF), file encoding UTF-8 WITHOUT BOM (WidgetLog.cs:29).
- Rotation (WidgetLog.cs:21, 77-85): before each append, if the file exists and `Length > 1 MB (1024*1024)` -> `File.Move(path, path + ".1", overwrite: true)` (exactly one old generation, `widget.log.1`); file may overshoot by one line. Rotation failure (e.g. a tailer holds the file) is caught separately (Debug only) and the append still happens.
- Thread safety: static `Lock Gate` around everything (accounts' sessions log concurrently).
- Never throws (all exceptions -> `Debug.WriteLine`).
- API: `Write(string account, string evt, string details)`. `"-"` is used as the account for global events.
- Event names written by code in my scope: `unhandled` (details: `dispatcher <Type>: <msg>`, `tray <Type>: <msg>`, `fire-and-forget <Type>: <msg>`, or UsageStore's own log messages with account label = DisplayName at creation), `settings` (`unreadable at startup, retrying: <Type>: <msg>`), `sign-in-requested` (`source=tray|panel|add-account`), `export` (`skipped: ExportPath is settings.json`), `update` (`check current=<v>`, `up-to-date latest=<v>`, `download <url>`, `install v<v> into <dir>`, `failed <Type>: <msg>`), `browser-open` (`site=update url=<url>`; tray also `site=tray url=...`). Other events (nav-failed, webview-reset, webview-idle, login window, ...) come from ClaudeWebSession etc. (other agent).
- README Diagnostics (README:204-214): documents path, rotation at 1 MB with `widget.log.1`; "To reset the settings, quit the widget first, then delete `%APPDATA%\ClaudeUsageWidget\settings.json`: a running widget keeps its last good settings and writes them back."
- Failures that go ONLY to Debug output (invisible in release, therefore no trace in widget.log): StartupAsync login-window open failure (App.xaml.cs:315); WebView2 session check failure in RefreshAccountAsync (389; only the tooltip changes); clear-browsing-data failure on account removal (1031); usage export write failure (UsageExportWriter.cs:26); log rotation/write failure (WidgetLog); Autostart toggle failure (TrayIcon.cs).

#### 8.2 `CoreInfo` (CORE/CoreInfo.cs)
`Version` (static, computed once) = `ParseVersion(Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)`. `ParseVersion`: null/empty -> `"unknown"`; cut at first `+` (drops build metadata like a commit hash); empty after cut -> `"unknown"`. In the xUnit host there is no entry-assembly informational version so `Version` is `"unknown"` or the host's value; test asserts only non-empty. Used in: tray item text `"Claude Usage Widget v<version> — GitHub"`, the updater User-Agent and comparison.

### 2A §9. (moved to Section 4)

### 2A §10. (moved to Section 5)

### 2A §11. (moved to Section 6)

### 2A §12. (moved to Section 7)

---

## Part 2B: WebView2 sessions, login, fetching, organization and plan, usage decoding, stores, 429 backoff, service status

### 2B §0. Big picture (data flow)

1. No HTTP client talks to claude.ai. Every claude.ai API call is a real **top-level browser navigation** inside a hidden WebView2 (Edge/Chromium engine), using the cookie jar the sign-in window filled. The response body is read back with `document.body.innerText` (ClaudeWebSession.cs:709). Reason (comments at :11-30, windows-port spec 2026-08-05 "Fetch как браузерная навигация"): Cloudflare/Turnstile sees a normal page load; User-Agent is deliberately NOT overridden (native Chromium fingerprint).
2. The only plain HTTP call is the public status page (`StatusApi`, HttpClient).
3. Per account: one `ClaudeWebSession` (WebView2 profile, login window, fetch webview) + one `UsageStore` (state machine, 429 ladder) wrapped in `AccountRuntime` (AccountRuntime.cs:9). One global `StatusStore`.
4. Scheduling is entirely in `App.xaml.cs` (a single 300 s `DispatcherTimer`, startup, resume-from-sleep, tray "Refresh now"); Core stores have no timers (UsageStore.cs:11-16, StatusStore.cs:7-11).

### 2B §1. WebView2 environment, user-data folder, per-account profiles

#### 1.1 Shared environment (src/App/Web/WebViewEnvironment.cs)
- ONE `CoreWebView2Environment` per process (`_shared`, :16, `SharedAsync()` :32-37). Created with `CoreWebView2Environment.CreateAsync(userDataFolder: UserDataFolder)` (:87): browserExecutableFolder=null (system Evergreen Runtime), options=null (:84-86).
- User data folder (UDF): `%LOCALAPPDATA%\ClaudeUsageWidget\profiles\default` (:22-24). `Directory.CreateDirectory(UDF)` before creation (:83). Despite the name "default" this is the UDF, not a WebView2 profile.
- Failure caching rule: if the cached task is Faulted or Canceled it is dropped and recreated on next call (:34). Purpose: user may install the WebView2 Runtime / free a locked folder while the app runs; no restart needed (:26-28, ClaudeWebSession.cs:748-771). `ClaudeWebSession` additionally has its own per-session copy of that cache (`_environmentTask`, ClaudeWebSession.cs:69, 746-778) that resets the same way.
- All callers are on the UI thread, so no lock (:30-31, ClaudeWebSession.cs:758-767).

#### 1.2 Profiles
- Accounts are separated by WebView2 **profile name** inside the single UDF, NOT by separate UDFs (reason: one browser-process collection; WebView2 docs warn about many UDFs; WebViewEnvironment.cs:6-13, multi-account spec decision 1).
- Disk layout (measured by the author 2026-08-26, ClaudeWebSession.cs:37-44, spec lines 58-66): named profile -> `<UDF>\EBWebView\WV2Profile_<name>`; profile created without options -> `<UDF>\EBWebView\Default`. No `ProfileName` can address the latter.
- **Implicit-default-profile rule**: the legacy account (id `"default"`, `SettingsMigration.LegacyAccountId`, Core/Settings/SettingsMigration.cs:26) uses `profileName = null` and is created with NO controller options so the pre-multi-account sign-in survives upgrade (App.xaml.cs:849-856). Every other account uses profile name = its `AccountProfile.Id` (a `Guid.NewGuid().ToString()` set in App.xaml.cs:978). Verified by author: `ProfileName="default"` lost the sign-in, `null` kept it (spec :63-66).
- Profile name charset (because Id doubles as ProfileName; WidgetSettings.cs:6-12, spec :48-50): ASCII letters/digits plus `# @ $ ( ) + - _ ~ .` and space, <= 64 chars, not ending in `.` or space, case-insensitive (becomes a directory). GUID with dashes satisfies it.
- `ControllerOptionsFor(env, profileName)` (WebViewEnvironment.cs:47-55): null -> null options; else `environment.CreateCoreWebView2ControllerOptions()` with `ProfileName = profileName`.
- `CreateControllerAsync(env, parentHwnd, profileName, label)` (:59-68): picks `CreateCoreWebView2ControllerAsync(hwnd)` (no options) or `(hwnd, options)`, then `VerifyProfile`.
- `VerifyProfile` (:72-79): no-op for null; else compares `core.Profile.ProfileName` to the requested name, **ordinal-ignore-case**; mismatch throws `InvalidOperationException("WebView2 gave profile '{actual}' for account '{label}', expected '{name}'.")`.
- **INVARIANT** (WebViewEnvironment.cs:43-46, spec :90-93): login controller and fetch controller of one account MUST use the same profile name, else sign-in "succeeds" and dials stay empty forever with no error. Both build their options through the same helper.
- The login window uses the WPF `WebView2` control with `EnsureCoreWebView2Async(env, options)` (LoginWindow.cs:201-202); the fetch webview uses a raw controller on a hidden host HWND (below).

#### 1.3 WebView2 Runtime missing / environment failure
There is NO dedicated "runtime not installed" message, installer, or detection code. Observed handling:
- `CoreWebView2Environment.CreateAsync` throws -> task faulted -> reset on next call (above).
- In `RefreshAccountAsync` (App.xaml.cs:363-394) the cookie check is in try/catch. On exception: `_hasSessionCookie[id] = true` (deliberately NOT false, so the store does not claim "not signed in", :373-387), `_webViewFailing[id] = true`, `Debug.WriteLine`. Then `store.LoadAsync()` runs; `_fetch` = `FetchUsageAsync` hits the same exception inside its own `HasSessionCookieAsync` -> `UsageStore.PerformLoadAsync` outer `catch (Exception)` -> `Failed(UsageError.Network(ex.Message))` (UsageStore.cs:223-226). So the panel status line shows the raw exception message of the WebView2 SDK (text itself not in repo; inferred to be the SDK's own message).
- Tray tooltip for the TRAY account while `_webViewFailing[id]` is true: `"Claude Usage Widget — WebView2 error, see widget for details"` (App.xaml.cs:786). Cleared (set false) after the next successful cookie check (:369).
- Startup (App.xaml.cs:280-320): `HasSessionCookieAsync` exception is swallowed (hasSession=false), then it still tries `OpenLoginWindowAsync`; failure there is only `Debug.WriteLine` (:313-316) plus the `login-window create-failed` log line (ClaudeWebSession.cs:400). Then `RefreshAllAsync()` runs regardless (reports the same error honestly).
- Login window init failures (browser process failed to start, profile folder locked, etc.) are caught and only `Debug.WriteLine`d (LoginWindow.cs:111-118): the user sees an empty window (inferred).
- README: Windows 11 has the Runtime preinstalled; Windows 10 users must install Evergreen Runtime first (README.md:268-270). Nothing in code checks it.

### 2B §2. The hidden fetch WebView2 (ClaudeWebSession.cs)

#### 2.1 Creation (`CreateFetchWebViewAsync`, :792-912)
- `_fetchWebViewTask` caches the task; Faulted/Canceled reset (:786-789).
- Hidden host = a regular WPF `Window` never `Show()`n: `WindowStyle=None, ShowInTaskbar=false, ShowActivated=false, AllowsTransparency=false, Visibility=Hidden, Width=0, Height=0, Left=-32000, Top=-32000` (:820-831). HWND forced with `WindowInteropHelper.EnsureHandle()` (:833). Then `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` OR-ed into the ex-style via `GetWindowLongPtr/SetWindowLongPtr` (:835-837). Then `controller.IsVisible = false` (:845). History: a bare `HwndSource` ended up as a visible black "ClaudeWebSessionFetchHost" window because the WebView2 controller sets WS_VISIBLE on its parent (:796-819); commit b1853dd. Four insurance layers: off-screen, zero size, tool/noactivate styles, `IsVisible=false`.
- Controller created via `WebViewEnvironment.CreateControllerAsync(env, hwnd, _profileName, _accountId)` (:841-843) (note: label argument is the account id here, only used in the mismatch exception text).
- **Strong reference to controller in `_fetchController`** (:91, 849): the .NET `CoreWebView2Controller` wrapper closes the native controller on GC finalization; afterwards every call throws "CoreWebView2 members cannot be accessed after the WebView2 control is disposed" (:83-90). (Not an issue in Rust if the controller COM pointer is owned, but the lifetime requirement is real.)
- Event hooks registered at creation (:857-910):
  - `ProcessFailed`: ignored if `_fetchController != controller` (stale engine, :863). Logs `webview-process-failed kind=<ProcessFailedKind>` for EVERY kind (:868). Calls `ResetFetchWebView()` only for `BrowserProcessExited`, `RenderProcessExited`, `RenderProcessUnresponsive` (:870-875).
  - `DownloadStarting`: logs `nav-download uri=.. mime=.. disposition=..`, sets `args.Cancel = true; args.Handled = true` (:886-895). A JSON served as attachment turns a navigation into a download; it is cancelled so stray .json files are not dropped into Downloads.
  - `WebResourceResponseReceived`: only for request URIs containing the ordinal substring `/api/organizations`; if NOT (status==200 AND content-type starts with `application/json` case-insensitive) logs `resource path=<AbsolutePath> http=<status> type=<content-type|-> disposition=<content-disposition|->` (:896-906).
  - `NavigationStarting`: if `args.IsRedirected` logs `nav-redirect to=<uri>` (:907-910).
- These diagnostics were added for `WebErrorStatus.Unknown` with http=0 seen twice on 2026-09-04 (:878-884).

#### 2.2 Idle lease and closing (Core/Web/FetchIdleLease.cs; App :78-80, 497-563, 614-639)
Goal: four accounts held ~235 MB private memory each when webviews stayed open (FetchIdleLease.cs:5-10); close them between polls. Closing the last controller makes the browser process exit (~0.3 s later) and the next controller starts a new one (ClaudeWebSession.cs:619-622).
- `FetchIdleLease` (Core): `Delay = 15 s` (:27). `_users` counter + `_generation` counter. `Enter()`: users++, generation++ (:34-38). `Exit()`: throws `InvalidOperationException("Exit without a matching Enter.")` if users==0; users--; returns `_generation` (token) iff users==0 else null (:42-48). `ShouldRelease(token)` = `users == 0 && token == generation` (:51). `InUse` = users>0.
- Every use of the webview goes through `RunWithFetchWebViewAsync` (:497-523): `_idle.Enter()`; loop attempt 0..1: `EnsureFetchWebViewAsync()`, `SetMemoryTarget(webView, Normal)`, run the action; `finally ExitIdleLease()`.
- `FetchPageAsync` ALSO holds an outer lease across both attempts (:446, 483) so the 3 s retry pause does not start the idle countdown (:444-445).
- `ExitIdleLease` (:532-563): if `_idle.Exit()` returns a token: (a) if `_fetchWebViewTask` completed successfully, set `MemoryUsageTargetLevel = Low` (exceptions swallowed, logged `webview-idle low-memory-skipped dead|<ExceptionType>`, :549-550 because it runs inside finally blocks and an escape would skip the gate release and freeze the account); (b) fire `Task.Delay(15 s).ContinueWith(..., FromCurrentSynchronizationContext)` and when it fires on the UI thread `if (_idle.ShouldRelease(token)) CloseFetchWebView()`.
- `SetMemoryTarget` (:569-580): sets only if different; swallows `NotImplementedException` (older runtime lacking ICoreWebView2_19). Dead-webview exceptions propagate (the retry recreates).
- `CloseFetchWebView` (:623-639): `_fetchWebViewTask = null`; `_fetchController?.Close()` (all exceptions swallowed); `_fetchController = null`; `_hiddenHost?.Close(); _hiddenHost = null`. No log for the idle path.
- Consequence: a normal poll = create hidden window + controller (starting the browser process if it exited), cookie check, [orgs], usage, then 15 s later everything is closed. This happens every 5 minutes per account (and even for rate-limited/paused accounts, because the cookie check still runs, see 6.3).

#### 2.3 Death / crash recovery (`RunWithFetchWebViewAsync`, `IsWebViewDead`, `ResetFetchWebView`)
- `IsWebViewDead(ex)` (:594-601): true for `ObjectDisposedException`; `COMException` with HRESULT `0x8007139F` (ERROR_INVALID_STATE) or `0x80010108` (RPC_E_DISCONNECTED); `InvalidOperationException` whose `InnerException` is a `COMException` with one of those HRESULTs (the .NET wrapper rethrows 0x8007139F that way; read off IL of 1.0.4258.31, :585-592). Deliberately not matched by message and not "any InvalidOperationException".
- Retry rule (:513-516): `catch when (attempt == 0 && IsWebViewDead(ex))` -> `ResetFetchWebView()` (logs `webview-reset reason=fetch-webview-dead`, then CloseFetchWebView) and loop once more with a freshly created webview. The action is re-run from scratch. A second death on attempt 1 propagates (-> `Failed(Network(message))` in the store).
- Within `NavigateAndReadAsync` a dead-webview exception is rethrown RAW (not wrapped in `UsageException.Network`) so the wrapper can see it (:718-727).
- Proactive reset on `ProcessFailed` (see 2.1). Origin: commit 1040156 "keep WebView2 controller alive and recover from engine death" (death after sleep).
- A reset while a navigation is in flight: nothing explicitly completes the pending tcs; it would end via 30 s timeout or an exception (inferred).
- If webview creation throws after `_hiddenHost` was assigned, that host window is not closed (it is overwritten next time) (inferred, minor leak).

#### 2.4 Serialization
- `_fetchGate = SemaphoreSlim(1,1)` (:99) serializes navigations on the shared webview; held across both attempts of `FetchPageAsync` (:439-443). Independent of UsageStore's coalescing ("independent guarantee at the level of the webview", :93-98).
- The cancellation token passed in is always `CancellationToken.None` in practice (UsageStore.cs:205), so cancellation paths are effectively dead at runtime.

### 2B §3. How a page is fetched (`FetchPageAsync` / `NavigateAndReadAsync`)

#### 3.1 Requests made (exact)
| Purpose | Method | URL | Source |
|---|---|---|---|
| Organization list (pick + subscription fields) | GET (WebView2 `Navigate`) | `https://claude.ai/api/organizations` | ClaudeWebSession.cs:223, 259 |
| Usage | GET | `https://claude.ai/api/organizations/{organizationId}/usage` (id interpolated raw, no URL-encoding) | :293-294 |
| Sign-in page | GET | `https://claude.ai/login` | LoginWindow.cs:234 |
| Cookie read | n/a (CookieManager) | `GetCookiesAsync("https://claude.ai")` | ClaudeWebSession.cs:210, LoginWindow.cs:252 |
| Service status | GET (HttpClient) | `https://status.claude.com/api/v2/summary.json` | StatusApi.cs:8 |

No custom request headers, no UA override, no auth header, no body: the browser navigation sends its native headers and the profile's cookies (the one that matters is `sessionKey`; anything else in the jar, e.g. Cloudflare cookies, is sent automatically by Chromium (inferred)). The endpoint `api.anthropic.com/api/oauth/usage` is deliberately NOT used (sticky Cloudflare rate limit renewed by each request; README:149-153, StatusStore/UsageDecoder doc comments still call the usage body "/api/oauth/usage" but the live URL is the claude.ai one).
Status page request: `Accept: application/json` header only (StatusApi.cs:25); shared static `HttpClient` with default settings (default 100 s timeout, inferred); no User-Agent set.

#### 3.2 `NavigateAndReadAsync` (:642-740) step by step
1. TaskCompletionSource (`RunContinuationsAsynchronously`). A linked CTS with **30 s timeout** (:646-649; "same as original `URLRequest(timeoutInterval: 30)`"). On timeout: if caller token cancelled -> `TrySetCanceled`, else `TrySetException(UsageException(Network("Claude.ai request timed out.")))` (:650-654). Timeouts are NOT retried (not a NavigationFailedException). The navigation is not explicitly stopped on timeout (inferred).
2. Subscribe `NavigationCompleted`; `webView.Navigate(url)`; await tcs; always unsubscribe in finally.
3. In the handler, **HTTP status is evaluated BEFORE `IsSuccess`** (:660-669). Reason: a 4xx top-level navigation lands in Chromium's error page; WebView2 then reports `IsSuccess=false` + `WebErrorStatus.Unknown` while `HttpStatusCode` still carries the code (measured 2026-09-04: expired session answered 403 application/json, the old order turned "sign in again" into "navigation failed: Unknown" for days). `HttpStatusCode == 0` means no HTTP exchange happened (transport failure).
   - Any non-zero, non-2xx status: log `nav-http path=<AbsolutePath> http=<code> success=<IsSuccess> status=<WebErrorStatus>` first (:670-677).
   - 401 or 403 -> `UsageException(UsageError.Unauthorized)` (:681-685).
   - 429 -> `UsageException(UsageError.RateLimited(null))` (:686-690). Retry-After is NEVER available (navigation API exposes no headers); only `null` is ever passed in the web path.
   - other non-2xx (and non-0) -> `UsageException(Network($"Claude.ai returned HTTP {code}."))` (:691-696). Not retried.
   - `!IsSuccess` (code is 0 or 2xx) -> `NavigationFailedException(WebErrorStatus, HttpStatusCode)` (:698-707) which derives from `UsageException` and carries `UsageError.Network($"Claude.ai navigation failed: {status}.")` (:922-936; `{status}` is the .NET enum name, e.g. `Unknown`, `ConnectionReset`, `HostNameNotResolved`).
   - success -> run the injected JS (below), then unwrap the JSON string with `JsonSerializer.Deserialize<string>(raw)`; null -> `MalformedResponse`, else result is the page text.
   - any other exception in the handler: dead-webview -> rethrow raw; otherwise `UsageException(Network(ex.Message))` (:718-727).

#### 3.3 The only JavaScript injected (verbatim, ClaudeWebSession.cs:709-710)
```
await webView.ExecuteScriptAsync(
    "document.body.innerText || document.body.textContent || ''")
```
i.e. the JS source is exactly: `document.body.innerText || document.body.textContent || ''`. `ExecuteScriptAsync` returns the value as a JSON-encoded string (a JS string comes back as a quoted/escaped JSON string), so the C# deserializes it to a plain string (:711-714). No other script is injected anywhere in scope (the login window injects nothing).

#### 3.4 `FetchPageAsync` retry (:435-490)
- Acquire `_fetchGate` (await, token), `_idle.Enter()`.
- Loop `attempt = 1..2`. Each attempt: `RunWithFetchWebViewAsync(webView => NavigateAndReadAsync(webView, url, ct))`. On success with `attempt > 1`: log `retry-ok path=<AbsolutePath> attempt=<n>`.
- Catch ONLY `NavigationFailedException`: log `nav-failed path=<path> status=<WebErrorStatus> http=<code> attempt=<n>`; rethrow if `attempt >= 2` or `!NavigationRetryPolicy.IsTransient(ex.Status.ToString())`; else `Task.Delay(3 s)` and retry once (gate + lease still held).
- No other exception type is retried by this loop (the dead-webview retry is separate, inside RunWithFetchWebViewAsync). No retry on 401/403/429/other HTTP codes/timeouts.
- finally: `ExitIdleLease()` then `_fetchGate.Release()` (nested finally so a lease exception cannot keep the gate shut).
- `NavigationRetryPolicy` (Core/Web/NavigationRetryPolicy.cs): `RetryDelay = 3 s` (:13). Transient set, exact ordinal string match on the enum name: `Unknown`, `ConnectionAborted`, `ConnectionReset`, `OperationCanceled`, `Timeout` (:25-32). Everything else (DNS `HostNameNotResolved`, certificate errors, proxy, etc.) is not retried. `Unknown` is on the list because that is what claude.ai's transient failures report (observed 2026-09-04). Core compares strings because it has no WebView2 reference (:5-8).
- Worst-case time per page: 2 x 30 s + 3 s.

### 2B §4. Session cookie and login flow

#### 4.1 Session cookie definition (`SessionCookie.IsPresent`, ClaudeWebSession.cs:943-950)
Any cookie in `CookieManager.GetCookiesAsync("https://claude.ai")` with `Name == "sessionKey"` (ordinal), `Domain` ending with `claude.ai` (ordinal-ignore-case), and non-empty `Value`. One shared predicate for both the poll check (`HasSessionCookieAsync`, :207-212, via the fetch webview) and the login window's success detection (LoginWindow.cs:253).

#### 4.2 Login window (src/App/Web/LoginWindow.cs)
- Plain visible WPF `Window` hosting a WPF `WebView2` control; created by `ClaudeWebSession.CreateLoginWindowAsync` (:385-412) with the shared environment, the account's profile name, and `CurrentLabel` (live DisplayName from settings, falling back to the construction-time label; :57-63, 391).
- Title: `Sign in to Claude.ai — account "<label>"` (em dash; :70). Size 1000x720, `CenterScreen` (:71-73).
- Layout (rows Auto, Auto, *) (:139-175): (1) hint TextBlock, wrapped, margin 8,8,8,4, opacity 0.75, text: `This window signs in the widget account "<label>". Signing in with a magic link? Request it on this page, then paste the link from the e-mail here — opening it in your normal browser signs that browser in, not this one.` (:156-158); (2) a docked bar with a TextBox (paste box) and an "Open" button (content `Open`, padding 12,2,12,2, margin 6,0,0,0); (3) the WebView2.
- Paste box purpose: magic-link e-mails open the DEFAULT browser and the link is usually single-use, so it must be redeemed in this cookie jar (:131-138). Enter key or Open click -> `NavigateToPastedLink` (:181-194): trim; empty -> ignore; must parse as absolute URI with scheme `https` else tooltip `That is not an https link.` and no navigation; otherwise tooltip cleared and `CoreWebView2.Navigate(uri.ToString())`. **https only, NO host allow-list** (tracking redirectors and login.microsoftonline.com federated accounts must pass).
- Initialization (`Loaded` -> `InitializeAsync`, :104-119, 196-235): 
  1. `EnsureCoreWebView2Async(env, ControllerOptionsFor(env, profileName))`; if `_closed` return; `VerifyProfile`.
  2. **`core.Profile.ClearBrowsingDataAsync()` on EVERY window open** (:212): wipes the whole profile (cookies, cache, etc.) before each sign-in, because a rejected Turnstile challenge survives window recreation and repeats the loop otherwise (:207-211). Consequence: opening the login window for an already-signed-in account signs it out (inferred from the call; only reachable via tray "Sign in", panel "Sign in" button, or add-account).
  3. Re-check `_closed` after each await (:45-53, 203, 213): if the user closed the window during the awaits, do not start the timer or navigate.
  4. Subscribe `NavigationCompleted` -> cookie check (exceptions swallowed, Debug only); start the 2 s `DispatcherTimer`; `core.Navigate("https://claude.ai/login")`.
- **Login success detection = polling**, no cookie-change event exists in WebView2: after every `NavigationCompleted` and every **2 s** timer tick (:82-99), `CheckForSessionCookieAsync` (:237-269): return if already signed in or `_closed` or `CoreWebView2` null; read cookies for `https://claude.ai`; if `SessionCookie.IsPresent`: `_signedIn = true`, stop timer, **raise `SignedIn` FIRST, then `Close()`** (order matters: `Closed` handler in the session unsubscribes `SignedIn`; closing first would lose the event and the dials would wait for the next 300 s tick; :258-268).
- Closed handler (:120-128): `_closed=true`, stop timer, log `login-window closed signedIn=<True|False>`. Constructor logs `login-window open` (:68). "Gave up" = closed with `signedIn=False`; nothing else happens (no state change).
- All async-void handlers (timer tick, Loaded, NavigationCompleted) catch everything and `Debug.WriteLine` (an unhandled exception would kill the process).

#### 4.3 Session-side window management (`ClaudeWebSession.OpenLoginWindowAsync`, :349-383)
- At most one login window per account. If `_loginWindow` exists: log `login-window reuse existing`, `Show()`, `Activate()` (Activate may not steal foreground due to Windows' foreground lock), return it.
- Else if `_openLoginWindowTask` is still running (`IsCompleted == false`): log `login-window still opening`, return the pending task. (A bug fixed 2026-09-04: `??=` kept a COMPLETED task forever, so 8 clicks produced 8 `still opening status=RanToCompletion` lines and no window.)
- Else `_openLoginWindowTask = CreateLoginWindowAsync()`; its `finally` nulls the field (success: later calls use the `_loginWindow` branch; failure: next call retries). Failure logs `login-window create-failed <ExceptionType>: <message>` and rethrows.
- `SignedIn` event (:114, 414) is forwarded to `App.OnSignedIn(accountId)`.

#### 4.4 Triggers for opening a login window (App.xaml.cs)
- Startup, TRAY account only, if `HasSessionCookieAsync` is false or threw (:280-317). Other unsigned accounts are not prompted (four windows would be "an ambush", :282-285).
- Tray "Sign in to Claude.ai…" -> tray account; logs `sign-in-requested source=tray` (:131-135).
- Panel "Sign in" button (shown only in the NoCredentials notice and the duplicate-account notice) -> tray account; `source=panel` (:231-238).
- "Add account…" (tray): creates `AccountProfile(Guid, "New account", null, null, 0)` (max `AccountLimits.Max = 4` accounts; Core/Settings/SettingsMigration.cs:13), saves settings, creates runtime, rebuilds layout, logs `source=add-account`, opens its login window (:970-999). Note: no immediate usage refresh here; it happens from `OnSignedIn` or the next tick (inferred).
- `OnSignedIn(accountId)` (:440-453): `ClearCachedOrganization()` (the login may have been a different Claude account) then `RefreshAccountAsync(account)` for THAT account only.

### 2B §5. Organization selection, subscription fields, plan label

#### 5.1 `OrganizationPicker.Pick(body)` (Core/Web/OrganizationPicker.cs:31-62)
- Parse with `JsonDocument`; parse error -> null; root not an array -> null.
- Iterate array elements that are objects. Candidate = has `capabilities` array containing a JSON string equal to `"chat"` (:157-167). First candidate remembered; first candidate with string property `raven_type == "team"` remembered. Selected = team candidate, else first chat-capable one, else null.
- Id = string property `uuid`, falling back to string property `id` (`??`, so a present-but-empty `uuid` string does NOT fall back; it then maps to null because `string.IsNullOrEmpty` -> null, :169-173). Non-string values are treated as absent.
- Ports `fetchOrganizationID` of the Swift original (`ClaudeWebSession.swift:58-74`).
- In `FetchUsageAsync`: null/empty result -> `UsageException(MalformedResponse)` (:225-226).

#### 5.2 `OrganizationPicker.Fields(body, pickedId)` (:107-125) -> `OrganizationFields(Capabilities, RateLimitTier, RavenType)`
- Never throws. Non-JSON / non-array / org not found -> `OrganizationFields.Unknown` (all three null).
- Org walk `Picked` (:130-140): first OBJECT element whose `Id` equals `pickedId` (NOT required to be chat-capable); with null pickedId the first object.
- `Capabilities`: `capabilities` array filtered to string entries only (non-strings skipped); missing/non-array -> **empty list, never null** for a found org. Sentinel semantic: null = never asked / org not found; empty = asked and told nothing.
- `RateLimitTier` = string property `rate_limit_tier` (non-string -> null); `RavenType` = string property `raven_type`.

#### 5.3 `OrganizationPicker.Describe(body, pickedId)` (:71-98) log string
`not-an-array` / `picked-organization-not-found` / `not-json`, else `keys=<comma-joined property names of the picked org> <space-joined tierish props as Name=<raw JSON text>>`, where tierish = lowercase name contains `tier`, `plan`, `billing`, `subscription`, or equals `raven_type` or `capabilities`. Format is `$"keys={names} {tierish}"` (note the always-present space after keys even when tierish is empty).

#### 5.4 When the organizations endpoint is fetched (`FetchUsageAsync`, ClaudeWebSession.cs:215-305)
Sequence per poll:
1. `HasSessionCookieAsync()`; false -> `UsageException(NoCredentials)` (:217-218).
2. `organizationId = settings.Account(id)?.OrganizationId`.
3. **If empty**: fetch `/api/organizations`; `Pick`; null -> `MalformedResponse`; then **duplicate check**: `settings.AccountWithOrganization(orgId, thisAccountId)` (another account whose stored OrganizationId equals it, `WidgetSettings.cs:117-118`). If found: log `organization duplicate-of <otherDisplayName>`, throw `UsageException(UsageError.DuplicateAccount(otherName))` and do NOT save the id (a stored duplicate would be polled forever and show another account's numbers; left unpicked each poll re-checks so signing in with the right login clears it; :228-236). Else `SaveOrganizationId` (Load -> `WithAccount` -> Save; IOException/UnauthorizedAccessException swallowed with a `settings` log line) and `RecordSubscription(body, orgId)` (reuses the same body, no second request).
4. **Else if** `!_subscriptionFetchFailed && profile exists && SubscriptionRefresh.ShouldRefresh(profile.SubscriptionFetchedAt, now)`: a second org-list fetch ("cheap, changes about never; deliberately not the usage endpoint because that is where the 429 budget is spent", :247-256), then `RecordSubscription(body, orgId)`. Failure handling:
   - `UsageException` (any kind incl. 401/429/timeout/malformed): swallowed, `_subscriptionFetchFailed = true`, log `organization refresh-failed <Kind>`. Rationale: the plan line is decoration; a blip or a 429 here must not fail a poll (and a 429 would pause the whole account).
   - Any other non-cancellation exception (dead/disposed webview): latch set and rethrown (the poll fails).
   - `OperationCanceledException`: not latched, propagates.
   - No profile (account removed mid-poll): branch skipped (:240-244).
   - **`_subscriptionFetchFailed` is a per-process-run latch, never reset** (set at :272, 286; read :244). After one failure no more refreshes this run for that account.
5. Fetch `/api/organizations/{id}/usage`, decode with `UsageDecoder.Snapshot`. If it throws `UsageException` with kind `Unauthorized` (401 OR 403; NOT the DuplicateAccount error which is thrown earlier), call `ClearCachedOrganization()` (`SaveOrganizationId(null)`; subscription fields intentionally stay, :336-343) and rethrow.

`RecordSubscription` (:313-334): `fields = Fields(body, orgId)`. If `LearnedNothing(fields)`: log `organization refresh-unparsed <Describe>` and `SaveSubscriptionFields(fields)` anyway (merges = keeps stored fields, advances only the stamp). Else log `organization <Describe> tier=<SubscriptionTier.Label(...)>` and save.
`SaveSubscriptionFields` (:165-182): ONE settings save inside `WithAccount`: `merged = SubscriptionRefresh.Merge(existing-from-the-record-as-it-is-now, fetched)`, then writes Capabilities, RateLimitTier, RavenType and `SubscriptionFetchedAt = DateTimeOffset.Now`. The stamp is written even when the body told nothing (records the ASK; bounds retries to one per hour). Raw fields stored, never a label, so a label-mapping fix in a new build applies without re-fetching.

#### 5.5 `SubscriptionRefresh` (Core/Web/SubscriptionRefresh.cs)
- `Interval = 1 hour` (:17).
- `ShouldRefresh(last, now)` = `last == null || now - last >= 1h || now < last` (:24-25). Inclusive at exactly 1 h; future stamp (clock moved back / hand edit) also refreshes.
- `LearnedNothing(f)` = all three of Capabilities/RateLimitTier/RavenType are null (:37-38). An empty capability list is NOT "nothing".
- `Merge(existing, fetched)` = `LearnedNothing(fetched) ? existing : fetched` (:49-50). Real answers always overwrite wholesale (an account that left Team must stop being Team); empty answers never blank a good plan.

#### 5.6 `SubscriptionTier` (Core/Web/SubscriptionTier.cs) - plan label rules (exact)
Inputs: capabilities list (membership, ordinal case-sensitive, never by index), `rate_limit_tier` (trimmed), `raven_type`.
Helpers: `Strip(t)` removes a leading `default_` (ordinal) (:165-166). `Named(plan, tier, family)` (:150-161): `rest = Strip(tier)`; if `rest == family` -> plan; if not `rest.StartsWith(family + "_")` -> plan; else words = text after `family_` split on `_` (empties removed), each word with first char upper-cased (invariant) and the rest unchanged, joined by space -> `"<plan> <words>"`; if no words -> plan.
`Label(caps, tier, raven)` (:103-127), in order:
1. `ravenType` non-empty OR caps contains `raven` -> `Named("Team", tier, "raven")` (e.g. `default_raven` -> `Team`, `default_raven_premium` -> `Team Premium`).
2. caps contains `claude_max` -> `MaxPlan(tier)`: `Strip(tier)` == `claude_max_20x` -> `Ultra`; == `claude_max_5x` -> `Max`; else `Named("Max", tier, "claude_max")` if that is not bare `Max` (e.g. `Max 40x`), else `Max or Ultra` (:138-143). (Plans were renamed: Max 5x is sold as "Max", Max 20x as "Ultra"; tier strings kept old names; bare "Max" is never a fallback.)
3. caps contains `claude_pro` -> `Pro`.
4. tier contains `enterprise` (ignore case) OR any capability contains `enterprise` (ignore case) -> `Enterprise`.
5. `Strip(tier) == "claude_ai"` -> `Free` (that tier is shared by free and pro; reaching here means no paid cap).
6. tier empty -> `Free`; else `Prettify`: `Strip(tier)` with `_` -> space, first char upper-cased (empty -> `Free`) (e.g. `default_claude_ultra` -> `Claude ultra`, `some_new_plan` -> `Some new plan`).
`LabelOrNull(caps, tier, raven)` = null when ALL THREE are null (never asked), else `Label` (AND guard, :92-96). `Label(null,"","")` = `Free`.
`LabelFor(profile)` / `LabelFor(profile, usage)` (:36-52): null profile -> null; else `LabelOrNull(profile fields)`; then **if label == "Team" exactly and `usage["seven_day_fable"]` exists (any utilization) -> `Team Premium`** (the seat type is not in the org body; a Standard seat gets Fable only via credits). Other model buckets (`seven_day_opus`, `seven_day_sonnet`) are not witnesses; Ultra with a Fable bucket stays `Ultra`.
`HasFiveHourLimit(profile)` (:66-72): null profile -> true; `caps = Capabilities ?? []`; if RavenType non-empty or caps has `raven` -> true; else `!caps.Contains("claude_max") || Strip(tier.Trim()) == "claude_max_5x"`. So false for Ultra (`claude_max_20x`), unrecognised Max tiers (`claude_max_40x`, `claude_max`, null tier); true for everything else and never-fetched accounts. Used by AccountDials/DialModel to show "no limit" vs missing data (AccountDials.cs:15).
`SubscriptionTier.Ultra = "Ultra"` const. Where used: panel plan line + tray tooltip via `AccountDials.For` (AccountDials.cs:14-20), usage export `plan` (App.xaml.cs:429), log line (ClaudeWebSession.cs:329).
Sample organization bodies (author's own accounts, 2026-09-04 / 2026-11-01), documented in SubscriptionTier.cs:6-9, tests: `capabilities=["claude_max","chat"] rate_limit_tier="default_claude_max_20x"`; `["raven","chat"] default_raven raven_type="team"`; `["chat","claude_max"] default_claude_max_5x`. Pro/Free/Enterprise are research-derived (helixml/helix subscription_profile.go, lugia19/Claude-Usage-Extension claude-api.js) and unverified live.

### 2B §6. Usage decoding and math (Core/Usage)

#### 6.1 `UsageDecoder.Snapshot(json)` (Core/Usage/UsageDecoder.cs:14-52)
Input: raw page text from `/api/organizations/{id}/usage`.
1. `JsonDocument.Parse`; failure -> `UsageException(MalformedResponse)` (a Cloudflare HTML "Just a moment" body ends here, test `RejectsGarbage`). Root must be an object else `MalformedResponse`.
2. For EVERY root property whose value is an object that has a property `utilization` of JSON kind Number (booleans excluded by kind): bucket keyed by the property name = `UsageBucket(Utilization = utilization.GetDouble(), ResetsAt = ResetDate(resets_at or absent))`. Other members (null, strings like `"currency":"EUR"`, arrays, objects without numeric utilization) are silently ignored. Key names are whatever the server sends (observed: `five_hour`, `seven_day`, `seven_day_opus`, `seven_day_sonnet`, `seven_day_fable`, `seven_day_oauth_apps`, `seven_day_overage_included`, `extra_usage`); unknown keys survive.
3. `FoldScopedLimits` (:88-115): if root has `limits` array: for each element that is an object with `kind == "weekly_scoped"` (string), `percent` Number, `scope` object, `scope.model` object, `scope.model.display_name` string: `name = Slug(display_name)`; skip if empty; `key = "seven_day_" + name`; **skip if a bucket with that key already exists** (a real top-level bucket wins; also first limits entry wins); else add `UsageBucket(percent, ResetDate(entry.resets_at))`. Malformed entries skipped without failing. (Since the Fable launch the server leaves flat `seven_day_<model>` null and reports scoped models only in `limits`.) Entries with other kinds (e.g. `five_hour`) are ignored (no `seven_day_five_hour`).
4. `buckets.Count == 0` -> `MalformedResponse`; else `new UsageSnapshot(buckets)` (SourceUpdatedAt null for web data).
`Slug` (:118-135): invariant-lowercase; keep chars where `char.IsLetterOrDigit` (Unicode letters/digits kept); each maximal run of other chars is a separator; non-empty runs joined by `_`. `"Claude Opus 4.5"` -> `claude_opus_4_5`; `"!!!"` -> `""` (skipped).
`ResetDate(value)` (:56-77): JSON string -> `DateTimeOffset.TryParse(text, InvariantCulture, AssumeUniversal | AdjustToUniversal)` (offset-less text = UTC; any .NET-parseable format; failure -> null); JSON number -> if `> 0`, `DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000))` (i.e. the number is epoch SECONDS, fractions allowed); anything else (null, bool, absent, <= 0) -> null. "The exact wire format could not be observed, so both handled" (:54-55). Observed in tests: ISO with `Z`, with `.123Z`. A ms-epoch number would produce a far-future date and could throw `ArgumentOutOfRangeException` if beyond year 9999, which the store converts to `Failed(Network(message))` (inferred).
No clamping at decode: utilization stored as given (0..100 expected; may exceed 100).

#### 6.2 `UsageMath` (Core/Usage/UsageMath.cs)
- `RemainingText(resetsAt?, now)`: null if no reset; `seconds = (int)Math.Floor((resets - now).TotalSeconds)`; `<= 0` -> null. `days = s/86400`, `hours = (s%86400)/3600`, `minutes = (s%3600)/60`. Format: `days>0` -> `"{d}d {h}h"` (hours shown even if 0); `hours>0` -> `"{h}h {m}m"` (minutes shown even if 0, e.g. `1h 0m`); `minutes>0` -> `"{m}m"`; else `"{s}s"`. Tests: 59 -> `59s`, 600 -> `10m`, 3600 -> `1h 0m`, 90000 -> `1d 1h`, past/missing -> null. Also reused for "updated X ago" (age text).
- `Fraction(utilization)` = `min(max(utilization/100, 0), 1)`.
- `PercentText(fraction)` = `(int)Math.Round(fraction*100 + 1e-9, MidpointRounding.AwayFromZero)` + `"%"` (epsilon mandatory: 0.575*100 = 57.49999999999999 must give 58%).

#### 6.3 `Thresholds.Level(fraction)` (Core/Usage/ThresholdLevel.cs:12-18)
`< 0.6` Ok, `< 0.85` Warning, else Danger (so exactly 0.6 -> Warning, 0.85 -> Danger). UI colours: green/amber/red (windows-port spec).

#### 6.4 `ModelBuckets` (Core/Usage/ModelBuckets.cs)
- Preference order: `seven_day_fable`, `seven_day_opus`, `seven_day_sonnet` (:8). Excluded non-model keys: `seven_day_oauth_apps`, `seven_day_overage_included` (:12).
- `IsModelKey(key)` = starts with `seven_day_` (ordinal) and not excluded. Plain `seven_day` is not a model key (no trailing underscore).
- `Available(snapshot)`: all model keys; the known ones (in preference order, only if present) first, then all other model keys sorted with `StringComparer.Ordinal`.
- `Resolve(preferred, snapshot)`: preferred if it is in `Available`; else first available; else null. (A saved preference that disappears from the server's response silently falls back.)
- `Label(key)`: strip `seven_day_` prefix and `ToUpperInvariant` (`seven_day_fable` -> `FABLE`, `seven_day_claude_opus_4_5` -> `CLAUDE_OPUS_4_5`); a key without the prefix is just upper-cased.
- Used for the third dial, tray menu "Model limit" list (App.xaml.cs:535, 541-543), and export.

#### 6.5 `UsageTypes` (Core/Usage/UsageTypes.cs)
- `UsageBucket(double Utilization, DateTimeOffset? ResetsAt)`; `UsageSnapshot(Buckets dictionary, SourceUpdatedAt?)` with indexer returning null for missing keys; value equality (SourceUpdatedAt, count, per-key bucket equality; GetHashCode XOR over keys).
- `UsageErrorKind`: NoCredentials, Unauthorized, MalformedResponse, RateLimited, Network.
- `UsageError(Kind, RetryAfterSeconds?, Message?)` with statics `NoCredentials`, `Unauthorized`, `MalformedResponse`; factories `DuplicateAccount(otherName)` (kind **Unauthorized** with a custom Message), `RateLimited(int?)`, `Network(string)`. Records compare by value (so `RateLimited(600) != RateLimited(null)`).
- `UsageException(UsageError)` : message = `error.Description`; not sealed (App subclasses it with `NavigationFailedException`).
- `UsageState`: `Loading`, `Ok(Snapshot, FetchedAt)`, `Failed(UsageError)`.

### 2B §7. UsageStore state machine and 429 backoff (Core/Store/UsageStore.cs)

#### 7.1 Constants
`RefreshIntervalSeconds = 300` (:20). `RetryMarginSeconds = 300` (:28; production observation: Retry-After understates the ban by minutes and the ban counter outlives expiry, so an early retry starts a fresh hour-long ban). Ladder `RateLimitBackoffSeconds = [3600, 21600, 86400]` (1 h, 6 h, 24 h; :34).

#### 7.2 Construction / persistence (:83-139)
Injected: `fetch(ct)`, `hasCredentials()`, `now()`, `loadRetryState()`, `saveRetryState(state)`, optional `log`. In the App: fetch = `session.FetchUsageAsync`, hasCredentials = `_hasSessionCookie[id]` (missing = false), now = `DateTimeOffset.Now`, retry state = the account's `RetryPausedUntil`/`ConsecutiveRateLimits` in settings (App.xaml.cs:808-825, 859-866), log -> `WidgetLog.Write(DisplayName, "unhandled", message)`.
- Initial `CurrentState = Loading`.
- Ctor: `loadRetryState()`; if it throws -> start unpaused and leave the file alone (`_persistedKnown=false`). If a saved pause with `Until > now()`: `RetryPausedUntil = Until`, `_consecutiveRateLimits = saved count`, `CurrentState = Failed(RateLimited(null))` (state set directly, `Changed` not raised). Else if persisted state was readable: `PersistRetryState(null)` (clears an expired pause on disk). Note: counters of an expired pause are not restored, so after restart past expiry the ladder starts again at step 1.
- `PersistRetryState(state)`: skipped if `_persistedKnown` and equal to what is believed stored (avoids rewriting+fsyncing settings.json every successful poll); any exception swallowed, `_persistedKnown=false` (retry later); store never fails because a write failed.

#### 7.3 `LoadAsync()` (:144-181) - coalescing
Lock `_inFlightGate`; if a load is in flight return that task (timer, manual refresh and resume handler can fire together; overlapping callers share one fetch). Else publish a `TaskCompletionSource` as `_inFlight` BEFORE starting, run `RunLoadAsync` outside the lock (so synchronous completion cannot clobber the field), clear `_inFlight` in `finally`. The lock exists because `LoadAsync` is also called from the `PowerModeChanged` worker thread (but see 9.3: App marshals to the UI thread first).

#### 7.4 `PerformLoadAsync` (:183-227)
All of it inside an outer `try`; any non-UsageException (including exceptions from injected `now`/`hasCredentials`) -> `SetState(Failed(Network(ex.Message)))`.
1. If `RetryPausedUntil` set and `now() < pausedUntil`: **return silently** (no state change, no `Changed`, no fetch). State stays whatever it was (typically `Failed(RateLimited)`).
2. `RetryPausedUntil = null` (deadline passed; in-memory only; persisted pause is cleared only by a success).
3. `!hasCredentials()` -> `SetState(Failed(NoCredentials))`, return (no fetch; LastSnapshot kept).
4. `snapshot = await fetch(None)`; `Publish(snapshot)`.
5. `catch UsageException`: if kind `RateLimited`: `_consecutiveRateLimits++`; `index = min(count-1, 2)`; `wait = max(error.RetryAfterSeconds ?? 0, ladder[index])`; `deadline = now() + (wait + 300 s)`; `RetryPausedUntil = deadline`; persist `UsageRetryState(deadline, count)`. Then for ANY UsageException kind: `SetState(Failed(error))`.
   - Actual pauses: 1st 429 -> 3900 s (65 min); 2nd consecutive -> 21900 s (6 h 5 min); 3rd+ -> 86700 s (24 h 5 min). Since the web path always passes `RateLimited(null)`, Retry-After never lengthens it (tests cover RetryAfter 600/60 below the ladder).
   - Non-429 errors do NOT reset `_consecutiveRateLimits` and do not touch the pause.
6. `Publish` (:229-236): `LastSnapshot = snapshot`; counter 0; `RetryPausedUntil = null`; persist null (no-op if unchanged); `SetState(Ok(snapshot, snapshot.SourceUpdatedAt ?? now()))`.

#### 7.5 `SetState` and `Changed` (:238-254)
`CurrentState = state` then each `Changed` subscriber invoked individually; a throwing subscriber (e.g. a render reading a held settings.json) is caught (except `OutOfMemoryException`) and logged via `_log("Changed subscriber failed: <Type>: <message>")`; it neither skips other subscribers (the export) nor turns a good fetch into Failed. In the App `Changed` -> `OnStoresChanged` -> `Dispatcher.Invoke(Render widget, tooltip, tray icon, tray menu state, band)` (App.xaml.cs:489-502).

#### 7.6 What the UI sees (state -> text), with file refs
`AccountRuntime.Snapshot` (AccountRuntime.cs:14-15) = the snapshot of `Ok`, else `LastSnapshot` (dials dimmed, not blank). The panel `dimmed = state is not Ok` (DesktopWidgetWindow.cs Draw). The status line and notice describe the TRAY account only.
`StatusLine.Text(state, now, retryUntil)` (Core/Views/DialModel.cs:129-142):
- `Loading` -> `loading…` (single-char ellipsis)
- `Ok`: if `now - fetchedAt >= 15 min` -> `updated <RemainingText(now, fetchedAt)> ago` (e.g. `updated 15m ago`, `updated 1h 2m ago`), else null (silence)
- `Failed NoCredentials | Unauthorized` -> null (covered by the blocking notice)
- `Failed MalformedResponse` -> `unexpected response from the API`
- `Failed RateLimited` -> `rate limited · retry in <RemainingText(retryUntil, now)>` or just `rate limited` if no deadline/passed
- `Failed Network` -> the raw `error.Message`
Blocking notice (`NoticeFor`, DesktopWidgetWindow.cs:363-371):
- NoCredentials -> title `Not signed in`, detail `Use the menu to sign in to Claude.ai`, Sign-in button shown.
- Unauthorized without custom message -> title `Session expired`, detail `The Claude.ai session expired. Sign in again from the widget menu.`, NO sign-in button.
- Unauthorized with message (duplicate account) -> title `Account added twice`, detail `This Claude.ai login is already added as "<otherName>". Sign in again with another login.`, Sign-in button shown.
Tray tooltip (App.xaml.cs:767-806): WebView2-failing -> `Claude Usage Widget — WebView2 error, see widget for details`; else `Claude Usage Widget` or `Claude Usage Widget — <LABEL value> · <LABEL value> · ...` (e.g. `5H 42% · 7D 18% · FAB 8%`) plus `\n<status line>` when non-null. Startup with unreadable settings: `Claude Usage Widget — settings.json unreadable, retrying` (:171).

#### 7.7 Complete list of user-visible error / state strings produced in scope
`UsageError.Description` (UsageTypes.cs:97-105):
- NoCredentials: `No Claude.ai web session was found.`
- Unauthorized: `Message ?? "The Claude.ai session expired. Sign in again from the widget menu."`
- MalformedResponse: `The server returned something unexpected.`
- RateLimited: `The API is rate limited. The widget retries on its own.`
- Network: its message or empty string.
Network messages generated in scope: `Claude.ai request timed out.`; `Claude.ai returned HTTP <code>.`; `Claude.ai navigation failed: <WebErrorStatus>.`; any `ex.Message` (WebView2/COM/IO exception text, `WebView2 gave profile ...`, `Exit without a matching Enter.` etc.); for StatusApi `HTTP <code>` / transport message (never displayed; swallowed by StatusStore).
Service status strings (ServiceStatus.cs): dial labels `OK`, `SLOW`, `PARTIAL`, `DOWN`, `MAINT`, `—` (em dash, Unknown); line words `operational`, `degraded`, `partial outage`, `major outage`, `under maintenance`, `state unknown`; line = `service <word>`, null in `Cell` mode when Operational.
Login window: title/hint/tooltip strings in 4.2.

### 2B §8. Service status (Core/Status, Core/Store/StatusStore.cs)

- Endpoint: `GET https://status.claude.com/api/v2/summary.json`, header `Accept: application/json`, no credentials (StatusApi.cs:8, 24-25). Transport exceptions (non-cancellation) -> `UsageException(Network(msg))` (:30-35); then `HttpValidation.Validate(status)` (Core/HttpValidation.cs:8-21): 2xx ok; 401/403 -> Unauthorized; 429 -> RateLimited(retryAfter=null here); else `Network("HTTP <code>")`. Body read as string, `StatusDecoder.Status(body)`.
- `StatusDecoder.Status` (ServiceStatus.cs:88-135): parse error or non-object root -> `MalformedResponse`. Loop `components` (array of objects): entries with string `name == "Claude Code"` (exact, case-sensitive) and string `status`: map via `ServiceStatusParser.Component`; first result that is not Unknown is returned (an unknown status string continues the loop). Then fall back to `status.indicator` via `ServiceStatusParser.Indicator` (first non-Unknown wins). Otherwise `MalformedResponse`. `status.description` and other components are ignored. Component named "Claude Code" wins even over a worse page roll-up (test `PrefersClaudeCodeComponent`).
- Component mapping: `operational`->Operational, `degraded_performance`->Degraded, `partial_outage`->PartialOutage, `major_outage`->MajorOutage, `under_maintenance`->Maintenance, else Unknown. Indicator mapping: `none`->Operational, `minor`->Degraded, `major`->PartialOutage, `critical`->MajorOutage, `maintenance`->Maintenance, else Unknown.
- `StatusStore` (Core/Store/StatusStore.cs): initial `Status = Unknown`. `RefreshIntervalSeconds = 300` (const, but the actual cadence is the App's shared timer). `LoadAsync` coalesces concurrent calls exactly like UsageStore. On fetch exception of any kind: previous status kept, `Changed` not raised, `LoadAsync` does not throw (:90-103: "a transient glitch shouldn't declare the service unavailable"). On success: `Status = fetched; Changed?.Invoke()` (fires even if the value is unchanged; subscriber exceptions are NOT isolated here, unlike UsageStore: they would fault the `LoadAsync` task -> the awaiting `RefreshAllAsync` -> FireAndForget log `fire-and-forget ...`; inferred).
- App wiring: `_statusStore = new StatusStore(new StatusApi().FetchAsync)` created in `CompleteStartup` (App.xaml.cs:213-214); `Changed` -> `OnStoresChanged`. Loaded at the end of every `RefreshAllAsync` (:360) and on tray "Refresh now" (:129). Status is global (not per account); panel shows it as a dial (`StatusMode.Cell`) or a line (`StatusMode.Line`) per settings.
- Status dial click opens the status page (StatusDialControl; out of scope).

### 2B §9. Scheduling and wiring in App.xaml.cs

#### 9.1 Startup order (`OnStartup` :68-161, `CompleteStartup` :205-269)
1. Theme resources, preview hooks, single-instance mutex `ClaudeUsageWidget.SingleInstance` (second instance `Shutdown()`s), dispatcher/WinForms unhandled exception loggers (log event `unhandled`).
2. Tray icon created and its events wired (tray Refresh = tray account only then status; Sign in; accounts add/rename/remove/select; etc.).
3. `SettingsStore(%APPDATA%\ClaudeUsageWidget\settings.json)`. `TryBuildAccounts()`: reads settings; on `IOException`/`UnauthorizedAccessException` logs `settings unreadable at startup, retrying: ...` and `RetryStartupUntilSettingsLoad` (DispatcherTimer 2 s; after 1 minute interval becomes 30 s; no half-started widget; never defaults over a merely-held file; :163-202).
4. `BuildAccounts` (:831-845): empty account list (first run) -> creates account `AccountProfile("default", "Claude", null, null, 0)` with `TrayAccountId = "default"`, saves; builds one `AccountRuntime` per profile via `CreateAccountRuntime` (:847-869): profile name null iff id is `"default"`; `ClaudeWebSession` + `UsageStore` + `store.Changed += OnStoresChanged` + `session.SignedIn += OnSignedIn`.
5. `CompleteStartup`: theme, `StatusStore`, widget window (+events), **`_refreshTimer = DispatcherTimer(Interval 300 s)` whose Tick runs `RefreshAllAsync`** (:244-246); `SystemEvents.PowerModeChanged += OnPowerModeChanged`; first render; show widget/band per settings; `FireAndForget(StartupAsync)`.
6. `StartupAsync` (:280-320): tray account only: `HasSessionCookieAsync`; if false/throws -> `OpenLoginWindowAsync` (errors swallowed); then `RefreshAllAsync()`. So the first poll is at startup (not after a 300 s wait); the timer's first tick is 300 s after `Start()`.

#### 9.2 `RefreshAllAsync` (:322-361) and stagger
- Snapshot of `_accounts`. For `i` in account order: `offset = PollSchedule.OffsetFor(i, count)`. `offset == 0` (account index 0): `await RefreshAccountAsync(account)` inline (so subsequent accounts are scheduled only AFTER account 0's refresh completes; their offsets are relative to that moment, inferred drift of up to the first fetch's duration). Otherwise `Task.Delay(offset).ContinueWith(UI-thread continuation)`: skip if `!_accounts.Contains(account)` (removed meanwhile; NOTE `AccountRuntime` is a record so after a rename `_accounts` holds a different instance and this check fails for the pre-rename instance, i.e. a rename during the wait drops that account's poll for this cycle, inferred), else `FireAndForget(RefreshAccountAsync)`.
- Then `await _statusStore.LoadAsync()` (not delayed; waits only for status).
- `PollSchedule.OffsetFor(index, accountCount, cycleSeconds = 300)` (Core/Store/PollSchedule.cs:13-22) = `TimeSpan.FromSeconds((double)cycle * index / count)`; throws `ArgumentOutOfRangeException` if `count <= 0` or index out of `[0,count)`. Four accounts -> 0, 75, 150, 225 s; two -> 0, 150; three -> 0, 100, 200. Rationale: avoid four simultaneous claude.ai requests (spec decision 10).

#### 9.3 `RefreshAccountAsync(account)` (:363-394)
1. `_hasSessionCookie[id] = await Session.HasSessionCookieAsync()` and `_webViewFailing[id] = false`; on exception: `_hasSessionCookie[id] = true`, `_webViewFailing[id] = true` (see 1.3).
2. `await Store.LoadAsync()` (coalesced; skipped silently while paused; the cookie check above still runs, i.e. a webview is created even for a paused account).
3. `ExportUsage(account)` (export is out of scope; it only writes on fresh `Ok`).
Per poll the hidden webview therefore performs: cookie check (1 call), [org list on first pick or hourly refresh], usage page. Typical healthy poll = 1 navigation.

#### 9.4 Other triggers
- **Resume from sleep**: `PowerModeChanged` with `PowerModes.Resume` -> `Dispatcher.Invoke(() => FireAndForget(RefreshAllAsync))` (:478-487); handler runs on a system thread so it is marshalled; the whole `RefreshAllAsync` (staggered) runs, not a single account. The timer is not reset. The 3 s navigation retry for transient failures (`Unknown`, `ConnectionReset`, ...) is what covers a resuming network adapter (NavigationRetryPolicy.cs:11-12). Dead webview after sleep is handled by 2.3. Unsubscribed in `OnExit`.
- **Tray "Refresh now"**: tray account only + status (:124-130). Guarded by `_statusStore is null` during delayed startup. Still subject to a 429 pause.
- **Sign-in completed**: that account only (`OnSignedIn`).
- No refresh on account add (until sign-in) and none on tray-account switch (`OnAccountSelected` only re-renders).
- Account removal (`OnAccountRemoveRequestedAsync`, :1017-1048): `Session.ClearBrowsingDataAsync()` first (through the idle-lease wrapper; wipes the profile; failures only Debug-logged), then removes the account from settings (and clears `TrayAccountId` if it was that one), drops the runtime, rebuilds layout. Done in this order so the next account with the same id cannot inherit a session. (Existing `ClaudeWebSession` objects are not disposed; their idle close timer will close any open webview.)
- Rename: `ReloadAccountProfiles()` replaces `Profile` in live runtimes with `with {Profile = ...}` (sessions/snapshots kept). The session's `_accountLabel` (used in log lines) is a construction-time copy and stays stale until restart; only the login window reads the live name (ClaudeWebSession.cs:48-63).
- Fire-and-forget wrapper (:1067-1078): any exception -> `Debug.WriteLine` and log `unhandled fire-and-forget <Type>: <message>` under account `-`.
- Exit: unsubscribes power events, stops timer, disposes tray, closes windows (no explicit webview shutdown).

#### 9.5 Threading summary
Everything touching WebView2 / WPF runs on the UI (Dispatcher) thread with `ConfigureAwait(true)`. The only other thread is `SystemEvents.PowerModeChanged`, marshalled via `Dispatcher.Invoke`. Core stores nevertheless use a lock around the in-flight task. Settings reads/writes happen from fetch paths (`SaveOrganizationId`, `SaveSubscriptionFields` use `Load()` read-modify-write and throw IOException when held; `SaveQuietly` swallows only `IOException`/`UnauthorizedAccessException`; reads for rendering use `Current()` which falls back to last good settings).

### 2B §10. Persisted data touched by this scope

Settings file `%APPDATA%\ClaudeUsageWidget\settings.json` (SettingsStore, JSON; per `AccountProfile`, WidgetSettings.cs:14-24): `Id`, `DisplayName`, `OrganizationId` (set on pick, cleared on 401/403 from usage), `RetryPausedUntil`, `ConsecutiveRateLimits` (UsageStore pause), `ExportPath`, `Capabilities`, `RateLimitTier`, `RavenType`, `SubscriptionFetchedAt` (subscription refresh), plus global `TrayAccountId`, `ModelBucket`, etc. Writes are atomic (temp + replace) via `FileReplace`.
WebView2 data: UDF `%LOCALAPPDATA%\ClaudeUsageWidget\profiles\default`, profiles under `EBWebView\Default` (legacy account) and `EBWebView\WV2Profile_<guid>`; session cookie `sessionKey` lives there (there is NO other credential storage).
Log: `%LOCALAPPDATA%\ClaudeUsageWidget\widget.log` (+ `.1`), UTF-8 no BOM, rotated when size > 1 MB before an append (overwrite the `.1`), lock-guarded, never throws; line = `LogLine.Format(now, account, evt, details)` (Core, out of scope; example in 2026-09-04 spec: `2026-09-04T11:39:45.123+03:00 low nav-failed path=/api/organizations/x/usage status=Unknown http=0 attempt=1`, newlines in details -> space). Nothing is logged on a healthy poll.

#### widget.log events written by code in scope (account label = DisplayName at runtime construction, `-` if none)
`settings` (`<what> not saved: <Type>: <msg>`, `unreadable at startup, retrying: ...`), `organization` (`duplicate-of <name>`, `refresh-failed <Kind>`, `refresh-unparsed <describe>`, `<describe> tier=<label>`), `login-window` (`open`, `closed signedIn=<bool>`, `reuse existing`, `still opening`, `create-failed ...`), `retry-ok`, `nav-failed`, `nav-http`, `nav-download`, `nav-redirect`, `resource`, `webview-process-failed`, `webview-reset`, `webview-idle`, `sign-in-requested` (`source=tray|panel|add-account`), `export` (skipped), `unhandled` (dispatcher/tray/fire-and-forget/`Changed subscriber failed`).

### 2B §11. (moved to Section 4)

### 2B §12. (moved to Section 5)

### 2B §13. Bug / design history worth preserving in the port (from comments and specs)

- 403 on expired session read as "navigation failed: Unknown" for days because `IsSuccess` was checked before `HttpStatusCode` (2026-09-04). Keep status-first order.
- `??=` on a task kept completed login-window task forever -> panel Sign-in did nothing (2026-09-04). Keep "reuse existing window, share only a pending creation".
- GC of the .NET controller wrapper killed the fetch webview after a Gen2 collection (commit 1040156).
- Hidden WebView2 host became a visible black window (commit b1853dd).
- Fetch JSON served as attachment -> download instead of page (diagnostics added 2026-09-04).
- Account polls burst at the same instant -> stagger (spec decision 10).
- Retry-After understates ban; early retry renews it -> +300 s margin and 1h/6h/24h ladder, persisted across restarts (commits 3a7e6fb, f4fdd1e).
- Wrong login in a new account window showed another account's numbers -> duplicate-organization refusal (commit f977516, user report 2026-10-29).
- Organization refresh could blank a good plan -> Merge keeps stored fields; hourly cadence; latch on failure.
- Plans renamed (Max 20x -> Ultra, Max 5x -> Max); Ultra has no five-hour limit; Team Premium inferred from a Fable bucket (commit fd3eb24).
- Login window must wipe the profile on every open (Turnstile rejection loop); cookie check must be polling (no cookie-change event); raise `SignedIn` before `Close()`.
- Pre-multi-account sign-in lives in the IMPLICIT profile; a named profile `default` does not see it (measured).
- WebView2 SDK 1.0.4258.31 wrapper turns HRESULT 0x8007139F into `InvalidOperationException` with a `COMException` inner (relevant only if the Rust port uses raw COM: look at the raw HRESULT directly).

### 2B §14. (moved to Section 6)

---

## Part 2C: desktop widget window, dials, themes, formatting, layout preview, rename window

### 2C §0. (moved to Section 7)

### 2C §1. Desktop widget window (`DesktopWidgetWindow`)

#### 1.1 Window style and z-order

- WPF `Window` subclass, created once per process (`APP:217`), never recreated. Properties (`DWW:191-198`): `WindowStyle=None`, `AllowsTransparency=true` (a per-pixel-alpha layered window), `Background=Transparent`, `ShowInTaskbar=false`, `ShowActivated=false`, `ResizeMode=NoResize`. No `Topmost`. No owner. `SizeToContent` not used: `Width`/`Height` are assigned explicitly from `PanelMetrics` (`DWW:318-319`).
- In `OnSourceInitialized` (`DWW:383-394`) the extended style is OR-ed with `WS_EX_NOACTIVATE (0x08000000)` and `WS_EX_TOOLWINDOW (0x00000080)` via `GetWindowLongPtrW/SetWindowLongPtrW(GWL_EXSTYLE=-20)` (`DWW:649-679`). Effects: never takes focus on click; not in Alt+Tab; no taskbar button.
- **Bottom-pinned z-order** (`DWW:396-418`): an HwndSource hook watches `WM_WINDOWPOSCHANGING (0x0046)` and, for every such message, rewrites the `WINDOWPOS` struct: `hwndInsertAfter = HWND_BOTTOM (1)` and clears `SWP_NOZORDER (0x0004)` from `flags`, writes it back, returns 0 with `handled=false` so DefWindowProc continues. Thus every move/size/show reasserts "behind every regular window". The hook is installed once; there is no timer or other z-order enforcement. Whether it ends up above or below desktop icons/wallpaper host is not stated beyond the class comment "above the wallpaper, below any regular window" (`DWW:21-24`) (unverified).
- **Click-through**: there is no explicit `WS_EX_TRANSPARENT`. Click-through exists only as a property of layered windows: pixels with alpha 0 pass the mouse through (stated in `C/Views/PanelOpacity.cs:21-24`). Consequences: area outside the rounded corners is click-through; the panel background is hit-testable only because its painted alpha never drops below 1/255 (see 4.5).
- Per-monitor DPI: manifest `src/ClaudeUsageWidget.App/app.manifest` declares `dpiAwareness=PerMonitorV2`, `dpiAware=true/PM`, `gdiScaling=true`, and `supportedOS` GUIDs for Win7..Win11 (also `UseWindowsForms=true`, `net10.0-windows`, csproj:17,22,23). There is no `DpiChanged`/`WM_DPICHANGED`/`DisplaySettingsChanged` handler anywhere in the app (grep) (see 1.9).
- Single instance: named mutex `ClaudeUsageWidget.SingleInstance` (`APP:85-91`); second instance calls `Shutdown()`.

#### 1.2 Construction, first-run position, restored state (`DWW:187-252`)

1. Reads `settings.Current()` (non-throwing cached read): `WidgetSide` -> `_side = ClampSide(WidgetSide)` (`DWW:213`), `PositionLocked` (`DWW:214`).
2. `_accountCount = Accounts.Count` or 1 if zero (`DWW:219`); `ApplyLayoutAndSize(_side)` (`DWW:220`).
3. If **both** `WidgetX` and `WidgetY` are non-null: `Left=x; Top=y; ClampToScreen()` (`DWW:222-229`). Otherwise `WindowStartupLocation = CenterScreen` (first run) (`DWW:233`). Until the window is shown after CenterScreen, `Left/Top` are NaN; every function that touches them guards with `double.IsNaN` (`DWW:124,174,344,622`, and `160`).
4. 500 ms `DispatcherTimer` for debounced geometry persistence (`DWW:236-241`).
5. Subscribes to window events `MouseLeftButtonDown/MouseMove/MouseLeftButtonUp/LostMouseCapture` (plain `+=`, not `handledEventsToo`) and `Closing -> FlushPendingPersist` (`DWW:247-251`).

App startup (`APP:205-262`): `ApplyTheme()` and `SystemTheme.Start` happen BEFORE the window is created; `RenderWidget()` before `Show()`; `Show()` only `if (settings.WidgetVisible)` (default true, `WS:31`). On exit `_widgetWindow.Close()` (`APP:1094`) triggers the flush.

#### 1.3 Panel geometry (all in `C/Views/PanelMetrics.cs`, `WidgetLayout.cs`)

`PanelMetrics.For(layout, accountCount, side, editMode=false, planLine=Hidden)` (`PanelMetrics.cs:176-234`):

| Quantity | Formula (scale = ClampSide(side)/170) |
|---|---|
| side limits | `DefaultSide=170`, `MinSide=150`, `MaxSide=340` (`WS:148-153`); scale range 0.882..2.0 |
| dial size | `68*scale` (`:184`) |
| padding | `12*scale` (`:182`) |
| gap (between dials in a block) | `10*scale` (`:183`) = `Theme.Gap` |
| block gap (between account blocks) | `2*gap = 20*scale` (`:190`) |
| corner radius of panel | `22*scale` (`THEME:123`) |
| blocks count | `max(accountCount, 1)` (`:196`) |
| panel rows/cols | `FlowGrid.Shape(PanelFlow, count)` (`:197`) |
| block rows/cols | `FlowGrid.Shape(Block.Flow, Block.LaidOut.Count)` (`:20`) |
| block width | `cols*dial + (cols-1)*gap` (`:21`); height `rows*dial + (rows-1)*gap` (`:22`) |
| name row height (`NameHeight`) | only when name is Above/Below: `13*scale + (plan Shown ? CaptionLineHeight*scale : 0)`; block height += `NameHeight + gap` (`:36-39`) |
| `CaptionLineHeight` | `8 * 1.1708984375 = 9.3671875` (Consolas line spacing 1.1709 em, measured via FormattedText) (`:152`) |
| panel width | `2*padding + cols*blockW + (cols-1)*blockGap` (`:199`) |
| status band | `StatusBand = (layout contains Status cell) ? padding : 2*padding + CaptionLineHeight*scale` (`:222-223`) |
| panel height | `padding + rows*blockH + (rows-1)*blockGap + statusBand + ToolbarReserve` (`:228-229`) |
| `BottomGap` | `max(0, (StatusBand - CaptionLine)/2)` (`:166`); `TopGap = padding` (`:161`) |
| `ToolbarReserve` | `editMode ? Toolbar.Height : 0` (`:204`) |

`FlowGrid.Shape(flow, n)` (`WidgetLayout.cs:46-57`): n<=0 -> (0,0); `Row` -> (1,n); `Column` -> (n,1); `Grid` -> (`(n+1)/2`, `min(n,2)`) (always 2 columns max; 3 items = 2x2 with a hole).
`GridTracks.Build(count,size,gap)` (`WidgetLayout.cs:70-82`): alternating `size, gap, size, ...` (gap is its own track, no trailing gap); `TrackOf(i)=2i`.

Toolbar metrics (`PanelMetrics.cs:49-102`): `ButtonSize=14*scale`, `pitch=2*scale`, `ButtonCount=9`. One row if `9*button + 8*pitch <= panelWidth - 2*padding`, else 2 rows x 5 columns (`(9+1)/2`); `Height = rows*button + (rows-1)*pitch + gap`. At 170 default: 142 <= 146 so one row at every shipped side; two rows only for a one-dial-wide panel (single-account Column flow at MinSide, test `LayoutTests` `APanelNarrowerThanTheStripWrapsToTwoRows`). The wrap "promises no fit": the panel is never widened.

Worked numbers (derived): 1 account Classic at 170 -> 170x170 (statusBand=padding=12: 12+146+12). 1 account default (Line) -> 170 x (12+146+33.367)=191.367. 2 accounts default Grid panel flow -> 336 wide (24+2*146+20); 3 or 4 accounts -> 336 x (12+2*146+20+33.367=357.367). 3 dials in Row with Name Above, 1 account -> 248 wide (test).

#### 1.4 Layouts and their data (Core)

- `LayoutFlow {Grid, Row, Column}`, `NamePlacement {Cell, Above, Below, Hidden}`, `BlockItem {Name, FiveHour, SevenDay, Model, Status}` (`WidgetLayout.cs:5-37`). Enum declaration order IS the cycle order of the toolbar buttons.
- `BlockLayout(Flow, Name, Order)`; `DefaultOrder=[Name,FiveHour,SevenDay,Model]`; `RequiredItems=[FiveHour,SevenDay]`; `Default = (Grid, Cell, DefaultOrder)`; `LaidOut` = `Order` if Name==Cell else `Order` minus `Name` (`:89-142`). `Toggle(item)` appends or removes (`:107-113`). `SwapCells(a,b)` swaps by IDENTITY of laid-out items inside `Order`; returns `this` if a==b or either index is out of range of `LaidOut` (`:123-134`).
- `WidgetLayout(PanelFlow, Block)`; `Default = (Grid, BlockLayout.Default)` (`:144-146`).
- `WidgetLayout.Sanitize(raw, mode, modelDial)` (`:159-168`): null -> Default; `Order` kept only if non-null, all distinct, contains both required items (`IsUsable`, `:184-187`), else `DefaultOrder`; THEN `Status` is forced present iff `mode==Cell` and `Model` present iff `modelDial==Shown` (appended at end if added, removed wherever it sits) (`With`, `:174-182`). Idempotent; returns same list when nothing changes. Flows/Name are never touched.
- `StatusModes.Resolve(saved, layout)` (`StatusMode.cs:29-32`): saved ?? (layout?.Block?.Order contains Status ? Cell : Line); null-safe.
- `ModelDials.Resolve(null)=Shown`; `PlanLines.Resolve(null)=Hidden` (`ModelDial.cs:22`, `PlanLine.cs:22`).
- `PanelViews` (`PanelView.cs`): `Classic` = `WidgetLayout(Grid, BlockLayout(Grid, Hidden, [FiveHour,SevenDay,Model,Status]))` with StatusMode Cell; `Accounts` = `WidgetLayout.Default` with StatusMode Line (`:20-31`). `DefaultFor(n)` = n<=1 ? Classic : Accounts (`:35`). `Current(layout, mode)`: `(Hidden, Cell)` -> Classic, `(not Hidden, Line)` -> Accounts, else null (`:41-47`).
- `LayoutResolution.Resolve(data, accountCount)` (`PanelView.cs:67-87`): modelDial/planLine resolved; if `Layout==null && StatusMode==null` the layout is the named view for the account count (Classic for <=1, Accounts for >=2), else `layout=data.Layout`, `mode=StatusModes.Resolve(...)`; result passes through `Sanitize`. `Apply(data, view)` writes `Layout`, `StatusMode`, `ModelDial` on top of the data (`:91-100`) - used by the tray Layout submenu (`APP:669-675`).
- `LayoutCycle.Next<T>(v)`: `values[(IndexOf(v)+1) % n]`; a value outside the enum maps to the first (`LayoutCycle.cs:14-18`).
- `AccountLimits.Max = 4` (`Settings/SettingsMigration.cs:13`); settings with more accounts are truncated from the end on load.

Persisted layout JSON shape (inferred from `SettingsStore` options and the App test fixture `DesktopWidgetWindowTests.cs:242-266`): `"Layout": {"PanelFlow":"Row","Block":{"Flow":"Column","Name":"Above","Order":["Name","FiveHour","SevenDay","Model"],"LaidOut":[...]}}`. Enums are written as NAMES; read tolerant: unknown name -> enum default (value 0), ordinal accepted if defined (`Settings/TolerantEnumConverter.cs`), indented JSON, trailing commas allowed. `LaidOut` is a computed get-only property that the serializer (inferred) writes but ignores on read.

#### 1.5 What each block draws (`WRV:797-967`)

Grid construction (`BuildGrid`, `WRV:797-819`): `DialGrid` rows/cols from `GridTracks.Build(metrics.Rows, block.Height, blockGap)` / `(metrics.Columns, block.Width, blockGap)`. Block `i` goes to row `i / columns`, col `i % columns` (row-major) (`:814-815`). `DialGrid` is centered in the panel (xaml `HorizontalAlignment/VerticalAlignment=Center`) with margin `(padding, padding, padding, StatusBand)` (`WRV:460-461`).

Per block (`BuildBlock`, `:823-850`): if name is Above/Below -> a 2-row Auto grid: header (name stack, `Height = NameHeight`, margin: Above -> bottom `gap`, Below -> top `gap`) and the cells grid; else only the cells grid. Cells (`BuildCells`, `:852-910`): grid tracks from `Block.Rows/Columns` with `DialSize` and `gap`; item `i` at row `i / cols`, col `i % cols`.
Each laid-out item becomes:
- `Name` (only if Name==Cell): a `DialSize x DialSize` Grid containing a `NameRingControl` (bare track ring) + the name stack constrained to width `DialSize - 2*DialSize*8/68` (= 0.765*DialSize) (`:864-882`; `RING:19` `TextInset = 8/68`).
- `Status`: a `StatusDialControl` `DialSize x DialSize` (`:883-888`).
- `FiveHour/SevenDay/Model`: a `DialControl` `DialSize x DialSize`, stored at index 0/1/2 (`:891-892`, `DialIndex :913-919`).
Every cell is wrapped in a `Border{Background=Transparent}` ("host") registered in `_cells` with `CellHit(blockIndex, cellIndexInLaidOut)` so empty corners are hit-testable (`:896-906`).

Name stack (`NameStack`, `:933-957`): vertical StackPanel centered vertically; account name `TextBlock`: Consolas, SemiBold, font size `11*scale`, brush `Text`, centered text, `TextTrimming=CharacterEllipsis`; tooltip = full DisplayName (`:982-988`). If `PlanLine==Shown` a second TextBlock: caption size `8*scale`, brush `Dim`, centered, ellipsis, **fixed height `CaptionLine`** (`:947-954`); its text is `row.PlanLabel ?? ""` (empty, not collapsed, when plan not fetched) (`:995`). For Above/Below the stack width is unconstrained (block width) (`:834`).

Account rows -> view (`C/Views/DialModel.cs`): `AccountRow.ForAll(accounts, snapshots, preferredModelKey, now)` gives one row per account in settings order (always; a missing snapshot keeps a row with n/a dials) (`:93-103`). Row = `(AccountId, DisplayName, Dials[3], SessionResetsIn, PlanLabel)`. `SessionResetsIn` is only used by the taskbar band, never drawn on the panel (panel shows each dial's `Remaining`). The dial/plan derivation: see section 5.

`SetContent` (`WRV:971-1037`): throws `InvalidOperationException` if `rows.Count != blocks.Count` (guarded by `DWW.Draw` skipping on mismatch, `DWW:296`); fills names/plans/dials; status dial gets `Status` and `Dimmed`; then builds the status-line text and the notice (sections 1.6, 5).

#### 1.6 Status line and notice (panel overlays)

Status line (`WRV:1010-1025`, `WRX:164-169`): text = join with `" · "` of non-empty parts `[ServiceStatusText.Line(status, _statusMode), StatusLine.Text(state, now, retryUntil)]`. `Line()`: in Cell mode and Operational -> null (silent); otherwise `"service " + LineLabel` where LineLabel = operational / degraded / partial outage / major outage / under maintenance / state unknown (`SS:72-92`). In Line mode it always speaks (even "service operational"). `StatusLine.Text` (`DialModel.cs:129-157`): Loading -> `"loading…"` (U+2026); Ok -> null unless `now - fetchedAt >= 15 min` then `"updated {RemainingText(now, fetchedAt)} ago"` (null if that formats to null); Failed(NoCredentials|Unauthorized) -> null; MalformedResponse -> `"unexpected response from the API"`; RateLimited -> `"rate limited · retry in {RemainingText(retryUntil, now)}"` or `"rate limited"` if deadline null/passed; Network -> `error.Message` (may be null/empty -> dropped). Any other state -> throws ArgumentOutOfRange. TextBlock: Consolas Medium, size `8*scale`, brush Dim, bottom-centered, `Height=CaptionLine`, bottom margin `bandBelow + BottomGap` (`WRV:465-472`), hit-test invisible, Collapsed when text empty. **It is placed in the status band at the bottom, and in Cell mode it overdraws the bottom padding** (band = padding only). The line is recomputed only when `Draw()` runs (no per-second timer exists; refresh interval is 300 s, `UsageStore.cs:20`, `APP:244`) so countdown strings go stale between polls/redraws (inferred).

Notice card (`WRV:1027-1036`, `DWW:366-374`, `WRX:85-101`): visible only for tray-account states
- `Failed(NoCredentials)` -> title `"Not signed in"`, detail `"Use the menu to sign in to Claude.ai"`, **Sign in button shown**;
- `Failed(Unauthorized)` with `Message==null` -> title `"Session expired"`, detail `UsageError.Description` = `"The Claude.ai session expired. Sign in again from the widget menu."`, **no button**;
- `Failed(Unauthorized)` with `Message!=null` (duplicate-account) -> title `"Account added twice"`, detail = message (`This Claude.ai login is already added as "{name}". Sign in again with another login.`), button shown;
- everything else -> hidden.
Card: Border over the whole panel (same corner radius, same band margin), background = Overlay brush (panel colour alpha 0.92), centered StackPanel with margin = padding; title Consolas SemiBold `14*scale` Text brush; detail Consolas Medium `8*scale` Dim brush, wrapped, top margin `5*scale`; button margin top `8*scale`, text size `8*scale`. Button chrome (`WRX:28-49`): rounded rect radius 10, padding 10,4, background Dim brush with **Opacity 0.35 (0.55 on hover) on the whole chrome Border, which also dims its label** (inferred from element tree), foreground Text brush, hand cursor. It is deliberately NOT hit-test-disabled: a click on the card background bubbles to the window and starts a drag (`WRX:76-84`).

Dimming: `dimmed = (state is not UsageState.Ok)` (`DWW:298`), i.e. Loading or Failed dims **every** dial and the status dial of **all** rows - the state is the tray account's only (`DWW:257-260`).

#### 1.7 Interaction: drag, resize, lock (non-edit mode)

All of this is window-level, manual (no `DragMove`, because NOACTIVATE) (`DWW:483-595`).

- `PositionLocked` (property, `DWW:60-70`): when true `OnWindowMouseLeftButtonDown` returns immediately -> no drag, no resize (`:485`); cursor forced to Arrow on hover (`:562`). Setting it stores to the view (glyph/tooltip update) and persists `PositionLocked` (Load -> `with` -> Save, `:638-642`). Toggled from: toolbar lock button, hover-header lock button, tray "Lock position" (`APP:602-610`). Not blocked by lock: edit-mode cell drag, edit-mode toolbar.
- **Edge bands**: `EdgeBand = 8` DIPs, fixed (not scaled), measured against the window's `ActualWidth/ActualHeight` (which includes the edit strip band) (`DWW:32,435-454`). `HitTestEdge`: corners win (TopLeft, TopRight, BottomLeft, BottomRight), then single edges, else None.
- Mouse down (`:483-509`): if edge -> start resize (`_resizeStartPoint`, `_resizeStartSide=_side`), else start drag (`_dragAnchor=pos`); `CaptureMouse()`; `e.Handled=true`.
- Mouse move (`:511-563`): first a safety check - if `_dragging||_resizing` but `Mouse.LeftButton != Pressed`, reset both flags and release capture. Drag: `delta = pos - anchor` (both window-relative), `Left += dx; Top += dy` (no clamping during drag; the panel can be dragged off-screen), then `ReconsiderStripSide()` and `SchedulePersistGeometry()`. Resize: `newSide = ClampSide(startSide + ResizeDelta(edge, dx, dy))`; `ResizeDelta`: Left `-dx`, Right `dx`, Top `-dy`, Bottom `dy`, TopLeft `max(-dx,-dy)`, TopRight `max(dx,-dy)`, BottomLeft `max(-dx,dy)`, BottomRight `max(dx,dy)` (`:461-472`). The top-left corner never moves; the panel always grows right/down. `ApplySide` ignores changes `< 0.5` (`:597-603`); otherwise `_side=side; ApplyLayoutAndSize(side)` (rebuild grid, recompute size, clamp, redraw) on every mouse move. Note (inferred): dx/dy are re-read in window coordinates while the window itself grows, so for Right/Bottom edges the drag is not 1:1 with the cursor (width = side * k, k up to ~2).
- Hover cursor (`:562`, `CursorFor :474-481`): Left/Right -> SizeWE, Top/Bottom -> SizeNS, TopLeft/BottomRight -> SizeNWSE, TopRight/BottomLeft -> SizeNESW, None -> Arrow. A child's own Cursor (Hand on buttons/status dial) takes precedence (inferred, WPF rule).
- Mouse up (`:565-574`) and lost capture (`:587-595`): clear flags; up also releases capture; both `SchedulePersistGeometry()`.
- A press on a toolbar button or the status dial is consumed by that control (`e.Handled`), so it neither drags nor resizes; a press anywhere else on the panel (dials, name, padding, notice background, header background) starts a drag/resize.

#### 1.8 Edit mode ("Edit layout") - in-widget editing

Entry/exit routes (all go through `APP.SetEditingLayout`, `APP:470-476`): tray "Edit layout…" item (`APP:145`), toolbar Done (`APP:225`), hover-header pencil (`APP:227`). `SetEditingLayout` sets the flag, ticks the tray item, sets `window.EditMode`, refreshes the menu. It is forced OFF when the widget is hidden by any route (eye/toolbar Hide `APP:455-464`, tray Show-on-desktop off `APP:599`).

Window `EditMode` setter (`DWW:107-153`), only on a real transition:
1. On entering: `_stripAbove = Left/Top not NaN && (Top + _stripAbovePad - ToolbarReserve >= WorkArea().Top)` (strip above the panel if it still fits in the work area, else below) (`:123-125`). First run (NaN) -> below.
2. `_root.StripAbove=_stripAbove; _root.EditMode=value;` then `Width=_root.Width; Height=_root.Height` (window grows by the reserve) (`:127-132`).
3. `ApplyStripBand(value && _stripAbove ? ToolbarReserve : 0)` moves `Top -= (newPad - oldPad)` so the PANEL stays at the same screen position (`:137,158-162`); `if (!value) _stripAbove=false`.
4. `ClampToScreen()`; `SchedulePersistGeometry()`; `Draw()` (`:140-151`).
- **Invariant**: the strip lives in a band OUTSIDE the rounded panel; panel size and screen position are the same in and out of the mode. Caveat (inferred): with the strip below and the panel near the bottom of the work area, `ClampToScreen` (`:342-350`) pushes the whole window up, so the panel does move then; persisted position then reflects the moved panel.
- `ReconsiderStripSide()` (`:172-185`) runs on every drag tick and every resize inside the mode: flips side when the panel's top gets within `reserve` of the work-area top; on a flip during a drag it shifts `_dragAnchor.Y += before - Top` so the window jump is not read as mouse travel.
- View side (`WRV:126-152`): leaving the mode cancels any cell drag; `ApplySize(Metrics(editMode))` -> panel border and notice get margin `(0, above, 0, below)` where `above/below` = reserve on the strip's side (`WRV:449-453`); `RefreshToolbar`, `RefreshEditChrome`, `SetHovering(RootGrid.IsMouseOver)`.
- Strip ("ToolbarPill", `WRX:158-162`, `WRV:480-524`): overlay-brush pill, `HorizontalAlignment=Center`, aligned flush to the outer edge of the band (Top if above, Bottom if below), margin 0, padding `gap/3`, corner radius `min(22*scale, pillHeight/2)`; buttons in a Grid with explicit tracks from `ToolbarMetrics.Rows/Columns`; each button `14*scale` square, glyph font size `9*scale`, Segoe MDL2 Assets, Dim brush. Button `i` at row `i / columns`, col `i % columns`.
- Nine buttons, left to right (`WRV:293-337`, glyph code points read from the source bytes):

| # | Glyph | Action | Tooltip (`WRV:355-375`) |
|---|---|---|---|
| 0 | U+E73E (CheckMark) | `EditDoneRequested` | `Done — leave edit mode` |
| 1 | U+E71D | `LayoutEdited(layout with PanelFlow=Next)` | `Accounts on the panel: {PanelFlow} — click to cycle` |
| 2 | U+E8FD | `LayoutEdited(Block.Flow=Next)` | `Dials in a block: {Block.Flow} — click to cycle` |
| 3 | U+E185 | `LayoutEdited(Block.Name=Next)` | `Account name: {Block.Name} — click to cycle` |
| 4 | U+E8EC (Ticket) | `PlanLineSelected(Next)` | `Subscription plan: {PlanLine} — click to cycle` |
| 5 | U+E946 | `StatusModeSelected(Next)` | `Service status: {StatusMode} — click to cycle` |
| 6 | U+EC4A (SpeedHigh) | `ModelDialSelected(Next)` | `Model dial: {ModelDial} — click to cycle` |
| 7 | U+E72E locked / U+E785 unlocked | `LockToggleRequested` | locked: `Position and size are locked — click to unlock`, else `Click to lock the widget position and size`; locked glyph uses Warning colour |
| 8 | U+E890 | `HideRequested` | `Hide the widget — bring it back from the tray icon` |

(`{...}` are the C# enum names, e.g. `Grid`, `Above`, `Shown`, `Cell`.) Only codepoints E73E, E8EC, EC4A, E72E, E785 are named in the code comments; the others are as read from the file (names unverified).
- Wiring of toolbar events (`APP:882-956`): layout edits -> `OnLayoutSelected` (loads settings, `Resolve`, saves `Layout=Sanitize(layout, status, modelDial)` plus `StatusMode`, `ModelDial`; `RebuildLayout`; `RenderWidget`; refresh tray menu); `StatusModeSelected` -> saves `StatusMode`, `ModelDial`, `Layout=Sanitize(resolved layout, mode, modelDial)`; `ModelDialSelected` -> same with ModelDial; `PlanLineSelected` -> saves `PlanLine` only, then rebuilds (panel is sized for the line) (`APP:949-956`).
- Cell chrome (`WRV:173-216`, `EditLayer` Canvas over everything, hit-test invisible): a 1 px dashed rectangle (`StrokeDashArray {3,3}`) in the Dim brush around every laid-out cell host (name cell included when Name==Cell; Above/Below name row gets none). Rebuilt only when the set of cell bounds changed (cache `_chromeBounds`, driven by `RootGrid.LayoutUpdated`); cleared on leaving the mode; brush refreshed on theme change by clearing the cache (`WRV:283-287`).
- Cell drag-swap (`WRV:625-705`): `PreviewMouseLeftButtonDown/Move/Up` on RootGrid.
  - Down: ignored if source is inside the toolbar (walks BOTH visual and logical trees because the glyph hits an implicit `Run`, `:600-611`); else `CancelDrag()`, remember `_dragStart` and `_dragSource = HitCell(point)`; only if a cell was hit set `e.Handled=true`, which stops the window's drag/resize AND the status dial's click (`:625-653`). A press on padding/gaps/strip background is not handled and moves/resizes the panel as usual.
  - Move (needs `_dragSource`): if `_dragging` and left not pressed -> cancel. Drag starts when `|dx| >= SystemParameters.MinimumHorizontalDragDistance` OR `|dy| >= MinimumVerticalDragDistance` (system SM_CXDRAG/SM_CYDRAG, 4 px by default; compare in DIPs); then `RootGrid.CaptureMouse()`, ghost created (`:655-688`).
  - Ghost (`StartGhost :719-736`): Rectangle of the cell's size filled with a live `VisualBrush(host)` (Stretch None), `Opacity=0.6`, centered on the cursor in RootGrid coordinates (`MoveGhost`); target highlight (`HighlightTarget :745-767`): 2 px solid rectangle, Text brush, around the cell under the cursor when its cell index differs from the source's (index compare only - a cell in another block with the same index is "same", no highlight).
  - Up: hit-test, `CancelDrag`, and if both cells exist and `from.Cell != to.Cell` raise `LayoutEdited(layout with Block = Block.SwapCells(from.Cell, to.Cell))` (`:690-705`). Since all blocks share one `BlockLayout`, a swap in any block rearranges every block. Dropping on padding/gap/same index = no-op. Click without exceeding the threshold = nothing.
  - `LostMouseCapture` on RootGrid -> `CancelDrag` (`:244`).
  - Hit test = WPF `RootGrid.InputHitTest(point)` then walk up visual (or logical for ContentElements) parents until a registered host (`:577-590`) - deliberately not arithmetic in Core.
- Hover header (non-edit only) (`WRX:107-136`, `WRV:1055-1077`): a Border at the top, full panel width, Overlay brush, top corners radius `22*scale`, `Opacity=0, IsHitTestVisible=false` by default; shown (`Opacity=1, hit-testable`) while the pointer is over `RootGrid` and edit mode is off, instantly (no animation). Contents: Grid columns `Auto,*,Auto,*,Auto` with margin `(10s,10s,10s,5s)`: eye (U+E890, tooltip `Hide widget — bring it back from the tray icon`), pencil (U+E70F, tooltip `Edit layout — drag a cell onto another to swap them`), lock (U+E785 / U+E72E; tooltips `Click to lock the widget position and size` / `Position locked — click to unlock`; Warning colour when locked). Buttons `16*scale` square, glyph `12*scale`, Dim brush, flat template (transparent background, hand cursor) (`WRX:10-26`, `WRV:410-413`). The header is not offset by the strip band (it exists only when the strip does not) (inferred).
- Rendering `LayoutPreview.PreviewDrag` mimics a drag with the same ghost/highlight calls (`WRV:781-795`).

#### 1.9 Positioning, persistence, clamping, monitors, DPI

Persisted fields touched by the window (`WS:29-110`): `WidgetX`, `WidgetY`, `WidgetSide`, `PositionLocked`, `WidgetVisible`; read: `WidgetSide, PositionLocked, WidgetX/Y, Accounts, Layout, StatusMode, ModelDial, PlanLine`.

- `PersistGeometry()` (`DWW:618-630`): returns if Left/Top NaN; `data = settings.Load()` (read-modify-write; `Load` can throw `IOException`/unparseable, in which case the exception goes out of a `DispatcherTimer` tick -> App's `DispatcherUnhandledException` handler logs and swallows, `APP:100-107`); saves `WidgetX = Left`, `WidgetY = Top + _stripAbovePad` (the PANEL's top, never the strip band's), `WidgetSide = _side`.
- Debounce (`:605-616`): 500 ms `DispatcherTimer`, restarted on every drag tick / resize tick / mouse-up / lost capture / edit-mode toggle; flushed on `Closing`. No flush on `Hide()`.
- `PersistWidgetVisible(false)` on the eye (saved immediately before `Hide()`, `:376-381`); `PersistPositionLocked` on lock change.
- After first show with CenterScreen nothing is persisted until the user moves/resizes or toggles edit mode.
- `ClampToScreen()` (`:342-350`): `area = WorkArea()`; `Left = max(area.Left, min(Left, area.Right - Width))`, `Top = max(area.Top, min(Top, area.Bottom - Height))`. Left/top win when the panel is bigger than the area. Called: constructor (after restoring X/Y), end of `ApplyLayoutAndSize` (BEFORE the redraw, deliberately, `:327-332`), `EditMode` setter. **Not** called on drag, not after display/DPI changes, not on a timer (inferred from grep). So a panel dragged off-screen stays off until the next relaunch/layout change.
- `WorkArea()` (`:356-358`): `System.Windows.Forms.Screen.FromPoint(new Point((int)Left, (int)Top)).WorkingArea` - the monitor containing (or nearest to) the window's TOP-LEFT point; only that monitor's work area is used, so a panel spanning monitors is moved inside the monitor holding its top-left; truncation `(int)` toward zero for negative coordinates.
- **DPI hazard (inferred, not tested by anyone)**: `Left/Top/Width/Height` are WPF DIPs, `Screen.WorkingArea` and the `(int)Left,(int)Top` point are physical pixels, and they are compared/mixed directly (`:348-349,356-358`). At 100% scale (this machine) nothing visible; at 125-200% the clamp bounds and the monitor lookup are wrong. Saved `WidgetX/Y` are DIPs of the window's DPI at save time (same mixing on restore). The Rust port must pick one unit; replicate-or-fix is a decision.
- Add/remove accounts (the one non-user size change): `Render()` detects `rows.Count != _accountCount`, rebuilds (`DWW:261-274`); `App` calls `RebuildLayout(count)` (`DWW:307-311`). Regression pinned in `DesktopWidgetWindowTests`: the clamp must run before the redraw, because the redraw once threw on a stale 4-row frame and left the window hanging off the right edge.
- `_last` caches the last frame so a rebuild can repaint without waiting up to 5 minutes (`DWW:276-303`).

#### 1.10 Show / hide

- Visible flag: `WidgetVisible` (default true). Startup shows only if true (`APP:262`).
- Eye (hover header or toolbar Hide): window saves `WidgetVisible=false`, `Hide()`, raises `HideRequested` -> `APP.OnWidgetHideRequested` -> edit mode off + menu refresh (`DWW:376-381`, `APP:455-464`).
- Tray "Show on desktop" (`APP:585-600`): saves flag, `Show()`/`Hide()`, edit mode off when hiding. No window before `CompleteStartup` (returns).
- Re-show: the `WM_WINDOWPOSCHANGING` hook re-forces HWND_BOTTOM; position unchanged.

### 2C §2. Dial rendering (all custom `OnRender`, `DIAL`, `DBASE`, `DTEXT`, `SDIAL`, `RING`)

Design size `DesignSize = 68`; per-control `scale = min(ActualWidth, ActualHeight)/68`; centre = control centre (`DIAL:78-83`).

- Constants at design size (`DIAL:83-88`, `SDIAL:62-66`, `RING:32-34`): `arcInset=4*scale`, `arcWidth=5*scale`, ring centre-line radius `size/2 - arcInset` (=30 at 68). Stroke is centred on that radius, round caps (`DBASE:40-41`).
- **Track**: full ellipse, `Theme.TrackBrush`, always drawn (even with no data, no-limit, dimmed) (`DIAL:90`).
- **Fill arc** (`DIAL:101-122`): only if `Fraction` has a value. `clamped = min(max(f,0), 0.9999)`; if `clamped <= 0` nothing is drawn (0% shows no dot; any >0 shows at least a round-capped dot). Start point = angle -90 deg (12 o'clock); end = `DialGeometry.AngleDegrees(clamped) = -90 + 360*clamped` (`C/Views/DialGeometry.cs:10`); points via `cx + r*cos(a)`, `cy + r*sin(a)` with screen Y down so positive angles run clockwise (`DBASE:34-38`). `StreamGeometry` figure not filled, not closed; `ArcTo(end, Size(r,r), rotation 0, isLargeArc = clamped > 0.5, SweepDirection.Clockwise, isStroked=true, isSmoothJoin=false)`. 100% is drawn as 0.9999 (a tiny gap hidden by the round caps).
- **Fill colour**: `Dimmed ? Theme.Dim : Theme.ColorFor(Thresholds.Level(fraction))`. Thresholds (`THR:14-17`): `fraction < 0.6` -> Ok (Accent); `< 0.85` -> Warning; else Danger. Boundaries are exact: 0.59 Ok, 0.6 Warning, 0.84 Warning, 0.85 Danger (test `ThresholdLevelTests`). The comparison is on the clamped 0..1 fraction (UsageMath.Fraction), not on the rounded display percent.
- **Text** (`DIAL:124-137`): three centred lines stacked as one block, vertically centred on the control centre, `spacing = 1` DIP (NOT scaled), each line's height = FormattedText line height (Consolas 1.1709 em):
  1. Title (e.g. `5H`) - Consolas SemiBold `8*scale`, Dim brush;
  2. Value - Consolas SemiBold `14*scale`, brush Text (Dim when dimmed): `"∞"` if `NoLimit`; else `UsageMath.PercentText(fraction)` if fraction has a value; else `"n/a"`;
  3. Remaining - Consolas Medium `8*scale`, Dim brush: `Remaining ?? "—"` (em dash U+2014).
  `FormattedText` culture is `InvariantCulture`, LTR, font size floored at 1 (`DTEXT:23-31`), uses `VisualTreeHelper.GetDpi(this).PixelsPerDip` (`DTEXT:21`).
- **Status dial** (`SDIAL`): full transparent rectangle first (so the whole square hit-tests, `:60`); track ring; then a FULL ring (not a fraction) in colour: `Dimmed || Unknown ? Dim : ColorFor(status)`; brush opacity 0.4 only when status is Unknown (`:70-74`). Text: `STATUS` (label style, Dim) over `ServiceStatusText.Label(status)` = `OK / SLOW / PARTIAL / DOWN / MAINT / —` in value style, **shrunk to fit** `maxWidth = size - 12*scale` by factor `max(0.6, maxWidth/textWidth)` (`DTEXT:36-44`); spacing 1. Tooltip `Claude service status — click to open status.claude.com`; Hand cursor. Click semantics (`SDIAL:93-136`): mouse down handles the event (so window drag does not start) and captures; mouse up within `[0,ActualWidth]x[0,ActualHeight]` of the same control after an armed press -> `WidgetLog.Write("-","browser-open","site=status-dial url=https://status.claude.com")` then `Process.Start(url, UseShellExecute=true)`; release outside or lost capture disarms. (Spec: opening on press was changed to click.)
- **Name ring** (`RING`): only the track ring at the same geometry; `IsHitTestVisible=false`; the name `TextBlock` is laid over it.
- **Repaint on theme change**: `DialControlBase` subscribes `Theme.Changed -> InvalidateVisual` on Loaded and unsubscribes on Unloaded (static event, avoids leaks as the grid is rebuilt on every layout change) (`DBASE:25-29`).
- Fonts (`THEME:106-116`): `Consolas`; Label SemiBold 8*s, Value SemiBold 14*s, Caption Medium 8*s. Comment: Consolas chosen as a monospaced substitute for SwiftUI `.monospacedDigit()`.

Pixel examples at default (scale 1, from `assets/widget.png`): 2% dial shows a short green dash at the top; 100% dial is a full red ring; "FABLE 95%" shows the small gap by the cap; status dial shows `STATUS`/`OK` in green.

### 2C §3. Themes and colours

#### 3.1 Choice and resolution
- `ThemeChoice {System, Dark, Light}` (settings field `Theme`, nullable; null = System) and `ThemeKind {Dark, Light}` (`C/Views/ThemeChoice.cs`). `ThemeChoices.Resolve(saved, system)` = saved Dark -> Dark, Light -> Light, System/null -> the `system` kind (`:24-30`).
- `APP.ApplyTheme()` (`APP:637-641`): `Theme.Apply(ThemeChoices.Resolve(data.Theme, SystemTheme.AppsKind), PanelOpacities.Resolve(data.PanelOpacity))`. Called at startup (before windows), on tray theme pick (saves `Theme` first), on tray opacity pick (saves `PanelOpacity` first), and on `SystemTheme.Changed` (`APP:643-665`). Tray icon digits and taskbar band follow `SystemTheme.TaskbarKind` ALWAYS, independent of the setting (`APP:518,748`).
- Taskbar vs apps theme: `AppsUseLightTheme` = what the panel/menu follow under System; `SystemUsesLightTheme` = taskbar colour (tray icon digits, band text) (`SYS:8-16`).

#### 3.2 System theme detection (`SYS`)
- Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, values `AppsUseLightTheme`, `SystemUsesLightTheme` (`SYS:19-22`).
- `Read(name)`: opens key; value is `int` and `!= 0` -> Light; missing key/value, non-int, or any exception -> **Dark** (`:55-66`).
- `AppsKind`/`TaskbarKind` are initialised by static initialisers (first access, read once) and updated only through the event.
- Change detection (`:29-53`): `Start(dispatcher)` subscribes `SystemEvents.UserPreferenceChanged` (WinForms/Win32 `WM_SETTINGCHANGE`-driven; delivered on a thread-pool/system-events thread). Handler ignores categories other than `General`; re-reads both values; if both equal the cached ones returns; else `dispatcher.BeginInvoke` -> sets both fields, raises `Changed` on the UI thread. `Stop()` unsubscribes (called on exit, `APP:1085`).
- `App.OnSystemThemeChanged`: `ApplyTheme()` (no-op if kind and opacity unchanged), `RefreshTrayIcon()`, `RenderTaskbarBand()`.

#### 3.3 `Theme` static (`THEME`)
- `Theme.Current` (Palette), `Theme.Kind`, `Theme.PanelOpacity` (default 0.82), `Theme.PanelBackgroundBrush`, event `Theme.Changed` raised after a swap on the thread that called `Apply`.
- `Apply(kind, opacity)` (`:52-63`): returns early if kind, palette and opacity are all unchanged; else swaps, builds `PanelBackgroundBrush = Palette.Panel` at alpha `PanelOpacities.Painted(opacity)` (frozen), calls `PublishResources()`, raises `Changed`.
- `PublishResources()` (`:67-75`): writes 4 brushes to `Application.Current.Resources`: keys `Theme.TextBrush`, `Theme.DimBrush`, `Theme.PanelBackgroundBrush`, `Theme.OverlayBackgroundBrush` (consumed by `WRX` via `DynamicResource`). Called at startup before the first view (`APP:74`).
- Consumers by mechanism: XAML `DynamicResource` (panel border, notice, header, pill, status line text, notice texts, sign-in button); code that stamped colours by hand re-applies on `Theme.Changed` (`WRV:265-288`: toolbar glyph Dim, both lock glyphs re-stated because local values beat DynamicResource, name labels Text, plan labels Dim, edit-chrome rebuilt); custom-drawn controls invalidate. A theme swap never moves/rebuilds anything.
- Panel metrics constants on Theme: `Padding(scale)=12*scale`, `Gap=10*scale`, `CornerRadius=22*scale` (`:119-123`).

#### 3.4 Palette values (`PAL:61-86`)
Opaque base colours (alpha derived below):

| Role | Dark | Light |
|---|---|---|
| Panel | `#1E2230` | `#F3F5F9` |
| Track | `#404557` | `#B7BECC` |
| Text | `#C7CCDE` | `#262B3A` |
| Dim | `#73788C` | `#6C7388` |
| Accent (threshold Ok, status Operational) | `#A6D189` | `#4E9A4C` |
| Warning (threshold Warning; Degraded; PartialOutage) | `#E5C890` | `#C9962A` |
| Danger (threshold Danger; MajorOutage) | `#E78284` | `#D2565A` |
| Info (Maintenance) | `#8AB4E6` | `#3877C8` |

Derived: `PanelAlpha = 0.82`, `OverlayAlpha = 0.92` (`PAL:22,25`). Alpha byte = `(byte)Math.Round(alpha*255)` (default `MidpointRounding.ToEven`): 0.82 -> 209 (`0xD1`), 0.92 -> 235 (`0xEB`) (`PAL:90-91`). Brushes: `TrackBrush`, `TextBrush`, `DimBrush`, `WarningBrush`, `AccentBrush` (opaque), `PanelBackgroundBrush` (0.82 default; replaced by `Theme.Apply` with the user opacity), `OverlayBackgroundBrush` (0.92; used by header, notice card, edit strip pill, so they stay legible when the panel is see-through), `PanelOpaqueBrush` (alpha 255; tray menu and rename dialog), `PanelBrush(alpha)`.
- `ColorFor(ThresholdLevel)`: Ok->Accent, Warning->Warning, Danger->Danger (`PAL:40-46`). `ColorFor(ServiceStatus)`: Operational->Accent; Degraded, PartialOutage->Warning; MajorOutage->Danger; Maintenance->Info; Unknown->Dim (`PAL:48-56`).
- Contrast notes in comments: Light track was lifted to `#B7BECC` (contrast 1.7 vs panel, same as dark track) because `#D3D8E3` was 1.3 and rings vanished (`PAL:71-77`).
- Other themed colours in scope: edit outlines Dim 1px dashed (3,3); drop target outline Text 2 px; ghost opacity 0.6; lock glyph Warning when locked; Sign in button chrome Dim at 0.35/0.55 opacity; the `LayoutPreview` taskbar plate `#202020` (preview only, `PREV:631`).

#### 3.5 Panel opacity (`C/Views/PanelOpacity.cs`)
- Setting `PanelOpacity` (nullable double). `Resolve(null or NaN)=0.82`; otherwise clamped to [0,1] (`:18-19`). `Presets = [1.0, 0.82, 0.6, 0.4, 0.2, 0.0]` (`:13`); `IsPreset(v, p) = |v-p| < 0.005` (tick the menu only for exact steps) (`:29`).
- `Painted(o) = max(o, 1/255)` (`:25`): the painted alpha byte is never 0 so the layered window keeps receiving mouse events (hover header, drag, edit) - the setting and tray tick stay the user's value.
- Which surfaces use the user's opacity: the `Theme.PanelBackgroundBrush` key, i.e. the panel border only (`WRX:68`). The Theme comment (`THEME:31-33`) says the toolbar pill also reads it, but the XAML binds the pill, header and notice to `OverlayBackgroundBrush`, the fixed 0.92 alpha (`WRX:85,107,158`); the code wins (comment appears stale, inferred). Dial and text stay opaque at every value.

#### 3.6 Rename window colours
Takes `Theme.Current` once at construction (no subscription): window and TextBox `Background = PanelOpaqueBrush`, `Foreground = TextBrush`, TextBox `BorderBrush = TrackBrush`, `CaretBrush = TextBrush` (`RNW:33,40-43`). Buttons keep default WPF styling; title bar is the system's.

### 2C §4. Formatting (exact strings)

#### 4.1 Durations - `UsageMath.RemainingText(resetsAt?, now)` (`UM:9-23`)
`seconds = (int)Math.Floor((resetsAt - now).TotalSeconds)`; null input or `seconds <= 0` -> `null`. `days = s/86400`, `hours = (s%86400)/3600`, `minutes = (s%3600)/60`. Output: days>0 -> `"{d}d {h}h"`; else hours>0 -> `"{h}h {m}m"`; else minutes>0 -> `"{m}m"`; else `"{s}s"`. Examples: `45s`, `10m`, `1h 0m`, `1d 1h`, `2d 0h`, `1h 12m`. No zero-padding, no culture (integers via interpolation), no larger units than days. `StatusLine` reuses it as `RemainingText(now, fetchedAt)` for the "ago" age.

#### 4.2 Percent - `UsageMath.PercentText(f)` (`UM:37-38`)
`(int)Math.Round(f*100 + 1e-9, MidpointRounding.AwayFromZero)` then `"%"`. The 1e-9 shift defeats binary-FP .5 cases (0.575 -> `58%`, test `TrayTextTests.RoundsRatherThanTruncates`). `UsageMath.Fraction(u) = clamp(u/100, 0, 1)` (`UM:27-28`); values >100 -> 1 -> `100%`.

#### 4.3 TrayText (`TT:12-42`)
- `LabelFor(title)`: `title.Length <= 4 ? title : title[..3]` -> `5H`, `7D`, `FAB` (from FABLE), `OPUS` (4 chars kept), `SON`, `MOD` (from `MODEL`), `FIVE`, `FIV`.
- `ValueFor(dial)`: `NoLimit` -> `"∞"` (U+221E); else fraction -> `PercentText`; else `"—"` (U+2014). (Dial control itself says `n/a` where the tray says `—`.)
- `Metrics(dials)`: dials whose `Title.Length > 0`, mapped to `(LabelFor, ValueFor)`, order preserved; empty input -> empty.
- `IconIndex(chosen, dials)`: the tray icon draws one number; if `chosen < dials.Count` and that dial `NoLimit` and the week dial (index 1) exists with a non-null fraction -> 1, else `chosen` (`:25-28`). Setting untouched.
- Tray tooltip text (APP, in scope for strings) (`APP:797-805`): `"Claude Usage Widget — " + join(" · ", "{Label} {Value}")`, or `"Claude Usage Widget"` when no metrics; plus `"\n{statusLine}"` if the status line is non-null; WebView failure: `"Claude Usage Widget — WebView2 error, see widget for details"`; startup retry: `"Claude Usage Widget — settings.json unreadable, retrying"`.

#### 4.4 BandText (`TT:49-83`)
`MaxNameLength = 8`: `Entries(rows)` -> per row `BandEntry(Name = DisplayName truncated to first 8 chars (UTF-16 code units) no ellipsis, Percent = ValueFor(BandDial(dials)), ResetsIn = row.SessionResetsIn)`. `BandDial` = `dials[0]`, except when `dials[0].NoLimit && IsSpent(dials[1])` -> `dials[1]` (week figure). `MetricEntries(row)` -> `TrayText.Metrics(row.Dials)` mapped to `BandEntry(Label, Value, null)` (3 columns, no reset times). `BandViews.Resolve(saved, count)` = `saved ?? (count <= 1 ? Metrics : Accounts)` (`C/Views/BandView.cs:19-20`).

#### 4.5 Status/words
`ServiceStatusText.Label`: OK, SLOW, PARTIAL, DOWN, MAINT, `—`. `LineLabel`: operational, degraded, partial outage, major outage, under maintenance, state unknown. Line prefix `service `. (`SS:59-92`).

#### 4.6 Dial derivation (`C/Views/DialModel.cs`)
- `DialModel(Title, Fraction?, Remaining?, NoLimit=false)`. `Make(key,title,snapshot,now)`: no bucket -> `(title, null, null)`; else `(title, UsageMath.Fraction(util), RemainingText(resetsAt, now))` (`:10-19`).
- `All(...)` (`:31-68`): always 3 dials: `5H` (`five_hour`), `7D` (`seven_day`), third = resolved per-model bucket (title `ModelBuckets.Label(key)` = suffix after `seven_day_` uppercased invariant, or `"MODEL"` when none / no snapshot). Model key: preferred key if present in snapshot, else first of `Available`: known order `seven_day_fable, seven_day_opus, seven_day_sonnet`, then unknown `seven_day_*` ordinal-sorted; excluded: `seven_day_oauth_apps`, `seven_day_overage_included` (`Usage/ModelBuckets.cs`).
  - **NoLimit**: if `!HasFiveHourLimit(profile)` and snapshot exists and has no `five_hour` bucket but has `seven_day` -> 5H dial becomes `Remaining="no limit"`, `NoLimit=true`, fraction null (`:45-46`). `HasFiveHourLimit` is false only for a claude_max profile whose tier (after stripping `default_`) is not `claude_max_5x` and which is not a raven/team (`Web/SubscriptionTier.cs:66-72`); null profile -> true.
  - **Spent week**: if `IsSpent(7D)` (`Fraction >= 1`) and 5H not NoLimit -> 5H's `Remaining` replaced by 7D's `Remaining` (percent unchanged) (`:56`). The per-model bucket never does this.
- `AccountRow.SessionResetsIn` (`:111-114`): `dials[0].NoLimit ? (IsSpent(dials[1]) ? dials[1].Remaining : null) : dials[0].Remaining`.
- Plan label (`Web/SubscriptionTier.cs`, displayed under the name): `null` when capabilities, tier and raven type are ALL null (not fetched; draws blank); else Team (raven) [+ title-cased suffix, e.g. `Team Premium`], `Ultra` (claude_max + `claude_max_20x`), `Max` (`claude_max_5x`), other max tiers `Max <Suffix>` or `Max or Ultra`, `Pro`, `Enterprise`, `Free` (tier `claude_ai` or empty), else prettified tier. Team becomes `Team Premium` if the snapshot has a `seven_day_fable` bucket (`:43-52`).

### 2C §5. Layout preview (`PREV`, developer switch)

- Trigger: env var `CUW_RENDER_PREVIEW` = directory; `RunIfRequested()` is called in `OnStartup` after `Theme.PublishResources()` and before the mutex (`APP:74-76`). Creates the directory, renders, then `Environment.Exit(0)` - the app never starts (`PREV:28-101`). Ignored when unset/whitespace. A second preview switch `CUW_RENDER_MENU` exists for the tray menu (out of scope).
- Output: `dark/`, `light/` (`Theme.Apply(kind)`), then `dark-opacity30/` (`Theme.Apply(Dark, 0.3)`), then restore `Theme.Apply(Dark, 0.82)` (`:83-98`).
- Fixed fixtures: four accounts `personal/work/low/shared` (Ultra via `claude_max_20x`, Team via raven, Max via `claude_max_5x`, `shared` with no plan fields) and snapshots (`Snap(five, seven, model)` with resets +72 min / +3 d): a1 `(2,100,95)`, a2 `(62,30,12)`, a3 `(44,15,8)`, a4 null; model bucket key `seven_day_fable` (`:42-59,680-688`).
- Eight base cases at side 228: `1-classic`, `1-grid-cell`, `3-grid-cell`, `4-grid-cell`, `3-row-above`, `2-column-below`, `2-grid-status`, `2-panelrow-hidden` (`:61-77,113-125`). Mode = `StatusModes.Resolve(null, layout)`.
- Extra PNGs: `edit-mode`, `edit-mode-drag` (degraded status), `edit-mode-after-swap` (SwapCells(0,3)), `edit-mode-drag-name-above` (strip above), `edit-mode-status-cell`; `toolbar-{min,default,max,wrap}-{base,above,below}`; `status-line-{operational,degraded}`; `model-hidden`; `padding-170`, `padding-207`; `plan-line-{above,cell,below}-{off,on,on-edit}` and `plan-line-unfetched-*`; `band-metrics`, `band`; folder `ultra/` (week-only Ultra: `plan-line-*`, band); README heroes `hero-classic.png` (side 300, scale 2) and `hero-rows.png` (side 260, scale 2).
- Mechanics: constructs a `WidgetRootView`, `ApplyLayout`, `SetContent`, `Measure/Arrange/UpdateLayout` at the declared size, `RenderTargetBitmap(w*scale, h*scale, 96*scale dpi, Pbgra32)` -> PNG (`:646-671`). Band preview: `TaskbarBandContent` on a `#202020` plate 560x40, padding 18 (`:615-642`).
- Purpose: judge layouts offscreen because the panel sits behind windows; README (lines 233-236, 292-307) describes it. Tests do not depend on it.

### 2C §6. Rename window (`RNW`, invoked by `APP:1001-1015`)

- `RenameWindow.Ask(current)` shows a modal dialog (`ShowDialog()`, no owner) and returns the text if `DialogResult == true`, else null (`RNW:70-74`). Doc comment says null also for "an empty string", but the window itself does not check - the caller does.
- Window: title `Rename account`, width 320, height auto (`SizeToContent=Height`), `NoResize`, `CenterScreen`, not in taskbar (`:23-28`). Content: StackPanel { TextBox (margin 12,12,12,6, initial text = current name), button row right-aligned (margin 6) with `Rename` (IsDefault -> Enter, sets `DialogResult=true`) and `Cancel` (IsCancel -> Esc), both width 90, margin 6 } (`:36-64`). On `Loaded`: focus the input and `SelectAll` (`:66`). No max length, no validation in the window.
- Caller (`APP:1001-1015`): `data = settings.Load()`, find account (return if gone), `name = Ask(account.DisplayName)`, `IsNullOrWhiteSpace(name)` -> abort; else `Save(data.WithAccount(id, DisplayName = name.Trim()))`, `ReloadAccountProfiles()`, `RenderWidget()`, `RenderTaskbarBand()`, `RefreshTrayMenuState()`. **Hazard (inferred)**: `data` is read BEFORE the modal loop and saved AFTER it; polls running during the dialog (dispatcher keeps pumping in the nested loop) can write account fields (retry state, plan fields) that this stale snapshot then overwrites. The Rust port should re-read before saving.
- Persisted: `Accounts[i].DisplayName`. No duplicate-name check anywhere in this path.

### 2C §7. Settings fields touched by in-scope code

`WidgetVisible`, `PositionLocked`, `WidgetSide` (150..340), `WidgetX`, `WidgetY` (nullable pair), `Layout` (`PanelFlow`, `Block{Flow,Name,Order}`), `StatusMode?`, `ModelDial?`, `PlanLine?`, `Theme?`, `PanelOpacity?`, `Accounts[].DisplayName`, read-only: `TrayAccountId`, `ModelBucket`, `BandView?` (`WS:29-110`). Nullable-by-design fields must not get defaults when written (comments on each). All saves are `settings.Load()` -> `with` -> `settings.Save()`; draws use `settings.Current()` (cached, non-throwing). Save/Load semantics (atomic replace, `.bad-<stamp>` copy, retry while held) live in `Settings/SettingsStore.cs` (outside scope).

### 2C §8. (moved to Section 4)

### 2C §9. Edge cases and failure handling (collected)

- Window/geometry: NaN Left/Top guarded everywhere; `PersistGeometry` refuses NaN; `EditMode` first-run picks "below" (`DWW:124`); `ClampToScreen` before `Draw` so a draw failure cannot leave the window oversized and off-screen (`DWW:327-335`); `Draw` skips when the cached frame's row count mismatches (`DWW:296`); `SetContent` throws on mismatch; app-level `DispatcherUnhandledException` swallows and logs (`APP:100-107`).
- Stuck drag/resize protection: three layers (LostMouseCapture, "button not pressed" check on every move, up handler) in both the window and the edit-mode cell drag (`DWW:515-529,587-595`; `WRV:244,666-670`).
- Settings unreadable at startup: window not created at all until accounts are known; retry 2 s for the first minute, then 30 s (`APP:163-188`).
- Zero accounts: panel sized for one block (`PanelMetrics.cs:196`); App creates a default account on first run if none (`APP:831-845`); `RenderWidget` returns if no tray account.
- Hand-edited layout: corrupt `Order` falls back to default order, then status/model cells re-applied; unknown enum name -> default enum value; `LayoutCycle` recovers a bad enum value to the first one.
- Theme: registry failure -> Dark; same theme re-applied = no-op; dial/WRV handlers detached on Unloaded.
- Opacity 0: painted alpha min 1/255 keeps hit-testing.
- Resize inside edit mode: strip band is recalculated (`ApplyStripBand`, `ReconsiderStripSide`, `DWW:313-336`).
- Status dial: click armed/disarmed logic; edit mode never opens it because the preview handler handles cell presses.
- Notice buttons/header: header only hit-testable when visible; toolbar clicks not swallowed by the edit preview handler (`IsInToolbar`).

### 2C §10. (moved to Section 5)

### 2C §11. (moved to Section 6)

---

## Part 2D: tray icon, tray menu, taskbar band, Win32

### 2D §0. Big picture

- The process has no main window. `ShutdownMode=OnExplicitShutdown` (`App.xaml:5`). It lives while the tray icon exists; Quit calls `Shutdown()`.
- The tray icon is a WinForms `NotifyIcon` plus a `ContextMenuStrip`, wrapped by `TrayIcon` (`TI:39-41`). There is no custom message window and no direct `Shell_NotifyIcon` call in the repo.
- The menu is an owner-painted WinForms `ContextMenuStrip` with a custom `ToolStripProfessionalRenderer` (`WMR`).
- The taskbar band is a WPF `Window` owned by `Shell_TrayWnd`. It is transparent (`AllowsTransparency`) and topmost, and positioned by `SetWindowPos`. Text is drawn by hand in `OnRender`.
- Single-instance guard: named mutex `"ClaudeUsageWidget.SingleInstance"`, `initiallyOwned: true`, no `Global\` or `Local\` prefix (`APP:85-91`). A second instance calls `Shutdown()` and returns from `OnStartup`. The mutex is held in a field for the process lifetime.
  - The two preview switches `LayoutPreview.RunIfRequested()` and `MenuPreview.RunIfRequested()` run BEFORE the mutex (`APP:76-77`).
  - Origin: live debugging on 2026-08-06 found two zombie instances fighting over band position and visibility (commit e81996b).
- Unhandled-exception policy (`APP:100-113`).
  - `DispatcherUnhandledException`: write widget log `("-", "unhandled", "dispatcher <Type>: <msg>")` and set `Handled = true`.
  - `System.Windows.Forms.Application.ThreadException`: log `"tray <Type>: <msg>"`. Menu click handlers run under WinForms' callback, so they need this separate hook.
  - `FireAndForget` (`APP:1067-1078`) catches and logs `"fire-and-forget <Type>: <msg>"`.
  - Net effect: a failing menu handler never kills the process.

### 2D §1. Tray icon

#### 1.1 Creation and lifecycle

- `new TrayIcon()` is created in `OnStartup` BEFORE settings are read and before accounts exist (`APP:119`). The icon and menu therefore exist while startup may still be waiting on `settings.json`.
- Constructor order (`TI:150-170`):
  1. `BuildMenu()` builds the whole menu.
  2. `ApplyMenuIcons()` runs after `Menu` is assigned, because it needs `Menu.DeviceDpi`.
  3. `Theme.Changed += OnThemeChanged`.
  4. `Menu.Opening += MenuOpening?.Invoke()`.
  5. `MenuPlacement.Attach(Menu)`.
  6. `_notifyIcon = new NotifyIcon { ContextMenuStrip = Menu, Visible = true }`.
  7. `SetIcon(TrayIconRenderer.Render(null, SystemTheme.TaskbarKind))`, i.e. the placeholder ring.
  8. `SetTooltip("Claude Usage Widget")`.
- No Click, DoubleClick, MouseClick or BalloonTip handlers exist anywhere (grep of `src`).
  - Left-click and double-click do nothing.
  - Right-click opens the `ContextMenuStrip`. This is WinForms `NotifyIcon` default behaviour, not code in the repo.
  - Keyboard activation also opens it. `MP:32-34` says "Opened covers every way the menu opens (right-click, keyboard on the tray icon)".
- Dispose (`TI:644-661`, called from `APP:1091`):
  1. unsubscribe `Theme.Changed`;
  2. `_notifyIcon.Visible = false`, then `Dispose()`;
  3. `Menu.Dispose()`;
  4. dispose and `DestroyIcon` the current icon.
  - Reason (`APP:1088-1090`): the NotifyIcon otherwise lingers visually until the next mouse movement.
  - `OnExit` order (`APP:1080-1105`): unsubscribe `PowerModeChanged`, `SystemTheme.Stop()`, stop the refresh timer, tray Dispose, widget `Close()`, band `Detach()` then `Close()`.
- Re-creation on Explorer restart (TaskbarCreated): there is NO code for it. A grep for `TaskbarCreated`, `WM_TASKBAR` and `RegisterWindowMessage` finds only framework DLLs. The port relies on WinForms `NotifyIcon` re-adding its icon itself (inferred: WinForms handles `TaskbarCreated` internally). A Rust port must register `"TaskbarCreated"` and re-add the icon explicitly.
- Windows 11 overflow: a comment (`TI:428-429`) says Windows hides icons in the overflow at its own discretion. This is why one icon describes one account (the "tray account") instead of four icons.

#### 1.2 Icon replacement and GDI handle ownership (`TI:172-187`)

- `SetIcon(Icon)` assigns `_notifyIcon.Icon`, remembers it as `_currentIcon`, then disposes the previous one and calls `NativeMethods.DestroyIcon(previous.Handle)`.
- Reason: `NotifyIcon.Icon =` does not take ownership; icons from `Icon.FromHandle(HICON)` must be destroyed manually or the process's GDI quota leaks.
- The handle is read before `Dispose()`.
- `NativeMethods.DestroyIcon` is a `user32!DestroyIcon` DllImport (`TI:664-671`).

#### 1.3 Tooltip (`TI:189-194`, `APP:767-806`)

- `SetTooltip` truncates to 127 characters (`text[..127]`) because `NOTIFYICONDATA.szTip` is 128 characters including NUL and WinForms throws at 128 or more.
- Initial text: `"Claude Usage Widget"`.
- Startup with unreadable settings: `"Claude Usage Widget — settings.json unreadable, retrying"` (`APP:171`).
- WebView2 check failed for the tray account: `"Claude Usage Widget — WebView2 error, see widget for details"` (`APP:786`). It sticks until the failure clears.
- Normal, from `UpdateTrayTooltip`:
  - If there are zero metrics the text is `"Claude Usage Widget"`.
  - Otherwise `"Claude Usage Widget — " + join(" · ", metrics.Select(m => $"{m.Label} {m.Value}"))`, e.g. `Claude Usage Widget — 5H 42% · 7D 18% · FAB 8%`.
  - If `StatusLine.Text(state, now, RetryPausedUntil)` is non-null, `"\n" + statusLine` is appended. The status goes on a separate line, not joined with ` · `.
- `metrics` come from `TrayText.Metrics(AccountDials.For(profile, snapshot, ModelBucket, now).Dials)`.
  - `TrayText.cs` filters dials with an empty title.
  - Label is `LabelFor(title)`: the title as-is if length ≤ 4, otherwise the first 3 characters. So "FABLE" becomes "FAB", "OPUS" stays, and the placeholder "MODEL" becomes "MOD".
  - Value (`ValueFor`): `"∞"` if `NoLimit`; `UsageMath.PercentText(fraction)` such as `"42%"` (`(int)Math.Round(f*100+1e-9, AwayFromZero)`); `"—"` (em dash) when there is no fraction.
- When the tooltip is refreshed:
  - `CompleteStartup`;
  - `OnStoresChanged` (any usage or status store `Changed`);
  - `OnModelBucketSelected`;
  - `OnAccountSelected`;
  - account removal.
  - It is not refreshed on timer or theme change.
- It always describes the tray account: `TrayAccount()` is the account whose id equals `TrayAccountId`, else the first, else null (`APP:873-877`). The update returns early if null.

#### 1.4 Icon drawing (`TIR`, `APP:507-519`)

- Entry: `Render(string? valueText, ThemeKind taskbar)` returns a `System.Drawing.Icon` wrapping a NEW HICON (via `bitmap.GetHicon()`) that the caller must destroy.
- Size = `SystemInformation.SmallIconSize` (`TIR:35`). That is 16x16 at 100%, larger at higher DPI (DPI source inferred: process is PerMonitorV2 plus system DPI). The app does not re-render on a DPI or monitor change; the icon is only redrawn on data, metric, account or theme events.
- Ink colour (`TIR:40`): `taskbar == Light` gives `#1B1B1B`, otherwise white.
  - It follows the TASKBAR mode (`SystemUsesLightTheme`), never the widget theme.
  - There is no threshold colouring (green/amber/red) in the tray icon; ink only.
- Bitmap: transparent background, `SmoothingMode.AntiAlias`, `TextRenderingHint.AntiAliasGridFit`. This is grayscale AA, deliberately not ClearType, to avoid coloured halos on transparency (`TIR:45-50`).
- Ring (placeholder, when `DigitsOnly` returns null): `Pen(ink, 2f)`; `DrawEllipse(inset=2, inset=2, W-4, H-4)` (`TIR:83-88`).
- Digits: `DigitsOnly(valueText)` (`TIR:75-81`) returns null if null or empty. Otherwise it does `valueText.TrimEnd('%')` and returns the result only if it contains at least one `char.IsDigit`.
  - So `"42%"` gives `"42"`, `"100%"` gives `"100"`, and `"—"` or `"∞"` give null, which draws the ring.
  - Commit a228d4c: the cold start feeds `"—"`, and the em dash was previously drawn into the icon.
- `DrawDigits` (`TIR:90-102`): `StringFormat` center and center; rect = the whole icon; `g.DrawString(digits, font, SolidBrush(ink), rect, format)`.
- `FitFont` (`TIR:110-124`): for `em = H*0.95` stepping `-0.5` while `em >= 6`, make `Font("Segoe UI", em, Bold, GraphicsUnit.Pixel)`.
  - Measure with `g.MeasureString(digits, font).Width`. The first font with `width <= W - 1` wins.
  - Otherwise fall back to a 6 px Bold font.
  - Note: GDI+ `MeasureString` includes its internal padding, so the fit is slightly conservative.
- Which metric is drawn (`APP:507-519`, `TI:286-291`, `TrayText.IconIndex`):
  - `TrayMetricKey` maps via `MetricIndex`: `"seven_day"` to 1, `"model"` to 2, anything else to 0 (`"five_hour"`).
  - `TrayText.IconIndex(chosen, dials)`: if the chosen dial `NoLimit` (an account without a 5-hour window) AND the week dial (index 1) has a non-null Fraction, show the week instead (index 1). The setting is not changed.
  - Value is `metrics[index].Value`.
  - Because `"∞"` has no digit, a NoLimit account whose week has no figure gets the ring.
- When the icon is redrawn: `CompleteStartup`, `OnStoresChanged`, `OnTrayMetricSelected`, `OnModelBucketSelected`, `OnSystemThemeChanged` (taskbar mode flip), `OnAccountSelected`, account removal.
- `SystemTheme.TaskbarKind` is read from the registry at type initialisation. `TrayIcon`'s ctor reads it before `SystemTheme.Start`. See §5.

#### 1.5 Persisted data touched by the tray icon

- `TrayMetricKey` (string, default `"five_hour"`).
- `TrayAccountId` (string?).
- `ModelBucket` (string?).
- The icon pixels themselves are never persisted.

### 2D §2. Tray menu

#### 2.1 Items, in order (`TI:375-546`, `ApplyMenuIcons` `TI:556-593`)

Root-menu order, with icon glyph codepoints (Segoe MDL2 Assets, PUA). Every top-level item below except "Model limit" gets an icon via `Set(caption, glyph)`. `Set` is skipped silently if the caption does not exist.

| # | Caption (exact) | Glyph | Kind | Action / handler |
|---|---|---|---|---|
| 1 | `Claude Usage Widget v{CoreInfo.Version} — GitHub` (em dash U+2014) | U+E774 | leaf | `OpenUrl("https://github.com/Reva1v/claude-usage-widget")`. Menu stays open. |
| 2 | `Report an Issue` | U+EBE8 | leaf | `OpenUrl(".../issues")`. Menu stays open. |
| 3 | `Check for updates and install…` (U+2026) | U+E896 | leaf | `menu.Close()` then `UpdateRequested`, then `FireAndForget(Updater.CheckAndInstallAsync(Shutdown))` (`APP:121`). |
| - | separator | | | |
| 4 | `Refresh now` | U+E72C | leaf | `RefreshRequested`, then `menu.Close()`. Handler `APP:124-130`. |
| 5 | `Sign in to Claude.ai…` | U+E77B | leaf | `SignInRequested`. Menu stays open. Handler `APP:131-135`. |
| - | separator | | | |
| 6 | `Tray shows` | U+E7C4 | submenu | `5H`, `7D`, `MODEL` or `MODEL (Fable)`; see 2.3. |
| 7 | `Model limit` | none | submenu | Visible only if more than one bucket is available; see 2.4. |
| 8 | `Accounts` | U+E716 | submenu | dynamic; see 2.5. |
| 9 | `Layout` | U+E80A | submenu | dynamic; see 2.6. |
| 10 | `Theme` | U+E790 | submenu | `System`, `Dark`, `Light`. |
| 11 | `Opacity` | U+E7B5 | submenu | `100 %`, `82 %`, `60 %`, `40 %`, `20 %`, `0 %`. |
| 12 | `Show on desktop` | U+E7F4 | check toggle | `ShowOnDesktopToggled(!Checked)`. |
| 13 | `Taskbar band` | U+E90E | check toggle | `TaskbarBandToggled(!Checked)`. |
| 14 | `Band position` | U+E8A0 | submenu | `Near tray` / `Left corner`. |
| 15 | `Band shows` | U+E7B3 | submenu | `Three metrics` / `All accounts`. |
| 16 | `Lock position` | U+E72E | check toggle | `LockPositionToggled(!Checked)`. |
| 17 | `Launch at login` | U+E7E8 | check toggle | `OnLaunchAtLoginClicked` (local, registry). |
| - | separator | | | |
| 18 | `Quit Claude Usage Widget` | U+E8BB | leaf | `QuitRequested`, then `Shutdown()`. |

- A `Telegram bot` item (glyph U+E99A, a robot) is referenced in `ApplyMenuIcons` (`TI:587-589`) but only exists on the personal branch. The `Set` call is a no-op on this branch. The spec says E8BD but the code uses E99A.
- Toggle items are created without `CheckOnClick`, so their `Checked` state changes only through `SyncMenuState`. Each click handler computes `!Checked` and raises the event. `Launch at login` is the exception: it flips itself in place.
- The menu is not closed by item clicks, except the explicit Closes noted above (see 2.8).

#### 2.2 What each command does (handler traced)

- GitHub and Issues links (`TI:633-642`): write widget log `("-", "browser-open", "site=tray url=<url>")`, then `Process.Start(new ProcessStartInfo(url){UseShellExecute=true})`. A failure there propagates to WinForms `ThreadException`, which logs it and does not crash.
- Update (`APP:121`, `Updater.cs`; another inventory presumably covers it in depth). What the menu sees:
  - Single flight: a `_busy` flag.
  - Logs `("-", "update", ...)`.
  - Asks the GitHub latest-release endpoint (`LatestRelease.Endpoint`).
  - If not newer: message box `"You have the latest version (v{ver})."`.
  - Source builds and non-writable install folders get a "Open the release page?" ask instead.
  - Otherwise it asks `"Version v.. is available (you have v..). Download and install it now? The widget will restart."`, downloads the win-x64 zip to `%TEMP%\ClaudeUsageWidget-update-<ver>`, writes `install.ps1`, starts `powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ... -ProcId -Source -Target -Exe -Work`, then calls `quit()`.
  - Failure: log, then message box `"Update failed: <msg>"`.
- Refresh now (`APP:124-130`):
  - No-op if `_statusStore` is null (startup not complete).
  - Refreshes only the tray account: `RefreshAccountAsync(account)` (cookie check, `Store.LoadAsync()`, export).
  - Then `_statusStore.LoadAsync()`.
  - Not all accounts, because the endpoint rate-limits.
- Sign in (`APP:131-135`): logs `(trayAccount DisplayName or "-", "sign-in-requested", "source=tray")` and calls `account.Session.OpenLoginWindowAsync()` for the tray account (only if one exists).
- Tray shows (`APP:563-569`): `settings.Load()`, `Save(data with {TrayMetricKey = key})` where key is `"five_hour"`, `"seven_day"` or `"model"`. Then `RefreshTrayIcon()` and `RefreshTrayMenuState()`. The tooltip is not refreshed.
- Model limit (`APP:571-583`): `Save(ModelBucket = key)` with null for Auto. Re-renders the widget, tooltip, icon and menu state.
- Accounts submenu actions:
  - Account select (`APP:958-968`): `Save(TrayAccountId = id)`, `RenderWidget`, `RenderTaskbarBand`, `UpdateTrayTooltip`, `RefreshTrayIcon`, `RefreshTrayMenuState`. The band follows the tray account (user report 2026-10-29).
  - Add account (`APP:970-999`): no-op if count ≥ `AccountLimits.Max` (4). Creates `AccountProfile(Guid.NewGuid().ToString(), "New account", null, null, 0)`, saves, builds a runtime, rebuilds the layout, renders the widget and band, refreshes the menu. Logs `(name, "sign-in-requested", "source=add-account")` and opens the login window.
  - Rename… (`APP:1001-1015`): modal `RenameWindow.Ask(currentName)`; empty or whitespace or cancel means nothing; otherwise `DisplayName = name.Trim()`; `ReloadAccountProfiles`, render widget and band, refresh menu.
  - Sign out and remove (`APP:1017-1048`): NO confirmation prompt. `runtime.Session.ClearBrowsingDataAsync()` first (failure is swallowed to Debug output). Then remove from settings (`TrayAccountId` set to null if it was the removed one) and from `_accounts`. Rebuild the layout and render the widget, band, tooltip, icon and menu.
- Layout submenu: `Classic` / `Account rows` call `OnPanelViewSelected` (`APP:669-675`): `Save(LayoutResolution.Apply(Load(), view))`, `_widgetWindow.RebuildLayout(accounts)`, `RenderWidget`, refresh menu. `Edit layout…` calls `SetEditingLayout(!_editingLayout)` (`APP:470-476`): sets `TrayIcon.EditingLayout`, `widget.EditMode`, and refreshes the menu. Edit mode is a session state, never persisted (`APP:28-30`).
- Theme (`APP:643-648`): `Save(Theme = choice)`, then `ApplyTheme()`. That does `Theme.Apply(ThemeChoices.Resolve(data.Theme, SystemTheme.AppsKind), PanelOpacities.Resolve(data.PanelOpacity))`, which raises `Theme.Changed` (the menu re-themes itself, see 2.7).
- Opacity (`APP:650-655`): `Save(PanelOpacity = value)`, `ApplyTheme()`.
- Show on desktop (`APP:585-600`): no-op if the widget window is null. `Save(WidgetVisible)`, widget `Show()` or `Hide()`; if hiding then `SetEditingLayout(false)`, else refresh the menu.
- Taskbar band (`APP:612-618`): `Save(TaskbarBandEnabled)`, `SetTaskbarBandVisible(enabled)`, refresh the menu.
- Band position (`APP:620-626`): `Save(BandPosition)`, `_bandWindow?.SetPosition(position)`, refresh the menu. It is stored even while the band is off.
- Band shows (`APP:628-633`): `Save(BandView = view)` (an explicit value, so it no longer follows the account count), `RenderTaskbarBand`, refresh the menu.
- Lock position (`APP:602-610`): no-op if the widget is null; `_widgetWindow.PositionLocked = locked` (the setter saves `PositionLocked`); refresh the menu.
- Launch at login (`TI:613-631`, `Autostart.cs`):
  - `desired = !Checked`; `Autostart.SetEnabled(desired)`; then `Checked = desired`.
  - On any exception: `Checked = Autostart.IsEnabled()` (re-read the registry rather than roll back), plus a `Debug.WriteLine`.
  - Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value name `ClaudeUsageWidget`, data `"<Environment.ProcessPath>"` (quoted). Enabled = the value exists AND is a `string`. Disable = `DeleteValue(..., throwOnMissingValue:false)`. It opens the key writable, else `CreateSubKey`, else throws.
  - The source of truth is the registry, not settings.json. `SyncMenuState` re-reads `Autostart.IsEnabled()` on every sync.
- Quit: `Shutdown()`.

#### 2.3 "Tray shows" (`TI:413-420`, `220-233`)

- Items `5H`, `7D`, `MODEL`. Exactly one is ticked, by `MetricIndex(TrayMetricKey)`. An unknown key ticks 5H, consistent with the icon.
- The third item's text is `MODEL` before any snapshot. Once a bucket resolves it is `MODEL (<TitleCase(label)>)`.
  - `ResolvedModelLabel = ModelBuckets.Label(ModelBuckets.Resolve(data.ModelBucket, snapshot))`, null if there is no snapshot.
  - `Label`: `seven_day_fable` gives `FABLE`; a key without the `seven_day_` prefix is upper-cased whole.
  - `TitleCase` gives first char upper, rest lower: `Fable`. The menu-only nicety; the dial keeps `FABLE`.

#### 2.4 "Model limit" (`TI:238-260`)

- `Visible = AvailableModelBuckets.Count > 1`. The list comes from `ModelBuckets.Available(trayAccountSnapshot)`, or empty with no snapshot.
- Items are rebuilt on every sync:
  - `Auto` (ticked if `SelectedModelBucket` is null or empty, click gives `ModelBucketSelected(null)`);
  - a separator;
  - one item per available bucket, labelled `ModelBuckets.Label(key)`. This stays SHOUTY ("FABLE", "OPUS"); no title-casing here, unlike 2.3. Ticked if `SelectedModelBucket == key`.
- If the stored bucket is not in the available list, nothing besides possibly Auto is ticked (only Auto's rule is "null/empty").
- Hidden entirely at 0 or 1 buckets because `Resolve` picks the only one anyway.

#### 2.5 "Accounts" (`TI:297-337`)

- Rebuilt on every `SyncMenuState`.
- One item per `AccountProfile`:
  - Text `DisplayName`.
  - Ticked if `Id == TrayAccountId`. Note: this uses the raw setting; if `TrayAccountId` is null or stale, nothing is ticked, even though `TrayAccount()` falls back to the first account.
  - Clicking selects it as the tray account.
  - It has its own nested submenu: `Rename…` and `Sign out and remove`.
- A separator if there is at least one account.
- `Add account…`:
  - Enabled iff `accounts.Count < AccountLimits.Max` (4, `SettingsMigration.cs:13`).
  - When at the limit, it is disabled with tooltip `"The widget shows at most 4 accounts."`.
- Comment says "Disabled rather than hidden: a missing item reads as a bug".
- After rebuild, `MenuChrome.StyleItems` re-applies padding to the new items, recursing into the nested Rename/Sign-out dropdowns.

#### 2.6 "Layout" (`TI:344-373`)

- Items:
  - `Classic` (tooltip `"A 2x2 grid of dials with the service status as a dial — the panel as it was before accounts."`);
  - `Account rows` (tooltip `"One row per account with its name, and the service status as a line under the rows."`);
  - a separator;
  - `Edit layout…`.
- Classic and Account rows are ticked per `PanelViews.Current(layout, status)`, which can be null, so a hand-arranged panel ticks neither.
- `Edit layout…`:
  - Ticked = `TrayIcon.EditingLayout`.
  - Enabled iff the widget is visible (`data.WidgetVisible`).
  - Tooltip when enabled: `"Toolbar on the panel: flows, name, status, lock, hide. Drag a cell onto another to swap them."`
  - Tooltip when disabled: `"Show the widget on the desktop first."`

#### 2.7 Other submenus

- Theme: `System` (tooltip `"Follow Windows' app theme."`), `Dark`, `Light`. Ticked by `data.Theme ?? ThemeChoice.System`.
- Opacity (`TI:455-464`): the presets come from `PanelOpacities.Presets = [1.0, 0.82, 0.6, 0.4, 0.2, 0.0]` (`PanelOpacity.cs`). Captions are `$"{Math.Round(value*100)} %"` (number, a space, the percent sign). The `0 %` item has tooltip `"Dials and text only, no panel behind them."`.
  - Ticked via `PanelOpacities.IsPreset(state.PanelOpacity, value)`, tolerance 0.005 absolute. A hand-edited in-between value ticks nothing. A null setting resolves to 0.82 first via `Resolve`.
  - `Resolve` clamps to [0,1], and NaN or null gives 0.82.
  - Painted alpha is `Math.Max(opacity, 1/255)` so a 0% layered WPF window stays hit-testable. This is the panel's concern but the menu tick stays the user's value.
- Band position: `Near tray` ticked iff `BandPosition != "left"` (any unknown value counts as tray); `Left corner` ticked iff `BandPosition == "left"`.
- Band shows: `Three metrics` (tooltip `"5H, 7D and the model for the tray account."`) and `All accounts` (tooltip `"Every account's name over its 5H figure and reset time."`). Ticked from `BandViews.Resolve(data.BandView, accountCount)`: a null setting resolves to Metrics with at most 1 account, else Accounts.

#### 2.8 State sync, enable/disable, keep-open rules

- `SyncMenuState(TrayMenuState)` (`TI:200-272`) is called:
  - at startup (`CompleteStartup` via `RefreshTrayMenuState`);
  - on every `Menu.Opening` (`MenuOpening` event, `APP:142`);
  - after every handler;
  - on every store `Changed` (`OnStoresChanged`).
  - So checkmarks stay right even if the state was changed elsewhere (the widget's own lock or eye button).
  - It returns early before `CompleteStartup` (`APP:528`, widget null), so the menu shows defaults until then.
- `TrayMenuState` (`TI:19-33`) fields: `TrayMetricKey`, `AvailableModelBuckets`, `SelectedModelBucket`, `ShowOnDesktop` (= `WidgetVisible`), `PositionLocked` (the widget's live value), `TaskbarBandEnabled`, `BandPosition`, `ResolvedModelLabel`, `Accounts`, `TrayAccountId`, `PanelView?`, `BandView` (resolved), `Theme` (`?? System`), `PanelOpacity` (resolved).
- Enabled and disabled conditions in the whole menu: only `Add account…` (account limit) and `Edit layout…` (widget hidden). Everything else is always enabled, including `Refresh now` and the toggles while signed out. `Model limit` is hidden, not disabled, with fewer than 2 buckets.
- Keep-open rule (`TI:517-531`, `608-611`): `menu.Closing` and the `DropDown.Closing` of every submenu that has items (or is `Model limit`) at build time cancel the close when `CloseReason == ItemClicked`. Esc, a click outside or loss of focus still close the menu, and so does an explicit `menu.Close()` (CloseCalled).
  - Explicit closes: `Refresh now` and `Check for updates and install…` (so a dialog does not appear under a menu holding input).
  - Gap noted by reading the code (inferred, not tested): `Accounts` and `Layout` have no items when the subscription loop runs. `Tray shows`, `Theme`, `Opacity`, `Band position` and `Band shows` are not empty and are covered; `Model limit` is covered by the explicit check. So `Accounts` and `Layout` dropdowns themselves have NO Closing subscription; only each per-account nested dropdown does. Clicks on the account entries and on the Layout items may therefore close the menu chain (inferred). Verify before porting the "stays open" rule uniformly.
- Origin: commits 3c820dc and 767a659 (2026-08-06).

#### 2.9 Custom rendering (`MC`, `WMR`, `MG`)

Chrome (`MenuChrome`):
- Font: `"Segoe UI Variable Text"` if installed (checked with `FontFamily.Families`), else `"Segoe UI"`, 9 pt (`GraphicsUnit.Point`) (`MC:15-17`). The font must be set before the first Show because item heights are measured then.
- Menu padding `Padding(2,2,2,2)` and item padding `Padding(2,4,2,4)` (left, top, right, bottom) (`MC:30-31`).
  - Reason: a Windows 11 row is about 28 px at 96 DPI; WinForms' own row is 20, so 4+4 padding gives 28.
  - Horizontal item padding is 2, not the original 12, because WinForms places the submenu off the ITEM's right edge, so every pixel of horizontal item padding became a gap before the submenu (23 px at padding 12, 3 px at 2, measured with `CUW_RENDER_MENU`; moving the submenu in `Opened` does not stick).
- `Attach` (root): `Renderer`, `Font`, `ShowImageMargin = true`, `ShowCheckMargin = false`, `Padding`, `DropShadowEnabled = true`, `StyleItems`, and `Opening += RoundCorners` (`MC:35-45`).
- `StyleItems` (recursive): per `ToolStripMenuItem` sets `Padding = ItemPadding` and `ImageScaling = None` (glyph bitmaps are pre-rendered at the menu's pixel size). If the item has dropdown items, it calls `StyleDropDown` and recurses. Empty submenus are skipped, because reading `.DropDown` would create one (`MC:54-72`).
- `StyleDropDown` (each submenu window): `Renderer`, `Font`, `Padding = MenuPadding`, and for `ToolStripDropDownMenu` `ShowImageMargin = true` and `ShowCheckMargin = false` (pinned so the tick is drawn in the image slot and radio items do not get a second gutter), plus `Opening += RoundCorners` (`MC:78-96`). It is called once per dropdown. `TI:541-543` calls it directly for the three initially-empty dynamic submenus (`Model limit`, `Accounts`, `Layout`).
- `RoundCorners` (`MC:101-113`): if `Environment.OSVersion.Version.Build < 22000` return; else `DwmSetWindowAttribute(drop.Handle, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref 2 /*DWMWCP_ROUND*/, 4)`. Exceptions are swallowed (square corners). The HRESULT is ignored (`PreserveSig`).

Renderer (`WidgetMenuRenderer : ToolStripProfessionalRenderer`, `RoundedEdges = false`) reads colours live from a mutable `Palette` (swapped by `Palette` setter, which also updates the colour table). Alpha is dropped: a menu window is opaque (`WMR:46-48`).
- Dark palette (`Palette.cs`): Panel `#1E2230`, Track `#404557`, Text `#C7CCDE`, Dim `#73788C`, Accent `#A6D189`, Warning `#E5C890`, Danger `#E78284`, Info `#8AB4E6`.
- Light palette: Panel `#F3F5F9`, Track `#B7BECC` (darker than the spec's first guess `#D3D8E3` because the dial rings vanished at 1.3:1 contrast; user, 2026-09-05), Text `#262B3A`, Dim `#6C7388`, Accent `#4E9A4C`, Warning `#C9962A`, Danger `#D2565A`, Info `#3877C8`.
- Colour use in the menu:
  - Background (`OnRenderToolStripBackground` and `OnRenderImageMargin`): Panel, `FillRectangle(AffectedBounds)`. No separate gutter strip.
  - Border: 1 px `Pen(Track)`, `DrawRectangle(X, Y, W-1, H-1)` (replaces the system border).
  - Hover (`OnRenderMenuItemBackground`): only when `Selected && Enabled`. A rounded rect `Rectangle(3, 0, ownerClientWidth - 6, item.Height)`, radius 4 (arcs of diameter 8), filled with `Color.FromArgb(153, Track)` (60% alpha), `SmoothingMode.AntiAlias`. The width comes from the menu's `ClientRectangle.Width`, not the item (WinForms lays items a few pixels wider than the window; the user saw an asymmetric margin on 2026-09-05; commit 7522570). A disabled hovered item falls to the base renderer, whose colour table maps `MenuItemSelected*` and `MenuItemBorder` to Track.
  - Text: `Enabled ? Text : Dim`. The text rectangle is re-centred vertically (below).
  - Arrow: `Selected ? Text : Dim` (the null item means not hovered).
  - Separator: 1 px `Pen(Track)` at `y = item.Height/2`, from x = 12 to `item.Width - 12`.
  - Tick (`OnRenderItemCheck` -> `DrawTick`): glyph U+E73E (CheckMark) in Accent, rendered at `box.Height` px and centred in the gutter. No box.
  - Colour-table overrides (only what the base renderer still paints): `ToolStripDropDownBackground`, `ImageMarginGradient*`, `CheckBackground`, `CheckSelectedBackground`, `CheckPressedBackground` = Panel; `SeparatorDark`, `SeparatorLight`, `MenuBorder`, `MenuItemSelected`, `MenuItemSelectedGradientBegin`, `MenuItemSelectedGradientEnd`, `MenuItemBorder` = Track.
- Checked items show the tick INSTEAD of the icon (`OnRenderItemImage` returns early if `Checked`). WinForms draws the check in the image slot and then the image over it. Suppressing the image (not redrawing the tick) is what keeps the tick single-weight (commit ff59013: redrawing doubled the AA glyph, so the four icon-bearing toggles looked bolder than the radio items).
- Vertical recentring (`CenteredInRow`): for text, icons and ticks, if `box.Height > 0 && box.Height < item.Height`, set `Y = (item.Height - box.Height)/2`. Reason (commit b3baa3f): vertical padding grows a WinForms row from the bottom, so text drew about 3 px high at 4+4 padding.
- Gutter centring (`CenteredInGutter`): after the row centring, `gutter = item.Owner.Padding.Left`; if `gutter > box.Width`, `X = (gutter - box.Width)/2`. The gutter is the drop-down's own left padding because `ToolStripDropDownMenu` widens it for the image margin.
- Icons are drawn with `DrawImage(image, box.X, box.Y, box.Width, box.Height)` at the recentred box.

Glyph bitmaps (`MenuGlyphs.Render(glyph, ink, sizePx)`):
- Cache `Dictionary<(glyph, ink.ToArgb(), sizePx), Bitmap>`, never evicted. Reason: `OnRenderItemCheck` runs on every hover frame, and a fresh Bitmap plus Font per frame would churn GDI handles.
- `sizePx < 1` is clamped to 1.
- Bitmap `sizePx x sizePx`, transparent, `SmoothingMode.AntiAlias`.
- Outline: `GraphicsPath.AddString(glyph, new FontFamily("Segoe MDL2 Assets"), Regular, emSize = sizePx*0.72f, PointF.Empty, StringFormat.GenericTypographic)`. `FillPath` after translating so the path's own bounding box is centred in the bitmap: `Translate((size - bounds.W)/2 - bounds.X, (size - bounds.H)/2 - bounds.Y)`.
- Reason (commit 1d3aad7): text layout centres the line box (ascent, descent, leading), which an icon glyph does not fill symmetrically, so it sat a pixel high.
- If the bounds are empty (font missing), nothing is drawn. The spec says the column stays reserved so text does not shift.

Menu icon application (`TI:556-593`):
- Ink = `Theme.Current.Dim` (RGB).
- Pixel size `px = round(16 * Menu.DeviceDpi / 96.0)`.
- Not re-rendered if the window moves to a monitor with another scale (a comment says the menu is short-lived and reopens near the tray).
- Items are matched to glyphs by caption text through a dictionary built from top-level `ToolStripMenuItem` text. Duplicate captions would throw in `ToDictionary` (inferred: none exist).
- Theme change (`TI:598-603`): `_renderer.Palette = Theme.Current`, `ApplyMenuIcons()`, `Menu.Invalidate()`. The menu is NOT rebuilt.
- The spec mentions "Text on hover via a second bitmap". That was NOT implemented: icons are Dim only; hover only changes the background and the arrow/text colour.

Spec versus final code drift (docs `2026-09-05-themes-and-tray-menu-design.md` versus code): the spec states 30 px rows, 12 px horizontal padding, an 18 px icon column, 10 px icon-text gap, menu padding 6, and a Lock glyph `E72E/E785`. Final code is 28 px rows, padding 2/4, menu padding 2, no explicit column sizes, hover inset 3, and glyph `E72E` only. The plan's `MenuChrome` showed padding (12,6,12,6) and menu padding 6, then the fix commits changed them. The code is the truth. The spec's `MenuMetrics` helper was never created.

#### 2.10 Placement (`MP`)

- NotifyIcon opens the menu through `ContextMenuStrip.ShowInTaskbar`-style placement: the menu's bottom edge is AT the cursor (on the taskbar), constrained to the screen bounds, not the work area (WinForms 8 source switches `WorkingAreaConstrained` off first). The lower rows overlap the taskbar, which, being topmost and just clicked, may paint over them (`MP:7-18`). Added in release 847a400 (2026-10-02).
- `Within(menu, workArea)` (pure, unit-tested; `MP:25-30`): `x = max(wa.Left, min(menu.X, wa.Right - menu.Width))`, `y = max(wa.Top, min(menu.Y, wa.Bottom - menu.Height))`. The size is unchanged. If the menu is larger than the area, the top and left edge win, because the lower and max clamps are applied in that order: first min with the far edge, then max with the near edge. The first rows are the ones read.
- `Attach(menu)` (ROOT menu only, not submenus; `MP:36-43`): `Opened += Place`, and `SizeChanged += (if Visible) Place`. SizeChanged covers a re-layout while open (the store refresh adds or removes model items, which WinForms grows downward from a fixed top-left corner, back into the taskbar).
- `Place` (`MP:45-58`):
  1. `screen = Screen.FromRectangle(menu.Bounds)`.
  2. If `!screen.Bounds.IntersectsWith(menu.Bounds)` return (the preview mode at -31000; a real menu is clamped to a screen).
  3. `target = Within(menu.Bounds, screen.WorkingArea)`; set `menu.Location` only if it differs.
  4. `SetWindowPos(menu.Handle, HWND_TOPMOST, 0,0,0,0, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)`. This always runs, even if the location is unchanged.
- Multi-monitor: the screen is chosen by where the menu rect is, and the work area is that screen's (so negative coordinates work). Taskbar edge: handled generically via the work area (bottom lifts the menu up, top pushes it down, a side taskbar pushes it sideways).

#### 2.11 Menu preview / developer switch (`MPV`)

- Env var `CUW_RENDER_MENU` = output directory (whitespace or empty means off). Run in `OnStartup` before the single-instance mutex, after `Theme.PublishResources()`.
- For `ThemeKind.Dark` then `Light`: `Theme.Apply(kind)`, then render into `<dir>/dark/` and `<dir>/light/`. Then `Environment.Exit(0)`.
- Per theme: a new `TrayIcon` (so a real NotifyIcon is created briefly, inferred) and `SyncMenuState` with a canned state:
  - `TrayMetricKey "five_hour"`, buckets `["seven_day_fable","seven_day_opus"]`, `SelectedModelBucket null`, `ShowOnDesktop true`, `PositionLocked false`, `TaskbarBandEnabled true`, `BandPosition "tray"`, `ResolvedModelLabel "FABLE"`;
  - accounts `a1/personal` and `a2/work`, `TrayAccountId "a1"`, `PanelView Classic`, `BandView Metrics`, `Theme System`, `PanelOpacity` default (0.82).
- A hidden 1x1 `Form` at (-32000,-32000) is the owner. `menu.Show(new Point(-31000,-31000))`, then `Application.DoEvents()`.
- Outputs:
  - `menu.png` (`DrawToBitmap` over a magenta fill);
  - `menu-zoom.png` (the first 3 rows at 4x, nearest-neighbour, with a red line through the middle of each row);
  - `menu-hover.png` (the `Layout` item selected);
  - `submenu.png` (the `Accounts` dropdown shown);
  - `metrics.txt` with menu size, padding, font and DPI, per-item `h`, `w`, `content`, `pad` and label (separators as `———`), submenu size, bounds of menu and submenu, and the gap (`drop.Left - menu.Right`).
- Origin: commit 35e8eb9 (2026-09-05): "the next change to it is judged from numbers rather than from a screenshot someone had to take by hand".
- The README says it writes menu.png, submenu.png and metrics.txt for both palettes; the actual set is larger (also zoom and hover PNGs).

### 2D §3. Taskbar band window (`TBW`)

#### 3.1 What it shows

- A row of columns, drawn in `TaskbarBandContent.OnRender` (`TBW:992-1127`). Each column: a name or label on the top line, and a value line below.
- Two views (`BandView`, `BandView.cs`; `BandText` in `TrayText.cs`):
  - Metrics view (three metrics): the tray account's `BandText.MetricEntries(row)`: `TrayText.Metrics(row.Dials)` mapped to `BandEntry(label, value, null)`: `"5H"`, `"7D"`, `<model label: FAB/OPUS/…>` with values like `42%`, `∞`, `—` and NO reset times. Used when `BandViews.Resolve(setting, accountCount) == Metrics` and a tray row exists.
  - Accounts view: `BandText.Entries(rows)`, one per account: `Name` = `DisplayName` truncated to 8 characters (hard cut, no ellipsis; `MaxNameLength = 8`), `Percent` = `TrayText.ValueFor(BandDial(dials))`, `ResetsIn` = `row.SessionResetsIn` (string or null).
  - `BandDial`: dials[0] (5H), except when dials[0] `NoLimit` AND `DialModel.IsSpent(dials[1])` (week fraction ≥ 1); then dials[1] is shown with its own countdown.
  - So a NoLimit account (Ultra) shows `∞` with no reset time. If its week is spent, it shows `100%` with the week's reset time (`"2d 0h"` in the test).
  - `Percent` is `—` for no data (never "0%").
- `BandViews.Resolve(saved, count) = saved ?? (count <= 1 ? Metrics : Accounts)`.
- The tray row choice (`APP:743-744`): the row whose `AccountId == TrayAccountId`, else the first row.
- Entries are pushed by `RenderTaskbarBand` (`APP:733-749`):
  - Returns if `_bandWindow` is null.
  - `AccountRow.ForAll(data.Accounts, SnapshotsById(), data.ModelBucket, now)`.
  - `_bandWindow.Render(entries, SystemTheme.TaskbarKind)`.
- When it is called: `OnStoresChanged`, `OnSystemThemeChanged`, `OnBandViewSelected`, `OnAccountSelected`, account add, rename and remove, and `SetTaskbarBandVisible(true)` (before Dock). There is NO timer-driven refresh, so a countdown string changes only when a store fires `Changed` (inferred: each poll).
- Screenshot `assets/taskbar-band.png` (viewed): two bars. "At the left corner: one account's three figures": `5H 2%`, `7D 100%`, `FAB 95%`, label small above and value bigger, bold monospace. "Beside the tray: every account's 5-hour window": `personal 2% 3d 0h`, `work 62% 1h 12m`, `low 44% 1h 12m`, `shared —`; name above, big percent with a small reset time to its right. Rendered, not photographed (README says so). Text on the dark taskbar is white with a shadow.

#### 3.2 Text drawing details (`TaskbarBandContent`)

- Constants: label font size 10 DIP, value font size 14 DIP, line spacing 1 DIP, column spacing 14 DIP, pair spacing (between percent and reset time on the bottom line) 5 DIP, shadow offset 1 DIP (`TBW:994-1003`).
- Font family `Theme.FontFamily` = `Consolas`; label weight `SemiBold` and value weight `SemiBold` (`Theme.cs:106-110`); `DialText.Format` builds `FormattedText` with `CultureInfo.InvariantCulture`, LTR, normal style and stretch, and `Math.Max(fontSize, 1)`. The pixels-per-DIP comes from `VisualTreeHelper.GetDpi(this).PixelsPerDip`.
- Ink and shadow follow the TASKBAR mode (`SystemUsesLightTheme`), not the widget theme (`TBW:1005-1022`):
  - Dark taskbar: ink white; shadow `ARGB(200,0,0,0)`.
  - Light taskbar: ink `#1B1B1B`; shadow `ARGB(160,255,255,255)`.
  - The shadow is a full second draw of the same column at `(+1,+1)` DIP, drawn first, with no blur. It is needed because on a transparent background over an arbitrary taskbar colour plain white would blend in.
- Column (`TBW:1035-1045`): `Width = max(name.Width, percent.Width + (resets ? 5 + resets.Width : 0))`; `Height = name.Height + 1 + max(percent.Height, resets?.Height)`. Reset time uses the label size (10) and weight, the percent uses the value size (14).
- Drawing (`TBW:1078-1120`): the block is vertically centred at `ActualHeight/2`; the column x runs from 0 stepping `width + 14`.
  - Name is horizontally centred on the column.
  - The bottom line is centred as a unit (`startX = centerX - BottomWidth/2`): percent first, then the reset time at `startX + percent.Width + 5`, vertically centred on the percent's box (NOT sharing a baseline, because the 4 pt smaller time looked too low).
- Measure (`TBW:1055-1076`): column widths are built with `Brushes.White` (measuring only); total = sum of widths + 14 per gap; height is `availableSize.Height` unless infinite, in which case `10 + 1 + 14 = 25`. `_columnWidths` is stored for `OnRender`.
- The root `Border` has horizontal padding 8 DIP each side, vertical 0 (`TBW:64, 223-228`). Window width = `content.DesiredSize.Width + 16` DIP.
- An empty entry list gives zero content width, so the window is 16 DIP wide, effectively an empty strip (inferred).

#### 3.3 Window technique (`TBW:20-55`, `205-292`, `405-425`)

- A top-level WPF `Window`, with `WindowStyle=None`, `ResizeMode=NoResize`, `ShowInTaskbar=false`, `ShowActivated=false`, `AllowsTransparency=true` (set in the constructor, before the HWND exists), `Background=Transparent`, initial `Height=40` DIP (`DefaultHeightDip`, a placeholder).
- `OnSourceInitialized`: `GetWindowLongPtr(GWL_EXSTYLE)` OR `WS_EX_NOACTIVATE (0x08000000)` OR `WS_EX_TOOLWINDOW (0x80)`, set back. It does NOT set `WS_EX_LAYERED` or call `SetLayeredWindowAttributes`: `AllowsTransparency` already makes it layered, and mixing `LWA_ALPHA` breaks WPF's `UpdateLayeredWindow` pipeline. It then adds an `HwndSource` hook for `WM_DPICHANGED` (0x02E0), which calls `Reposition()` and returns 0 (`handled` is left false).
- OWNER = `Shell_TrayWnd`: `SetWindowLongPtr(hwnd, GWLP_HWNDPARENT(-8), tray)` (`TBW:708`). For a top-level window this sets the owner, not the parent. Windows keeps an owned window above its owner.
- `HWND_TOPMOST` is applied EXACTLY ONCE per (re)dock via `SetWindowPos` in the "stale" branch, plus ad-hoc re-asserts on burial (see 3.7). It is NOT applied periodically: re-asserting topmost on the 5 s tick drags the owner+owned cluster (the taskbar) above shell context menus and clips them (NetSpeedTray issue #200: 23/23 repros with periodic reassertion, 0/23 without; verified live that a taskbar right-click menu stays intact).
- `Window.Topmost` is never used; z-order is only via `SetWindowPos`.
- WS_CHILD embedding into `Shell_TrayWnd` (TrafficMonitor technique) was the first implementation (commits 9f98a39, 9048bdb), deleted outright in 244bf21 (see 3.12).

#### 3.4 Positioning (`TBW:657-771`, `855-864`, `976`)

Every `Reposition()` (`RepositionCore`):
1. Own HWND sanity: if `ownHwnd == 0` or `!IsWindow(ownHwnd)`: stop the timer, unhook events, raise `Lost`, return.
2. `tray = FindWindow("Shell_TrayWnd", null)` looked up fresh every call (handles are never cached; Explorer can recreate it). If 0, return and keep the previous geometry and visibility until the next tick.
3. Probe and visibility (see 3.5). If the band is hidden for fullscreen, return (a hidden window is not repositioned).
4. Stale check: `GetWindowLongPtr(own, GWLP_HWNDPARENT) != tray` is "stale". A freshly created HWND has owner 0, so the first Dock is naturally stale. If stale, `SetWindowLongPtr(own, GWLP_HWNDPARENT, tray)`.
5. `notify = FindWindowEx(tray, 0, "TrayNotifyWnd", null)`; `dpi = GetDpiForWindow(tray)` (0 is treated as 96). Note the DPI is the taskbar window's, while the content is measured with the band window's own `PixelsPerDip`.
6. `GetWindowRect(tray)`: on failure return. `bandHeightPx = tray.Bottom - tray.Top` (the band is as tall as the taskbar).
7. Measure the content NOW (`_content.Measure(infinite, infinite)`), `contentWidthDip = DesiredSize.Width + 16`, `bandWidthPx = round(contentWidthDip * dpi / 96)`, `gapPx = round(8 * dpi / 96)`. Measuring here rather than in `Render()` is deliberate (live bug "F/5", see 3.12).
8. X:
   - `BandPosition == "left"`: `trayRect.Left + round(8 DIP * dpi/96)` (flush to the taskbar's left edge with an 8 DIP gap).
   - Otherwise ("tray", and any unknown value): `notifyRect.Left - gapPx - bandWidthPx` if `TrayNotifyWnd` exists and its rect is readable. Else the fallback `trayRect.Right - bandWidthPx - gapPx`.
   - `LeftPositionOffsetDip = GapDip = 8`, `OuterPaddingDip = 8`.
9. Y = `trayRect.Top`; size = `bandWidthPx x bandHeightPx` (physical pixels, set directly via `SetWindowPos`).
10. Anti-burial check `EnsureNotBuried(own, tray)` runs here for the non-stale case, BEFORE the geometry short-circuit (see 3.7).
11. Geometry short-circuit: if not stale AND `x == lastX && tray.Top == lastY && w == lastW && h == lastH`, return. Reason: every `SetWindowPos` on a layered window costs a composition, and the 5 s tick would otherwise pay it forever (measured idle CPU about 0.05-0.16% of one core over 60 s after the fix; commit 4b7327c).
12. Otherwise `SetWindowPos(own, stale ? HWND_TOPMOST : 0, x, tray.Top, w, h, stale ? SWP_NOACTIVATE : (SWP_NOZORDER | SWP_NOACTIVATE))`, and remember the geometry.
13. `_lastX/Y/Width/Height` start at `int.MinValue` so the first call always applies. They are not reset on Detach (stale is true after re-dock because the owner was cleared, so the SetWindowPos still runs).

Notes and limits (observed from code, not stated):
- Only the primary taskbar (`Shell_TrayWnd`) is used. `Shell_SecondaryTrayWnd` taskbars on other monitors are ignored.
- There is no taskbar-edge handling: it assumes a horizontal taskbar and uses `trayRect.Top` and its full height. A top taskbar works (Y = top); a vertical Windows 10 taskbar would place it nonsensically (inferred).
- No taskbar alignment (centred or left) or Widgets-button logic. `"left"` simply sits 8 DIP from the taskbar's left edge, and with Windows 11 centred icons this is empty space; with left-aligned icons it may overlap (the comment says "e.g. where the Widgets button sits if the user disabled it", `WidgetSettings.cs:48-50`).
- No special handling for an auto-hidden taskbar (inferred: not mentioned anywhere in the code). The probe samples WindowFromPoint at the taskbar rect, which for an auto-hidden taskbar is off or at the edge of the screen.
- DPI: the manifest declares PerMonitorV2 plus `gdiScaling` (`app.manifest`). `WM_DPICHANGED` triggers `Reposition()`. Physical pixels are used for SetWindowPos, and the DIP-to-pixel conversion uses the taskbar's DPI (`ToPhysical` = `(int)Math.Round(dip*dpi/96.0)`).

#### 3.5 Visibility: probe, hysteresis, fullscreen hiding

`ProbeTaskbarCover(ownHwnd, tray)` (`TBW:916-974`), ground truth instead of heuristics:
- If `GetWindowRect(tray)` fails: Visible.
- Sample points: x = `tray.Left + (int)(taskbarWidth * f)` for `f` in `ProbeFractions = [0.35, 0.55, 0.8]`; y = `(tray.Top + tray.Bottom)/2`.
- For each point:
  - `hit = WindowFromPoint(pt)`; 0 means Visible (return immediately).
  - `root = GetAncestor(hit, GA_ROOT(2))`.
  - If `root == tray || root == ownHwnd || root == 0`: Visible (return immediately). Our own band legitimately hangs above the strip, and WindowFromPoint passes through the transparent pixels of layered windows.
  - If `IsCloaked(root)` (DWMWA_CLOAKED = 14, `cloaked != 0`; a failed query counts as NOT cloaked): Visible (return immediately). A cloaked window is a ghost that DWM does not paint.
  - Otherwise remember `firstRoot`, and if later roots differ set `allSameRoot = false`.
- After the loop (all three points covered by foreign windows):
  - `allSameRoot && firstRoot == GetForegroundWindow()` gives `ObscuredByForeground` (the user entered fullscreen themselves: a game, a YouTube player).
  - Else `Obscured` (inactive covering windows, suspected service overlay such as ShareX).
- The taskbar counts as covered only when ALL three points are covered by foreign, non-cloaked windows (a single point may be legitimately covered by the volume or calendar flyout).
- This handles: maximized windows (do not touch the strip), fullscreen on another monitor (does not touch OUR taskbar), Minimize/Win+D (taskbar on top).

Hysteresis (`RequestFullscreenVisibility` `TBW:525-557`, `ApplyFullscreenVisibility` `563-569`, the timer's Tick `250-291`):
- `RepositionCore` calls `RequestFullscreenVisibility(cover != Visible, cover == ObscuredByForeground)`.
- The first determination after each `Dock()` (`_fullscreenStateEstablished == false`) applies immediately with no delay, and sets the flag.
- If `hidden == _hiddenForFullscreen` (already applied): cancel any pending transition, stop the timer.
- Else if the same transition is already pending: return, with NO re-timing. Edge: a slow-hide (1200 ms) pending is not shortened if a later probe says `ObscuredByForeground` (read from code).
- Otherwise: set pending, stop the timer, set the interval, start:
  - Show (`!hidden`): `ShowStabilityMs = 150`.
  - Hide, covered by the foreground window: `FastHideStabilityMs = 150`.
  - Hide, otherwise: `HideStabilityMs = 1200`.
  - History: 300 ms -> asymmetric 1200/300 (7bfbaed) -> 350 fast hide (bf0071a) -> 150/150 (2447b01). Several comments in the code still say "300ms" or "200 ms"; the constants are authoritative.
- Timer Tick: stop, take the pending value (return if none), clear pending. Then re-probe NOW (needs `ownHwnd != 0` and `FindWindow(Shell_TrayWnd) != 0`, else return and drop the transition): `fresh = ProbeTaskbarCover(...) != Visible`. If `fresh != hidden`, log `"stability expired: verdict flipped (<hidden> -> <fresh>), transition cancelled"` and return (cancel). Else `ApplyFullscreenVisibility(hidden)`.
  - Reason (commit 7bfbaed): ShareX lays an invisible full-width WinForms window over the taskbar for ~0.3-1 s on every other window's maximize/restore. The probe honestly says covered; applying that stale verdict after the overlay vanished made the band blink on every maximize, and no event re-runs the probe in that gap (destroying a window is not a LOCATIONCHANGE).
  - `InvalidOperationException` in Tick: stop the reposition timer, unhook, raise `Lost` (the same zombie-window rule).
- `ApplyFullscreenVisibility(hidden)`: no-op if equal; logs `"apply: hiddenForFullscreen A -> B"`; sets `_hiddenForFullscreen`; `Hide()` or `Show()` (WPF).
- After a Show-back via the timer there is no immediate `Reposition()`; the next tick or event does it (read from the code).
- `Dock()` conditional `Show()` (`TBW:316`): only if `!_hiddenForFullscreen`, because the Reposition inside `Dock` may have just synchronously hidden it. An unconditional Show here (before round 7) re-showed a window that had just been hidden for fullscreen.

Fullscreen history: three generations of heuristics were replaced by the probe (commit 5b3ba08): rect-vs-`rcMonitor`; `SW_SHOWMAXIMIZED` short-circuit via `GetWindowPlacement` (a maximized IntelliJ with custom chrome reported a near-monitor rect); a same-monitor and whole-monitor (4 px tolerance) invariant (IntelliJ "Toggle Full Screen Mode" on the OTHER monitor misled it). The related Win32 declarations (`GetWindowPlacement`, `MONITORINFO`, `MonitorFromWindow`, `GetMonitorInfo`, `MonitorDefaultToNearest`, `SwShowMaximized`) remain in `Win32.cs` UNUSED.

#### 3.6 Timers and WinEvent hooks

Timers (all WPF `DispatcherTimer` on the UI thread):
| Timer | Interval | Purpose |
|---|---|---|
| `_repositionTimer` | 5 s (`TBW:233`) | backstop `Reposition()`; started at end of `Dock()`, stopped by `Detach`/`Lost` |
| `_locationDebounceTimer` | 100 ms (`TBW:242`) | re-check after LOCATIONCHANGE; restarted by each event, `Reposition()` when quiet |
| `_visibilityStabilityTimer` | 150 or 1200 ms (set per request) | visibility hysteresis, see 3.5 |

Hooks (`HookFullscreenEvents`, `TBW:452-468`): installed in `Dock()`, idempotent (skipped if `_foregroundHook != 0`). All four are `SetWinEventHook(min, max, hmodWinEventProc=0, proc, idProcess=0, idThread=0, WINEVENT_OUTOFCONTEXT=0)`, system-wide, no DLL injection, delivered on the installing (UI) thread through its message pump.
| Hook | Event(s) | Handling (`OnWinEvent` `TBW:578-627`) |
|---|---|---|
| `_foregroundHook` | `EVENT_SYSTEM_FOREGROUND` 0x0003 | `Dispatcher.BeginInvoke(Reposition)` |
| `_minimizeHook` | `EVENT_SYSTEM_MINIMIZESTART` 0x0016 to `MINIMIZEEND` 0x0017 (one hook, range) | `BeginInvoke(Reposition)`. Fixes the band flickering on the Minimize button (foreground may not change right away). |
| `_reorderHook` | `EVENT_OBJECT_REORDER` 0x8004 | NOT a full Reposition: calls `CheckBuriedNow()` synchronously if `Dispatcher.CheckAccess()`, else `BeginInvoke`. A sync call removes the last visible buried frame (a BeginInvoke queue lagged a frame or two). No idObject filter. |
| `_locationHook` | `EVENT_OBJECT_LOCATIONCHANGE` 0x800B | Ignore unless `idObject == OBJID_WINDOW(0) && idChild == CHILDID_SELF(0)`; on the dispatcher, ignore unless `hwnd == GetForegroundWindow()`; restart the 100 ms debounce, then `Reposition()`. Catches F11 and borderless toggles with no foreground change. |
- The delegate `_winEventProc` is stored in a field so the GC cannot collect it while native code holds the pointer (classic P/Invoke trap).
- `UnhookFullscreenEvents()`: unhook all four (if non-zero), stop the debounce and stability timers, clear the pending state. Called from `Detach` and every `Lost` path.
- Measured latency (commit messages): foreground path under about 25 ms; LOCATIONCHANGE path about 206 ms with the old 200 ms debounce (now 100); with the 300 ms hysteresis it was about 286-334 ms and 520-530 ms (now lower: 150 ms steps).

#### 3.7 Burial detection (`EnsureNotBuried`, `CheckBuriedNow`, `TBW:798-853`)

- Problem (live 2026-08-06): the shell raises Shell_TrayWnd (or an app window) with `SWP_NOOWNERZORDER`, so the owned band stays BELOW the app, or even below its owner. An opaque taskbar then paints over the transparent band: it looks gone but is formally visible and not `Hide()`-d. Clicking the taskbar (a normal raise, WITH owned windows) brought it back, which confirmed it.
- Detection without a hit-test (WindowFromPoint cannot be used: the band is transparent): `GetWindowRect(own)`, then walk `GetWindow(hwnd, GW_HWNDPREV=3)` from the band upward, max 64 iterations. For each window `above`:
  - If `above == tray`: the owner is above the owned window, so the invariant is broken. Log `"buried under the taskbar itself — re-asserting topmost"`, `SetWindowPos(own, HWND_TOPMOST, 0,0,0,0, NOMOVE|NOSIZE|NOACTIVATE)`, return.
  - Skip if `!IsWindowVisible(above)`, if `GetWindowRect(above)` fails, or if its rect does not intersect the band's rect (strict inequalities on both axes).
  - Skip classes `"#32768"` (context menus) and `"Xaml_WindowedPopupClass"` (flyouts): temporary windows. Re-asserting over them is bug #200.
  - Otherwise log `"buried under <hwnd> cls=<class> — re-asserting topmost"` and re-assert `HWND_TOPMOST` once, then return.
- Callers: `RepositionCore` (non-stale only; the stale branch re-asserts topmost itself) before the geometry short-circuit, and the REORDER hook through `CheckBuriedNow` (returns if `_hiddenForFullscreen`, own HWND is 0 or invalid, or the tray is 0).
- Ordering bug history: it originally sat AFTER the geometry short-circuit and so almost never ran (the burial happens precisely when nothing moved; live z-order inspection showed the band 15 hops below `Shell_TrayWnd` with an empty diag log; commit 072208b). The tray-itself case was first skipped by the walk (commit 39aef95, 2026-08-06; band went dark when showing or hiding Telegram from the tray).
- Possible repeated re-assert: any visible foreign window above and overlapping that is not a menu or flyout triggers a re-assert on each check (inferred; for example an invisible overlay such as ShareX is `IsWindowVisible`).

#### 3.8 Diagnostic log (`TBW:889-907`)

- Env var, EXACT name: `CLAUDE_BAND_DIAG`; on only if its value is the string `"1"`. Read once into a `static readonly` at type initialisation, so changes need an app restart.
- File: `Path.Combine(Path.GetTempPath(), "claude-band-diag.log")` (`%TEMP%\claude-band-diag.log`), append only, never rotated, never cleared.
- Line format: `"{DateTime.Now:HH:mm:ss.fff} {message}"` + newline (`File.AppendAllText`, default UTF-8 encoding). `IOException` is swallowed.
- Messages, exact templates:
  - `probe f={fraction} pt=({x},{y}) hit=0 -> visible`
  - `probe f={fraction} pt=({x},{y}) hit={hit} root={root} cls={className} tray={tray} own={ownHwnd}` (HWNDs as decimal integers; `fraction` formatted with current culture, so `0,35` is possible on some locales).
  - `probe f={fraction}: root {root} is DWM-cloaked ghost -> visible`
  - `probe verdict: OBSCURED by foreground window (real fullscreen)`
  - `probe verdict: OBSCURED (all points foreign)`
  - `apply: hiddenForFullscreen {old} -> {new}` (`True`/`False`)
  - `stability expired: verdict flipped ({hidden} -> {fresh}), transition cancelled`
  - `buried under the taskbar itself — re-asserting topmost`
  - `buried under {hwnd} cls={cls} — re-asserting topmost`
- The probe lines are written on every probe (every Reposition: every 5 s plus each event), so the file grows quickly while the env var is on.
- README documents it (`README.md:259-260`).
- This is distinct from `%LOCALAPPDATA%\ClaudeUsageWidget\widget.log` (`WidgetLog`, 1 MB cap, not part of this scope except for the `browser-open` line).

#### 3.9 Lifecycle: Dock, Detach, Lost, SetPosition, Explorer restart

- `SetTaskbarBandVisible(true)` (`APP:682-702`):
  1. Lazily create `TaskbarBandWindow` once and subscribe `Lost`.
  2. `SetPosition(settings.BandPosition)` (no reposition yet, the timer is not enabled).
  3. `RenderTaskbarBand()` BEFORE `Dock()`, so `Reposition` sees the current content width.
  4. `Dock()`.
- `SetTaskbarBandVisible(false)` calls `Detach()` only (hides, never closes; the window survives disables).
- Startup: `if (settings.TaskbarBandEnabled) SetTaskbarBandVisible(true)` after the widget `Show()` (`APP:266`).
- `Dock()` (`TBW:301-318`): `EnsureHandle()`; reset `_hiddenForFullscreen = false` and `_fullscreenStateEstablished = false`; `HookFullscreenEvents()`; `Reposition()`; `Show()` unless hidden for fullscreen; `_repositionTimer.Start()`.
- `SetPosition(position)` (`TBW:327-332`): no-op if unchanged; stores; `Reposition()` immediately only if the reposition timer is enabled.
- `Detach()` (`TBW:338-371`): stop the reposition timer; unhook events (stops the other timers too); if the HWND is 0 or `!IsWindow` raise `Lost` and return (the zombie case); `SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, 0)` (clear the owner); `Hide()` (an `InvalidOperationException` here raises `Lost`). Idempotent.
- `Render()` (`TBW:375-403`): `_content.SetMetrics`; then `Reposition()` only if the HWND already exists. Fresh data almost always changes a column's width (`—` becomes `0% 1d 3h`), and previously the rightmost column was cut until the next 5 s tick (user, 2026-08-26, "low cut off").
- `Lost` event semantics: the native HWND disappeared outside Detach/Close. `App.OnTaskbarBandLost` (`APP:715-728`): drop the reference, `Close()` the dead shell (swallowing `InvalidOperationException`; orphaned hidden WPF windows had been found stacking up), and create a fresh one via `SetTaskbarBandVisible(true)` if the setting is still on. WPF cannot `Show()` again after its HWND vanished this way (`InvalidOperationException`).
- `RepositionCore`'s catch of `InvalidOperationException`: stop the timer, unhook, raise `Lost` (`TBW:638-655`).
- Explorer restart: while `Shell_TrayWnd` is missing, `RepositionCore` returns with geometry and visibility unchanged. Once it exists again, the owner differs from the current tray, which is "stale": rebind the owner and re-apply `HWND_TOPMOST` plus geometry on the next tick or event (within 5 s). With the old WS_CHILD design, Explorer restart destroyed the band's HWND (the crash history is in 3.12). With the owned design the window survives and self-heals via the staleness check; `Lost` is a backstop. TaskbarCreated is not registered (the band relies on polling every 5 s).
- Re-dock after Detach: `_hiddenForFullscreen`, `_fullscreenStateEstablished` reset; the owner was cleared, so it is stale and topmost is re-applied.
- Multi-instance: prevented globally (mutex, see section 0).

#### 3.10 Settings fields touched by the band

- `TaskbarBandEnabled` (bool, default false).
- `BandPosition` (string, `"tray"` default or `"left"`; any other value behaves as tray).
- `BandView` (nullable enum `Metrics`/`Accounts`; null follows the account count; written as an enum NAME by `TolerantEnumConverter`).
- Read-only: `TrayAccountId`, `Accounts`, `ModelBucket`.

#### 3.11 Edge cases and failure handling summary (band)

- Closed WPF window: `Handle` returns 0 rather than throwing, and `EnsureHandle()` on it throws `InvalidOperationException`; this once crashed the whole process unhandled, confirmed in the Windows Application event log (commit 9048bdb). Hence the try/catch plus `IsWindow` checks.
- Detach racing a dead HWND: `Detach()` on a zombie used to call `Hide()` and crash (commit f77b3c8). It now checks `IsWindow` and raises `Lost` itself.
- `FindWindow` failure keeps the previous state; `GetWindowRect` failures return early. The diag log swallows `IOException` only.
- P/Invoke bug: `GetClassNameNative` and `GetMonitorInfo` originally lacked an explicit `EntryPoint`, giving `EntryPointNotFoundException` on every call, silently swallowed by the dispatcher exception handler (commit 244bf21).
- DPI-split bug "F/5" (3.12).
- Stale `SetWindowPos` on every tick is avoided via the geometry short-circuit.

#### 3.12 Live-bug history (band and tray), consolidated

| When | What broke | Why the design is what it is |
|---|---|---|
| 2026-08-05 (9f98a39) | Original design: WS_CHILD embed into `Shell_TrayWnd` (TrafficMonitor), opaque window (cross-process layered black-rectangle risk), top-level overlay fallback. | Superseded. |
| 2026-08-05 (9048bdb) | On Windows 11 the Mica compositing made WS_CHILD content illegible. Tried WS_POPUP->WS_CHILD order, `WS_EX_LAYERED`+`LWA_ALPHA`, `HWND_TOP` reposition: no help for the attached path (pixel-sampled), but `LWA_ALPHA` fixed the overlay path. Explorer restart destroyed the child HWND; a closed WPF window's `Handle` returns 0 and `EnsureHandle()` threw, crashing the process (event log). Attach gap confirmed exactly 8 px. | Led to the `Lost` event, `IsWindow` checks and the Reposition try/catch. |
| 2026-08-05 (f77b3c8) | `Detach()` on a zombie window threw. | `Detach` guards with `IsWindow` and raises `Lost`. |
| 2026-08-06 (244bf21) | WS_CHILD abandoned. Bare Topmost overlay "periodically fell back behind the shell's Mica layer" and lacked a mechanism to stay above during shell activity. | Owned window (`GWLP_HWNDPARENT=Shell_TrayWnd`): normal top-level, real `AllowsTransparency`, always above its owner; `HWND_TOPMOST` set once, never periodically (NetSpeedTray #200). Not destroyed with the taskbar. White text with a 1 px shadow; `BandPosition`; EntryPoint P/Invoke bug found. |
| 2026-08-06 (4b7327c) | `"left"` sat 160 DIP in (inherited idea of trailing Start/Search/Widgets); the user meant literally the left edge. The third column clipped ("FAB/53%" drawn as "F/5") because content was measured before the HWND/DPI existed. Idle CPU from per-tick `SetWindowPos`. Labels "SES/WEEK" changed to "5H/7D". | Left offset = 8 DIP; measure inside `RepositionCore`; geometry short-circuit. |
| 2026-08-06 (b2b322c) | Hide and show lagged up to 5 s (tick-only detection). | Foreground and LOCATIONCHANGE hooks (system-wide, out-of-context); the 5 s timer stays as a backstop. |
| 2026-08-06 (0c21ee3, eabdc78, 5b3ba08) | Fullscreen heuristic false positives: maximized IntelliJ (custom chrome), IntelliJ Toggle Full Screen on the OTHER monitor, the Minimize button. | Replaced by the 3-point ground-truth probe; minimize hooks added. |
| 2026-08-06 (e81996b) | Two zombie instances running (earlier `dotnet run` launches) fought over the band. Orphaned hidden WPF windows accumulated after `Lost`. | Named mutex single-instance guard; `Close()` the dead window on `Lost`; `CLAUDE_BAND_DIAG` diag log added ("band visibility has glitched only on the live machine, and the main thing lacking was facts"). |
| 2026-08-06 (24671da, 39aef95, 072208b, bf0071a, 2447b01) | Burial: the shell raises the taskbar/app with `SWP_NOOWNERZORDER`; the band hides under an app window or under the taskbar itself (Telegram show/hide from the tray). The check sat after the geometry short-circuit and never ran. One visible buried frame remained. | `GW_HWNDPREV` walk with a topmost re-assert only on burial; menus/flyouts exempt; check moved before the short-circuit; REORDER hook handled synchronously; fast hide for foreground coverage; timers cut to 150/150/100 ms. |
| 2026-08-06 (7bfbaed) | ShareX's transient invisible overlay made the band blink on every maximize. | Re-probe at timer expiry, cancel if the verdict flipped; hide delay 1200 ms vs show delay 150 ms. |
| 2026-08-06 (c50a363) | Deadlock (SDL_app) exclusive-fullscreen ghost: DWM-cloaked window kept above `Shell_TrayWnd` for hours; the band hid for tens of seconds at a time. | `DWMWA_CLOAKED` check; cloaked equals no coverage; a failed query counts as not cloaked. |
| 2026-08-05 (a228d4c) | `"—"` was drawn into the tray icon on every cold start; an unknown `TrayMetricKey` left all three "Tray shows" ticks blank while the icon showed 5H. | `DigitsOnly` checks for any digit; `TrayIcon.MetricIndex` shared by menu and icon. |
| 2026-08-06 (3c820dc, 767a659) | WinForms closed the menu on the first toggle click. | Cancel `ItemClicked` closes on root and submenus; explicit `Close()` for actions. |
| 2026-08-26 | Band's rightmost column cut off until the next tick ("low cut off"). | `Render()` triggers `Reposition()`. |
| 2026-09-05 (35e8eb9, b3baa3f, 1d3aad7, ff59013, 7522570) | Menu rows 32 px tall; submenu gap 23 px (horizontal item padding); text 3 px high in the row; glyphs a pixel high in their bitmaps; ticks drawn twice (bold); hover highlight asymmetric. | Padding 2/4, recentring helpers, outline-based glyph centring, suppress icon not redraw tick, inset hover from the menu width; `CUW_RENDER_MENU` added to measure instead of guess. |
| 2026-10-02 (847a400) | The tray menu opened with its bottom on the taskbar and the lowest rows were overdrawn by the taskbar. | `MenuPlacement`: lift into the work area, plus `HWND_TOPMOST`. |
| 2026-10-29 (user report) | The band's three metrics kept the old account after the tray account changed until the next poll. | `OnAccountSelected` calls `RenderTaskbarBand`. |
| 2026-09-08 | An account-add click left no log trace. | `sign-in-requested source=add-account` log line. |

### 2D §4. Win32.cs inventory (`W32`, `internal static class Win32`)

All are `user32.dll` unless noted. "Used" counts come from a grep over `src/*.cs`.

Constants:
- `GwlExStyle = -20`: `GetWindowLongPtr`/`SetWindowLongPtr` index for extended style; used in band `OnSourceInitialized`.
- `GwlpHwndParent = -8`: sets the OWNER of a top-level window; band owner bind, clear in `Detach`, staleness read.
- `WsExNoActivate = 0x08000000`, `WsExToolWindow = 0x80`: band ex-style (no activation, hidden from Alt-Tab).
- `HwndTopMost = -1` (`nint`): z-order insert-after for topmost: band stale branch, burial re-asserts, `MenuPlacement`.
- `SwpNoZOrder 0x0004`, `SwpNoActivate 0x0010`, `SwpNoMove 0x0002`, `SwpNoSize 0x0001`: `SetWindowPos` flags; topmost re-assert is `NOMOVE|NOSIZE|NOACTIVATE`; normal geometry is `NOZORDER|NOACTIVATE`; the stale case is `NOACTIVATE` only.
- `GwHwndPrev = 3`: `GetWindow` command for the window above in z-order; burial walk.
- `WmDpiChanged = 0x02E0`: band `WndProc`.
- `MonitorDefaultToNearest = 2`: unused (0 references).
- `WinEventOutOfContext = 0`, `EventSystemForeground = 0x0003`, `EventObjectLocationChange = 0x800B`, `EventObjectReorder = 0x8004`, `EventSystemMinimizeStart = 0x0016`, `EventSystemMinimizeEnd = 0x0017`: the four hooks.
- `GaRoot = 2`: `GetAncestor` flag; the probe.
- `ObjIdWindow = 0`, `ChildIdSelf = 0`: the LOCATIONCHANGE filter.
- `SwShowMaximized = 3`: unused.
- `DwmwaCloaked = 14`: used inside `IsCloaked`.
- `DwmwaWindowCornerPreference = 33`, `DwmwcpRound = 2`: tray menu corners (`MenuChrome`).

Structs: `RECT {Left,Top,Right,Bottom}`, `POINT {X,Y}` (used by `WindowFromPoint` and the probe), `MONITORINFO`, `WINDOWPLACEMENT` (both unused).

Delegate: `WinEventDelegate(hWinEventHook, eventType, hwnd, idObject, idChild, idEventThread, idEventTime)` with `[UnmanagedFunctionPointer(StdCall)]`.

Functions:
- `GetWindow(hwnd, uCmd)`: the burial walk.
- `IsWindowVisible(hwnd)`: the burial walk.
- `SetWinEventHook(min, max, hmod, proc, idProcess, idThread, flags)` / `UnhookWinEvent(hook)`: the four hooks.
- `WindowFromPoint(POINT)` (POINT passed by value): the probe.
- `GetAncestor(hwnd, flags)`: root of the hit window.
- `GetWindowPlacement` (`SetLastError`): unused.
- `FindWindow(string?, string?)` (Unicode, `SetLastError`): finds `"Shell_TrayWnd"`.
- `FindWindowEx(parent, after, class, window)` (Unicode): finds `"TrayNotifyWnd"` under the taskbar.
- `GetWindowLongPtr` (`EntryPoint = GetWindowLongPtrW`) / `SetWindowLongPtr` (`SetWindowLongPtrW`) (`SetLastError`): ex-style and owner.
- `GetWindowRect(hwnd, out RECT)` (`SetLastError`): the taskbar, notify area, own rect, and above-windows.
- `SetWindowPos` (`SetLastError`): band positioning, topmost re-assert, and menu topmost.
- `IsWindow`: liveness of own HWND.
- `GetDpiForWindow` (Win10 1607+): the taskbar's DPI. 0 is treated as 96 by the caller.
- `GetForegroundWindow`: probe (foreground equals covering window) and the LOCATIONCHANGE filter.
- `GetClassNameW` as `GetClassNameNative` (`EntryPoint` explicit, Unicode); wrapper `Win32.GetClassName(hwnd)` uses a 256-char `StringBuilder`; used by the diag log and burial class exemptions.
- `dwmapi!DwmGetWindowAttribute(hwnd, attr, out uint, size)`: `IsCloaked` returns true iff HRESULT == 0 AND value != 0.
- `dwmapi!DwmSetWindowAttribute(hwnd, attr, ref int, size)` (`PreserveSig = true`): menu rounded corners; the HRESULT is ignored.
- `MonitorFromWindow`, `GetMonitorInfoW` (explicit `EntryPoint`): unused leftovers.
- `TrayIcon.cs` also has `NativeMethods.DestroyIcon(nint)` (user32).
- `DesktopWidgetWindow` has its own native class (the desktop widget; another inventory).
- Also in `SystemTheme`: registry and `Microsoft.Win32.SystemEvents.UserPreferenceChanged`; no P/Invoke.

### 2D §5. (moved to Section 4)

### 2D §6. (moved to Section 5)

### 2D §7. Docs consulted and takeaways

- `docs/superpowers/specs/2026-09-05-themes-and-tray-menu-design.md`: design for the Theme setting, the two registry values, `SystemTheme`, the palettes, the renderer/chrome spec, the "what follows the theme" table (panel/menu follow the Theme setting; tray digits and band text follow the taskbar mode), error handling (a missing registry value means dark; a failing `DwmSetWindowAttribute` is ignored; a missing glyph font leaves the icon column empty), and risks (`UserPreferenceChanged` arrives on a thread-pool thread; the boxed check requires overriding both `OnRenderItemCheck` and `OnRenderImageMargin`; the menu font must be set before the first Show).
- `docs/superpowers/plans/2026-09-05-themes-and-tray-menu.md`: Task 5 (icon and band follow the taskbar mode) and Task 6 (renderer, chrome, glyphs) show the first-draft code that the code later diverged from: `Font.DrawString` glyphs became path-based, menu padding 6 and item padding 12/6 became 2/2 and 2/4, hover inset 4 became 3 from the menu width, the `MenuColorTable` is mutable rather than a copy, and `ApplyMenuIcons` skipped `Update` and `Opacity` items that came later. The plan also says "the menu cannot be rendered offscreen", which `CUW_RENDER_MENU` later disproved.
- `docs/superpowers/specs/2026-08-05-windows-port-design.md` and `plans/2026-08-05-windows-port.md` (Task 13/16/17): the original menu list (Refresh now, Sign in, Show widget, Taskbar band, Lock position, Launch at login, Quit; Model limit; Tray shows), the original tray ring (a 16x16 GDI+ ring, pen 2 px), the original band design (child of `Shell_TrayWnd`, 8 px gap, 5 s reposition, label 10 px over value 14 px, monospace white) and the overlay fallback `TryAttach()`. Original tooltip `SES 42% · WEEK 18% · FAB 8%`, now `5H/7D`. Both documents are Russian; Windows target "Windows 11". Note the spec's "Show widget" is now "Show on desktop".
- README: tray menu list (matches the code, minus `Opacity` and `Check for updates` which are missing from the README bullets), the band description (owned window, probe, burial, diag env var), and the `CUW_RENDER_*` developer switches. The `assets/tray-menu.png` screenshot shows v0.2.7 and predates `Check for updates and install…` and `Opacity`: it lacks both.
- No `task-17-report.md` / `task-17-brief.md` file exists in the repo (code comments reference them: round 2, 5, 6, 7 findings). The round numbers in comments: "round 2" legibility/Mica, "round 4" bare topmost fallback, "round 5" positioning/clipping/sluggish, "round 6" 5 s detection lag, "round 7" flicker hysteresis, "round 6-8" fullscreen heuristics.

### 2D §8. (moved to Section 6)

### 2D §9. (moved to Section 7)

---

# 3. Settings schema (settings.json)

The full schema comes from part 2A §3.7: every field with its JSON name, type, default, clamping, enum tolerance and migration history. Part 2C §7 adds which view code reads and writes which fields. Store behaviour, file paths, atomic replace and the migration code are in 2A §3.

### From 2A §3.7

#### 3.7 `settings.json` full schema (CORE/Settings/WidgetSettings.cs)
Top level = `WidgetSettingsData` (record, `init` props; JSON names = C# names, PascalCase). Every property is always written (nulls included). Enums are strings (names).

| JSON key | C# type | Default (when absent) | Range / semantics / tolerance | Source line |
|---|---|---|---|---|
| `WidgetVisible` | bool | `true` | whether panel shows at start; persisted by eye icon / tray "Show on desktop" | 31 |
| `PositionLocked` | bool | `false` | panel drag lock | 32 |
| `ModelBucket` | string? | null (= auto: ModelBuckets.Resolve picks first available in preference fable>opus>sonnet then unknown keys alphabetically) | e.g. `"seven_day_fable"`; honoured only while the server still returns that bucket, else auto | 36 |
| `WidgetSide` | double | `170` (`WidgetSettings.DefaultSide`) | clamped on read by the window to [150, 340] (`MinSide`=150, `MaxSide`=340; `ClampSide` = min(max(side,150),340)); NaN is not valid JSON so a save with NaN throws (test) | 40; 144-153 |
| `WidgetX` | double? | null | panel top-left X (DIPs); null -> centered on first run. Applied only if BOTH X and Y non-null, then clamped to the work area of the screen containing the point (DesktopWidgetWindow.cs:222-229, 342-350) | 42 |
| `WidgetY` | double? | null | see above; stored value is panel top (window top + strip pad) | 43 |
| `TrayMetricKey` | string | `"five_hour"` | `"five_hour"`, `"seven_day"`, `"model"`; any other value behaves as five_hour (`TrayIcon.MetricIndex`, TrayIcon.cs:286) | 44 |
| `TaskbarBandEnabled` | bool | `false` | | 45 |
| `BandPosition` | string | `"tray"` | `"tray"` or `"left"`; any other value displays/behaves as "tray" (TrayIcon.cs:264-265) | 50 |
| `BandView` | `BandView?` (enum `Metrics`, `Accounts`) | null -> `Metrics` if accountCount <= 1 else `Accounts` (`BandViews.Resolve`) | "never default it here" | 56; BandView.cs |
| `Theme` | `ThemeChoice?` (enum `System`, `Dark`, `Light`) | null -> System (follow Windows apps theme) | | 61; ThemeChoice.cs |
| `PanelOpacity` | double? | null -> `0.82` (`PanelOpacities.Default`) | `Resolve`: NaN or null -> 0.82, else clamp to [0,1]; tray presets `[1.0, 0.82, 0.6, 0.4, 0.2, 0.0]`; a preset is "ticked" only if `abs(value-preset) < 0.005`; painted alpha is `max(opacity, 1/255)` (so a fully transparent layered window stays hit-testable) | 66; PanelOpacity.cs:9-29 |
| `TrayAccountId` | string? | null -> first account | id of the account the tray icon/tooltip/refresh/panel notice describe | 70 |
| `ExportDirectory` | string? | null = export OFF | directory for every account's `<slug>.widget.json` | 82 |
| `Layout` | `WidgetLayout?` | null -> derived (see below) | `{ "PanelFlow": enum LayoutFlow {Grid,Row,Column}, "Block": { "Flow": LayoutFlow, "Name": enum NamePlacement {Cell,Above,Below,Hidden}, "Order": [BlockItem...] } }`, `BlockItem` = `Name, FiveHour, SevenDay, Model, Status`. `Sanitize` makes Order usable: must have no duplicates and contain FiveHour AND SevenDay else reset to `[Name, FiveHour, SevenDay, Model]`; then adds/removes `Status` per StatusMode==Cell and `Model` per ModelDial==Shown (appended at the end when added) | 87; WidgetLayout.cs |
| `StatusMode` | `StatusMode?` (enum `Cell`, `Line`) | null -> `Cell` if Layout.Block.Order contains Status else `Line` | | 93; StatusMode.cs:29-32 |
| `ModelDial` | `ModelDial?` (enum `Shown`, `Hidden`) | null -> `Shown` | | 99; ModelDial.cs:22 |
| `PlanLine` | `PlanLine?` (enum `Hidden`, `Shown`) | null -> `Hidden` | | 106; PlanLine.cs:22 |
| `Accounts` | `IReadOnlyList<AccountProfile>` | `[]` | max 4 after load (extra dropped from the end); empty -> migration/BuildAccounts create `default` | 108 |
| `OrganizationId` | string? | null | LEGACY (pre-accounts). Read only by migration; cleared on load; keep in the record so old files are readable | 134 |
| `RetryPausedUntil` | DateTimeOffset? | null | LEGACY, same | 135 |
| `ConsecutiveRateLimits` | int | 0 | LEGACY, same | 136 |

Layout resolution when `Layout` and `StatusMode` are BOTH null (fresh install, or pre-setting file) (`LayoutResolution.Resolve`, CORE/Views/PanelView.cs:67-87): view = `PanelViews.DefaultFor(accountCount)` -> `Classic` for <=1 account else `Accounts`. Classic = `WidgetLayout(PanelFlow Grid, Block(Flow Grid, Name Hidden, Order [FiveHour, SevenDay, Model, Status]))`, StatusMode Cell. Accounts = `WidgetLayout.Default` = (Grid, Block(Grid, Cell, [Name, FiveHour, SevenDay, Model])), StatusMode Line. Otherwise `layout = data.Layout` (null -> Default) and `mode = StatusModes.Resolve`. `PanelViews.Current(layout, mode)`: (Hidden name, Cell status) = Classic; (non-hidden name, Line) = Accounts; else null (custom). Resolved result is then `Sanitize`d with the ModelDial switch.
Records with positional constructors deserialize missing members as default (null for refs, 0 for enum): a `Layout` missing its `Block` would hold null Block -> NullReference in Sanitize (inferred, untested).

##### `AccountProfile` (each element of `Accounts`; positional record, WidgetSettings.cs:14-24)
| JSON key | C# type | Default | Notes |
|---|---|---|---|
| `Id` | string | (required, no default) | doubles as WebView2 ProfileName. Allowed alphabet: ASCII letters/digits plus `# @ $ ( ) + - _ ~ .` and space; max 64 chars; may not end in `.` or space (doc comment lines 7-10; NOT validated in code in this scope). `"default"` = legacy account on the implicit profile (profile name null). New accounts: dashed GUID |
| `DisplayName` | string | (required) | shown in rows/tooltips/log label; used for export file slug; "New account" for fresh adds; "Claude" for first-run default; "default" for migrated |
| `OrganizationId` | string? | (required slot; null ok) | claude.ai organization id; cleared by `ClearCachedOrganization` on sign-in; two accounts may not share an org (`AccountWithOrganization` -> `UsageError.DuplicateAccount`) |
| `RetryPausedUntil` | DateTimeOffset? | (required slot) | per-account 429 pause end |
| `ConsecutiveRateLimits` | int | (required slot) | escalation counter (UsageStore ladder 1h / 6h / 24h, other agent) |
| `ExportPath` | string? | null | per-account export file override; wins over `ExportDirectory`; hand-edited only; null/whitespace = no override |
| `Capabilities` | IReadOnlyList<string>? | null | raw `/api/organizations` capabilities (e.g. `["claude_max","chat"]`); null = "never fetched" sentinel |
| `RateLimitTier` | string? | null | e.g. `default_claude_max_20x`, `default_raven` |
| `RavenType` | string? | null | e.g. `team` |
| `SubscriptionFetchedAt` | DateTimeOffset? | null | stamp of the last subscription-field fetch; null = fetch now; refresh interval 1 h; a future stamp also triggers refresh (`SubscriptionRefresh.ShouldRefresh`, CORE/Web/SubscriptionRefresh.cs) |
Missing `Id`/`DisplayName` in JSON would deserialize as null (inferred; no validation).

Helper methods on `WidgetSettingsData`: `Account(id)` = first with that id; `AccountWithOrganization(orgId, exceptId)` = first other account with same OrganizationId; `WithAccount(id, update)` rewrites just that account, an id no longer present changes nothing (does NOT resurrect a removed account, even if a poll finishes late).

Example minimal file (as written by test strings): `{"WidgetVisible": false, "Accounts": [{"Id":"a1","DisplayName":"work","OrganizationId":"org-1","ConsecutiveRateLimits":0}]}`.

### 2C §7. Settings fields touched by in-scope code

`WidgetVisible`, `PositionLocked`, `WidgetSide` (150..340), `WidgetX`, `WidgetY` (nullable pair), `Layout` (`PanelFlow`, `Block{Flow,Name,Order}`), `StatusMode?`, `ModelDial?`, `PlanLine?`, `Theme?`, `PanelOpacity?`, `Accounts[].DisplayName`, read-only: `TrayAccountId`, `ModelBucket`, `BandView?` (`WS:29-110`). Nullable-by-design fields must not get defaults when written (comments on each). All saves are `settings.Load()` -> `with` -> `settings.Save()`; draws use `settings.Current()` (cached, non-throwing). Save/Load semantics (atomic replace, `.bad-<stamp>` copy, retry while held) live in `Settings/SettingsStore.cs` (outside scope).

# 4. External contracts

These are the contract tables from each part. Overlaps are intentional: each part lists the contracts its own code touches.

### 2A §9. External contracts in my scope

#### 9.1 HTTP
| Who | Method + URL | Headers | Response fields used |
|---|---|---|---|
| Updater | `GET https://api.github.com/repos/Reva1v/claude-usage-widget/releases/latest` | `Accept: application/vnd.github+json`, `User-Agent: ClaudeUsageWidget/<version>` (no auth, no API-version header) | `tag_name`, `html_url`, `assets[].name`, `assets[].browser_download_url` |
| Updater | `GET <browser_download_url>` (the `*-win-x64.zip` asset; redirect to GitHub's CDN is followed by default HttpClient) | same UA (default headers) | zip stream, 5 min total timeout |
| App wiring only (other agent details) | `StatusApi.FetchAsync` -> `https://status.claude.com` (README:198-202: prefers "Claude Code" component status, falls back to page-wide) ; claude.ai web calls through WebView2 `claude.ai/api/organizations/<id>/usage` (README:134-135) | | |
| TrayIcon opens in default browser | `https://github.com/Reva1v/claude-usage-widget` and `.../issues` (TrayIcon.cs:43-44) | | |

#### 9.2 Files / registry / env / process
- Files: see 3.1 (settings.json, .tmp, .bad-*, widget.log(.1), export files `<ExportDirectory>\<slug>[-id[-n]].widget.json` or `ExportPath`, update work dir `%TEMP%\ClaudeUsageWidget-update-<v>\{release.zip, files\, install.ps1}`).
- Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ClaudeUsageWidget` (REG_SZ, quoted exe path; read/write); `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` `AppsUseLightTheme`, `SystemUsesLightTheme` (DWORD, read only).
- Env vars: `CUW_RENDER_PREVIEW`, `CUW_RENDER_MENU` (dev render modes, exit after render), `CLAUDE_BAND_DIAG=1` (band log to `%TEMP%\claude-band-diag.log`). Standard folders: `ApplicationData` (%APPDATA%), `LocalApplicationData` (%LOCALAPPDATA%), `Path.GetTempPath()` (%TEMP%).
- Mutex: `ClaudeUsageWidget.SingleInstance` (named, session-local).
- Process/command line: the app takes NO command-line arguments (`OnStartup` does not read `e.Args`). Updater spawns `powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File <install.ps1> -ProcId <pid> -Source <dir> -Target <dir> -Exe <exe> -Work <dir>`, hidden, not elevated; relaunch of the exe via `Start-Process -FilePath <exe>` (no args). Browser opens use `Process.Start(url) UseShellExecute=true`.
- Windows system events consumed: `SystemEvents.PowerModeChanged` (Resume only), `SystemEvents.UserPreferenceChanged` (General), Win32 band/taskbar stuff elsewhere.

### 2B §11. External contracts (consolidated)

| Contract | Detail |
|---|---|
| Sign-in page | `https://claude.ai/login` in a visible WebView2 on the account's profile; arbitrary `https://` pasted links accepted |
| Session cookie | name `sessionKey`, domain ending `claude.ai`, non-empty value, read with `CookieManager.GetCookies("https://claude.ai")` |
| Orgs endpoint | `GET https://claude.ai/api/organizations` (navigation). Body: JSON array of objects. Fields used: `uuid`/`id` (string), `capabilities` (string[]; `chat` required for pick; stored raw), `raven_type` (string; `team` preferred; stored raw), `rate_limit_tier` (string; stored raw). Other fields only logged by name via `Describe` (tierish values quoted: names containing tier/plan/billing/subscription, plus raven_type, capabilities). Known others seen in tests/comments: `name`, `billing_type`, `analytics_subscription_plan`, `plan_display_name`, `subscription_pause` |
| Usage endpoint | `GET https://claude.ai/api/organizations/{orgId}/usage` (navigation). Body: JSON object; buckets = members that are objects with numeric `utilization` (0-100) and optional `resets_at` (ISO-8601 string or epoch seconds number); optional `limits[]` with `kind:"weekly_scoped"`, `percent`, `resets_at`, `scope.model.display_name` |
| HTTP status semantics (navigation) | 401/403 -> unauthorized (clears cached org); 429 -> rate limited (no Retry-After available); other non-2xx -> network error `Claude.ai returned HTTP n.`; HttpStatusCode 0 + !IsSuccess -> navigation failure with WebErrorStatus (retry once if transient) |
| Status endpoint | `GET https://status.claude.com/api/v2/summary.json`, `Accept: application/json`; fields `components[].name == "Claude Code"`, `components[].status`, `status.indicator` |
| Scraped value | `document.body.innerText || document.body.textContent || ''` |
| Files | settings.json (APPDATA), widget.log(.1) (LOCALAPPDATA), WebView2 UDF `LOCALAPPDATA\ClaudeUsageWidget\profiles\default` |
| Win32/WebView2 pieces | hidden host HWND with WS_EX_NOACTIVATE\|WS_EX_TOOLWINDOW, off-screen -32000,-32000, size 0; controller `IsVisible=false`; profile `ClearBrowsingDataAsync`; `MemoryUsageTargetLevel` Normal/Low |
| Mutex | named mutex `ClaudeUsageWidget.SingleInstance` (App.xaml.cs:85-86) |

### 2C §8. External contracts in scope

Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` DWORD values `AppsUseLightTheme`, `SystemUsesLightTheme` (read only; missing = dark).
Win32 / OS behaviours relied on:
- `GetWindowLongPtrW` / `SetWindowLongPtrW` with `GWL_EXSTYLE (-20)`; `WS_EX_NOACTIVATE 0x08000000`, `WS_EX_TOOLWINDOW 0x00000080`.
- `WM_WINDOWPOSCHANGING 0x0046`, `WINDOWPOS` struct, `HWND_BOTTOM = 1`, `SWP_NOZORDER = 0x0004`.
- Layered window semantics (`AllowsTransparency`): alpha-0 pixels not hit-testable.
- `System.Windows.Forms.Screen.FromPoint(...).WorkingArea` (monitor + work area lookup; uses `MonitorFromPoint`/`GetMonitorInfo`, inferred).
- `SystemEvents.UserPreferenceChanged` category `General`.
- `SystemParameters.MinimumHorizontalDragDistance/MinimumVerticalDragDistance` (SM_CXDRAG/SM_CYDRAG).
- `Process.Start(UseShellExecute=true)` for `https://status.claude.com`.
- Segoe MDL2 Assets and Consolas fonts must be installed (glyph codepoints listed in 1.8; a missing glyph renders as a hollow box - comment `WRV:290-292`).
- Per-Monitor-V2 DPI manifest.
- Env vars: `CUW_RENDER_PREVIEW`, `CUW_RENDER_MENU`.
- URLs: `https://status.claude.com` (`SDIAL:24`).
- Logging: `WidgetLog.Write("-", "browser-open", "site=status-dial url=...")` (`SDIAL:142`).

### 2D §5. External contracts

Win32 and shell:
- Window class names: `"Shell_TrayWnd"` (primary taskbar), child `"TrayNotifyWnd"` (notification area), flyout class `"Xaml_WindowedPopupClass"` and context-menu class `"#32768"` (exempt from burial).
- GWLP_HWNDPARENT owner binding; `GW_HWNDPREV` z-order walk; `WM_DPICHANGED`.
- WinEvents: FOREGROUND, MINIMIZESTART/END, REORDER, LOCATIONCHANGE (system-wide, out-of-context).
- DWM: `DWMWA_CLOAKED` (14), `DWMWA_WINDOW_CORNER_PREFERENCE` (33) with `DWMWCP_ROUND` (2), gated by `Environment.OSVersion.Version.Build >= 22000`.
- NotifyIcon (WinForms): tooltip limit 127 characters; the `TaskbarCreated` re-add is done by WinForms.
- DPI manifest: PerMonitorV2, `dpiAware true/PM`, `gdiScaling true`, `supportedOS` GUIDs for Win 7 to 10/11.

Registry:
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`: `AppsUseLightTheme` (DWORD int; "Default app mode"; drives the widget panel and the menu when Theme=System) and `SystemUsesLightTheme` (the taskbar's own mode; drives the tray digits and the band text always). Nonzero int means Light; missing, non-int or exception means Dark (`SystemTheme.cs`).
  - `SystemTheme.Start(dispatcher)` subscribes `SystemEvents.UserPreferenceChanged` with category `General` only, re-reads both values, and returns if neither changed. Otherwise it hops with `Dispatcher.BeginInvoke` and raises `Changed` (the handler runs on the UI thread). The event fires on a thread-pool thread (`OnUserPreferenceChanged`).
  - `AppsKind` and `TaskbarKind` are static properties with initialisers that read the registry at type initialisation, before `Start`.
  - `SystemTheme.Start` happens in `CompleteStartup`, after the tray icon already rendered its first ring.
  - `OnSystemThemeChanged` -> `ApplyTheme()`, `RefreshTrayIcon()`, `RenderTaskbarBand()`.
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ClaudeUsageWidget` = `"<exe path>"` (autostart).

Environment variables:
- `CLAUDE_BAND_DIAG=1` (exact string `1`) -> `%TEMP%\claude-band-diag.log`.
- `CUW_RENDER_MENU=<dir>` -> menu preview (section 2.11).
- `CUW_RENDER_PREVIEW=<dir>` -> layout preview (out of my scope).

Other:
- Named mutex `ClaudeUsageWidget.SingleInstance` (session-local).
- Files: `%APPDATA%\ClaudeUsageWidget\settings.json` (settings), `%LOCALAPPDATA%\ClaudeUsageWidget\widget.log` (widget log), `%TEMP%\claude-band-diag.log`.
- URLs: `https://github.com/Reva1v/claude-usage-widget` and `/issues` (tray links), plus the update endpoint (`LatestRelease.Endpoint`, in Core).
- Fonts the visuals depend on: `Segoe UI` (tray digits, Bold), `Segoe UI Variable Text` (menu, if present, 9 pt), `Segoe MDL2 Assets` (menu glyphs), `Consolas` (band text).

# 5. Test inventory

Test classes by area, each with a one-line summary of the behavior it pins down. The whole suite: 33 classes in `Tests/ClaudeUsageWidget.Core.Tests` and 3 in `Tests/ClaudeUsageWidget.App.Tests`, plus the `Sta.cs` helper. README claims 473 Core and 15 App tests (not re-counted).

### 2A §10. Tests in scope (class -> what it pins)

All xUnit 2.9.3 (`xunit.runner.visualstudio` 4.0.0, `Microsoft.NET.Test.Sdk` 18.10.1; Core tests also `coverlet.collector` 10.1.0). Core.Tests TFM net10.0; App.Tests TFM net10.0-windows with UseWPF + UseWindowsForms. README claims 473 Core tests + 15 App tests (README:310-311; not verified by running).

- **CoreInfoTests**: `ParseVersion` strips `+build` suffix ("1.2.3+abcdef1234" -> "1.2.3"); null / "" / "+abcdef" -> "unknown"; `CoreInfo.Version` never empty.
- **HttpValidationTests**: 200/204 pass; 401 and 403 -> Unauthorized; 429 with Retry-After 600 -> RateLimited(600); 429 without -> RateLimited(null); 500 -> Network("HTTP 500").
- **LatestReleaseTests**: picks the `-win-x64.zip` asset among others (strips `v` from tag, page URL kept); release with empty assets still parses with null AssetUrl; not-a-release payloads are null (`{"message":"Not Found"}`, empty tag_name, missing html_url, `[]`); `IsNewer` numeric comparison (0.2.10 > 0.2.9, "v0.3.0" > "0.2.10", equal and older false, "unknown"/"garbage" false).
- **LogLineTests**: exact line format with `+03:00` offset and milliseconds; newline in details folded to a space; CR and LF each folded separately in ALL arguments (`"wo\r\nrk"` -> `"wo  rk"`).
- **SettingsMigrationTests**: legacy single-account fields carried into `default` account (org id, retry pause, count) and `TrayAccountId = "default"`; legacy fields cleared; migrates even with no org id; caps account list at `AccountLimits.Max` (4) from the end (7 -> 4, a0 first); already-migrated file returned as the same instance.
- **SmokeTests**: placeholder `Assert.True(true)` (infra runs).
- **UsageExportTests** (largest): both windows + reset stamps; units (atMs ms, resets s: `atMs == fiveResetAt*1000`); null snapshot -> null; snapshot with neither window (only `seven_day_opus`) -> null; one window enough (other null); Ultra (week only) exports `"fiveHour":null` and `"fiveResetAt":null`, never 0; model bucket = what the dial shows (fable first; preferred bucket wins; unavailable preferred falls to the 4 nulls when no model bucket); `availableModels` order fable, sonnet and `[]` (not null) when none; JSON camelCase keys for all 5 v1 + 5 v2 + `availableModels` + `plan`; round trip keeps names/units; `plan` key always present and `"plan":null` when none (not omitted); plan label travels from raw fields (raven/team -> "Team", claude_max 20x -> "Ultra"); `FileNameFor` slug cases (personal, " Work ", "work.shared@example", "--"->id, ""->id); `ExportPathFor`: plain names for unique accounts, second same-slug account gets `-<id>`, account with own ExportPath doesn't take plain name, account missing from settings gets `-gone`, matched by id not position, names come from settings not the caller, explicit ExportPath (case-insensitively `C:\X\Work.widget.json`) blocks another account's plain name, later account's own path still reserves, suffixed-name collision (`personal-id-2` vs account named that) resolved to `personal-id-2-id-3`; `WritesOver` refuses settings.json in several spellings (case, `..`); no override and no directory -> null.
- **WidgetSettingsAccountTests**: `Account(id)` finder; `AccountWithOrganization` ignores the excepted id; `UsageError.DuplicateAccount` is Unauthorized kind and names the other account, default Unauthorized text contains "expired"; `WithAccount` rewrites only that account; unknown id changes nothing.
- **WidgetSettingsTests** (store + FileReplace, mostly real files in temp dirs, Windows-only: hard links via kernel32 `CreateHardLink`, `icacls`, symlinks): `ClampSide` (100->150, 170, 500->340); missing file loads defaults (visible, 170, "five_hour"); round trip of many fields incl. two accounts; layout with Status cell survives file; StatusMode survives / null for old file; ModelDial survives / null (reads Shown); PlanLine survives / null (reads Hidden); subscription fields (Capabilities, RateLimitTier, RavenType) and fetch stamp survive; old account JSON lacking them loads null (fetch-now); ExportDirectory survives / absent = null; corrupt file loads defaults (first load); Save replaces file (hard link keeps old bytes, no leftover temp files); Save waits out a reader with ReadWrite share for 150 ms; failed save (NaN) leaves old file and no temp; a held (FileShare.None) file during Load is waited out (100 ms vs 750 ms budget) and then not clobbered with defaults; unreleasable hold -> Load throws IOException and nothing saved; read-only file -> `UnauthorizedAccessException` immediately (no retry); symlinked settings keeps link and updates target (skips if no privilege 1314); failed replace deletes temp (read-only target); stale read-only `.tmp` does not block save (unique temp names); reader sharing only Read is waited out; ACL denying replace fails at once (`IsTransient` false) (uses icacls deny W,D on file and DC on dir); trailing comma accepted; unknown enum name falls back for that field only and accounts survive; unparseable file copied aside ONCE (`s.json.bad-*`, content preserved) even after two loads and a save; file turning unparseable AFTER a good load -> `SettingsUnparseableException`, file untouched, `Current()` still returns last good org; unparseable file that cannot be copied aside (held earlier `.bad-held` copy) -> not loaded as defaults; failed write with undeletable temp rethrows ORIGINAL IOException with `Data["TempCleanupFailed"]`; file vanishing after a good load -> last good; `Current()` returns last good while held and throws if no read has succeeded.
- **AccountRowTests** (touches view-model `AccountRow.ForAll` in CORE/Views/AccountDials.cs): one row per account in settings order (3 dials each, fraction 0.62, SessionResetsIn "1h 12m"); band time follows the 5H dial into a spent week ("3d 0h"); account with null/missing snapshot keeps its row with null-fraction dials and null SessionResetsIn; single account normal; no accounts -> no rows; plan label from raw fields ("Ultra","Team","Max"); Team seat reads Premium from its OWN snapshot (Fable bucket); Ultra account with week-only snapshot -> `Dials[0].NoLimit` true and SessionResetsIn null, Max not; never-fetched fields -> PlanLabel null (not Free); fetched but empty capabilities `["chat"]` -> "Free"; uses DisplayName not Id.
- **App.Tests/WebViewDeathTests** (`ClaudeWebSession.IsWebViewDead`, ASC/Web/ClaudeWebSession.cs:594-602): dead if `ObjectDisposedException`; `COMException` with HRESULT 0x8007139F (ERROR_INVALID_STATE) or 0x80010108 (RPC_E_DISCONNECTED); or `InvalidOperationException` whose inner is a COMException with one of those HRESULTs (the WebView2 wrapper's disposed error); NOT dead: bare InvalidOperationException, inner E_FAIL (0x80004005), `NotImplementedException` (old runtime lacking ICoreWebView2_19).
- **App.Tests/Sta.cs**: helper `Sta.Run(Action)` runs a test body on a new STA thread (own Dispatcher per test) and rethrows the failure with original stack (ExceptionDispatchInfo) -- used by the window tests (DesktopWidgetWindowTests, MenuPlacementTests, not in my scope).
- **CI** (`.github/workflows/ci.yml`): name CI; triggers `push` and `pull_request` on branch `main` only; one job `build-and-test` on `windows-latest`; steps: `actions/checkout@v4`, `actions/setup-dotnet@v4` with `dotnet-version: '10.0.x'`, `dotnet build -c Release`, `dotnet test -c Release --no-build`. (A Rust rewrite on `rust-port` is not covered by this workflow.)
- Test csproj files: Core.Tests references Core only; App.Tests references the App project (and so WebView2 package) with UseWPF/UseWindowsForms; both `IsPackable=false`, `Using Xunit`.

Coverage gaps worth knowing for the port (inferred from absence): nothing tests App.xaml.cs wiring (startup retry, mutex, power resume, stagger scheduling), `Autostart`, `Updater` (only the `LatestRelease` parsing), `WidgetLog` rotation, `UsageExportWriter`, the `CUW_RENDER_*` modes, or migration through a real file with legacy JSON text (only in-memory records). `TolerantEnumConverter` is only covered by one test (unknown name in a nullable field, and the fallback value itself is not asserted).

### 2B §12. Test classes in scope (Tests/ClaudeUsageWidget.Core.Tests)

- **FetchIdleLeaseTests** (71 lines): last `Exit()` returns a token and `ShouldRelease` is true; no token while another user holds it (`InUse` tracks); a new `Enter` inside the idle window invalidates the earlier token; token not honoured while in use; `Exit` without `Enter` throws `InvalidOperationException`; `Delay > 0` and `<= 300 s / 10` (i.e. <= 30 s).
- **NavigationRetryPolicyTests**: the 5 transient names are true; `HostNameNotResolved`, `"Unknown "` (trailing space), `"unknown"` (case), `""`, `CertificateCommonNameIsIncorrect` false; `RetryDelay == 3 s`.
- **OrganizationPickerTests**: prefers team+chat over earlier personal and non-chat; falls back to first chat-capable; accepts `id` when `uuid` missing; `Describe` lists every key in order and quotes tier-like values but not `name`; `Describe("nope", null) == "not-json"`; `Fields` returns raw capabilities/tier/raven for the picked org; empty (not null) capabilities for a found org with none; null capabilities when picked org absent or body not JSON; non-string capabilities skipped and non-string `rate_limit_tier` -> null; `Pick` null for `[]` and garbage.
- **SubscriptionRefreshTests**: cadence is 1 h; null stamp refreshes; 59 min waits; exactly 1 h refreshes (inclusive); `LearnedNothing` true only for `Unknown`; empty-capability list is an answer; `Merge` keeps existing when nothing learned, replaces when learned; nothing + nothing stays nothing; stamp 5 minutes in the future refreshes.
- **SubscriptionTierTests** (252 lines): 20x -> `Ultra`; raven -> `Team`; Premium-seat org body still `Team`; 5x with chat first -> `Max`; `claude_pro` -> `Pro`; `["chat"]` + `default_claude_ai` -> `Free`; raven via capability alone or via `raven_type` alone -> `Team`; `default_raven_premium` -> `Team Premium`; Max-family with `default_claude_max`/`something_else`/null -> `Max or Ultra`; `default_claude_max_40x` -> `Max 40x`; `HasFiveHourLimit` theory (20x/40x/bare max false; 5x, pro, raven, none true) and null profile true; Enterprise via capability or tier; unknown tiers prettified (`Claude ultra`, `Some new plan`); nothing at all -> `Free` (also `null,"",""`); `LabelOrNull` null only when all three null (AND guard); `LabelFor` null profile -> null, reads profile fields, per-profile correctness; Team seat + `seven_day_fable` -> `Team Premium`, with sonnet/opus/no per-model/`seven_day_oauth_apps`/no usage -> `Team`; Ultra with fable bucket stays `Ultra`; never-fetched profile -> null.
- **ModelBucketsTests**: filters out non-model keys (`five_hour`, `seven_day`, `seven_day_oauth_apps`, `seven_day_overage_included`, `extra_usage`); known order fable/opus/sonnet; unknown models appended alphabetically (`seven_day_aardvark` before `seven_day_zebra`, after opus); `Resolve(null)` -> fable if present else first available; honours a present preference, drops a stale one; null when no model bucket; `Label` strips prefix and upcases; `IsModelKey` theory.
- **ThresholdLevelTests**: 0 and 0.59 Ok; 0.6 and 0.84 Warning; 0.85 and 1 Danger.
- **UsageDecoderTests** (211 lines): decodes 4 buckets and ignores scalar `currency`; keeps unknown keys; ISO dates with `Z` and fractional seconds; epoch-seconds numeric `resets_at`; missing/boolean reset -> null; bucketless body, HTML garbage, boolean utilization -> `MalformedResponse`; folds `weekly_scoped` limits into `seven_day_<slug>` with reset parsed, ignores `kind:five_hour`; slug `"Claude Opus 4.5"` -> `seven_day_claude_opus_4_5`; real top-level bucket beats a limits entry; incomplete scoped entries skipped (leaves 1 bucket); limits alone suffice; malformed limit entries (string/number/null) tolerated; unsluggable `!!!` skipped (no `seven_day_` key).
- **UsageMathTests**: `RemainingText` 59s/10m/1h 0m/1d 1h; past or null -> null; `Fraction` scales and clamps (140 -> 1, -5 -> 0); `PercentText` 0.575 -> `58%` (epsilon), 0.574 -> `57%`, 0 -> `0%`, 1 -> `100%`.
- **UsageTypesTests**: snapshot equality by contents; indexer null for missing; the three fixed descriptions; rate-limited description identical with/without Retry-After; Network description = message; `RateLimited(600) == RateLimited(600)` and `!= RateLimited(null)`; no description contains `(` or `"`; `UsageException.Message == Error.Description`.
- **PollScheduleTests**: four accounts -> 0/75/150/225; single account 0; all offsets within [0, 299] for 1..8 accounts; offsets distinct; throws for index -1, index == count, count 0.
- **StatusStoreTests**: starts Unknown; publishes on success; keeps last status when a later fetch throws; a failing first load leaves Unknown and does not throw; overlapping loads coalesce into one call (gated, deterministic) incl. a two-OS-thread barrier variant; `Changed` fires once per successful load and never on failure.
- **UsageStoreTests** (471 lines): starts Loading; publishes `Ok(snapshot, now)` + LastSnapshot; NoCredentials fails without calling fetch; Unauthorized surfaced; arbitrary exceptions -> `Failed(Network)`; last snapshot kept on failure; 429 pauses until `now + 3600 + 300` and a second load makes no call; resumes after deadline; 429 without Retry-After uses ladder; consecutive 429s escalate 1h -> 6h -> 24h (each + 300 s); success resets escalation; pause persists across a rebuilt store (no fetch, state `Failed(RateLimited(null))`); coalescing (single and two-thread, gated); `Changed` fires once on success and NOT while paused; failing retry-state write (expired pause clear at construction) does not stop construction; unreadable retry state starts unpaused and writes nothing; failing write does not fail a good fetch; unchanged retry state not rewritten (exactly 2 saves in the scenario: pause, then clear); a throwing `Changed` subscriber neither fails the fetch nor skips other subscribers and is logged (message contains the exception text); a clear that failed at construction is retried on the next success and then skipped (`attempts == [null, null]`).
- **ServiceStatusTests** (includes class **StatusDecoderTests**): component mapping, indicator mapping, unknown strings -> Unknown (`teleported`, `""`); dial labels incl. `—`; `Line` speaks in Line mode even when operational (`service operational`), silent in Cell mode when operational but `service major outage` when not; `LineLabel` spelled-out (`under maintenance`, `state unknown`). StatusDecoder: Claude Code component (degraded) beats page roll-up `none`; Claude Code `operational` beats `critical` indicator and a `major_outage` other component; indicator is fallback when component absent; no component and no indicator -> `MalformedResponse`; non-JSON -> `MalformedResponse`.
App has no test project; navigation retry and WebView paths were verified live from the log (2026-09-04 spec, last paragraph). Nothing in `ClaudeWebSession`, `LoginWindow`, `WebViewEnvironment` or `App.xaml.cs` is covered by automated tests.

### 2C §10. Tests (class + what it pins)

Core.Tests (`Tests/ClaudeUsageWidget.Core.Tests/`):
- `BandTextTests` (7): band entry fields (name/percent/time) for each account; name truncated to 8 chars and 8-char `default` kept whole; no data shows `—` not 0%; no reset -> null not ""; empty rows -> empty list.
- `BandViewTests`: `Resolve` theory (null+1->Metrics, null+2->Accounts, saved wins); `MetricEntries` = `5H/7D/FAB` with `3%/68%/58%` and null reset times; Ultra (no five-hour) shows `∞` with no reset in both band views; Ultra with spent week shows `100%` and `2d 0h` in the Accounts band (and `Dials[0].NoLimit` stays true).
- `DialGeometryTests` (3): fraction 0 -> -90, 0.25 -> 0, 1 -> 270.
- `ThresholdLevelTests`: 0/0.59 Ok, 0.6/0.84 Warning, 0.85/1 Danger.
- `ThemeChoiceTests`: 6-combination `Resolve` theory (follows system only for System/null); `Theme` round-trips through `SettingsStore` as `"Theme": "Light"`, old file reads null.
- `PanelOpacityTests`: Resolve default/clamp/NaN; `Painted` never below 1/255 (`0 -> 1/255`, `0.001 -> 1/255`, `0.2`, `1.0`); presets descending and contain 0.82; `PanelOpacity` JSON round-trip (`"PanelOpacity": 0.4`).
- `TrayTextTests`: metrics build `5H 57% / 7D 38% / FAB 8%`; rounding 0.575 -> 58%; missing fraction -> `—` with label `MOD`; NoLimit -> `∞`; `IconIndex` fallback theory (chosen 0 with NoLimit -> 1; others stay) and no fallback when week has no figure; label cut rules (`OPUS`, `W`, `FIVE` kept; `SON`, `FIV` cut); empty title -> no metric; no models -> none.
- `DialModelTests` (18 facts/theory) + `StatusLineTests` (9, same file): builds from bucket (`2h 0m`); missing bucket/snapshot -> empty dial; three dials in fixed order (`5H,7D,FABLE`), `MODEL` title with no model bucket; pinned model bucket honoured; spent week moves 5H countdown to the week (`3d 0h`) but percent stays, 99% does not, spent per-model does not; Ultra week-only = NoLimit with `"no limit"`; other plans/unknown plan = n/a; unrecognised Max tier keeps NoLimit; neither window -> not NoLimit; Ultra with a five-hour bucket shows number; null snapshot not NoLimit. Status line: silent when healthy; `updated 16m ago` at >= 15 min; `unexpected response from the API`; `HTTP 500`; null for NoCredentials/Unauthorized; `loading…`; rate limited with/without/expired deadline (`rate limited · retry in 10m`, `rate limited`).
- `AccountRowTests` (12): one row per account in order with 3 dials and `1h 12m`; band time follows 5H into a spent week; row kept for null/missing snapshot; single account normal; no accounts -> no rows; plan labels `Ultra/Team/Max`, `Team Premium` from own snapshot; Ultra NoLimit vs Max; unfetched plan -> null (not Free); chats-only org -> `Free`; DisplayName not Id.
- `LayoutTests.cs` classes: `FlowGridTests` (grid is 2 columns: 1->(1,1), 2->(1,2), 3->(2,2), 4->(2,2); row/column; 0 -> (0,0)); `GridTracksTests` (gap tracks, sums, `TrackOf`); `LayoutSettingsTests` (Sanitize null/duplicates/missing required/permutation kept/status+model rules/idempotence/fallback gets mode/Toggle/LaidOut/SwapCells by identity incl. name Above, out-of-range no-op, swaps survive Sanitize); `StatusModeTests` (saved wins; read from order; `Order=null` no crash); `LayoutCycleTests` (flows, name 4 placements, 2-value toggles, out-of-range enum restarts); `PanelMetricsTests` (170 square, 3==4 accounts shape, row grows width/column grows height, name-above cost = NameHeight + gap, 248 wide, aspect preserved, side clamped, zero accounts usable, block gap = 2*gap, gap 10, top gap == bottom gap for every name placement and status mode, height = padding + content + band, status dial drops caption band, edit mode grows window by reserve only, nine buttons on one row at Min/Default/Max side, nine 16 pt would overflow, plan line reserves one caption line for Above/Below only, top==bottom with plan, narrow panel wraps to 2x5, reserve = rows+gap).
- `PanelViewTests`: Classic layout/mode, Accounts == Default, `DefaultFor` (0,1->Classic; 2,4->Accounts), `Current` recognition, file without layout resolves by account count, saved layout kept, model-hidden respected, `Apply` writes both fields and keeps other switches, classic has no caption band.
App.Tests (`Tests/ClaudeUsageWidget.App.Tests/`):
- `DesktopWidgetWindowTests` (2, STA, needs a desktop session/primary screen): adding an account (4 -> 5, `RebuildLayout(5)` with a stale 4-row frame) keeps the panel inside the work area (left edge always; right edge when it fits) and grows; removing an account rebuilds without throwing and shrinks. Fixture settings JSON includes a real layout (`Row`/`Column`/`Above`, side 222). Tolerance 0.5 px.
Not covered by any test: dial rendering, drag/resize math, `ResizeDelta`, edge hit test, edit-mode strip-side logic, persistence debounce, `SystemTheme`, palette hex values, `RenameWindow`, notice texts, status-dial click.

### 2D §6. Tests (one-liners)

- `Tests/ClaudeUsageWidget.App.Tests/MenuPlacementTests.cs` (6 `[Fact]`s on the pure `MenuPlacement.Within`; 1920x1080 with a 48 px bottom taskbar = work area 0,0,1920,1032):
  - `AMenuHangingOverABottomTaskbarIsLiftedOntoItsTopEdge`: opened at cursor (1700,1060), menu 260x600 gives (1440, 432).
  - `AMenuAlreadyInsideTheWorkAreaStaysWhereItIs`: unchanged.
  - `ATopTaskbarPushesTheMenuDownBelowIt`: work area top 48, menu y 20 gives 48.
  - `ASideTaskbarPushesTheMenuOffIt`: work area width 1872, x 1700 gives 1612.
  - `AMenuTallerThanTheWorkAreaKeepsItsTopRowsOnScreen`: 1200 tall menu gives y = 0.
  - `ASecondMonitorWorkAreaIsHonouredInItsOwnCoordinates`: work area at x -1920; menu y 480 gives 432.
  - No test exists for placement behaviour (`Attach`/`Place`), `TrayIconRenderer`, `MenuChrome`, `WidgetMenuRenderer`, or `TaskbarBandWindow` (none in this scope's test files; the other App tests are `DesktopWidgetWindowTests`, `WebViewDeathTests`, not covered here).
- `Tests/ClaudeUsageWidget.Core.Tests/BandViewTests.cs`:
  - `ResolveFollowsTheSavedValueThenTheAccountCount` (4 inline cases: null+1 gives Metrics, null+2 gives Accounts, saved Accounts+1 gives Accounts, saved Metrics+3 gives Metrics).
  - `MetricEntriesAreTheTrayColumnsWithoutResetTimes`: labels `5H`/`7D`/`FAB`, percents `3%`/`68%`/`58%`, `ResetsIn` all null.
  - `AnAccountWithoutAFiveHourWindowShowsNoLimitInBothBandViews`: Ultra tier; metric percents `∞`/`77%`/`1%`; accounts entry `∞` with null `ResetsIn`.
  - `ASpentWeekWithoutAFiveHourWindowShowsTheWeekInTheAccountsBand`: week 100% with no 5H gives `100%` and `ResetsIn "2d 0h"`, while `row.Dials[0].NoLimit` stays true.
- `Tests/ClaudeUsageWidget.Core.Tests/BandTextTests.cs` (7 `[Fact]`s):
  - `ShowsFiveHourAndTheResetTimeForEveryAccount`: name, `62%`, `1h 12m` per account.
  - `ShortensALongAccountNameSoTheBandStaysNarrow`: `"personal-low"` becomes `"personal"` (8 chars).
  - `KeepsAnEightCharacterNameWhole`: `"default"` stays whole.
  - `AnAccountWithNoDataReadsAsADashRatherThanZero`: `"—"` not `0%`.
  - `TheNameThePercentAndTheTimeStaySeparateFields`: three separate fields.
  - `NoResetTimeIsNullRatherThanAnEmptyString`: null.
  - `NoAccountsIsNoEntriesNotACrash`: empty.
- README counts (stale): "473 tests in Core.Tests and 15 in App.Tests" (`README.md:309-310`). Not re-verified.

# 6. Platform-specific and hard-to-port parts

## 6.0 Riskiest areas, ranked (summary across all parts)

1. **WebView2 as the only claude.ai transport (2B §1-§3, §14.1-14.9).**
   - Every API call is a top-level navigation in a hidden, off-screen WebView2, and the body is read through `innerText`. A native HTTP client would lose the Cloudflare-passing browser fingerprint.
   - The port has to host WebView2 controllers on its own HWNDs with COM and STA threading.
   - It has to handle `NavigationCompleted` status semantics and detect a dead WebView from the raw HRESULTs `0x8007139F` and `0x80010108`.
   - It has to keep the idle lease, the per-account serialization gate and the event-token lifetimes.
2. **Profile and user-data-folder compatibility (2B §1.2, §14.2).**
   - The legacy `default` account must use the *implicit* default profile; the others use named profiles keyed by account id, all under `%LOCALAPPDATA%\ClaudeUsageWidget\profiles\default`.
   - If either is wrong, existing users are silently signed out.
   - The login window clears the profile's browsing data each time it opens.
3. **Taskbar band (2D §3, §8.6-8.11).**
   - The band is a per-pixel-transparent layered window *owned by* `Shell_TrayWnd`.
   - Its visibility comes from a probe: `WindowFromPoint` at 3 points, with exemptions for cloaked windows, menus and XAML popups.
   - A hysteresis state machine applies it: show after 150 ms, fast hide after 150 ms, slow hide after 1200 ms.
   - It uses system-wide `SetWinEventHook` callbacks, burial detection by walking the `GW_HWNDPREV` chain, and re-finding the taskbar after an Explorer restart.
   - Each rule exists because of a live bug, recorded in 2D §3.12.
4. **Desktop widget window (2C §1, §11.1-11.9).**
   - The window is layered, non-activating and pinned to the bottom of the z-order through a `WM_WINDOWPOSCHANGING` rewrite.
   - Fully transparent pixels must stay clickable, which is why a 1/255 painted alpha exists.
   - Drag and resize are manual. The edit-mode strip is reserved space that moves the window's top.
   - Positions are persisted in DIPs. The current clamp mixes DIPs and pixels, a latent bug above 100% scaling.
5. **Pixel-faithful rendering (2C §2, §4, §11.4-11.6; 2D §1.4, §2.9).**
   - Geometry rests on Consolas metrics (line height = size × 1.1709).
   - Arcs are round-capped, with a 0.9999 clamp.
   - Tray digits use GDI+ `MeasureString` fitting with Segoe UI Bold.
   - Menu glyphs are Segoe MDL2 Assets codepoints, centred by their path bounds.
   - The theme palette has exact hex values.
6. **Owner-drawn tray menu (2D §2, §8.1-8.4, §8.14).**
   - A WinForms `ContextMenuStrip` provides toggles that keep the menu open, dynamic submenus, per-item state sync on open, Win11 rounded corners and custom placement.
   - A native `HMENU` cannot do this, so the port needs a custom popup window.
   - The tray icon needs `Shell_NotifyIconW`, re-adding the icon on `TaskbarCreated`, and HICON lifetime management.
7. **Settings semantics (2A §3, Section 3, §11.7-11.9).**
   - The JSON has PascalCase keys and tolerant enum parsing; unknown values fall back, they never fail.
   - Every read sanitizes the layout. Legacy single-account files are migrated, with a 4-account cap.
   - An unparseable file is never replaced by defaults: the last good settings are kept, the file is copied to a deduplicated `.bad-*`, and startup retries every 2 s, then every 30 s.
   - The atomic temp-file-and-move replace retries with backoff on sharing violations.
   - Menu handlers do `Load()` then `Save`, while renders read `Current()`.
   - The Rust build must read existing files and keep them readable by the WPF build.
8. **Usage store and 429 handling (2B §7, §14.10-14.11).**
   - The pause ladder is 1 h, 6 h, 24 h, plus 300 s; `Retry-After` is never available on the navigation path.
   - The pause and counters persist per account in settings.json.
   - Concurrent loads coalesce. The last good snapshot stays drawn, dimmed, while the account is failing.
   - The `_subscriptionFetchFailed` latch is per run.
9. **Threading model (2A §11.2, §11.17; 2B §9.5).**
   - One UI thread owns windows, WebView2, timers and the menu.
   - System events are marshalled onto it. The poll stagger uses `Task.Delay` continuations on the UI context.
   - Every handler swallows and logs exceptions, so the process never dies; in Rust that means `catch_unwind` at event boundaries.
10. **Updater (2A §5).**
    - Manual only. It reads the latest GitHub release, downloads the `*-win-x64.zip`, and writes and launches a hidden PowerShell script.
    - The script waits for the process to exit, copies the files with 20 retries, then restarts the app.
    - Writability and source-build checks fall back to opening the release page. A Rust exe has to choose an equivalent swap strategy.

### 2A §11. Tricky-to-port items (Win32/WPF/.NET specific)

1. **Single-instance named mutex** (name `ClaudeUsageWidget.SingleInstance`, session scoped). Rust: `CreateMutexW` with the same name + check `GetLastError == ERROR_ALREADY_EXISTS`. Keep the handle for process life. The updater relies on the old process fully exiting before relaunch.
2. **Dispatcher semantics**: many things rely on single-threaded UI affinity (timers, `Task.Delay.ContinueWith(FromCurrentSynchronizationContext)`, `Dispatcher.Invoke` from system threads, fire-and-forget). A Rust port needs one UI/event thread with marshalling for PowerModeChanged and UserPreferenceChanged callbacks (`WM_POWERBROADCAST`/`PBT_APMRESUMEAUTOMATIC` or `RegisterPowerSettingNotification`; `WM_SETTINGCHANGE` "ImmersiveColorSet" for theme).
3. **Power resume**: .NET `SystemEvents.PowerModeChanged` (hidden message window; Resume = `PBT_APMRESUMEAUTOMATIC`/`SUSPEND`). Only Resume is used; no network-ready wait.
4. **Theme tracking**: registry `Personalize` values + `UserPreferenceChanged(General)`; two separate kinds (apps vs taskbar).
5. **Tray icon**: WinForms `NotifyIcon` + a custom-drawn ContextMenuStrip; icon is rendered each time from a number (GDI+/WPF bitmap -> HICON, DPI aware); tooltip text; the icon must be disposed on exit or it ghosts; NotifyIcon ThreadException behaviour. Menu closes/doesn't close on item click rules (TrayIcon.CancelCloseOnItemClick) are WinForms-specific.
6. **Per-Monitor V2 DPI manifest**, `gdiScaling`, supportedOS entries: must be embedded in the Rust exe manifest (e.g. via build script/resource) for crisp rendering across monitors.
7. **Atomic file replace on Windows**: `File.Move(overwrite)` = `MoveFileExW(REPLACE_EXISTING)`; error code semantics (ERROR_SHARING_VIOLATION 32, ERROR_LOCK_VIOLATION 33, ACCESS_DENIED for held-by-reader, read-only attribute, deny ACL), the probe-open logic to classify transience, fsync (`FlushFileBuffers`), symlink target resolution (replace target, keep link), unique temp names. Rust's `std::fs::rename` already replaces on Windows but error classification and retry/backoff (50/100/200/400 ms) must be reproduced by hand; `File.ReadAllText` handles BOMs (a hand-edited file with BOM must still parse: use a BOM-tolerant read).
8. **System.Text.Json behaviours to mimic with serde**: PascalCase keys; case-sensitive keys; nulls written; unknown keys ignored; trailing commas allowed on read but comments not; enums as names, tolerant read (case-insensitive, unknown -> first variant, numeric ordinal accepted, numeric strings accepted; non-string/number token -> whole-file failure); nullable enums distinct from non-null; `DateTimeOffset` ISO-8601 (with offset); positional record defaults for missing members; indented 2-space output; no BOM; NaN fails serialization. `Accounts` is an array; `Order` is an array of enum names. Round-trip fidelity of the user's hand-edited fields is the contract (legacy fields must still be readable).
9. **Settings failure semantics** (last-good cache, `Current()` vs `Load()`, `SettingsUnparseableException` as an IO-class error, hash-deduplicated `.bad-<stamp>` copies, startup retry 2 s -> 30 s) are subtle and test-pinned; port the state machine, not just read/write.
10. **Autostart** via `RegisterValue` in HKCU Run with a QUOTED path; checkbox re-read after failure.
11. **Updater**: relies on PowerShell to swap files while the exe is closed. A Rust port could do the same or use a helper exe/rename trick (a running exe on Windows can be renamed but not overwritten; the .NET app avoided that by the script). Behaviours to keep: manual trigger, GitHub latest-release API, `-win-x64.zip` suffix match (asset name will change for Rust unless the release skill keeps it), `System.Version` comparison semantics, source-build detection (`ClaudeUsageWidget.App.dll` beside exe; Rust dev build needs its own rule), writable-folder probe, `DefaultDesktopOnly` message boxes (MessageBoxW with `MB_DEFAULT_DESKTOP_ONLY`), temp dir cleanup. The zip extraction flattening rule (find exe by file name anywhere in the zip).
12. **Message boxes without an owner** (`MB_DEFAULT_DESKTOP_ONLY`): modal; Win32 `MessageBoxW`.
13. **WebView2**: per-account profiles, implicit default profile for id "default" vs named profiles (see WebViewEnvironment) is a persistence contract: a Rust port that uses WebView2 (e.g. via the `webview2-com` crate) must keep the same user data folder `%LOCALAPPDATA%\ClaudeUsageWidget\profiles\default` and ProfileName rule or users lose sign-ins. (other agent covers details)
14. **Event-log file**: UTF-8 no BOM, CRLF endings, local time with offset `zzz` format and invariant culture; rotation by size at 1 MB with overwrite-move; global lock; never throws.
15. **Poll scheduling**: stagger `300 s * i / n`, the first account awaited inline (its latency shifts others), delayed tasks check the account still exists; resume triggers an immediate staggered refresh of everyone; manual "Refresh now" only the tray account + status; UsageStore coalescing and per-account 429 pause persisted in settings.
16. **Export**: epoch ms vs s units; null-not-omitted; slug rules operate on `ToLowerInvariant` + ASCII filter (Unicode names collapse to `-`, e.g. Cyrillic names -> account id); path comparison via `Path.GetFullPath` case-insensitively (Windows semantics; Rust must normalize `..` and relative paths lexically/with GetFullPathNameW, not canonicalize (files may not exist)).
17. **`FireAndForget` + `DispatcherUnhandledException` swallow-all policy**: a tray widget must not die from handler exceptions. In Rust that means catching panics at event-handler boundaries (`catch_unwind`) and logging `unhandled ...` lines.
18. **Environment.ProcessPath, SpecialFolder paths**: APPDATA (Roaming) for settings, LOCALAPPDATA for log/profiles, TEMP for updater; use `SHGetKnownFolderPath`.
19. **Hidden coupling**: `TrayMetricKey`/`BandPosition` are free strings with tolerant fallbacks; `Layout` sanitization must run on every read (not only at write time); migrating legacy files happens only on read and is persisted by the next save of anything.
20. **Version source of truth** = csproj `<Version>`; Rust would use `CARGO_PKG_VERSION`; `IsNewer` returns false for non-parseable current versions (so an "unknown" build never offers updates).

### 2B §14. Things that are tricky to port to Rust, and why

1. **WebView2 is the only transport.** A native HTTP client (reqwest) will not reproduce the Cloudflare/Turnstile-passing fingerprint; the port must keep Edge WebView2 (e.g. `webview2-com` + `windows` crates) with the SAME UDF path and profile layout, or users lose their sign-ins. Do not set a custom UA.
2. **Implicit default profile vs named profiles.** The legacy account must be created WITHOUT `ICoreWebView2ControllerOptions` (-> `EBWebView\Default`); others need `ProfileName` (requires `ICoreWebView2Environment10`/ControllerOptions and a runtime that supports it). The `ProfileName` read-back check and the "login controller == fetch controller profile" invariant must be preserved.
3. **COM/STA threading and message loop.** WebView2 must be created and used on an STA thread that pumps messages; all `await ... ConfigureAwait(true)` in the C# map to "continue on the UI thread". The Rust port needs a single UI-thread executor, a way to marshal from the power-notification thread (`WM_POWERBROADCAST` / `PBT_APMRESUMEAUTOMATIC` or `RegisterSuspendResumeNotification`), and UI-thread timers (300 s refresh, 2 s cookie poll, 15 s idle close, 3 s retry delay, 30 s navigation timeout, 75/150/225 s stagger).
4. **Hosting the controller without a visible window.** Hidden host HWND (off-screen, zero size, `WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE`, never shown) plus `IsVisible=false`; WebView2 may set `WS_VISIBLE` on its parent (the C# needed four layers of insurance). In Rust there is no WPF `Window`; create a plain Win32 window and never `ShowWindow`.
5. **Login window = a real embedded browser.** The C# uses the WPF `WebView2` control (child HWND, focus, resizing, accelerators, DPI). In Rust you must create the controller on your own HWND, forward `WM_SIZE` to `put_Bounds`, handle focus (`MoveFocus`), DPI, and close semantics; also reproduce the TextBox/Button bar (hint, paste box, Open) in whatever UI toolkit is chosen. `EnsureCoreWebView2Async` needs a real parent window first (hence the `Loaded` hook). Window closed during async init must abort (the `_closed` flag after each await).
6. **No cookie-changed event**: success detection is polling every 2 s + after each `NavigationCompleted`; port as-is. `GetCookies("https://claude.ai")` is async COM.
7. **NavigationCompleted semantics**: `HttpStatusCode` vs `IsSuccess` vs `WebErrorStatus` ordering and the enum NAME strings used by `NavigationRetryPolicy` (`Unknown`, `ConnectionAborted`, `ConnectionReset`, `OperationCanceled`, `Timeout`). In Rust you get an integer `COREWEBVIEW2_WEB_ERROR_STATUS`; map to the same names. Reading the response text is `ExecuteScript` returning JSON-encoded string; must JSON-decode it.
8. **Dead-webview detection**: raw HRESULTs `0x8007139F` (ERROR_INVALID_STATE) and `0x80010108` (RPC_E_DISCONNECTED) (the .NET wrapper's exception translation does not exist in Rust, you see HRESULTs directly). `ProcessFailed` kinds to reset on: BrowserProcessExited, RenderProcessExited, RenderProcessUnresponsive. Only one retry on a fresh controller.
9. **Controller/handler lifetimes and event-token removal.** The C# relies on a strong field reference to the controller and on unsubscribing `NavigationCompleted` per navigation; with COM, event handlers (`add_*` tokens) must be removed and kept alive; closing the last controller lets the browser process exit and the next creation restarts it; `MemoryUsageTargetLevel` (ICoreWebView2_19) is optional and its absence must not fail.
10. **Async orchestration with exact serialization semantics**: per-account `SemaphoreSlim(1,1)` gate held across both attempts, the idle lease counter/generation token (a pure state machine worth porting 1:1 with its six tests), coalesced `LoadAsync` (share one in-flight future for concurrent callers; set before starting; clear in finally), and the "task cache that resets on fault/cancel" pattern for environment, webview and login window. Rust needs explicit shared-future / `Rc<RefCell<Option<Shared<..>>>>` equivalents.
11. **Per-process-run latch** `_subscriptionFetchFailed` and `UsageStore` in-memory counters vs persisted state have subtle interplay (pause cleared in memory at deadline but on disk only on success; counters not restored when the stored pause already expired).
12. **JSON leniency**: the decoder tolerates arbitrary shapes (`serde_json::Value` walking, not typed structs): numbers vs booleans, non-object members, `limits` junk. `DateTimeOffset.TryParse(Invariant, AssumeUniversal|AdjustToUniversal)` accepts many formats and offset-less text as UTC; Rust parsers (chrono/time) are stricter, so a lenient ISO-8601/RFC-3339 + fallback parser is needed. Numeric `resets_at` is epoch SECONDS (with fraction) converted via `(long)(seconds*1000)` milliseconds; guard against out-of-range (C# would throw `ArgumentOutOfRangeException` -> Failed(Network)).
13. **Numeric/text parity**: `Math.Round(x + 1e-9, AwayFromZero)` for percent text; `Math.Floor` and int cast for remaining seconds (watch int overflow on huge resets); `char.IsLetterOrDigit` (Unicode L* and Nd only) differs from Rust `char::is_alphanumeric` (includes Nl/No), relevant to the slug; `StringComparer.Ordinal` sorting is UTF-16 code-unit order vs Rust byte order (differs only for non-BMP); `ToUpperInvariant`/`ToLowerInvariant` vs Rust `to_uppercase` (multi-char expansions).
14. **Settings coupling**: this scope reads and writes `AccountProfile` fields through `SettingsStore` using `Load()` (read-modify-write that throws while the file is held) vs `Current()` (last good fallback), swallows only `IOException`/`UnauthorizedAccessException` in `SaveQuietly` and everything in the retry-state persist. Serialization of `DateTimeOffset` values in settings must stay compatible if the same settings.json is reused.
15. **Cross-thread exception isolation**: `UsageStore.SetState` isolates each subscriber; `StatusStore` does not; `FireAndForget` logs instead of crashing; async-void handlers in the login window must never panic the process (in Rust: catch_unwind or Result handling around every COM callback).
16. **Download cancellation** (`DownloadStarting` -> cancel + handled) and the diagnostic resource/redirect hooks are cheap to port but only matter for diagnosing `Unknown` navigation failures; the log vocabulary above should be kept so existing log-reading habits still work.
17. **Stagger and timer drift**: index 0 is awaited inline before others are scheduled, others use `Task.Delay` from that moment; the record-equality quirk in the `_accounts.Contains(account)` check (rename drops a scheduled poll). Decide consciously whether to reproduce or fix.
18. **Startup behaviours that are easy to miss**: poll at startup immediately (not after 300 s); login window auto-opens only for the tray account when it has no session; settings-unreadable retry loop (2 s for 1 min, then 30 s) with no half-started UI; single-instance named mutex `ClaudeUsageWidget.SingleInstance`; first-run creates account id `default`, name `Claude`.

### 2C §11. Things tricky to port to Rust

1. **Layered, non-activating, bottom-pinned window.** Needs `WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, `WM_MOUSEACTIVATE -> MA_NOACTIVATE`, and the `WM_WINDOWPOSCHANGING` rewrite (hwndInsertAfter=HWND_BOTTOM, clear `SWP_NOZORDER`) in a subclass/wndproc; `Hide/Show` semantics; no focus => no keyboard. WPF gave all of this plus per-pixel alpha compositing for free.
2. **Click-through of alpha-0 pixels.** Behaviour depends on the layered-window hit test; the 1/255 painted alpha hack (`PanelOpacities.Painted`) exists solely because of it. A Rust renderer (UpdateLayeredWindow/DComp/softbuffer) must reproduce the same hit-test rule, and must also treat the transparent area inside the band/outside rounded corners as pass-through.
3. **DPI.** WPF DIPs vs pixels: positions/size persisted as DIPs, and the clamp mixes DIPs with `Screen.WorkingArea` pixels (latent bug at >100%). PerMonitorV2 transitions have no handler (WPF handles scaling). Decide units, handle `WM_DPICHANGED`, per-monitor work areas (`MonitorFromPoint` with nearest-monitor default), negative virtual-desktop coordinates, truncation `(int)` semantics, and DIP font sizes (`FormattedText` uses per-visual `PixelsPerDip`).
4. **Text layout fidelity.** All geometry rests on Consolas metrics: `CaptionLineHeight = 8*1.1708984375`, `NameLineHeight = 13` for 11 pt, line height = size*1.1709, a fixed 1 DIP gap, min font size 1, `CharacterEllipsis` trimming, shrink-to-fit factor >= 0.6 for the status label, centred stacking with exact line heights. A different font or shaper changes the constants and the panel height. Consolas and Segoe MDL2 Assets are Windows-only fonts.
5. **Vector drawing**: thick round-capped arcs with a `0.9999` clamp, large-arc flag when > 0.5, clockwise sweep from -90 deg; track circles at centre-line radius `size/2 - 4*scale`; anti-aliasing and stroke centring must match to look the same.
6. **Layout engine**: WPF Grid with explicit gap tracks, Auto rows, centred `DialGrid` with a bottom margin equal to the status band, transparent hit hosts, `ClipToBounds`, `Border` corner radii, DynamicResource-driven brushes. In Rust everything must be computed by hand from `PanelMetrics` (good news: Core already holds the numbers; keep them in one place).
7. **Hit-testing**: edit mode relies on WPF `InputHitTest`, tunnelling `Preview*` events with `Handled` suppressing the window's own drag (and the status dial), `Capture`, `LostMouseCapture`, and separate visual/logical tree walks for glyph `Run` elements. Must be replaced by an explicit cell registry and an event-priority scheme (toolbar > cell press > window drag/resize), plus all the "button not actually pressed" safety nets.
8. **Manual drag/resize without `DragMove`** with incremental window-relative deltas, a scalar `side` driving both axes, fixed 8 DIP edge bands, resize repaint on every mouse move (full rebuild of the cells) - needs throttling consideration (inferred; WPF does a full `BuildGrid` per mouse move).
9. **Edit-mode strip band bookkeeping**: window grows by `ToolbarReserve`, `Top` shifts so the panel stays put, side flips live during drags, clamp interplay, and persisting the PANEL's top-left (`Top + _stripAbovePad`). Easy to get a drifting position.
10. **Tooltips and cursors**: WPF tooltips on name labels, header/toolbar buttons and status dial (with enum names in the text); `SizeWE/NS/NWSE/NESW`/`Hand`/`Arrow` cursor logic with child override.
11. **Ghost rendering**: live `VisualBrush` copy of a cell at 0.6 opacity. Needs an offscreen render of the cell or a re-draw of the cell into the overlay.
12. **System theme change**: `UserPreferenceChanged`(General) arrives off-thread; in raw Win32 it is `WM_SETTINGCHANGE` with lParam `"ImmersiveColorSet"` (inferred equivalent); re-read two registry DWORDs; compare to cache; marshal to the UI thread; missing => dark. Also the theme change must repaint without relayout.
13. **Debounced persistence + read-modify-write settings**: the 500 ms timer, flush on close, `Load()` can throw (held file) and the exception is swallowed by the dispatcher handler; settings are re-loaded per save so concurrent writers (polls) are merged field-wise - reproduce the pattern (and avoid the rename-dialog stale-data overwrite).
14. **Modal rename dialog** with default/cancel buttons, auto-focus and select-all, palette colours but system title bar; a native `DialogBox`/own window in Rust. Also nested message loop while the rest of the app keeps polling.
15. **Offscreen preview**: `RenderTargetBitmap` at 1x/2x with arbitrary layouts; the Rust equivalent is a snapshot-test harness (recommended for the port) - there is currently no automated pixel test.
16. **Enum/JSON compatibility** of `settings.json` (names, tolerant parsing, nullable fields, `LaidOut` extra field) so a Rust build reads existing files and the WPF build can still read what Rust writes.
17. **Shell-open** `https://status.claude.com` (`ShellExecuteW`), plus `WidgetLog` line `browser-open`.

### 2D §8. Things that are tricky to port to Rust, and why

1. NotifyIcon replacement. In Rust there is no `NotifyIcon`. A hidden message window plus `Shell_NotifyIconW` (`NIM_ADD/MODIFY/DELETE`, `NIF_ICON|NIF_TIP|NIF_MESSAGE`) is needed, and the code must re-add the icon on the registered `"TaskbarCreated"` message (WinForms did it implicitly here; nothing in the repo shows it). The tooltip limit is 127 characters plus NUL. Right-button-up and keyboard activation (`NIN_SELECT`/`NIN_KEYSELECT`/`WM_CONTEXTMENU`) must pop up the menu, with `SetForegroundWindow` before `TrackPopupMenu` and `WM_NULL` after if native menus are used.
2. Icon handle ownership. Render, replace and destroy HICONs (`CreateIconIndirect`/`DestroyIcon`), or GDI quota leaks (this was a real concern; `TI:180-186`).
3. Owner-drawn, keep-open menu with submenus. Native `HMENU` closes on every click, so the "toggles keep the menu open" behaviour, rounded Win11 corners (DWM attribute 33), per-item tooltips, rebuild-on-open, dynamic submenu content updated while open (store refresh), hover highlight and tick glyph drawn by hand, and the submenu gap quirk (submenu x derives from the item width) are all WinForms behaviour that the Rust port must reproduce with either owner-draw `MF_OWNERDRAW` or a custom popup window. Icons rendered per-DPI at `16*dpi/96` and not re-rendered on monitor change.
4. Glyph rendering. Centring the glyph path's own bounding box (not the text layout box) in a square; `Segoe MDL2 Assets` PUA codepoints (E774, EBE8, E896, E72C, E77B, E7C4, E716, E80A, E790, E7B5, E7F4, E90E, E8A0, E7B3, E72E, E7E8, E8BB, E73E; E99A on the personal branch); fallback when the font is missing. The `0.72*size` em and GDI+ AA path fill need matching in whatever 2D API is used.
5. Tray digit icon text fitting. GDI+ `MeasureString` (includes padding) with Segoe UI Bold in pixels, 0.5 px step search from `0.95*H` down to a 6 px floor, `width <= W-1`, `AntiAliasGridFit`, centred in the whole icon, `SmallIconSize` at the right DPI. The `"—"`, `"∞"` ring fallback rule (any digit present). Light/dark ink from `SystemUsesLightTheme`.
6. Per-pixel transparent layered window owned by `Shell_TrayWnd`. WPF `AllowsTransparency` (layered, `UpdateLayeredWindow`) must be replaced by `WS_EX_LAYERED` plus `UpdateLayeredWindow`, or DirectComposition; do NOT mix `SetLayeredWindowAttributes`. `WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW`, owner via `SetWindowLongPtr(GWLP_HWNDPARENT)`, topmost exactly once. Text must be drawn manually with a 1 px offset shadow, `Consolas SemiBold` 10 and 14 DIP, with DIP-to-pixel conversion (note that content measure DPI and the taskbar DPI used for sizing can differ; the "F/5" clipping bug came from measuring before the HWND had the real DPI).
7. System-wide `SetWinEventHook` out-of-context hooks. The callback must be an `extern "system"` function with no captured state (use a thread-local or a static pointer to the window state), and the installing thread must pump messages. The REORDER callback must run the burial check synchronously (a queue adds a visible frame of lag). LOCATIONCHANGE is very noisy (filter `OBJID_WINDOW`, foreground only, 100 ms debounce). Hooks and timers need teardown on Detach/Lost.
8. The z-order model. Owned window above owner, `SWP_NOOWNERZORDER` raising by the shell, `GW_HWNDPREV` chains (cap 64), exemptions for `#32768` and `Xaml_WindowedPopupClass`, and never periodic `HWND_TOPMOST` (NetSpeedTray #200). The check must run before the geometry short-circuit.
9. The visibility state machine. A single timer with per-direction delays (show 150, fast-hide 150, slow-hide 1200), a re-probe at expiry that cancels if the verdict flipped, immediate application of the first verdict per Dock, no re-timing of an already-pending transition (a quirk), and no reposition while hidden. Coordinates must be physical pixels: the process must be PerMonitorV2 so `GetWindowRect`, `WindowFromPoint` and `SetWindowPos` agree.
10. The probe semantics. 3 points at 0.35/0.55/0.8 of the taskbar width and at the taskbar's vertical middle; any single point that is the taskbar, the band itself, null, or a DWM-cloaked window means visible (`DwmGetWindowAttribute(DWMWA_CLOAKED)`; failure counts as not cloaked). `WindowFromPoint` sees through layered transparent pixels; `GetAncestor(GA_ROOT)`.
11. Explorer restart and zombie windows. Re-find `Shell_TrayWnd`/`TrayNotifyWnd` on every tick, never cache; a missing tray means keep the state; the owner compare triggers re-bind plus topmost. In Rust there is no WPF `Window.Close` exception, but HWND destruction detection (`IsWindow`) and a recreate path should be kept.
12. Registry and theme tracking. `SystemUsesLightTheme` vs `AppsUseLightTheme`; WinForms/.NET's `UserPreferenceChanged(General)` corresponds to `WM_SETTINGCHANGE` (typically `"ImmersiveColorSet"`) on a message window (inferred); the thread hop to the UI thread; dark default on any failure.
13. Win11 specifics. The corner attribute only when build >= 22000; font fallback `Segoe UI Variable Text` to `Segoe UI`; Windows 11 taskbar behaviour (centred icons, Widgets button) is not modelled; only the primary taskbar is handled and horizontal taskbars are assumed.
14. Menu placement. WinForms places the menu with its bottom edge at the cursor and clamps to screen bounds; the port should clamp to the work area of the monitor containing the menu (per-monitor, negative coordinates) and force topmost. Replicate `Within` exactly (the near edge wins for oversized menus).
15. Autostart and single-instance. Quoted exe path in `HKCU\...\Run`, an enabled check that requires a string value, state re-read on failure; a named mutex without a namespace prefix (session-local).
16. Settings round trips in the menu handlers: they use `Load()` (fresh from disk) then `Save(data with ...)` while rendering reads `Current()` (last-good cache). Preserving that read-modify-write pattern matters so a file held by another process is never overwritten with defaults.
17. Developer render switches (`CUW_RENDER_MENU`) depend on WinForms `DrawToBitmap`; the Rust port needs its own offscreen render path if it wants the same verification loop.

# 7. Discrepancies, oddities and open items

These are places where the code disagrees with the specs or the README, latent bugs found by reading, and items not verified. Decide for each one whether the port preserves it or fixes it.

### 2A §12. Discrepancies / oddities noticed
- First-run default account display name `"Claude"` (App.xaml.cs:838) vs migrated legacy account `"default"` (SettingsMigration.cs:42).
- `AccountProfile` doc says Id limited to 64 chars / charset, but no validation exists in Core (inferred from the files read in scope).
- `TrayAccountId` is not cleaned when migration truncates accounts to 4.
- StatusStore has a separate `RefreshIntervalSeconds = 300` const while `PollSchedule` and the App timer use `UsageStore.RefreshIntervalSeconds`.
- `UsageExportWriter` failure is Debug-only (not in widget.log) while the settings.json-skip case IS logged.
- README says "Windows 10: install the WebView2 Evergreen Runtime first"; the code has no runtime-missing UI of its own beyond the tooltip `"... WebView2 error, see widget for details"` and the panel's `Failed(Network(...))` message.

### 2C §0. Spec-vs-code divergences (read this first)

The three design docs are partly stale. The CODE wins; known differences:

1. `docs/.../2026-08-26-in-widget-editing-design.md` says the hover header (eye + lock) is deleted. **The code still has it**, now with three buttons: eye (hide), pencil (enter edit mode), lock (`WRX:107-136`, README line 112-113).
2. The layout-editor spec says the edit mode appends a hint "drag a cell to swap · Edit layout finishes" to the status line. **Removed** (comment `WRV:1015-1019`); nothing is appended in edit mode.
3. Edit-in-widget spec said "toolbar = 6 buttons at 16*scale, gap 4". **Now 9 buttons, 14*scale boxes, 2*scale gaps** (`C/Views/PanelMetrics.cs:56-73`).
4. Themes spec Light `Track` = `#D3D8E3`; **code is `#B7BECC`** (`PAL:78-86`, comment `PAL:74-77`).
5. Spec "Esc leaves edit mode" was abandoned: the window is `WS_EX_NOACTIVATE`, key events never arrive (measured 2026-08-26, layout-editor spec "Leaving"). **There is no keyboard handling anywhere in the widget** (grep: no `KeyDown`/`PreviewKeyDown` in DWW/WRV).
6. The README image `assets/account-rows.png` shows names as plain text; the code now draws a track-coloured ring behind a name that sits in a cell (`RING:15-36`, `WRV:872-880`).

### 2D §9. Unverified or open items

- Nothing was run. All behaviour is from source reading and git history; no live observation of the app.
- The WinForms-internal behaviours (NotifyIcon `TaskbarCreated` handling, which submenu closings really bubble, `SmallIconSize` DPI source) are inferred, not shown by repo code.
- The Accounts and Layout submenu keep-open gap (2.8) is a code-reading inference.
- `AccountRow`, `AccountDials`, `ModelBuckets.Available/Resolve`, `LayoutResolution`, `PanelViews`, `StatusLine`, `SettingsStore` and `DesktopWidgetWindow` internals were only skimmed or not read; they belong to other inventories.
- The task-17 reports and briefs referenced in comments are not in the repo; only commit messages preserve that history.

