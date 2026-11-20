# Contract: usage export file (`*.widget.json`)

Owner: `cuw-export`; written through `cuw-settings::file_replace`. Source: INV 2A §6, §1.10.

## When

After every account refresh; written only when that refresh produced `UsageState::Ok` (never from
the last-good snapshot) and a path resolves. Settings are re-read each time (`current()`).
If the path equals the settings file (normalized, case-insensitive) -> log `<name> export skipped:
ExportPath is settings.json` on every refresh and write nothing. Write failures are not logged to
widget.log (debug trace only, parity).

## Path (`export_path_for(settings, account_id, display_name)`)

Same three inputs as C# `UsageExport.ExportPathFor(data, accountId, displayName)`. The settings-file
guard is a separate call: the caller (cuw-app export wiring) checks `writes_over(path, settings_path)`
before writing (see When).

1. Account's `ExportPath` non-blank -> as is (relative allowed; directory created on write).
2. `ExportDirectory` blank -> None.
3. Else collision-free names: `taken` = normalized full paths (lexical `GetFullPathNameW`-style
   normalization, case-insensitive) of every account's own `ExportPath`; walk accounts without
   `ExportPath` in settings order; for each `free_path(dir, name, id)`; return when it is the requested
   id, else add to `taken`. An id missing from settings is processed last with the caller's name.
4. `free_path`: `<dir>\<slug(name,id)>.widget.json`, else `<dir>\<slug>-<slug(id,id)>.widget.json`,
   else `...-<n>.widget.json` for n = 2, 3, ...
5. `slug(name, fallback)`: trim, ASCII-lowercase (`ToLowerInvariant` lowercases non-ASCII too, but
   those become `-` anyway), chars outside `[a-z0-9_-]` -> `-`, collapse runs of `-`, drop leading and
   trailing `-`; empty -> fallback unchanged.

## Payload

Compact single-line JSON (no indent), keys in this order, all always present, `null` written:

| key | type | value |
|---|---|---|
| `fiveHour` | number/null | `five_hour.utilization` |
| `sevenDay` | number/null | `seven_day.utilization` |
| `atMs` | integer | now, epoch milliseconds |
| `fiveResetAt` | integer/null | `five_hour.resets_at`, epoch **seconds** (truncate) |
| `sevenResetAt` | integer/null | epoch seconds |
| `account` | string/null | display name |
| `modelKey` | string/null | `ModelBuckets::resolve(ModelBucket, snapshot)` |
| `modelLabel` | string/null | label of that key (`FABLE`) |
| `modelSevenDay` | number/null | its utilization |
| `modelResetAt` | integer/null | epoch seconds |
| `availableModels` | string array | `available(snapshot)` with `seven_day_` stripped, lowercase; `[]` when none |
| `plan` | string/null | `label_for(profile, snapshot)` |

No payload (nothing written) when the snapshot has neither `five_hour` nor `seven_day`.
Numbers: integral values without `.0` (`62`, not `62.0`), others shortest round-trip.

Example: `{"fiveHour":62,"sevenDay":30,"atMs":1787748230070,"fiveResetAt":1787750400,"sevenResetAt":1787860800,"account":"Personal","modelKey":"seven_day_fable","modelLabel":"FABLE","modelSevenDay":15,"modelResetAt":1788278400,"availableModels":["fable"],"plan":"Max"}`
