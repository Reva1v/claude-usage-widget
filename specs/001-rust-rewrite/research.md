# Research: Rust rewrite (Phase 0)

Format per item: **Decision / Rationale / Alternatives**. **VERIFY** = needs a live check in the
named stage; until then the decision stands but is unproven. Versions checked 2026-11-21 (see
plan.md Technical Context).

## R-01 Workspace location and crate split

- **Decision**: workspace in `rust/`, 21 crates prefixed `cuw-` (plan.md Project Structure). Pure
  crates `#![forbid(unsafe_code)]`, no `windows` dependency.
- **Rationale**: C# and Rust ship side by side until US6; separate roots keep `dotnet`/`cargo`,
  CI path filters and editor config apart. Small crates keep agent build loops in seconds.
- **Alternatives**: repo-root `crates/` (mixes with `src/`, `Tests/`, the `.sln`; root `Cargo.toml`
  confuses the C# release flow); 3-4 big crates (no parallel work, slow tests).

## R-02 UI thread and async model

- **Decision**: one STA UI thread (`CoInitializeEx(COINIT_APARTMENTTHREADED)`) runs the Win32 message
  loop and a small single-threaded executor in `cuw-win-core::ui`:
  - `spawn_local` for `!Send` futures; wake = `PostMessageW(WM_APP_WAKE)` to a hidden top-level window;
    the wndproc drains the run queue.
  - Re-entrant: a task is taken out of the queue (`Rc`), polled without holding any executor borrow,
    and never polled while it is already being polled. Nested message loops (`MessageBoxW`,
    `TrackMouseEvent` drags, modal sizing) keep polling other tasks.
  - `sleep(Duration)` and `UiTimer` use `SetTimer` on the hidden window.
  - Off-thread work (`run_blocking`) runs on a plain `std::thread` and the result comes back through a
    oneshot whose waker posts to the UI thread.
  - `post(f)` marshals a closure from any thread.
- **Rationale**: WebView2 objects are STA-bound and `!Send`; their completion handlers arrive through
  the message loop. The C# design is "everything on the Dispatcher" (INV 2B §9.5); this mirrors it.
- **Alternatives**: tokio + `LocalSet` (needs its own blocking loop, fights the Win32 loop, large
  dependency for two HTTP calls); `async-executor` (fine, but re-entrancy under nested loops must be
  handled anyway and the integration is the same size as the executor itself).

## R-03 HTTP client for status page and GitHub

- **Decision**: `ureq` 3.5.2 with `rustls` (ring), `platform-verifier` (Windows certificate store, so
  corporate TLS inspection roots work like in .NET), `win-system-proxy` (reads the WinINET proxy like
  HttpClient), `gzip`. Called only inside `run_blocking`. Wrapper crate `cuw-http` exposes
  `get_text` and `download_to_file`. claude.ai is never fetched over HTTP (INV 2B §0).
- **Rationale**: two endpoints, at most one call per 5 min; blocking + worker thread is the simplest
  correct model, no async runtime. Timeouts: status 100 s total (HttpClient default), GitHub 5 min.
- **Alternatives**: `reqwest` 0.13.5 (pulls tokio + hyper, heavier exe and build); WinHTTP via
  `windows` (no new dependency, native proxy/certs, but ~300 lines of unsafe; kept as fallback behind
  the same `cuw-http` API if `win-system-proxy` misses PAC scripts). **VERIFY S1**: status fetch
  behind the owner's network works.

## R-04 WebView2 environment and options compatibility

- **Decision**: one environment per process, created with
  `CreateCoreWebView2EnvironmentWithOptions(browserExecutableFolder = null, userDataFolder =
  %LOCALAPPDATA%\ClaudeUsageWidget\profiles\default, environmentOptions = None)`. The folder is
  created first. A failed creation is not cached (next call retries; INV 2B §1.1).
- **Rationale**: the C# code passes `options = null` (WebViewEnvironment.cs:84-87, INV 2B §1.1). A
  browser process already running on a UDF rejects a new environment whose options differ with
  `HRESULT_FROM_WIN32(ERROR_INVALID_STATE)` (0x8007139F); documented triggers include
  `ExclusiveUserDataFolderAccess` and `AreBrowserExtensionsEnabled`, and community reports add DPI
  awareness and other options. Passing no options object and keeping PerMonitorV2 (same as the C#
  manifest) gives the same configuration as the C# build. Only one widget runs at a time (shared
  mutex), but the C# browser process can outlive the C# app by ~0.3 s+ at the first Rust start, so
  matching options matters. On 0x8007139F at environment creation the Rust app waits 2 s and retries
  once, then reports `Network(<message>)` (same visible failure as C#).
- **Alternatives**: `CoreWebView2EnvironmentOptions::default()` from `webview2-com` (sets
  `TargetCompatibleBrowserVersion` and other fields explicitly; equivalent in theory, unproven, so
  not used). Separate UDF per account (rejected in C# history; loses sessions).
- **Sources**: [CoreWebView2EnvironmentOptions](https://learn.microsoft.com/microsoft-edge/webview2/reference/winrt/microsoft_web_webview2_core/corewebview2environmentoptions),
  [ICoreWebView2EnvironmentOptions2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2environmentoptions2),
  [Process model](https://learn.microsoft.com/en-IE/microsoft-edge/webview2/concepts/process-model),
  [WebView2Feedback #2703](https://github.com/MicrosoftEdge/WebView2Feedback/issues/2703).
- **VERIFY S1**: start Rust within 1 s of quitting C# on the owner's profile; no 0x8007139F.

## R-05 Profiles (per-account sessions)

- **Decision**: account id `"default"` -> controller created with
  `ICoreWebView2Environment::CreateCoreWebView2Controller(hwnd)` (no options: implicit default
  profile, `EBWebView\Default`). Any other id -> `ICoreWebView2Environment10::
  CreateCoreWebView2ControllerOptions`, `put_ProfileName(id)`, `CreateCoreWebView2ControllerWithOptions`.
  After creation read `ICoreWebView2_13::Profile().ProfileName()` and compare ordinal-ignore-case;
  mismatch -> error `WebView2 gave profile '<actual>' for account '<label>', expected '<name>'.`
  Login and fetch controllers use the same helper (INVARIANT, INV 2B §1.2).
- **Rationale**: INV 2B §1.2, §14.2: `ProfileName="default"` loses the legacy sign-in; only the
  implicit profile keeps it.
- **Alternatives**: none acceptable (users would be signed out; FR-003).

## R-06 Hidden fetch WebView2 on the STA thread

- **Decision**: per account a plain Win32 top-level window, class `CuwFetchHost`, created with
  `WS_POPUP`, ex-style `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, position (-32000,-32000), size 0x0,
  never `ShowWindow`; controller `put_IsVisible(false)`. The wndproc swallows `WM_SHOWWINDOW` and
  rewrites `WM_WINDOWPOSCHANGING` to keep `SWP_HIDEWINDOW` and the off-screen position (the C# saw
  WebView2 set `WS_VISIBLE` on its parent). Controller COM pointer and every event token are owned by
  the session struct; tokens are removed in `close()`.
  Event hooks as INV 2B §2.1 (ProcessFailed reset kinds, DownloadStarting cancel+handled,
  WebResourceResponseReceived diagnostics for `/api/organizations`, NavigationStarting redirect log).
  Idle lease 15 s with `ICoreWebView2_19::put_MemoryUsageTargetLevel` Low/Normal (missing interface =
  skip; `E_NOINTERFACE` is the Rust equivalent of `NotImplementedException`).
- **Navigation + read**: `Navigate(url)`; await `NavigationCompleted` (token removed after); read
  `ICoreWebView2NavigationCompletedEventArgs2::HttpStatusCode`, `IsSuccess`, `WebErrorStatus`;
  classify with `cuw-claude::nav::classify` (HTTP status first, INV 2B §3.2); on success
  `ExecuteScript("document.body.innerText || document.body.textContent || ''")` and JSON-unwrap with
  `cuw-claude::nav::unwrap_script_result`. 30 s timeout per navigation (UI timer racing the event).
- **Cookies**: `ICoreWebView2_2::CookieManager().GetCookies("https://claude.ai", handler)` ->
  iterate `ICoreWebView2CookieList` -> `(name, domain, value)` -> `cuw-claude::cookie::is_session_cookie`.
- **Dead detection**: raw HRESULT 0x8007139F or 0x80010108 from any call, or the session's own
  `Closed` state -> dead -> reset and retry once (INV 2B §2.3).
- **Rationale**: same four layers of insurance as C#; all logic that can be pure is in `cuw-claude`.
- **Alternatives**: `HWND_MESSAGE` parent (WebView2 controllers need a real window; message-only
  parents are reported to fail creation), visible 1x1 window (risk of taskbar/alt-tab artifacts).
- **VERIFY S1**: no visible window appears during polls (watch with Spy++/`console-watch`), and
  `WebErrorStatus` integer values match the names in contracts/claude-web.md (check against
  `webview2-com-sys` constants).

## R-07 Login window

- **Decision**: Win32 top-level window 1000x720 DIP, centred, title
  `Sign in to Claude.ai — account "<label>"`; child controls: STATIC hint (wrapped), EDIT paste box,
  BUTTON `Open`, and the WebView2 controller filling the rest (`put_Bounds` on `WM_SIZE`,
  `MoveFocus` on `WM_SETFOCUS`, `NotifyParentWindowPositionChanged` on move). Enter in the EDIT
  (subclass) or the button -> https-only check (tooltip `That is not an https link.`). Init:
  controller -> verify profile -> `ICoreWebView2Profile2::ClearBrowsingDataAll` -> navigate
  `https://claude.ai/login` -> cookie poll every 2 s and after each NavigationCompleted -> on success
  raise SignedIn then close. A `closed` flag is checked after every await.
- **Rationale**: INV 2B §4.2 exactly; native controls keep keyboard/IME/clipboard for free.
- **Alternatives**: drawing the bar with D2D (needs a text editor), WebView2-hosted HTML bar
  (changes the look and needs script injection, which the C# avoids).

## R-08 Desktop widget window: per-pixel alpha, bottom z-order, no activation

- **Decision**: `WS_POPUP` + `WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, presented with
  `UpdateLayeredWindow(ULW_ALPHA)` from a 32 bpp premultiplied top-down DIB section. Direct2D draws
  into the DIB through `ID2D1DCRenderTarget` (`D2D1_ALPHA_MODE_PREMULTIPLIED`, `BindDC` on the memory
  DC). `WM_MOUSEACTIVATE -> MA_NOACTIVATE`; `WM_WINDOWPOSCHANGING` rewrite: `hwndInsertAfter =
  HWND_BOTTOM`, clear `SWP_NOZORDER` (INV 2C §1.1). Panel background alpha = `max(opacity, 1/255)`
  (painted byte >= 1), so the panel stays hit-testable while alpha-0 pixels (outside the rounded
  corners) pass clicks through, exactly the layered-window rule the C# relies on.
- **Rationale**: only layered windows get per-pixel hit-test pass-through to *other processes'*
  windows. `WM_NCHITTEST -> HTTRANSPARENT` only forwards within the same thread, so a DirectComposition
  window (`WS_EX_NOREDIRECTIONBITMAP`) would block clicks on the desktop behind transparent corners.
  ULW cost is trivial at these sizes (< 800x800 px) and redraws are rare (data, hover, drag).
- **Alternatives**: DirectComposition + `WS_EX_NOREDIRECTIONBITMAP` (GPU-composited, but breaks
  click-through; would need `WS_EX_TRANSPARENT` toggling by mouse polling), `SetLayeredWindowAttributes`
  (uniform alpha only, and mixing it with ULW is invalid; INV 2D §3.3).
- Hover detection uses `TrackMouseEvent(TME_LEAVE)`; drag/resize uses `SetCapture` and window-relative
  deltas as INV 2C §1.7; the ghost in edit mode is drawn in the same surface (cell re-rendered at 0.6
  opacity) instead of a `VisualBrush`.

## R-09 Units, DPI, clamp (FR-024 fix 1)

- **Decision**: layout math in DIPs (`f64`, `cuw-layout`); the window converts with the DPI of its
  monitor (`GetDpiForWindow`, updated on `WM_DPICHANGED`, using the suggested rect). Persisted
  `WidgetX/WidgetY/WidgetSide` stay DIPs: `x_dip = x_px / scale(monitor holding the panel's top-left)`.
  Restore: for each monitor `m`, test whether `(x*scale_m, y*scale_m)` lies in `m`'s rect; first hit
  wins, else primary monitor's scale. Clamp in physical pixels against that monitor's work area
  (`MonitorFromPoint(MONITOR_DEFAULTTONEAREST)` + `GetMonitorInfoW`), left/top edge winning (INV 2C
  §1.9). At 100 % this is identical to the C# result.
- **Rationale**: removes the DIP/pixel mix-up the spec names as a bug while keeping the file format
  the C# build reads.
- **Alternatives**: persist pixels (C# would misplace the panel after a switch back), replicate the
  bug (spec forbids).
- **VERIFY S2**: at 150 % on the owner's monitor the Rust panel opens where the C# panel was.

## R-10 DirectWrite metrics vs WPF

- **Decision**: fonts by family name through DirectWrite: `Consolas` (panel, band), `Segoe UI`
  (tray digits, Bold), `Segoe UI Variable Text` with fallback `Segoe UI` (menu, 9 pt = 12 DIP),
  `Segoe MDL2 Assets` (glyphs). Line height = `(ascent + descent + lineGap) / designUnitsPerEm *
  size` from `IDWriteFontFace::GetMetrics`; for Consolas this is 2398/2048 = 1.1708984375, the same
  constant the C# uses (`CaptionLineHeight = 8 * 1.1708984375`), so panel geometry constants stay in
  `cuw-layout` as numbers and do not depend on measuring. Text widths (band columns, status label
  shrink-to-fit, name ellipsis) come from `IDWriteTextLayout::GetMetrics().widthIncludingTrailingWhitespace`
  through the `TextMeasure` trait. Weights: SemiBold 600, Medium 500, Bold 700. Panel text uses
  grayscale AA on the layered surface (ClearType is wrong on transparent targets), same as WPF on a
  layered window.
- **Rationale**: WPF `FormattedText` uses the same font tables; differences are AA and hinting only,
  which FR-023 allows.
- **Alternatives**: hard-coded advance widths (breaks on fallback fonts), GDI (no per-pixel alpha).
- **VERIFY S2**: compare `CUW_RENDER_PREVIEW` PNGs of both builds side by side per layout preset.

## R-11 Tray icon

- **Decision**: hidden top-level window (not `HWND_MESSAGE`, see R-13) owns the icon;
  `Shell_NotifyIconW(NIM_ADD)` with `uID = 1` (no GUID: a GUID binds the icon to the exe path),
  `NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP`, then `NIM_SETVERSION(NOTIFYICON_VERSION_4)`.
  Right-click and keyboard (`WM_CONTEXTMENU`, also `NIN_KEYSELECT`) open the menu; left click does
  nothing (parity). Tooltip truncated to 127 UTF-16 units. `RegisterWindowMessageW("TaskbarCreated")`
  -> `NIM_ADD` again + re-render (FR-024 fix 4). Icon pixels: size =
  `GetSystemMetricsForDpi(SM_CXSMICON, dpi)` with `dpi = GetDpiForWindow(Shell_TrayWnd)` (fallback
  `GetDpiForSystem`); drawn by D2D into a WIC premultiplied BGRA bitmap, grayscale AA; HICON via
  `CreateIconIndirect` (32 bpp color DIB + empty mask); previous HICON destroyed after `NIM_MODIFY`.
  Digit fit (pure, `cuw-layout::icon::fit_em`): Segoe UI Bold, em from `0.95*H` down in 0.5 px steps to 6, first with
  `measured_width + gdiplus_padding <= W - 1`, where `gdiplus_padding = em / 3` emulates GDI+
  `MeasureString`'s 1/6 em per side so the chosen size matches the C# result. Ink `#1B1B1B` on a light
  taskbar, white on dark; ring = 2 px pen inset 2.
- **Rationale**: INV 2D §1; the C# used `SystemInformation.SmallIconSize` (system DPI); using the
  taskbar's DPI gives the "tray's pixel size" the spec asks for and equals it on single-monitor
  setups. Re-rendered on TaskbarCreated, display change and theme change.
- **VERIFY S1**: the 1/3 em padding guess against C# icons for "5", "42", "100" at 100 % and 150 %.

## R-12 Owner-drawn popup menu

- **Decision**: custom popup window class `CuwMenu` per open (sub)menu: `WS_POPUP`, ex-style
  `WS_EX_TOOLWINDOW | WS_EX_TOPMOST`, class style `CS_DROPSHADOW`; Win11 (build >= 22000)
  `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE=33, DWMWCP_ROUND=2)`, result ignored. Painted
  with D2D `ID2D1HwndRenderTarget` (opaque, Panel colour, 1 px Track border, hover rounded rect radius
  4 at alpha 153 inset 3 from the menu width, ticks U+E73E in Accent instead of the icon, separators,
  arrows, glyph bitmaps centred by path bounds at `0.72*px`). Metrics: row 28 DIP (2+4+text+4+2
  model), menu padding 2, item padding 2/4; exact numbers are taken from the C# `CUW_RENDER_MENU`
  `metrics.txt` fixture. Behavior: opens with `SetForegroundWindow` on the root menu, bottom edge at
  the cursor then `cuw-layout::placement::within(work area)` + `HWND_TOPMOST` (INV 2D §2.10); keyboard
  (Up/Down/Left/Right/Enter/Esc) and mouse; submenu opens on hover after 400 ms or click, positioned
  off the item's right edge (left if no room); closes on Esc, click outside (`WM_ACTIVATE` inactive
  to a non-menu window), explicit close actions; toggles and radio items keep the menu open and the
  model is re-synced and repainted in place (INV 2D §2.8). Tooltips per item via a comctl32 v6
  tooltip control in tracking mode. The menu content is a `MenuModel` built in `cuw-view` on every
  open and every store change while open.
- **Open item**: INV 2D §2.8 suspects that clicks on Accounts entries and Layout items close the C#
  menu (no Closing hook on those dropdowns). **VERIFY S0** (tasks T034, before S1-E and S1-H) on the C# build; default: keep open like
  every other submenu, and record the observed C# behavior as the rule.
- **Alternatives**: native `HMENU` + `MF_OWNERDRAW` (closes on every click, no tooltips; spec needs
  keep-open), WinUI/XAML islands (toolkit, rejected by owner).

## R-13 System events, taskbar band ownership, WinEvent hooks

- **Decision**: one hidden *top-level* window `CuwSystemEvents` (never shown; message-only windows
  do not receive broadcasts) receives `WM_SETTINGCHANGE` (re-read `AppsUseLightTheme`,
  `SystemUsesLightTheme`; notify only on change, INV 2C §3.2), `WM_POWERBROADCAST` /
  `PBT_APMRESUMEAUTOMATIC` (resume -> RefreshAll), `TaskbarCreated`, `WM_DISPLAYCHANGE`, and is the
  tray icon's callback window. Band: layered ULW window as R-08 but owned via
  `SetWindowLongPtrW(GWLP_HWNDPARENT, Shell_TrayWnd)`, `HWND_TOPMOST` once per dock and on burial
  only; four `SetWinEventHook(..., WINEVENT_OUTOFCONTEXT)` hooks installed on the UI thread; the
  `extern "system"` callback reaches the band through a thread-local `Weak` and runs inside the panic
  guard; REORDER runs the burial check synchronously, others post to the executor. All rules
  (geometry, probe verdict, hysteresis, burial decision) are pure in `cuw-band-logic`; the platform
  crate samples `WindowFromPoint`, `GetAncestor`, DWM cloaked, z-order walk and feeds them in. On
  `TaskbarCreated` the band re-docks at once (plus the 5 s backstop, parity).
- **Rationale**: INV 2D §3 rules exist because of live bugs; separating decisions from sampling makes
  them testable for the first time.
- **Alternatives**: `WS_CHILD` into `Shell_TrayWnd` (abandoned in C# history), DComp (click-through,
  see R-08; the band is display-only, but one technique for both windows is simpler).

## R-14 Settings JSON compatibility

- **Decision**: read: strip UTF-8 BOM, strip trailing commas outside strings (STJ
  `AllowTrailingCommas`), reject comments (STJ default), parse to `serde_json::Value` with
  `preserve_order`, then read each known key case-sensitively with per-type tolerant readers
  (contracts/settings-json.md): enums by name case-insensitive / defined ordinal / numeric string;
  unknown -> first variant; `null`, bool, object, array -> whole file unparseable for a non-nullable
  enum, `null` -> `None` for a nullable one. Unknown keys are kept in an `extra` map at top level, in
  each account and in `Layout`/`Block`, and written back after the known keys. Write: all known keys
  in C# declaration order, nulls written, enums as names, 2-space indent, CRLF line ends, no BOM,
  integral doubles without `.0` (STJ style), `LaidOut` written (computed, ignored on read).
  Atomic replace = `FileReplace` port: temp `<target>.<32 hex>.tmp`, `File::sync_all`,
  `MoveFileExW(MOVEFILE_REPLACE_EXISTING)` (std `fs::rename` does this on Windows), backoff
  50/100/200/400 ms, transient classification by `raw_os_error` 32/33/5 + read-only/probe-open with
  `share_mode` (`std::os::windows::fs::OpenOptionsExt`), symlink target resolved and replaced.
- **Rationale**: INV 2A §3 and §11.7-11.9; constitution IV requires keeping unknown fields (C# drops
  them, harmless for the C# reader). A manual `Value` walk gives exact tolerance semantics; serde
  derive + `#[serde(flatten)]` cannot express "unknown enum name -> first variant, null -> whole-file
  error" cleanly and mixes badly with flatten.
- **Alternatives**: serde derive with custom enum deserializers (would still need the trailing-comma
  pre-pass and loses key order of extras), `json5` (accepts comments, which STJ rejects).

## R-15 Updater swap and coexistence with C# leftovers

- **Decision**: Rust-to-Rust update (S5): same prompts and checks as INV 2A §5; download zip to
  `%TEMP%\ClaudeUsageWidget-update-<v>\release.zip`, extract, find `ClaudeUsageWidget.App.exe`
  anywhere in the tree, then in the install dir: rename running exe to `ClaudeUsageWidget.App.exe.old`
  (Windows allows renaming a running image), copy every extracted file into the install dir (20
  attempts, 500 ms apart), start the new exe with `--after-update <pid>`, quit. The new process
  waits (max 60 s) for `<pid>` to exit before taking the mutex, deletes `*.old` and the work dir. On
  copy failure: rename `.old` back, keep running, show `Update failed: <msg>`.
  "Cannot self-install" source-build check: a build-time flag `CUW_RELEASE_BUILD=1` set only by the
  release script (`option_env!`); any other build (cargo run/test) counts as a source build. It does
  NOT look at `ClaudeUsageWidget.App.dll`, so C# leftovers in the folder after the switch (WPF native
  DLLs, `*.dll` from the self-contained publish) never fool it; leftovers are not loaded by the Rust
  exe and are left in place (deleting user files is out of scope).
  C#-to-Rust (S6): the last C# updater installs the Rust zip unchanged (it searches the exe by name in
  a `*-win-x64.zip` and runs it with no args); the zip contains `ClaudeUsageWidget.App.exe` (and
  nothing that collides with C# file names).
- **Rationale**: no PowerShell dependency, one exe; versioning compatible with `System.Version`.
- **Alternatives**: keep the PowerShell script (works, fallback if the rename swap fails on locked
  folders in S5 testing); separate updater exe (a second binary to ship).
- **VERIFY S6**: real C# 0.2.x install updated to the first Rust release on a test account.

## R-16 DateTimeOffset round trip

- **Decision**: type `DateTimeOffset { instant: jiff::Timestamp, offset: jiff::tz::Offset }`
  (data-model.md). Read: lenient ISO-8601 (`T` or space, fraction 0-9 digits, `Z` or `±HH:MM`,
  offset-less = UTC; STJ accepts these forms). Write STJ-style: `yyyy-MM-ddTHH:mm:ss[.fffffff]±HH:MM`,
  fraction truncated to 7 digits (100 ns ticks) and trailing zeros trimmed, never `Z` (STJ writes
  `+00:00` for a zero offset), offset = the local offset at the time of the write (C# uses
  `DateTimeOffset.Now`). Comparisons use the instant only.
- **Rationale**: the C# reader must accept every value Rust writes (SC-003); >7 fraction digits is
  the risky case and is avoided.
- **VERIFY S1**: capture a C#-written settings.json with a pause and a subscription stamp as a
  fixture; byte-compare Rust's re-write of those values; run the C# build once on a Rust-written file.

## R-17 Usage `resets_at` and other lenient time parsing

- **Decision**: string -> lenient ISO parser of R-16, offset-less = UTC, failure -> None; number > 0
  -> epoch seconds (fraction allowed, `(seconds*1000) as i64` ms); out of range -> `Network(<msg>)`
  (C# threw `ArgumentOutOfRange` into Failed(Network)). The .NET parser also accepts loose formats
  (e.g. `11/21/2026`); not supported, recorded as an intentional narrowing since the server sends ISO.

## R-18 Logging format compatibility

- **Decision**: `cuw-log` owns `format_line(now_local, account, event, details)` =
  `yyyy-MM-ddTHH:mm:ss.fff±HH:MM <account> <event> <details>` with `\r` and `\n` each replaced by one
  space, CRLF, UTF-8 no BOM; rotation when size > 1 048 576 before an append (move over `.1`), global
  mutex, never fails. Producers call `cuw_log::event(account, event, details)` which emits a `tracing`
  event on target `cuw::widget`; the `WidgetLogLayer` writes those (and only those) to widget.log.
  Other `tracing` events (the C# `Debug.WriteLine` equivalents) go to `OutputDebugStringW` in debug
  builds and are dropped in release, matching C#.
- **Rationale**: Principle V (tracing) plus FR-019 (same file and format).

## R-19 Panic containment (FR-020)

- **Decision**: release profile keeps `panic = "unwind"`. `cuw-win-core::guard::guarded(label, f)`
  wraps every `extern "system"` entry (wndprocs, WinEvent callback, timer procs) and every COM
  completion/event handler closure (`catch_unwind(AssertUnwindSafe)`); a panic logs
  `- unhandled panic <label>: <message>` and returns a neutral value (`DefWindowProcW` result /
  `S_OK`). The executor polls each task inside the guard; a panicking task is dropped and logged as
  `fire-and-forget panic: <message>`. A panic hook records file:line into the same line. Errors (not
  panics) from handlers are logged as `unhandled tray <Type>: <msg>` style lines (contracts/log-format.md).
- **Rationale**: unwinding out of `extern "system"` aborts the process since Rust 1.81; the C# policy
  is "log and keep running".

## R-20 Lints and safety

- **Decision**: `[workspace.lints.rust] unsafe_op_in_unsafe_fn = "deny"`, `missing_debug_implementations
  = "warn"`; `[workspace.lints.clippy] all = { level = "deny", priority = -1 }`, `pedantic = { level =
  "warn", priority = -1 }`, `unwrap_used = "deny"`, `expect_used = "warn"`, `dbg_macro = "deny"`,
  `todo = "deny"`, `print_stdout = "deny"`, `print_stderr = "deny"`, `undocumented_unsafe_blocks =
  "deny"`, allow `module_name_repetitions`, `missing_errors_doc`, `must_use_candidate`,
  `cast_precision_loss` (layout math). Tests may `unwrap` (`#[cfg_attr(test, allow(clippy::unwrap_used))]`
  via `clippy.toml allow-unwrap-in-tests = true`). Pure crates `#![forbid(unsafe_code)]`.
- **Release profile**: `opt-level = "s"`, `lto = "fat"`, `codegen-units = 1`, `strip = true`,
  `panic = "unwind"`; dev profile `debug = "line-tables-only"`, `[profile.dev.package."*"] opt-level = 1`,
  `rust-lld` linker.

## R-21 Small parity decisions needing owner sign-off

- Staggered poll matched by account id (C# drops one poll after a rename during the wait). Proposed:
  match by id.
- Login/session log label stays the construction-time display name until restart (parity, kept).
- Updater mechanism R-15 (internal change; prompts identical).
- `resets_at` loose non-ISO formats not parsed (R-17).
- Tray icon size from the taskbar DPI instead of the system DPI (R-11; equal on one monitor).
- Developer switches `CUW_RENDER_PREVIEW` / `CUW_RENDER_MENU` keep their names but produce D2D
  renders (spec Assumption allows equivalent tooling).

## R-22 Single instance, autostart, paths

- **Decision**: `CreateMutexW(None, true, "ClaudeUsageWidget.SingleInstance")` (no prefix =
  session namespace, same object the C# build opens); `ERROR_ALREADY_EXISTS` -> exit 0 silently;
  handle kept for the process life. Autostart: HKCU `...\Run`, value `ClaudeUsageWidget`, REG_SZ
  `"<GetModuleFileNameW path>"` with quotes; enabled iff the value exists and is a string type, REG_SZ or REG_EXPAND_SZ, as .NET `GetValue` returns a string for both (INV 2A §4);
  after the switch the C# value already points to the same path and exe name, so it keeps working
  (FR-016). Paths via `SHGetKnownFolderPath(FOLDERID_RoamingAppData | FOLDERID_LocalAppData)` and
  `GetTempPathW`.
