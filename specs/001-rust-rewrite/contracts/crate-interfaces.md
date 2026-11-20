# Contract: public interfaces between crates

This is the API parallel agents code against. S0 commits every item below as a compiling skeleton
(`unimplemented!()` bodies). Changing a signature needs the coordinator's approval and an edit here
first. Types not spelled out are defined in [../data-model.md](../data-model.md). All library errors
are `thiserror` enums; every public enum/struct derives `Debug, Clone, PartialEq` where possible.

Conventions:
- `Timestamp = jiff::Timestamp`, `Duration = std::time::Duration`, `JsonMap =
  serde_json::Map<String, serde_json::Value>`.
- Time is always passed in (`now: Timestamp`); no pure crate reads the clock.
- Platform crates are `!Send` UI-thread objects unless stated; their methods must be called on the UI
  thread (debug-asserted).

---

## cuw-log (pure, std IO)

```rust
pub const WIDGET_TARGET: &str = "cuw::widget";
pub fn format_line(now: jiff::Zoned, account: &str, event: &str, details: &str) -> String; // no CRLF
pub fn event(account: &str, event: &str, details: impl std::fmt::Display);             // tracing::info!(target: WIDGET_TARGET, ...)
pub struct WidgetLogFile { /* path, lock */ }
impl WidgetLogFile {
    pub fn new(path: std::path::PathBuf) -> Self;
    pub fn append(&self, line: &str);        // rotation > 1 MiB to "<path>.1", never fails, never panics
}
pub struct WidgetLogLayer { /* Arc<WidgetLogFile>, clock: fn() -> jiff::Zoned */ }
impl WidgetLogLayer { pub fn new(file: std::sync::Arc<WidgetLogFile>) -> Self; }
impl<S: tracing::Subscriber> tracing_subscriber::Layer<S> for WidgetLogLayer { /* only WIDGET_TARGET */ }
pub fn type_name_of<E: ?Sized>(e: &E) -> &'static str;  // short type name for "<Type>: <msg>" details
```

## cuw-layout (pure)

```rust
// types: LayoutFlow, NamePlacement, BlockItem, StatusMode, ModelDial, PlanLine, BlockLayout, WidgetLayout,
//        PanelView, ResolvedLayout, PanelMetrics, BlockMetrics, ToolbarMetrics, RectD, RectI, Edge, CellHit,
//        ToolbarButton, HeaderButton (data-model §3)
pub const CAPTION_LINE: f64;           // 8.0 * 1.1708984375
pub const EDGE_BAND: f64;              // 8.0
pub const DEFAULT_ORDER: [BlockItem; 4];
pub fn clamp_side(side: f64) -> f64;   // [150, 340]
pub mod flow   { pub fn shape(flow: LayoutFlow, n: usize) -> (usize, usize); pub fn tracks(count: usize, size: f64, gap: f64) -> Vec<f64>; pub fn track_of(i: usize) -> usize; }
pub mod cycle  { pub trait Cycle: Sized { fn next(self) -> Self; } } // impl for all layout enums + ThemeChoice not here
impl BlockLayout { pub fn laid_out(&self) -> Vec<BlockItem>; pub fn toggle(&self, item: BlockItem) -> Self; pub fn swap_cells(&self, a: usize, b: usize) -> Self; pub fn default_block() -> Self; }
impl WidgetLayout { pub fn default_layout() -> Self; pub fn sanitize(raw: Option<&WidgetLayout>, mode: StatusMode, model: ModelDial) -> WidgetLayout; }
pub mod resolve {
    pub fn status_mode(saved: Option<StatusMode>, layout: Option<&WidgetLayout>) -> StatusMode;
    pub fn model_dial(saved: Option<ModelDial>) -> ModelDial;     // None -> Shown
    pub fn plan_line(saved: Option<PlanLine>) -> PlanLine;        // None -> Hidden
    pub fn panel_view_default(account_count: usize) -> PanelView;
    pub fn panel_view_layout(view: PanelView) -> (WidgetLayout, StatusMode);
    pub fn current_view(layout: &WidgetLayout, mode: StatusMode) -> Option<PanelView>;
    pub struct LayoutInputs<'a> { pub layout: Option<&'a WidgetLayout>, pub status_mode: Option<StatusMode>, pub model_dial: Option<ModelDial>, pub plan_line: Option<PlanLine> }
    pub fn resolve(inputs: LayoutInputs<'_>, account_count: usize) -> ResolvedLayout;
}
pub fn panel_metrics(layout: &WidgetLayout, account_count: usize, side: f64, edit_mode: bool, plan: PlanLine) -> PanelMetrics;
pub mod geometry {                     // all DIPs, relative to the PANEL's top-left (strip band excluded)
    pub struct PanelGeometry { pub metrics: PanelMetrics, pub blocks: Vec<BlockGeometry>, pub status_line: RectD, pub toolbar: Vec<(ToolbarButton, RectD)>, pub header: Vec<(HeaderButton, RectD)>, pub strip: Option<RectD> }
    pub struct BlockGeometry { pub rect: RectD, pub name: Option<RectD>, pub cells: Vec<(BlockItem, RectD)> }
    pub fn panel_geometry(m: &PanelMetrics, layout: &WidgetLayout, strip_above: Option<bool>) -> PanelGeometry;
    pub fn hit_cell(g: &PanelGeometry, p: (f64, f64)) -> Option<CellHit>;
    pub fn hit_toolbar(g: &PanelGeometry, p: (f64, f64)) -> Option<ToolbarButton>;
    pub fn hit_header(g: &PanelGeometry, p: (f64, f64)) -> Option<HeaderButton>;
    pub fn dial_angle_degrees(fraction: f64) -> f64;            // -90 + 360*f
}
pub mod interact {
    pub fn hit_edge(size: (f64, f64), p: (f64, f64)) -> Edge;   // corners first, EDGE_BAND unscaled
    pub fn resize_delta(edge: Edge, dx: f64, dy: f64) -> f64;
    pub fn apply_side(current: f64, proposed: f64) -> Option<f64>; // None if |delta| < 0.5
    pub fn drag_started(dx: f64, dy: f64, sm_cxdrag: f64, sm_cydrag: f64) -> bool;
    pub struct EditStrip { pub above: bool, pub reserve: f64 }
    pub fn enter_strip(panel_top_px: i32, reserve_px: i32, work_top_px: i32, first_run: bool) -> bool; // above?
    pub fn reconsider_strip(strip: &EditStrip, panel_top_px: i32, reserve_px: i32, work_top_px: i32) -> Option<bool>; // Some(new_above) on flip
}
pub mod placement {
    pub fn clamp_to_work_area(window: RectI, work: RectI) -> RectI;  // left/top win; physical px (FR-024 fix 1)
    pub fn within(menu: RectI, work: RectI) -> RectI;               // MenuPlacement.Within
    pub struct MonitorInfo { pub rect: RectI, pub work: RectI, pub dpi: u32, pub primary: bool }
    pub fn restore_position(x_dip: f64, y_dip: f64, monitors: &[MonitorInfo]) -> (i32, i32, u32); // px + chosen dpi (research R-09)
    pub fn to_dip(px: i32, dpi: u32) -> f64; pub fn to_px(dip: f64, dpi: u32) -> i32; // round half away from zero
}
pub trait TextMeasure { fn measure(&self, text: &str, style: TextStyle) -> (f64, f64); } // width, height in DIPs
pub struct TextStyle { pub font: FontRole, pub size_dip: f64 }
pub enum FontRole { ConsolasSemiBold, ConsolasMedium, SegoeUiBold, MenuText, Mdl2 }
```

## cuw-settings (pure, std IO; depends on cuw-layout)

```rust
// types: Settings, LegacyFields, AccountProfile, ThemeChoice, BandView, DateTimeOffset (data-model §1-2)
pub const ACCOUNT_LIMIT: usize = 4; pub const LEGACY_ACCOUNT_ID: &str = "default";
pub mod time { impl DateTimeOffset { pub fn parse_lenient(s: &str) -> Option<Self>; pub fn format_stj(&self) -> String; pub fn from_timestamp_local(t: Timestamp, offset: jiff::tz::Offset) -> Self; } }
pub mod json {
    pub fn parse(text: &str) -> Result<Settings, ParseError>;         // BOM, trailing commas, tolerant fields, NOT migrated
    pub fn to_string(s: &Settings) -> Result<String, SaveError>;      // exact writer rules, NonFinite error
    pub fn number(v: f64) -> serde_json::Number;                      // integral -> integer (shared with cuw-export)
}
pub enum Migrated { Unchanged(Settings), Migrated(Settings) }
pub fn migrate(raw: Settings) -> Migrated;
impl Settings {
    pub fn account(&self, id: &str) -> Option<&AccountProfile>;
    pub fn account_with_organization(&self, org: &str, except_id: &str) -> Option<&AccountProfile>;
    pub fn with_account(self, id: &str, f: impl FnOnce(&mut AccountProfile)) -> Self;
    pub fn tray_account(&self) -> Option<&AccountProfile>;  // TrayAccountId match, else first
}
pub mod file_replace {
    pub fn write(path: &std::path::Path, bytes: &[u8]) -> Result<(), ReplaceError>;
    pub fn read_to_string(path: &std::path::Path) -> std::io::Result<String>; // sharing-violation retry only
    pub const BACKOFF_MS: [u64; 4] = [50, 100, 200, 400];
}
pub struct SettingsStore { /* path, Mutex<last_good> */ }       // Send + Sync
impl SettingsStore {
    pub fn new(path: std::path::PathBuf) -> Self;
    pub fn path(&self) -> &std::path::Path;
    pub fn load(&self) -> Result<Settings, SettingsError>;     // read-modify-write source
    pub fn current(&self) -> Result<Settings, SettingsError>;  // render source: last good on IO/Unparseable
    pub fn save(&self, s: &Settings) -> Result<(), SettingsError>;
    pub fn update(&self, f: impl FnOnce(&mut Settings)) -> Result<Settings, SettingsError>; // load -> f -> save
}
#[derive(thiserror::Error)] pub enum SettingsError { Io(std::io::Error), Unparseable { path: PathBuf, copied: bool, source: Option<ParseError> }, Save(SaveError), Replace(ReplaceError) }
impl SettingsError { pub fn is_io_class(&self) -> bool; } // Io | Unparseable | Replace (startup retries on these)
#[derive(thiserror::Error)] pub enum ReplaceError { Io { source: std::io::Error, temp_cleanup_failed: Option<String> } }
```

## cuw-usage (pure)

```rust
// types: UsageBucket, UsageSnapshot, UsageError, UsageErrorKind, UsageState, ThresholdLevel (data-model §4)
impl UsageError { pub fn kind(&self) -> UsageErrorKind; pub fn description(&self) -> String; pub fn duplicate_account(other: &str) -> Self; pub fn network(msg: impl Into<String>) -> Self; }
impl UsageSnapshot { pub fn new(buckets: BTreeMap<String, UsageBucket>) -> Self; pub fn get(&self, key: &str) -> Option<&UsageBucket>; }
pub mod decode { pub fn snapshot(body: &str) -> Result<UsageSnapshot, UsageError>; pub fn slug(display_name: &str) -> String; pub fn reset_date(v: &serde_json::Value) -> Result<Option<Timestamp>, UsageError>; }
pub mod math {
    pub fn remaining_text(resets_at: Option<Timestamp>, now: Timestamp) -> Option<String>;
    pub fn fraction(utilization: f64) -> f64;
    pub fn percent_text(fraction: f64) -> String;          // round(f*100 + 1e-9) away from zero + "%"
    pub fn level(fraction: f64) -> ThresholdLevel;
}
pub mod buckets {
    pub const PREFERENCE: [&str; 3] = ["seven_day_fable", "seven_day_opus", "seven_day_sonnet"];
    pub fn is_model_key(key: &str) -> bool;
    pub fn available(snapshot: &UsageSnapshot) -> Vec<String>;
    pub fn resolve(preferred: Option<&str>, snapshot: &UsageSnapshot) -> Option<String>;
    pub fn label(key: &str) -> String;                     // strip "seven_day_", ASCII-uppercase (invariant)
}
pub mod http_validation { pub fn validate(status: u16, retry_after_seconds: Option<u32>) -> Result<(), UsageError>; }
```

## cuw-status (pure)

```rust
pub enum ServiceStatus { Operational, Degraded, PartialOutage, MajorOutage, Maintenance, Unknown }
pub fn decode(body: &str) -> Result<ServiceStatus, cuw_usage::UsageError>;
pub fn component(s: &str) -> ServiceStatus; pub fn indicator(s: &str) -> ServiceStatus;
pub fn label(s: ServiceStatus) -> &'static str;          // OK SLOW PARTIAL DOWN MAINT —
pub fn line_label(s: ServiceStatus) -> &'static str;     // operational ... state unknown
pub fn line(s: ServiceStatus, mode: cuw_layout::StatusMode) -> Option<String>;
pub const ENDPOINT: &str = "https://status.claude.com/api/v2/summary.json";
pub const PAGE_URL: &str = "https://status.claude.com";
```
(`cuw-status` depends on `cuw-layout` for `StatusMode`.)

## cuw-claude (pure)

```rust
pub const ORGANIZATIONS_URL: &str = "https://claude.ai/api/organizations";
pub fn usage_url(org_id: &str) -> String;
pub const LOGIN_URL: &str = "https://claude.ai/login";
pub const BODY_SCRIPT: &str = "document.body.innerText || document.body.textContent || ''";
pub mod org {
    pub fn pick(body: &str) -> Option<String>;
    pub fn fields(body: &str, picked: Option<&str>) -> SubscriptionFields;   // never fails
    pub fn describe(body: &str, picked: Option<&str>) -> String;
}
pub mod subscription {
    pub const INTERVAL: Duration;                                            // 1 h
    pub fn should_refresh(last: Option<Timestamp>, now: Timestamp) -> bool;
    pub fn learned_nothing(f: &SubscriptionFields) -> bool;
    pub fn merge(existing: SubscriptionFields, fetched: SubscriptionFields) -> SubscriptionFields;
}
pub mod tier {
    pub const ULTRA: &str = "Ultra";
    pub fn label(caps: &[String], tier: &str, raven: &str) -> String;
    pub fn label_or_null(f: &SubscriptionFields) -> Option<String>;
    pub fn label_for(f: Option<&SubscriptionFields>, usage: Option<&cuw_usage::UsageSnapshot>) -> Option<String>;
    pub fn has_five_hour_limit(f: Option<&SubscriptionFields>) -> bool;
}
pub mod nav {
    pub fn web_error_from_raw(raw: i32) -> WebErrorStatus; impl WebErrorStatus { pub fn name(&self) -> String; }
    pub fn classify(http: u16, is_success: bool, status: WebErrorStatus, path: &str, account: &str) -> NavOutcome; // logs nav-http
    pub fn unwrap_script_result(raw_json: &str) -> Result<String, cuw_usage::UsageError>;
    pub fn is_transient(status: &WebErrorStatus) -> bool;                    // exact names list
    pub const RETRY_DELAY: Duration;                                         // 3 s
    pub const NAVIGATION_TIMEOUT: Duration;                                  // 30 s
    pub fn is_webview_dead_hresult(hr: i32) -> bool;                         // 0x8007139F, 0x80010108
    pub fn process_failed_kind_name(raw: i32) -> String; pub fn process_failed_resets(raw: i32) -> bool; // 0,1,2
}
pub mod cookie { pub struct Cookie { pub name: String, pub domain: String, pub value: String } pub fn is_session_cookie(c: &Cookie) -> bool; pub fn any_session_cookie(cs: &[Cookie]) -> bool; }
pub mod lease { impl FetchIdleLease { pub const DELAY: Duration; pub fn new() -> Self; pub fn enter(&mut self); pub fn exit(&mut self) -> Result<Option<u64>, LeaseError>; pub fn should_release(&self, token: u64) -> bool; pub fn in_use(&self) -> bool; } }

/// Transport port, implemented by cuw-webview::Session (and fakes in tests).
pub trait PageFetcher {
    async fn has_session_cookie(&self) -> Result<bool, PageError>;
    async fn fetch_page(&self, url: &str) -> Result<String, PageError>;      // includes retry + dead-webview recovery
}
/// Settings port, implemented in cuw-app over SettingsStore (and fakes).
pub trait AccountSettings {
    fn organization_id(&self, account_id: &str) -> Option<String>;
    fn subscription(&self, account_id: &str) -> Option<(SubscriptionFields, Option<Timestamp>)>; // None = account gone
    fn other_account_with_organization(&self, org: &str, except_id: &str) -> Option<String>;     // display name
    fn save_organization_id(&self, account_id: &str, org: Option<&str>);                         // quiet, logs on failure
    fn save_subscription(&self, account_id: &str, merged: &SubscriptionFields, fetched_at: Timestamp); // quiet
}
pub async fn fetch_usage(state: &mut ClaudeAccountState, account_id: &str, label: &str,
    fetcher: &impl PageFetcher, settings: &impl AccountSettings, now: Timestamp) -> Result<cuw_usage::UsageSnapshot, PageError>;
pub fn clear_cached_organization(settings: &impl AccountSettings, account_id: &str);
impl PageError { pub fn into_usage_error(self) -> cuw_usage::UsageError; } // NavigationFailed -> Network("Claude.ai navigation failed: <name>."), Platform -> Network(message)
```
(`async fn` in traits is stable; ports are used with concrete types, so no `dyn` is needed.)

## cuw-store (pure)

```rust
// types: RetryState, PersistedRetry, UsageStore, StatusStore (data-model §7)
pub const REFRESH_INTERVAL: Duration; pub const RETRY_MARGIN: Duration; pub const LADDER: [Duration; 3];
pub struct LoadTicket(u64);
pub enum UsageLoadStart { JoinInFlight, Paused, Finished { changed: bool }, Fetch(LoadTicket) }
pub struct UsageLoadOutcome { pub changed: bool, pub persist: Option<Option<RetryState>> }
impl UsageStore {
    pub fn new(persisted: PersistedRetry, now: Timestamp) -> (Self, Option<Option<RetryState>> /* persist request */);
    pub fn state(&self) -> &cuw_usage::UsageState;
    pub fn last_snapshot(&self) -> Option<&cuw_usage::UsageSnapshot>;
    pub fn snapshot_for_display(&self) -> Option<&cuw_usage::UsageSnapshot>;
    pub fn retry_paused_until(&self) -> Option<Timestamp>;
    pub fn begin_load(&mut self, now: Timestamp, has_credentials: bool) -> UsageLoadStart;
    pub fn finish_load(&mut self, ticket: LoadTicket, result: Result<cuw_usage::UsageSnapshot, cuw_usage::UsageError>, now: Timestamp, local_offset: jiff::tz::Offset) -> UsageLoadOutcome;
    pub fn persist_done(&mut self, requested: Option<RetryState>, ok: bool);
}
pub enum StatusLoadStart { JoinInFlight, Fetch(LoadTicket) }
impl StatusStore {
    pub fn new() -> Self; pub fn status(&self) -> cuw_status::ServiceStatus;
    pub fn begin_load(&mut self) -> StatusLoadStart;
    pub fn finish_load(&mut self, ticket: LoadTicket, result: Result<cuw_status::ServiceStatus, cuw_usage::UsageError>) -> bool; // changed
}
pub mod schedule { pub fn offset_for(index: usize, count: usize, cycle: Duration) -> Result<Duration, ScheduleError>; }
```

## cuw-release (pure)

```rust
pub const ENDPOINT: &str = "https://api.github.com/repos/Reva1v/claude-usage-widget/releases/latest";
pub const REPO_URL: &str = "https://github.com/Reva1v/claude-usage-widget";
pub const ISSUES_URL: &str = "https://github.com/Reva1v/claude-usage-widget/issues";
pub const ASSET_SUFFIX: &str = "-win-x64.zip"; pub const EXE_NAME: &str = "ClaudeUsageWidget.App.exe";
pub mod version { pub fn parse_version(info: Option<&str>) -> String; }   // cut at '+', empty -> "unknown"; cuw-app passes env!("CARGO_PKG_VERSION")
impl LatestRelease { pub fn parse(json: &str) -> Result<Option<LatestRelease>, ReleaseError>; }
pub fn is_newer(latest: &str, current: &str) -> bool;
pub fn install_block(release: &LatestRelease, release_build: bool, dir_writable: bool, dir: &str) -> Option<InstallBlock>;
pub mod texts { pub fn up_to_date(cur: &str) -> String; pub fn open_page_prompt(latest: &str, why: &InstallBlock) -> String; pub fn install_prompt(latest: &str, cur: &str) -> String; pub fn failed(msg: &str) -> String; pub const CAPTION: &str = "Claude Usage Widget"; }
pub fn user_agent(version: &str) -> String;   // "ClaudeUsageWidget/<v>"
```

## cuw-export (pure, writes via cuw-settings)

```rust
pub fn payload(snapshot: Option<&UsageSnapshot>, now: Timestamp, display_name: &str, preferred_bucket: Option<&str>, plan: Option<&str>) -> Option<ExportPayload>;
pub fn to_json(p: &ExportPayload) -> String;           // compact, key order, integral numbers
pub fn export_path_for(s: &Settings, account_id: &str, display_name: &str) -> Option<PathBuf>;
pub fn file_name_for(display_name: &str, fallback_id: &str) -> String;
pub fn writes_over(export: &Path, protected: &Path) -> bool;
pub fn write(path: &Path, p: &ExportPayload) -> Result<(), ReplaceError>;  // caller only traces failures
```

## cuw-view (pure)

```rust
pub mod dials {
    pub fn all(f: Option<&SubscriptionFields>, snapshot: Option<&UsageSnapshot>, preferred: Option<&str>, now: Timestamp) -> [DialModel; 3];
    pub fn is_spent(d: &DialModel) -> bool;
    pub fn account_rows(accounts: &[AccountProfile], snapshots: &HashMap<String, Option<UsageSnapshot>>, preferred: Option<&str>, now: Timestamp) -> Vec<AccountRow>;
    pub fn subscription_of(p: &AccountProfile) -> SubscriptionFields;
}
pub mod tray_text {
    pub fn label_for(title: &str) -> String; pub fn value_for(d: &DialModel) -> String;
    pub fn metrics(dials: &[DialModel]) -> Vec<TrayMetric>;
    pub fn icon_index(chosen: usize, dials: &[DialModel]) -> usize;
    pub fn metric_index(key: &str) -> usize;                       // "seven_day"->1, "model"->2, else 0
    pub fn tooltip(metrics: &[TrayMetric], status_line: Option<&str>) -> String;   // "Claude Usage Widget — ..."
    pub const TOOLTIP_WEBVIEW_ERROR: &str; pub const TOOLTIP_SETTINGS_RETRY: &str; pub const TOOLTIP_DEFAULT: &str;
    pub fn icon_digits(value: &str) -> Option<String>;             // DigitsOnly
}
pub mod band_text { pub const MAX_NAME: usize = 8; pub fn entries(rows: &[AccountRow]) -> Vec<BandEntry>; pub fn metric_entries(row: &AccountRow) -> Vec<BandEntry>; pub fn resolve_view(saved: Option<BandView>, count: usize) -> BandView; }
pub mod status_line { pub fn text(state: &UsageState, now: Timestamp, retry_until: Option<Timestamp>) -> Option<String>; pub fn panel_line(status: ServiceStatus, mode: StatusMode, state: &UsageState, now: Timestamp, retry_until: Option<Timestamp>) -> Option<String>; }
pub mod notice { pub fn for_state(state: &UsageState) -> Option<Notice>; }
pub mod theme {
    pub const DARK: Palette; pub const LIGHT: Palette;
    pub fn resolve(saved: Option<ThemeChoice>, system: ThemeKind) -> ThemeKind;
    pub fn palette(kind: ThemeKind) -> &'static Palette;
    pub fn threshold_color(p: &Palette, l: ThresholdLevel) -> Rgb; pub fn status_color(p: &Palette, s: ServiceStatus) -> Rgb;
    pub const PANEL_ALPHA: f64 = 0.82; pub const OVERLAY_ALPHA: f64 = 0.92;
    pub fn alpha_byte(a: f64) -> u8;                               // round half to even
    pub fn taskbar_ink(kind: ThemeKind) -> Rgb;                    // Light -> #1B1B1B, Dark -> white
}
pub mod opacity { pub const DEFAULT: f64 = 0.82; pub const PRESETS: [f64; 6]; pub fn resolve(v: Option<f64>) -> f64; pub fn painted(v: f64) -> f64; pub fn is_preset(v: f64, p: f64) -> bool; pub fn caption(p: f64) -> String; }
pub mod panel { pub fn frame(s: &Settings, rows: Vec<AccountRow>, tray_state: &UsageState, status: ServiceStatus, retry_until: Option<Timestamp>, now: Timestamp) -> PanelFrame; }
pub mod menu {
    pub struct MenuState { pub settings: Settings, pub available_buckets: Vec<String>, pub resolved_model_label: Option<String>,
        pub position_locked: bool, pub editing_layout: bool, pub autostart: bool, pub widget_ready: bool, pub version: String }
    pub fn build(state: &MenuState) -> MenuModel;          // always the full INV 2D §2 list; until a stage implements an id, cuw-app hides it via `MenuModel::without(&[MenuId])`
    pub fn glyph_for(id: &MenuId) -> Option<char>;
    impl MenuModel { pub fn without(self, ids: &[MenuId]) -> Self; pub fn find(&self, id: &MenuId) -> Option<&MenuItem>; }
}
```

## cuw-band-logic (pure)

```rust
pub type Hwnd = isize;
pub const PROBE_FRACTIONS: [f64; 3] = [0.35, 0.55, 0.8];
pub const SHOW_MS: u32 = 150; pub const FAST_HIDE_MS: u32 = 150; pub const HIDE_MS: u32 = 1200;
pub const TICK: Duration; pub const LOCATION_DEBOUNCE: Duration; // 5 s, 100 ms
pub fn probe_points(tray: RectI) -> [(i32, i32); 3];
pub fn verdict(samples: &[ProbeSample], tray: Hwnd, own: Hwnd, foreground: Hwnd) -> Cover; // first-visible short-circuit
pub struct GeometryInputs { pub tray: RectI, pub notify: Option<RectI>, pub dpi: u32, pub content_width_dip: f64, pub left: bool }
pub fn geometry(i: &GeometryInputs) -> BandGeometry;
impl Visibility { pub fn new() -> Self; pub fn dock(&mut self); pub fn hidden(&self) -> bool;
    pub fn request(&mut self, hidden: bool, by_foreground: bool) -> VisibilityAction;
    pub fn timer_fired(&mut self, fresh_hidden: Option<bool> /* None = probe impossible */) -> VisibilityAction; }
pub struct ZOrderEntry { pub hwnd: Hwnd, pub visible: bool, pub rect: Option<RectI>, pub class: String }
pub fn burial(own: RectI, above: &[ZOrderEntry] /* nearest first, max 64 */, tray: Hwnd) -> BurialVerdict;
pub mod columns { pub struct Column { pub name_w: f64, pub name_h: f64, pub pct_w: f64, pub pct_h: f64, pub reset: Option<(f64, f64)> }
    pub fn width(c: &Column) -> f64; pub fn height(c: &Column) -> f64; pub fn content_width(cols: &[Column]) -> f64; } // +14 gaps, +5 pair
pub mod diag { /* one fn per message template in contracts/log-format.md */ }
```

## cuw-app-core (pure)

```rust
// types: Command, Effects, RefreshScope, StartupPhase (data-model §11)
pub fn apply(cmd: &Command, s: &mut Settings, ctx: &CommandContext) -> Result<Effects, CommandError>;
pub struct CommandContext { pub account_count: usize, pub editing_layout: bool, pub widget_exists: bool }
pub mod startup { pub fn retry_interval(phase: &StartupPhase, now: Timestamp) -> Duration; /* 2 s, 30 s after 1 min */ pub fn first_run_account() -> AccountProfile; /* default / Claude */ }
pub mod accounts {
    pub fn add(s: &mut Settings, new_id: &str) -> Result<AccountProfile, AccountError>;  // refuses at 4
    pub fn rename(s: &mut Settings, id: &str, typed: &str) -> Option<()>;               // None if blank or gone
    pub fn remove(s: &mut Settings, id: &str) -> bool;
    pub fn webview_profile_name(id: &str) -> Option<&str>;                              // "default" -> None
}
/// Refresh orchestration over ports; runs on the UI-thread executor in cuw-app.
pub trait Clock { fn now(&self) -> Timestamp; fn local_offset(&self) -> jiff::tz::Offset; async fn sleep(&self, d: Duration); }
pub trait AccountPort {                                  // one per account runtime
    fn id(&self) -> &str;
    async fn check_cookie(&self) -> Result<bool, String>; // Err = webview failing
    async fn load(&self);                                 // coalesced UsageStore load + fetch + persist + render notify
    fn export(&self);
}
pub trait AppPorts { type Account: AccountPort; fn accounts(&self) -> Vec<std::rc::Rc<Self::Account>>; fn account_alive(&self, id: &str) -> bool;
    fn set_webview_failing(&self, id: &str, failing: bool); fn set_has_cookie(&self, id: &str, v: bool); async fn load_status(&self); fn spawn(&self, f: futures_util::future::LocalBoxFuture<'static, ()>); }
pub async fn refresh_account<P: AppPorts>(ports: &P, account: &P::Account);
pub async fn refresh_all<P: AppPorts + 'static, C: Clock + 'static>(ports: std::rc::Rc<P>, clock: std::rc::Rc<C>); // stagger, id match, status last
```

---

## cuw-http (io)

```rust
pub struct Response { pub status: u16, pub body: String }
pub fn get_text(url: &str, headers: &[(&str, &str)], timeout: Duration) -> Result<Response, HttpError>;            // blocking
pub fn download_to_file(url: &str, headers: &[(&str, &str)], timeout: Duration, dest: &Path) -> Result<u16, HttpError>; // blocking
```

## cuw-win-core (win)

```rust
pub mod ui {
    pub fn init() -> Result<(), WinError>;                       // CoInitializeEx STA, hidden wake window, panic hook
    pub fn run() -> i32;                                         // message loop until quit()
    pub fn quit(code: i32);
    pub fn spawn_local(label: &'static str, f: impl Future<Output = ()> + 'static);
    pub fn post(f: impl FnOnce() + Send + 'static);              // from any thread
    pub fn sleep(d: Duration) -> impl Future<Output = ()>;
    pub fn run_blocking<T: Send + 'static>(f: impl FnOnce() -> T + Send + 'static) -> impl Future<Output = T>;
    pub struct UiTimer; impl UiTimer { pub fn new(f: impl FnMut() + 'static) -> Self; pub fn start(&self, every: Duration); pub fn stop(&self); pub fn is_running(&self) -> bool; }
    pub struct Callback<T>(/* oneshot */); pub fn callback<T: 'static>() -> (impl FnOnce(T), impl Future<Output = T>); // COM completion bridge
}
pub mod guard { pub fn guarded<R>(label: &str, fallback: R, f: impl FnOnce() -> R) -> R; }  // catch_unwind + log
pub mod instance { pub struct SingleInstance; pub fn acquire(name: &str) -> Result<Option<SingleInstance>, WinError>; pub const MUTEX_NAME: &str = "ClaudeUsageWidget.SingleInstance"; }
pub mod paths { pub fn roaming_app_data() -> Result<PathBuf, WinError>; pub fn local_app_data() -> Result<PathBuf, WinError>; pub fn temp() -> PathBuf; pub fn exe_path() -> Result<PathBuf, WinError>; }
pub mod shell { pub fn open_url(url: &str) -> Result<(), WinError>; }
pub mod dialogs { pub enum Icon { Info, Error, Question } pub fn message_box(text: &str, caption: &str, icon: Icon, yes_no: bool) -> bool; } // MB_DEFAULT_DESKTOP_ONLY, re-entrant safe
pub mod theme { pub fn apps_kind() -> ThemeKind; pub fn taskbar_kind() -> ThemeKind; }   // missing/failure -> Dark
pub mod autostart { pub const VALUE_NAME: &str = "ClaudeUsageWidget"; pub fn is_enabled() -> bool; pub fn set_enabled(on: bool, exe: &Path) -> Result<(), WinError>; pub fn value_for(exe: &Path) -> String; /* "\"<exe>\"" */ }
pub mod system_events {
    pub enum SystemEvent { ThemeChanged { apps: ThemeKind, taskbar: ThemeKind }, Resumed, TaskbarCreated, DisplayChanged, TrayCallback { msg: u32, x: i32, y: i32 } }
    pub struct SystemEvents; impl SystemEvents { pub fn create(on_event: impl FnMut(SystemEvent) + 'static) -> Result<Self, WinError>; pub fn hwnd(&self) -> isize; }
}
pub mod monitors { pub fn all() -> Vec<cuw_layout::placement::MonitorInfo>; pub fn from_point(x: i32, y: i32) -> cuw_layout::placement::MonitorInfo; pub fn drag_threshold_px() -> (i32, i32); }
pub mod hresult { pub fn of(e: &windows_core::Error) -> i32; }
```

## cuw-render (win)

```rust
pub struct Renderer;  impl Renderer { pub fn new() -> Result<Self, RenderError>; pub fn measurer(&self) -> &dyn cuw_layout::TextMeasure; }
pub struct LayeredSurface; impl LayeredSurface { pub fn new(r: &Renderer) -> Self;
    pub fn present(&mut self, hwnd: isize, pos_px: (i32, i32), size_px: (u32, u32), draw: impl FnOnce(&mut Canvas)) -> Result<(), RenderError>; } // ULW_ALPHA
pub struct Canvas<'a> { /* D2D target + dpi scale */ }
impl Canvas<'_> { pub fn clear(&mut self); pub fn rounded_rect(&mut self, r: RectD, radius: f64, fill: Rgba); pub fn text(&mut self, s: &str, style: TextStyle, rect: RectD, align: Align, color: Rgba);
    pub fn glyph(&mut self, ch: char, size_dip: f64, center: (f64, f64), color: Rgba); pub fn dashed_rect(&mut self, r: RectD, color: Rgba); pub fn rect_stroke(&mut self, r: RectD, width: f64, color: Rgba); }
pub mod dials { pub fn usage_dial(c: &mut Canvas, rect: RectD, d: &DialModel, dimmed: bool, p: &Palette); pub fn status_dial(c: &mut Canvas, rect: RectD, s: ServiceStatus, dimmed: bool, p: &Palette); pub fn name_ring(c: &mut Canvas, rect: RectD, p: &Palette); }
pub mod panel { pub struct PanelScene<'a> { pub frame: &'a PanelFrame, pub geometry: &'a PanelGeometry, pub palette: &'a Palette, pub opacity: f64, pub hover_header: bool, pub edit: Option<EditOverlay> } pub fn draw(c: &mut Canvas, s: &PanelScene<'_>); }
pub mod band { pub fn draw(c: &mut Canvas, entries: &[BandEntry], taskbar: ThemeKind, size_dip: (f64, f64)); pub fn content_width(r: &Renderer, entries: &[BandEntry]) -> f64; }
pub mod tray_icon { pub fn render(r: &Renderer, digits: Option<&str>, ink: Rgb, size_px: u32) -> Result<Hicon, RenderError>; pub struct Hicon; /* Drop = DestroyIcon */ }
pub mod menu_paint { pub fn draw_menu(c: &mut Canvas, items: &[MenuItem], hovered: Option<usize>, p: &Palette, layout: &MenuLayout); pub fn layout_menu(r: &Renderer, items: &[MenuItem], dpi: u32) -> MenuLayout; }
pub mod offscreen { pub fn render_png(r: &Renderer, size_px: (u32, u32), dpi: u32, path: &Path, draw: impl FnOnce(&mut Canvas)) -> Result<(), RenderError>; } // WIC software target, headless
```

## cuw-webview (win)

```rust
pub struct Environment; impl Environment { pub fn shared(udf: &Path) -> impl Future<Output = Result<std::rc::Rc<Environment>, WebError>>; } // failure not cached
pub struct Session;   // one per account; implements cuw_claude::PageFetcher
impl Session {
    pub fn new(udf: PathBuf, profile_name: Option<String>, account_id: String, label: String) -> std::rc::Rc<Self>;
    pub fn set_live_label(&self, f: Box<dyn Fn() -> String>);          // login window title uses the live name
    pub async fn open_login_window(self: &std::rc::Rc<Self>) -> Result<(), WebError>;
    pub fn on_signed_in(&self, f: Box<dyn Fn()>);
    pub async fn clear_browsing_data(&self) -> Result<(), WebError>;
    pub fn close(&self);
}
impl cuw_claude::PageFetcher for Session { /* gate, lease, retry, dead recovery */ }
#[derive(thiserror::Error)] pub enum WebError { Com { hresult: i32, message: String }, ProfileMismatch(String), Closed }
impl WebError { pub fn is_dead(&self) -> bool; }
```

## cuw-tray (win)

```rust
pub struct TrayIcon; impl TrayIcon {
    pub fn create(events_hwnd: isize) -> Result<Self, WinError>;           // NIM_ADD + NIM_SETVERSION 4, placeholder ring
    pub fn set_icon(&mut self, icon: cuw_render::tray_icon::Hicon);       // destroys the previous one after NIM_MODIFY
    pub fn set_tooltip(&mut self, text: &str);                            // truncate 127
    pub fn readd(&mut self);                                              // TaskbarCreated
    pub fn icon_size_px() -> u32;                                         // SM_CXSMICON at taskbar dpi
}
impl Drop for TrayIcon { /* NIM_DELETE */ }
pub struct MenuWindow; impl MenuWindow {
    pub fn new(renderer: std::rc::Rc<cuw_render::Renderer>, on_command: Box<dyn Fn(cuw_view::menu::MenuId)>, on_opening: Box<dyn Fn() -> cuw_view::MenuModel>) -> Self;
    pub fn show_at_cursor(&self);
    pub fn update(&self, model: cuw_view::MenuModel);                     // repaint in place while open
    pub fn close(&self); pub fn is_open(&self) -> bool; pub fn set_palette(&self, p: cuw_view::theme::Palette);
}
pub mod rename_dialog { pub async fn ask(current: &str, palette: &Palette) -> Option<String>; } // modeless window, resolves on Rename/Cancel/close
```

## cuw-widget (win)

```rust
pub enum WidgetEvent { HideRequested, LayoutEdited(WidgetLayout), EditDone, EditToggle, StatusMode(StatusMode), ModelDial(ModelDial), PlanLine(PlanLine), LockToggled(bool), SignIn, OpenStatusPage, PersistGeometry { x_dip: f64, y_dip: f64, side: f64 } }
pub struct WidgetWindow; impl WidgetWindow {
    pub fn create(renderer: std::rc::Rc<cuw_render::Renderer>, initial: WidgetInit, on_event: Box<dyn Fn(WidgetEvent)>) -> Result<Self, WinError>;
    pub fn render(&self, frame: PanelFrame, resolved: ResolvedLayout, palette: Palette, opacity: f64);
    pub fn rebuild(&self, account_count: usize, resolved: ResolvedLayout);  // clamp before redraw
    pub fn set_edit_mode(&self, on: bool); pub fn set_locked(&self, on: bool); pub fn locked(&self) -> bool;
    pub fn show(&self); pub fn hide(&self); pub fn close(&self);           // close flushes a pending persist
}
pub struct WidgetInit { pub x_dip: Option<f64>, pub y_dip: Option<f64>, pub side: f64, pub locked: bool, pub account_count: usize }
```
Geometry persistence is debounced 500 ms inside the window and emitted as `PersistGeometry`; the app
saves with `SettingsStore::update` (a failure is logged, not shown).

## cuw-band (win)

```rust
pub struct BandWindow; impl BandWindow {
    pub fn create(renderer: std::rc::Rc<cuw_render::Renderer>, on_lost: Box<dyn Fn()>) -> Result<Self, WinError>;
    pub fn set_position(&self, left: bool);
    pub fn render(&self, entries: Vec<BandEntry>, taskbar: ThemeKind);     // repositions if docked
    pub fn dock(&self); pub fn detach(&self); pub fn taskbar_created(&self); pub fn close(&self);
}
```

## cuw-updater (win)

```rust
pub struct UpdaterConfig { pub current_version: String, pub release_build: bool, pub exe_path: PathBuf }
pub async fn check_and_install(cfg: UpdaterConfig, quit: Box<dyn FnOnce()>);   // single flight; all dialogs, logs, swap
pub fn after_update(wait_pid: u32);                                            // called at startup with --after-update
```

## cuw-app (bin)

`main.rs`: parse `--after-update <pid>`; `CUW_RENDER_PREVIEW` / `CUW_RENDER_MENU` -> `preview::run(dir)`
and exit (before the mutex); `ui::init`; logging (`WidgetLogLayer` + debug layer); single instance
(exit 0 if taken); build objects; `ui::run()`. `wiring.rs` owns `App` (settings store, account
runtimes, stores, windows, timers) and implements `AppPorts`, `AccountSettings`, `Clock`. No business
rule lives here: decisions come from `cuw-app-core` and the pure crates.
