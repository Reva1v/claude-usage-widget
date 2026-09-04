# Multi-account design (2026-08-26)

Show all configured Claude accounts at once, instead of the single hardcoded `default` profile.

The user runs several Claude subscriptions and switches between them when one runs out of
tokens. The question the widget must answer without a click is
"where are there tokens left". A switcher answers it in two clicks, so it is not enough.

The seam already exists: `WidgetSettingsData.Accounts` (`AccountProfile`) is defined, serialized and
round-trip tested, and `App.OnStartup` carries the comment naming the switch a future task.

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | One `CoreWebView2Environment`, one profile per account via `CoreWebView2ControllerOptions.ProfileName` | Separate UDF per account spawns a full browser-process collection each; docs call that the "previous approach" and warn against many UDFs at once. Profiles give the same cookie separation inside one process collection |
| 2 | Layout: one row per account, three dials each (5H, 7D, per-model) | The user picked it over quadrants, bars and concentric rings: keeps the original dial language, reads as a table |
| 3 | Panel is no longer square | 12 dials plus a label column do not fit a square |
| 4 | `WidgetSide` becomes a scale, not a side | Keeps the author's invariant — one number drives both axes, so a drag cannot distort proportions |
| 5 | STATUS leaves the dial grid, becomes a line | Service status is global, not per account; a fourth equal dial would imply otherwise |
| 6 | ~~Reset time moves from the dial centre to its own column~~ — **reversed on screen**: the dial prints the time under the percentage at every size the panel uses, so a column repeated it verbatim. The column is gone and the row is narrower for it | The original claim ("at twelve dials only the percentage fits inside") was an assumption, and a screenshot of three real rows disproved it |
| 7 | Tray icon shows one selected account | New `Tray shows account` submenu beside the existing `Tray shows`. Four tray icons lose to Windows overflow |
| 8 | Taskbar band shows all four, 5H only, with reset time | `work 62 · 1h12m · pers 18 · 2h04m · …`. 5H+7D doubles the width and collides with the tray icons |
| 9 | Per-account rate-limit backoff | One account in 429 must not freeze the other three |
| 10 | Staggered polling | Four accounts on the same 5-minute tick would burst four requests at claude.ai |
| 11 | 1 to `AccountLimits.Max` (4) accounts, nothing tied to this author's setup | The widget ships to strangers, and one account is their normal case: a single-account install must behave exactly like the current release. The cap is one constant with its reasoning attached — above four the rows stop being readable at the default scale and the request budget stops being polite |

## Data model

`AccountProfile` gains the fields that are per-account today only by accident of there being one
account:

```csharp
public sealed record AccountProfile(
    string Id,                       // stable, generated once; used as the WebView2 ProfileName
    string DisplayName,              // editable; defaults to the organization name
    string? OrganizationId,          // moves out of WidgetSettingsData
    DateTimeOffset? RetryPausedUntil,// moves out of WidgetSettingsData
    int ConsecutiveRateLimits);      // moves out of WidgetSettingsData
```

`ProfileFolder` is dropped: with decision 1 the isolation key is a profile name inside the shared
user data folder, not a folder path.

`WidgetSettingsData` loses `OrganizationId`, `RetryPausedUntil` and `ConsecutiveRateLimits`, and
gains `TrayAccountId` (which account the tray icon and its metric describe).

`Id` is also the WebView2 profile name, so it must stay inside that character set: ASCII letters,
digits and `# @ $ ( ) + - _ ~ .` and space, at most 64 characters, not ending in `.` or space, and
case-insensitive because it maps to a directory on disk. A GUID with dashes satisfies all of it.

### Migration

A settings file written by an earlier version has a top-level `OrganizationId` and no `Accounts`.
Loading it must produce one account carrying that organization id and the existing retry state:
`MigratesSingleAccountSettings` asserts exactly that, and it is pure Core logic.

**The upgrade is silent — measured, not assumed.** WebView2 puts a NAMED profile under
`<UDF>\EBWebView\WV2Profile_<name>` and the one created with no controller options under
`<UDF>\EBWebView\Default`. The prefix makes the implicit profile unreachable by any `ProfileName`,
and every pre-multi-account sign-in lives in it.

So the migrated account — the first one, id `default` — keeps `profileName = null` and is created
with no controller options; accounts added afterwards get named profiles. Verified end to end on a
real signed-in account: the same build with `ProfileName = "default"` opened the sign-in window,
with `null` it did not.

## Core changes

Core stays pure and framework-free; all of this is unit-testable.

- `DialModel.All` already returns the three dials for one snapshot. Add `AccountRow` — a display
  name, the three dials, and the 5H reset text — and `AccountRows` building the list from a
  per-account snapshot map. Ordering is the settings order, never sorted by usage: decision 2 is a
  full picture, not a ranking.
- New `PanelMetrics.ForAccounts(accountCount, side)` owns panel width, height and dial size. The 2x2
  assumption currently lives in `WidgetRootView.ApplyLayout` as `(side - pad*2 - gap) / 2`; what
  moves to Core is the *sizing*, not the placement — WPF's own `Grid` places cells, and duplicating
  that in Core would be inventing a layout engine. `DialGeometry.AngleDegrees` is untouched.
- `TrayText.Metrics` is unchanged for the tray, and gains `BandMetrics(rows)` for decision 8.
- `UsageStore` becomes per-account: one store instance per account, each with its own retry state
  loader/saver. The coalescing and backoff logic is untouched — it is already correct for one
  account, and four instances of correct is correct.

## App changes

- `ClaudeWebSession` takes a profile name instead of a profile folder, and one instance is created
  per account off the shared environment.
- Login: one window per account, titled with the account's display name, so it is unambiguous which
  account is being signed into. **Invariant: the login controller and the fetch controller for an
  account must be created with the same `ProfileName`.** The author's comment on the single-environment
  design marks this trap for one account; per-account profiles multiply it, and it fails silently —
  the sign-in succeeds and that account's dials stay empty. It gets its own assertion.
- `Microsoft.Web.WebView2` is pinned at `1.0.4129.50`; `ProfileName` shipped in `1.0.1245.22`, so no
  package bump is needed.
- Tray menu gains an `Accounts` submenu: the account list with the tray-bound account checked,
  `Add account…` (opens a login window against a new profile, names the account from
  `/api/organizations` once signed in), `Rename`, `Remove`. `Remove` clears that profile's browser
  data, so it is a sign-out as well as a delete.
- `Refresh now` refreshes the tray-bound account only. Refreshing four accounts by hand is what the
  rate limit punishes.
- Polling: the existing 5-minute `DispatcherTimer` stays the cycle; accounts are offset inside it by
  `cycle / accountCount` (~75 s at four).

## Rejected alternatives

**Read Claude Code's own statusline payload instead of polling claude.ai.** The payload carries
`rate_limits.five_hour` / `seven_day` with `used_percentage` and `resets_at`, and `UsageSnapshot`
already has `SourceUpdatedAt` for exactly such a source — but no bridge exists in the code today; the
field is groundwork. Rejected as the primary source because the payload exists only while a Claude
Code session is running and only after its first API response, so an idle account would show
nothing — which is precisely the account the user needs to see. Worth revisiting as a *supplement*
that refreshes the active account for free between polls.

**A per-account UDF.** See decision 1.

**Ranking / highlighting the freest account.** The user chose the full picture without judgements: a
ranking rule has to answer "is 5H worth more than 7D" and "what about a 5H that resets in a minute",
and a wrong answer is worse than no answer.

## Testing

- Layout: `AccountRows` over 0, 1 and 4 accounts, including an account whose snapshot failed to load
  (row present, dials `n/a` — never a disappearing row that shifts the grid).
- Migration: the case above.
- Backoff isolation: one account in 429 does not change another account's next-poll time.
- Stagger: four accounts on one cycle produce four distinct offsets covering the cycle.
- Band text: reset time present, width bounded.

## Settled during implementation

- **7D reset time as a second column: no.** With the panel on screen at four columns the row is
  already 553 pt wide at the default scale; 7D resets in days, and a second time column buys a
  number nobody watches at the price of the row no longer fitting a 2560-wide screen.
- **The panel can outgrow its saved position.** It stopped being square, so a position saved by an
  older build can put the right edge off screen — found by screenshot, not by reasoning. The window
  clamps itself into the working area on every layout change.
- **The band keeps two lines per column**, so the reset time shares the top line with the account
  name rather than earning a third line.
