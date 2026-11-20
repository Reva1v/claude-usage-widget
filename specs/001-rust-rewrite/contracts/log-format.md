# Contract: widget.log and the band diagnostic log

Owner: `cuw-log` (widget.log), `cuw-band` (band diag). Source: INV 2A §8, 2B §10, 2D §3.8.

## widget.log

- Path `%LOCALAPPDATA%\ClaudeUsageWidget\widget.log`; directory created on every write.
- Line: `<local time yyyy-MM-ddTHH:mm:ss.fff±HH:MM> <account> <event> <details>` + CRLF; single
  spaces, no quoting; in each of account/event/details every `\r` and every `\n` becomes one space.
  UTF-8 without BOM. Example:
  `2026-09-04T11:39:45.123+03:00 low nav-failed path=/api/organizations/x/usage status=Unknown http=0 attempt=1`.
- Rotation: before an append, if the file exists and is larger than 1 048 576 bytes, move it over
  `widget.log.1`; a rotation failure does not stop the append.
- Process-wide lock; writing never fails the caller; nothing is logged on a healthy poll.
- Account field: display name captured when the account runtime is created (stale after rename until
  restart, parity), `-` for global events.

Producer API: `cuw_log::event(account, event, details)` -> `tracing` event target `cuw::widget`,
fields `account`, `event`, `details`; only `WidgetLogLayer` writes them. Other tracing events never
reach widget.log.

### Event vocabulary (must be kept)

| event | details |
|---|---|
| `unhandled` | `dispatcher <Type>: <msg>` is replaced by `panic <label>: <msg>` for panics in UI callbacks; `tray <Type>: <msg>` for errors returned by menu handlers; `fire-and-forget <Type>: <msg>` for failed tasks; `Changed subscriber failed: <Type>: <msg>` (render failures after a store change) |
| `settings` | `unreadable at startup, retrying: <Type>: <msg>`, `<what> not saved: <Type>: <msg>` |
| `sign-in-requested` | `source=tray`, `source=panel`, `source=add-account` |
| `export` | `skipped: ExportPath is settings.json` |
| `update` | `check current=<v>`, `up-to-date latest=<v>`, `download <url>`, `install v<v> into <dir>`, `failed <Type>: <msg>` |
| `browser-open` | `site=tray url=<u>`, `site=update url=<u>`, `site=status-dial url=<u>` |
| `organization` | `duplicate-of <name>`, `refresh-failed <Kind>`, `refresh-unparsed <describe>`, `<describe> tier=<label>` |
| `login-window` | `open`, `closed signedIn=<True|False>`, `reuse existing`, `still opening`, `create-failed <Type>: <msg>` |
| `retry-ok`, `nav-failed`, `nav-http`, `nav-download`, `nav-redirect`, `resource`, `webview-process-failed`, `webview-reset`, `webview-idle` | see contracts/claude-web.md |

`<Type>` for Rust errors = the error enum's variant path as `Display`ed by a `type_name()` helper
(e.g. `SettingsError::Io`); readers only rely on the event names and keys, not on .NET type names.
Booleans print as `True`/`False` (C# style) where C# printed them.

## Band diagnostic log

- On only when env `CLAUDE_BAND_DIAG` equals exactly `1`, read once at startup.
- `%TEMP%\claude-band-diag.log`, append only, never rotated; line `HH:mm:ss.fff <message>` + newline;
  IO errors swallowed.
- Messages (exact): `probe f={f} pt=({x},{y}) hit=0 -> visible`; `probe f={f} pt=({x},{y}) hit={hit}
  root={root} cls={class} tray={tray} own={own}`; `probe f={f}: root {root} is DWM-cloaked ghost ->
  visible`; `probe verdict: OBSCURED by foreground window (real fullscreen)`; `probe verdict: OBSCURED
  (all points foreign)`; `apply: hiddenForFullscreen {old} -> {new}`; `stability expired: verdict
  flipped ({hidden} -> {fresh}), transition cancelled`; `buried under the taskbar itself — re-asserting
  topmost`; `buried under {hwnd} cls={cls} — re-asserting topmost`. HWNDs decimal; `f` printed with
  `.` decimal separator always (the C# used the current culture; intentional, harmless change).
  Text built in `cuw-band-logic::diag`.
