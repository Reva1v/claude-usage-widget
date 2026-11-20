# Data model: Rust rewrite

Rust domain types per owning crate. JSON names/serialization rules for settings are in
[contracts/settings-json.md](contracts/settings-json.md); this file gives the in-memory types,
validation and state transitions. Public signatures of functions are in
[contracts/crate-interfaces.md](contracts/crate-interfaces.md). INV = behavior inventory.

Common conventions: instants are `jiff::Timestamp`; values that round-trip through settings keep
their offset as `DateTimeOffset`; all public enums are `#[non_exhaustive]` only where the server can
add values (none of the settings enums, because the tolerant reader maps unknowns to a variant).

## 1. Time (`cuw-settings::time`)

```rust
pub struct DateTimeOffset { pub instant: jiff::Timestamp, pub offset: jiff::tz::Offset }
// Eq/Ord compare `instant` only (C# DateTimeOffset equality is instant-based).
```

## 2. Settings (`cuw-settings`)

```rust
pub struct Settings {                       // C# WidgetSettingsData; field order = write order
    pub widget_visible: bool,               // "WidgetVisible", default true
    pub position_locked: bool,              // "PositionLocked", default false
    pub model_bucket: Option<String>,       // "ModelBucket", None = auto
    pub widget_side: f64,                   // "WidgetSide", default 170.0; clamp on use [150, 340]
    pub widget_x: Option<f64>,              // "WidgetX" DIPs; applied only with widget_y
    pub widget_y: Option<f64>,              // "WidgetY" DIPs, panel top (window top + strip pad)
    pub tray_metric_key: String,            // "TrayMetricKey", default "five_hour"; free string
    pub taskbar_band_enabled: bool,         // "TaskbarBandEnabled", default false
    pub band_position: String,              // "BandPosition", default "tray"; "left" or anything (= tray)
    pub band_view: Option<BandView>,        // "BandView", None = by account count
    pub theme: Option<ThemeChoice>,         // "Theme", None = System
    pub panel_opacity: Option<f64>,         // "PanelOpacity", None = 0.82
    pub tray_account_id: Option<String>,    // "TrayAccountId", None = first account
    pub export_directory: Option<String>,   // "ExportDirectory", None/blank = export off
    pub layout: Option<WidgetLayout>,       // "Layout" (type from cuw-layout)
    pub status_mode: Option<StatusMode>,    // "StatusMode"
    pub model_dial: Option<ModelDial>,      // "ModelDial"
    pub plan_line: Option<PlanLine>,        // "PlanLine"
    pub accounts: Vec<AccountProfile>,      // "Accounts", default []
    pub legacy: LegacyFields,               // "OrganizationId", "RetryPausedUntil", "ConsecutiveRateLimits"
    pub extra: serde_json::Map<String, serde_json::Value>, // unknown top-level keys, preserved
}
impl Default for Settings { /* values above */ }

pub struct LegacyFields {
    pub organization_id: Option<String>,
    pub retry_paused_until: Option<DateTimeOffset>,
    pub consecutive_rate_limits: i32,
}

pub struct AccountProfile {                 // positional record in C#; all keys written
    pub id: String,                         // "Id"; also WebView2 ProfileName ("default" = implicit profile)
    pub display_name: String,               // "DisplayName"
    pub organization_id: Option<String>,
    pub retry_paused_until: Option<DateTimeOffset>,
    pub consecutive_rate_limits: i32,
    pub export_path: Option<String>,
    pub capabilities: Option<Vec<String>>,  // None = never fetched; Some([]) = asked, told nothing
    pub rate_limit_tier: Option<String>,
    pub raven_type: Option<String>,
    pub subscription_fetched_at: Option<DateTimeOffset>,
    pub extra: serde_json::Map<String, serde_json::Value>,
}

pub enum ThemeChoice { System, Dark, Light }   // first variant = tolerant fallback
pub enum BandView { Metrics, Accounts }
pub const ACCOUNT_LIMIT: usize = 4;            // AccountLimits.Max
pub const LEGACY_ACCOUNT_ID: &str = "default";
pub const DEFAULT_SIDE: f64 = 170.0; pub const MIN_SIDE: f64 = 150.0; pub const MAX_SIDE: f64 = 340.0;
```

Helpers (INV §3): `account(id)`, `account_with_organization(org, except_id)`,
`with_account(id, f)` (unknown id = no change, never resurrects), `clamp_side`.

**Validation rules**
- Read never fails on values, only on structure (contracts/settings-json.md): unknown enum ->
  first variant; `WidgetSide` NaN cannot occur in JSON; saving NaN/inf fails (`SaveError::NonFinite`),
  leaving the old file and no temp (test-pinned).
- `id` charset (ASCII letters/digits, `# @ $ ( ) + - _ ~ .`, space, <= 64, not ending in `.`/space) is
  documented, not validated (parity); new ids are dashed lowercase UUIDv4.
- Migration on every successful load, never on save (INV 2A §3.6): `accounts.len() > 4` -> keep the
  first 4; `== 0` and the file existed and parsed -> one `AccountProfile{id:"default",
  display_name:"default", <legacy fields>}`, `tray_account_id = tray_account_id.or("default")`, legacy
  cleared; else unchanged (same value; test uses identity, Rust test asserts `Migrated::Unchanged`).

**SettingsStore state** (INV 2A §3.2-3.3)

| File state | `load()` first ever | `load()` after a good read | `current()` |
|---|---|---|---|
| missing | `Ok(Settings::default())` (empty accounts; not remembered, not migrated) | last good | same as load |
| parses | migrated, remembered | migrated, remembered | same |
| unparseable | bad copy kept; copy ok -> defaults; copy failed -> `Err(Unparseable{copied:false})` | `Err(Unparseable)` (bad copy kept) | last good, else `Err` |
| held > 0.75 s / ACL denies | `Err(Io)` | `Err(Io)` | last good, else `Err` |

`save(&Settings)` = FileReplace write then remember. Bad copy: `<path>.bad-<UTC yyyyMMddHHmmss>`,
skipped if any existing `<name>.bad-*` has the same SHA-256.

## 3. Layout (`cuw-layout`)

```rust
pub enum LayoutFlow { Grid, Row, Column }
pub enum NamePlacement { Cell, Above, Below, Hidden }
pub enum BlockItem { Name, FiveHour, SevenDay, Model, Status }   // declaration order = cycle order
pub enum StatusMode { Cell, Line }
pub enum ModelDial { Shown, Hidden }
pub enum PlanLine { Hidden, Shown }
pub struct BlockLayout { pub flow: LayoutFlow, pub name: NamePlacement, pub order: Option<Vec<BlockItem>>,
                         pub extra: JsonMap }                       // order None when missing in JSON
pub struct WidgetLayout { pub panel_flow: LayoutFlow, pub block: Option<BlockLayout>, pub extra: JsonMap }
pub enum PanelView { Classic, Accounts }
pub struct ResolvedLayout { pub layout: WidgetLayout /* block Some, order Some */, pub status: StatusMode,
                            pub model_dial: ModelDial, pub plan_line: PlanLine }
pub struct PanelMetrics { pub scale: f64, pub dial: f64, pub padding: f64, pub gap: f64, pub block_gap: f64,
    pub corner_radius: f64, pub rows: usize, pub columns: usize, pub block: BlockMetrics,
    pub width: f64, pub height: f64, pub status_band: f64, pub top_gap: f64, pub bottom_gap: f64,
    pub toolbar: ToolbarMetrics, pub toolbar_reserve: f64 }
pub struct BlockMetrics { pub rows: usize, pub columns: usize, pub width: f64, pub height: f64, pub name_height: f64 }
pub struct ToolbarMetrics { pub button: f64, pub pitch: f64, pub rows: usize, pub columns: usize, pub height: f64 }
pub struct RectD { pub x: f64, pub y: f64, pub w: f64, pub h: f64 }     // DIPs
pub struct RectI { pub left: i32, pub top: i32, pub right: i32, pub bottom: i32 } // physical px
pub enum Edge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
pub struct CellHit { pub block: usize, pub cell: usize }
pub enum ToolbarButton { Done, PanelFlow, BlockFlow, Name, PlanLine, StatusMode, ModelDial, Lock, Hide } // index 0..8
pub enum HeaderButton { Hide, Edit, Lock }
```

Rules: `Sanitize` (missing `block`/`order` treated as default rather than the C# NullReference;
unusable order -> `[Name, FiveHour, SevenDay, Model]`; then Status present iff `StatusMode::Cell`, Model
present iff `ModelDial::Shown`, appended at the end); idempotent. `LaidOut` = order minus `Name`
unless name placement is `Cell`. Panel metrics formulas INV 2C §1.3; `CAPTION_LINE = 8.0 *
1.1708984375`; `EDGE_BAND = 8.0` DIP unscaled.

**Edit-mode geometry state** (`EditStrip`): `{ above: bool, reserve: f64 }`; entering: above iff
`panel_top - reserve >= work_area_top` (first run: below); window top shifts by reserve so the panel
does not move; `reconsider` flips on drag/resize when the top gets within `reserve` of the work-area
top and reports the anchor shift.

## 4. Usage (`cuw-usage`)

```rust
pub struct UsageBucket { pub utilization: f64, pub resets_at: Option<jiff::Timestamp> }
pub struct UsageSnapshot { pub buckets: BTreeMap<String, UsageBucket>, pub source_updated_at: Option<jiff::Timestamp> }
// PartialEq by contents; get(key) -> Option<&UsageBucket>
pub enum UsageErrorKind { NoCredentials, Unauthorized, MalformedResponse, RateLimited, Network }
pub enum UsageError {
    NoCredentials,
    Unauthorized { message: Option<String> },   // Some = duplicate account
    MalformedResponse,
    RateLimited { retry_after_seconds: Option<u32> },
    Network { message: String },
}
pub enum UsageState { Loading, Ok { snapshot: UsageSnapshot, fetched_at: jiff::Timestamp }, Failed(UsageError) }
pub enum ThresholdLevel { Ok, Warning, Danger }  // <0.6, <0.85, else
```

`UsageError::description()` texts INV 2B §7.7; `duplicate_account(name)` = Unauthorized with
`This Claude.ai login is already added as "<name>". Sign in again with another login.`;
`kind()` maps variants.

## 5. Service status (`cuw-status`)

```rust
pub enum ServiceStatus { Operational, Degraded, PartialOutage, MajorOutage, Maintenance, Unknown }
```
Labels `OK SLOW PARTIAL DOWN MAINT —`; line words `operational, degraded, partial outage, major
outage, under maintenance, state unknown`; `line(status, mode)` None only for (Operational, Cell).

## 6. claude.ai rules (`cuw-claude`)

```rust
pub struct SubscriptionFields { pub capabilities: Option<Vec<String>>, pub rate_limit_tier: Option<String>, pub raven_type: Option<String> }
pub enum WebErrorStatus { Unknown, CertificateCommonNameIsIncorrect, CertificateExpired, ClientCertificateContainsErrors,
  CertificateRevoked, CertificateIsInvalid, ServerUnreachable, Timeout, ErrorHttpInvalidServerResponse,
  ConnectionAborted, ConnectionReset, Disconnected, CannotConnect, HostNameNotResolved, OperationCanceled,
  RedirectFailed, UnexpectedError, ValidAuthenticationCredentialsRequired, ValidProxyAuthenticationRequired,
  Other(i32) }                              // name() gives the .NET enum name; Other -> its number
pub enum NavOutcome { Success, Http { code: u16 } /* already mapped error */, Failed { status: WebErrorStatus, http: u16 } }
pub enum PageError { Usage(UsageError), NavigationFailed { status: WebErrorStatus, http: u16 }, Platform { message: String, dead: bool } }
pub struct FetchIdleLease { users: u32, generation: u64 }   // DELAY = 15 s
pub struct ClaudeAccountState { pub subscription_fetch_failed: bool }  // per-process-run latch
```

**FetchIdleLease transitions**: `enter()`: users+1, generation+1. `exit() -> Result<Option<u64>,
LeaseError>`: users==0 -> `Err(ExitWithoutEnter)`; users-1; `Some(generation)` iff users==0.
`should_release(token)` = users==0 && token==generation.

**Org pick / plan label**: INV 2B §5 exactly (team+chat > first chat; `uuid` then `id`; label rules
1-6; `label_or_null` AND guard; Team + `seven_day_fable` bucket -> Team Premium;
`has_five_hour_limit`).

## 7. Stores (`cuw-store`, sans-IO)

```rust
pub struct RetryState { pub until: DateTimeOffset, pub consecutive_rate_limits: i32 }
pub enum PersistedRetry { Unreadable, None, Some(RetryState) }
pub struct UsageStore { state: UsageState, last_snapshot: Option<UsageSnapshot>, retry_paused_until: Option<jiff::Timestamp>,
    consecutive_rate_limits: i32, in_flight: bool, persisted: Option<Option<RetryState>> /* None = unknown */ }
pub struct StatusStore { status: ServiceStatus, in_flight: bool }
pub const REFRESH_INTERVAL: Duration = 300 s; pub const RETRY_MARGIN: 300 s; pub const LADDER: [3600, 21600, 86400] s;
```

**UsageStore state machine** (INV 2B §7):

```text
new(persisted, now):
  Some(s) && s.until > now -> state = Failed(RateLimited{None}), paused_until = s.until, count = s.count (no Changed)
  Some(s) expired or None   -> state = Loading; if readable: request persist(None)
  Unreadable                -> state = Loading, persisted unknown, no write

begin_load(now, has_credentials):
  in_flight                 -> JoinInFlight
  paused_until > now        -> Paused (no change, no Changed)
  else paused_until = None (memory only)
       !has_credentials     -> state = Failed(NoCredentials), Changed           [no fetch]
       else in_flight = true -> Fetch(ticket)

finish_load(ticket, result, now):   in_flight = false
  Ok(snap)  -> last = snap, count = 0, paused_until = None, persist(None) if differs,
               state = Ok(snap, snap.source_updated_at ?? now), Changed
  Err(RateLimited{ra}) -> count += 1; wait = max(ra ?? 0, LADDER[min(count-1, 2)]);
               paused_until = now + wait + 300 s; persist(Some(until, count)); state = Failed(err), Changed
  Err(other) -> state = Failed(other), Changed      (count and pause untouched)
  Err from injected parts (platform) -> Failed(Network{message})

persist_done(requested, ok): ok -> persisted = Some(requested) ; !ok -> persisted = unknown
```

`snapshot_for_display()` = Ok snapshot else `last_snapshot` (dimmed). StatusStore: `begin_load ->
JoinInFlight | Fetch`; `finish_load(Ok(s))` -> status = s, Changed (even if equal); `Err` -> keep,
no Changed.

**PollSchedule**: `offset_for(i, n, cycle=300 s) = cycle * i / n`; error if `n == 0` or `i >= n`.

## 8. View models (`cuw-view`)

```rust
pub struct DialModel { pub title: String, pub fraction: Option<f64>, pub remaining: Option<String>, pub no_limit: bool }
pub struct AccountRow { pub account_id: String, pub display_name: String, pub dials: [DialModel; 3],
                        pub session_resets_in: Option<String>, pub plan_label: Option<String> }
pub struct TrayMetric { pub label: String, pub value: String }
pub struct BandEntry { pub name: String, pub percent: String, pub resets_in: Option<String> }
pub enum ThemeKind { Dark, Light }
pub struct Palette { pub panel: Rgb, pub track: Rgb, pub text: Rgb, pub dim: Rgb, pub accent: Rgb,
                     pub warning: Rgb, pub danger: Rgb, pub info: Rgb }   // DARK and LIGHT consts, INV 2C §3.4
pub struct Notice { pub title: &'static str, pub detail: String, pub sign_in_button: bool }
pub struct PanelFrame { pub rows: Vec<AccountRow>, pub status: ServiceStatus, pub status_mode: StatusMode,
    pub dimmed: bool, pub status_line: Option<String>, pub notice: Option<Notice>, pub plan_line: PlanLine }
pub struct MenuModel { pub items: Vec<MenuItem> }
pub struct MenuItem { pub id: MenuId, pub text: String, pub glyph: Option<char>, pub kind: MenuItemKind,
                      pub enabled: bool, pub visible: bool, pub tooltip: Option<String>, pub closes_menu: bool }
pub enum MenuItemKind { Action, Check { checked: bool }, Separator, Submenu(Vec<MenuItem>) }
pub enum MenuId { GitHub, Issues, CheckUpdates, RefreshNow, SignIn, TrayShows(TrayMetric3), ModelAuto, ModelBucket(String),
    SelectAccount(String), RenameAccount(String), RemoveAccount(String), AddAccount, PanelView(PanelView), EditLayout,
    Theme(ThemeChoice), Opacity(u8 /* preset index */), ShowOnDesktop, TaskbarBand, BandPosition(BandSide),
    BandView(BandView), LockPosition, LaunchAtLogin, Quit, Header /* submenu parents */ }
pub enum TrayMetric3 { FiveHour, SevenDay, Model }   // MetricIndex: "seven_day"->1, "model"->2, other->0
pub enum BandSide { Tray, Left }
```

Notice (FR-024 fix 3): NoCredentials -> `Not signed in` + button; Unauthorized(None) ->
`Session expired` + **button** (C# had none); Unauthorized(Some) -> `Account added twice` + button.
Opacity: `resolve(None|NaN)=0.82`, clamp [0,1]; presets `[1.0, 0.82, 0.6, 0.4, 0.2, 0.0]`; tick
when `|v-p| < 0.005`; `painted(o) = max(o, 1/255)`; alpha byte `round_half_even(a*255)`.

## 9. Band logic (`cuw-band-logic`)

```rust
pub enum Cover { Visible, Obscured, ObscuredByForeground }
pub struct ProbeSample { pub hit: Option<Hwnd>, pub root: Option<Hwnd>, pub cloaked: bool, pub class: String }
pub struct Visibility { hidden: bool, established: bool, pending: Option<bool> }   // + delay chosen
pub enum VisibilityAction { None, ApplyNow(bool), StartTimer { hidden: bool, ms: u32 }, CancelTimer }
pub struct BandGeometry { pub x: i32, pub y: i32, pub w: i32, pub h: i32 }
pub enum BurialVerdict { Fine, BuriedUnderTray, BuriedUnder { hwnd: Hwnd, class: String } }
```

**Visibility machine** (INV 2D §3.5): request(hidden, by_foreground): first after dock -> ApplyNow;
equal to applied -> cancel pending; same pending -> None (no re-timing); else pending = hidden,
timer = 150 (show) | 150 (hide by foreground) | 1200 (hide). Timer expiry: take pending; re-probe;
verdict flipped -> cancel (diag line); else apply. `dock()` resets `established` and `hidden`.

## 10. Release and export

```rust
pub struct LatestRelease { pub version: String, pub asset_url: Option<String>, pub page_url: String } // cuw-release
pub enum InstallBlock { NoAsset, SourceBuild, NotWritable { dir: String } }
pub struct ExportPayload { pub five_hour: Option<f64>, pub seven_day: Option<f64>, pub at_ms: i64,
    pub five_reset_at: Option<i64>, pub seven_reset_at: Option<i64>, pub account: Option<String>,
    pub model_key: Option<String>, pub model_label: Option<String>, pub model_seven_day: Option<f64>,
    pub model_reset_at: Option<i64>, pub available_models: Option<Vec<String>>, pub plan: Option<String> } // cuw-export
```

`Version` compare = `System.Version` semantics: 2-4 dot-separated non-negative ints after trimming
leading `v`/`V`; anything else unparseable -> `is_newer = false`; missing components compare as -1
(so `1.2` < `1.2.0`, as in .NET).

## 11. App core (`cuw-app-core`)

```rust
pub enum Command { /* one variant per MenuId action and per panel event: TrayMetric(TrayMetric3), ModelBucket(Option<String>),
   ShowOnDesktop(bool), LockPosition(bool), TaskbarBand(bool), BandPosition(BandSide), BandView(BandView), Theme(ThemeChoice),
   PanelOpacity(f64), PanelView(PanelView), LayoutEdited(WidgetLayout), StatusMode(StatusMode), ModelDial(ModelDial),
   PlanLine(PlanLine), SelectAccount(String), ToggleEditLayout, SetEditLayout(bool), Hide, RefreshNow, SignIn(Source),
   AddAccount, RenameAccount(String), RemoveAccount(String), CheckUpdates, OpenUrl(Site, String), Quit */ }
pub struct Effects { pub render_widget: bool, pub rebuild_layout: bool, pub tooltip: bool, pub tray_icon: bool,
    pub menu: bool, pub band: bool, pub apply_theme: bool, pub band_visibility: Option<bool>, pub band_position: Option<BandSide>,
    pub widget_visible: Option<bool>, pub edit_mode: Option<bool>, pub open_login: Option<String>, pub refresh: Option<RefreshScope>, pub quit: bool }
pub enum RefreshScope { All, TrayAccountAndStatus, Account(String) }
pub enum StartupPhase { WaitingForSettings { since: Timestamp }, Running }  // retry 2 s, 30 s after 1 min
```

Account lifecycle: add (refused at 4; id = UUIDv4, name `New account`, then open login), rename
(re-read settings after the dialog, change only `display_name` = trimmed, FR-024 fix 2), remove
(clear browsing data first, then settings; clear `tray_account_id` if it pointed there), select.
