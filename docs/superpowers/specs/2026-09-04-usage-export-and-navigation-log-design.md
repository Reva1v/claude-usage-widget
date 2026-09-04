# Usage export for every account, navigation-failure log and retry (2026-09-04)

Two small changes. The user asks: another tool must read every account's figures the way it
reads the one exported today, and the panel's
`Claude.ai navigation failed: Unknown.` must stop appearing for a transient blip.

## Export for every account

- New setting `WidgetSettingsData.ExportDirectory` (string?, null = off, the default). When set,
  every account writes `<ExportDirectory>\<UsageExport.FileNameFor(DisplayName)>.widget.json` after
  each successful refresh (`App.ExportUsage`). `AccountProfile.ExportPath` stays as a per-account
  override and wins when set.
- `FileNameFor`: trim, lowercase, every char outside `[a-z0-9_-]` → `-`, collapse runs, trim `-`;
  empty → the account `Id`. Pure, in Core.
- Payload v2 (`UsageExportPayload`), camelCase on disk, existing fields unchanged: add `account`
  (DisplayName), `modelKey` (e.g. `seven_day_fable`), `modelLabel` (`ModelBuckets.Label`),
  `modelSevenDay` (utilization), `modelResetAt` (epoch SECONDS). The bucket is
  `ModelBuckets.Resolve(settings.ModelBucket, snapshot)` — the one the third dial shows; all four
  null when there is none. `Payload` takes the preferred bucket key as a parameter.
- Still written only on a fresh `Ok` state, temp + move, never throws.

## Log

- `%LOCALAPPDATA%\ClaudeUsageWidget\widget.log`, always on, one line per event, UTF-8, rotated to
  `widget.log.1` at 1 MB (overwrite). App class `WidgetLog.Write(account, evt, details)` — static,
  locked, swallows every exception. Core pure `LogLine.Format(now, account, evt, details)`:
  `2026-09-04T11:39:45.123+03:00 low nav-failed path=/api/organizations/x/usage status=Unknown http=0 attempt=1`
  (newlines in details → space).
- Events: `nav-failed` (path, WebErrorStatus, HttpStatusCode, attempt), `nav-http` (non-2xx),
  `retry-ok`, `webview-process-failed` (kind), `webview-reset`, `login-window` (open/closed),
  `browser-open` (call site + url — `StatusDialControl.OpenStatusPage`, `TrayIcon` repo/issues; the
  line log the in-widget-editing spec asked for). Nothing at 5-minute success cadence: the file
  must stay small.

## Retry

- `NavigateAndReadAsync` reports `!IsSuccess` as `NavigationFailedException : UsageException`
  (App) carrying the `CoreWebView2WebErrorStatus`; `FetchPageAsync` catches it, logs `attempt=1`,
  and when `NavigationRetryPolicy.IsTransient(status.ToString())` (Core: `Unknown`,
  `ConnectionAborted`, `ConnectionReset`, `OperationCanceled`, `Timeout`) waits 3 s and navigates
  once more; a second failure surfaces as today (`attempt=2` logged); success logs `retry-ok`.
- Never retried: HTTP 401/403/429 and any non-2xx (the 429 ladder 1h→6h→24h stays untouched), the
  30 s timeout per attempt stays. Research 2026-09-04: `WebErrorStatus.Unknown` is undocumented
  beyond "an unknown error"; the log is what names the cause next time.

## Tests

Core.Tests: `FileNameFor` (cases: `personal`, ` Work `, `work.shared@example`, empty → id),
`Payload` v2 with and without a model bucket, `LogLine.Format` (newline folding, fixed clock),
`IsTransient` (the five true, `HostNameNotResolved`/`Unknown ` with a space false).
`WidgetSettingsTests`: `ExportDirectory` round-trip, old file without it → null. App has no test
project; the retry path is verified live from the log after a real refresh cycle.
