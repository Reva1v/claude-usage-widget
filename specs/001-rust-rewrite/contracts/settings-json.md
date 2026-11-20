# Contract: settings.json

Owner: `cuw-settings`. Shared with the C# build (FR-002, SC-003). Source: INV 2A §3, Section 3.

## Location and files

| Path | Use |
|---|---|
| `%APPDATA%\ClaudeUsageWidget\settings.json` | the file (`FOLDERID_RoamingAppData`) |
| `<target>.<32 lowercase hex>.tmp` | temp of one write, same dir; deleted on failure |
| `<settings.json>.bad-<UTC yyyyMMddHHmmss>` | copy of an unparseable file, once per distinct SHA-256 |

## Reading

1. Read bytes; retry on sharing violations (os error 32/33) after 50, 100, 200, 400 ms, then fail.
   Not found (file or dir) -> "missing" (store returns last good or defaults).
2. Decode UTF-8; strip one leading BOM `EF BB BF`.
3. Remove trailing commas that precede `}` or `]` (outside strings, whitespace allowed between).
   Comments are not stripped: `//` or `/* */` make the file unparseable (STJ default).
4. Parse with `serde_json` (`preserve_order`). Root `null` -> defaults. Root not an object ->
   unparseable.
5. Known keys are matched **case-sensitively**; a differently cased key is an unknown key (kept in
   `extra`, ignored).
6. Field readers (absent key = default; `null` for a non-nullable field: see table):

| Kind | Accepts | `null` | Wrong type |
|---|---|---|---|
| bool | `true`/`false` | unparseable | unparseable |
| double | JSON number | unparseable (non-nullable) / None | unparseable |
| int (`ConsecutiveRateLimits`) | integer number in i32 range | unparseable | unparseable (incl. `1.5`) |
| string | JSON string | nullable -> None; non-nullable (`TrayMetricKey`, `BandPosition`, `Id`, `DisplayName`) -> `""` (C# STJ stores null there and the app treats it as the default key / an empty name; Rust keeps the account and writes `""` back) | unparseable |
| enum | string: name case-insensitive, or decimal digits of a defined ordinal; number: defined ordinal; anything unknown -> **first variant** | nullable -> None; non-nullable (in `Order`) -> unparseable | bool/object/array -> unparseable |
| DateTimeOffset | ISO-8601 string (`T`, fraction 0-9 digits, `Z`/`±HH:MM`/none = UTC) | None | unparseable |
| string list (`Capabilities`) | array of strings | None | unparseable |

7. After a successful parse: migration (data-model.md §2), then `Sanitize` is NOT stored back;
   layout sanitizing happens at use (`LayoutResolution`), like C#.

## Writing

- Keys in this exact order, all present, `null` written for empty optionals:
  `WidgetVisible, PositionLocked, ModelBucket, WidgetSide, WidgetX, WidgetY, TrayMetricKey,
  TaskbarBandEnabled, BandPosition, BandView, Theme, PanelOpacity, TrayAccountId, ExportDirectory,
  Layout, StatusMode, ModelDial, PlanLine, Accounts, OrganizationId, RetryPausedUntil,
  ConsecutiveRateLimits`, then unknown top-level keys in their original order.
- Account keys: `Id, DisplayName, OrganizationId, RetryPausedUntil, ConsecutiveRateLimits,
  ExportPath, Capabilities, RateLimitTier, RavenType, SubscriptionFetchedAt`, then unknown keys.
- Layout: `{"PanelFlow": <enum>, "Block": {"Flow", "Name", "Order": [...], "LaidOut": [...]}}` +
  unknown keys of each object; `LaidOut` is computed (order minus `Name` unless name is `Cell`) and
  ignored on read.
- Enums as their names (`"Dark"`, `"Accounts"`, `"FiveHour"`).
- Numbers: integral finite doubles as integers (`170`, not `170.0`); others shortest round-trip
  (`0.82`, `222.5`). NaN/infinity -> save fails, nothing written.
- DateTimeOffset: `yyyy-MM-ddTHH:mm:ss[.F{1,7}]±HH:MM` (fraction truncated to 7 digits, trailing
  zeros trimmed, never `Z`).
- 2-space indent, `": "` after keys, CRLF line ends, UTF-8 without BOM, no trailing newline (STJ
  writes none).
- Atomic replace: write temp (create, exclusive), `sync_all`, rename over target with replace
  (retry while transient, classification below), on any failure delete temp and return the original
  error (temp-cleanup failure attached as `temp_cleanup_failed` text). If `settings.json` is a symlink
  whose target exists, the target is replaced and the link kept.
- Transient (retry up to 4 more times on the backoff): os error 32 or 33; os error 5 when the target
  is not read-only and opening it for write with share `READ|WRITE|DELETE` succeeds, or that probe
  fails with 32 or not-found. Anything else fails at once.

## Compatibility rules

1. Rust reads every file the C# build writes (fixtures: `tests/fixtures/settings/csharp-*.json`).
2. The C# build reads every file Rust writes: C# drops Rust's unknown keys on its next save (allowed).
3. Rust never writes defaults over an unreadable file: read-modify-write uses `load()`, which fails
   while the file is held or unparseable; renders use `current()`.
4. Legacy fields are kept in the type so old files parse; they are cleared in memory by migration and
   leave the file at the next save of anything.
5. Nullable "follow the default" fields (`BandView`, `Theme`, `PanelOpacity`, `StatusMode`,
   `ModelDial`, `PlanLine`, `Layout`) are never filled with defaults on write.

## Examples

Legacy single-account file (pre multi-account), must migrate to account `default`:

```json
{
  "WidgetVisible": true,
  "WidgetSide": 170,
  "TrayMetricKey": "five_hour",
  "OrganizationId": "org-1",
  "RetryPausedUntil": "2026-08-01T12:00:00+03:00",
  "ConsecutiveRateLimits": 1,
}
```

-> in memory: `accounts = [{Id:"default", DisplayName:"default", OrganizationId:"org-1",
RetryPausedUntil:2026-08-01T12:00:00+03:00, ConsecutiveRateLimits:1}]`, `TrayAccountId = "default"`,
legacy fields cleared. (Trailing comma accepted.)

Current file as both builds write it (Rust adds nothing unless unknown keys existed):

```json
{
  "WidgetVisible": true,
  "PositionLocked": false,
  "ModelBucket": null,
  "WidgetSide": 222,
  "WidgetX": 1500,
  "WidgetY": 700.5,
  "TrayMetricKey": "seven_day",
  "TaskbarBandEnabled": true,
  "BandPosition": "left",
  "BandView": "Accounts",
  "Theme": null,
  "PanelOpacity": 0.6,
  "TrayAccountId": "default",
  "ExportDirectory": null,
  "Layout": {
    "PanelFlow": "Row",
    "Block": {
      "Flow": "Column",
      "Name": "Above",
      "Order": [ "Name", "FiveHour", "SevenDay", "Model" ],
      "LaidOut": [ "FiveHour", "SevenDay", "Model" ]
    }
  },
  "StatusMode": "Line",
  "ModelDial": null,
  "PlanLine": "Shown",
  "Accounts": [
    {
      "Id": "default",
      "DisplayName": "personal",
      "OrganizationId": "11111111-2222-3333-4444-555555555555",
      "RetryPausedUntil": null,
      "ConsecutiveRateLimits": 0,
      "ExportPath": null,
      "Capabilities": [ "claude_max", "chat" ],
      "RateLimitTier": "default_claude_max_20x",
      "RavenType": null,
      "SubscriptionFetchedAt": "2026-11-21T10:15:02.1234567+02:00"
    },
    {
      "Id": "6f1c2d9e-0a3b-4c5d-8e7f-001122334455",
      "DisplayName": "work",
      "OrganizationId": null,
      "RetryPausedUntil": "2026-11-21T11:20:00+02:00",
      "ConsecutiveRateLimits": 1,
      "ExportPath": null,
      "Capabilities": null,
      "RateLimitTier": null,
      "RavenType": null,
      "SubscriptionFetchedAt": null
    }
  ],
  "OrganizationId": null,
  "RetryPausedUntil": null,
  "ConsecutiveRateLimits": 0
}
```

Tolerance cases (each a fixture + test): `"Theme": "dark"` -> Dark; `"Theme": "Purple"` -> System
(first variant, accounts survive); `"Theme": 2` -> Light; `"Theme": "2"` -> Light; `"Theme": true` ->
unparseable; `"Order": ["Name","Bogus"]` -> `[Name, Name]` -> Sanitize falls back to the default
order; `"widgetVisible": false` (wrong case) -> ignored, kept as unknown; file with `// comment` ->
unparseable -> `.bad-*` copy.

**VERIFY S1**: (a) STJ writes no trailing newline and uses CRLF in .NET 10 on Windows; (b) STJ
`DateTimeOffset` output trims fraction zeros. Capture a real C#-written file as
`csharp-current.json` and adjust the writer rules above to match it byte for byte where they differ.
