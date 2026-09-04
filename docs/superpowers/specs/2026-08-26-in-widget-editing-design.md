# In-widget editing design (2026-08-26)

Move every layout decision out of the tray and onto the panel, take the hover header off the
content it covers, and let the service status be either a dial in every block or one line for all
of them.

The cell editor shipped earlier the same day does one of these: cells swap by drag.
Everything else — which way accounts flow, which way dials flow inside a block, where the account
name sits, whether the status dial exists — is still a tray submenu. The user's words on seeing it:
«вообще я хотел чтобы все редактирование было в самом виджете». This design finishes that.

Two defects arrive with it, both visible in the same screenshot: the hover header's eye and lock
are drawn over the top of the panel, and with `NamePlacement.Above` they sit on the account names;
and with the status dial switched on, three blocks each draw a STATUS dial that says the same word.

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | An edit toolbar across the top of the panel, visible only in edit mode | The user's choice over click-to-cycle (undiscoverable — you have to be told it exists) and a right-click menu (that is the tray menu relocated, not editing in the widget). It also gives the top strip a single control, which is what fixes decision 2 |
| 2 | The hover header is deleted; eye and lock move into the toolbar | The user's choice. Outside edit mode the panel then carries no chrome at all, so there is nothing left that can cover content. **Cost, accepted:** hiding the widget is no longer one click on the panel — it is edit mode, or the tray's `Show on desktop` |
| 3 | `StatusMode` is `Cell` or `Line`, and in `Line` mode the line always shows | The user's choice over dropping the dial and over a single shared dial. The status is one fact about one service; repeating it per account was the complaint. A silent line reads as a lost dial — that already happened once — so in `Line` mode it states the state even when the service is fine |
| 4 | The tray keeps `Edit layout…` and loses the rest of the Layout submenu | The user's choice. One editing surface rather than two to keep agreeing with each other. Nothing is lost: the layout cannot be edited while the widget is hidden either way |
| 5 | The toolbar is RESERVED, not overlaid — the panel grows by its height on entering edit mode | Overlaying is what the hover header does, and covering content is the defect being fixed. The panel visibly changing height when the mode is entered is honest feedback that the mode is on |
| 6 | Every button cycles its own setting; there are no dropdowns | A dropdown on a 170-px panel is a menu again. Cycling is one hit target per setting, and the panel redraws under the cursor, so the value is never in doubt |

## The toolbar

One row at the top of the panel, only while `EditMode` is on. Left to right:

| Button | Cycles | Values |
|---|---|---|
| Accounts | `WidgetLayout.PanelFlow` | Grid → Row → Column |
| Dials | `WidgetLayout.Block.Flow` | Grid → Row → Column |
| Name | `WidgetLayout.Block.Name` | Cell → Above → Below → Hidden |
| Status | `StatusMode` | Cell → Line |
| Lock | `PositionLocked` | on → off |
| Hide | — | hides the widget and leaves edit mode |

Glyphs come from `Segoe MDL2 Assets`, which the panel already uses for the eye and the lock. Each
button carries a tooltip naming the setting and its current value, because a glyph alone cannot say
which of three flows is selected.

**Width is the constraint that decides the shape.** The panel's minimum side is 150 and the default
170; six buttons at `16 * scale` plus gaps fit that at the default, and the toolbar is not allowed
to widen the panel — `PanelMetrics` sizes from the content, and a toolbar wider than the dials
would make the panel jump sideways when the mode is entered. If six do not fit at `MinSide`, the
row wraps to two, and the reserved height follows. That is a measurement to take during
implementation, not a guess to encode here.

## Status: one setting, one source of truth

`StatusMode` is new in `WidgetSettingsData`, not a second way of saying what `Block.Order` already
says:

```csharp
public enum StatusMode
{
    /// A STATUS dial in every block, in the cell grid, draggable like any other.
    Cell,

    /// One line at the bottom of the panel, for all blocks, always visible.
    Line,
}
```

`BlockItem.Status`'s presence in `Order` FOLLOWS `StatusMode` rather than being set independently:
switching to `Cell` appends `Status` to `Order` if it is absent, switching to `Line` removes it.
Both directions run through `Sanitize`, which already tolerates the item being present or absent.
Two independent switches for one decision is how they end up disagreeing, and a user who has
`StatusMode = Line` and `Status` still in `Order` would see the state twice.

In `Line` mode the line reads the service state plainly — `service operational`, `service degraded`
— instead of falling silent when all is well. The existing status line keeps its other job: a
rate-limit countdown, a stale-data notice and the edit hint still append to it with the same ` · `
separator.

## What this removes

- `HeaderBorder`, `HeaderGrid`, `EyeButton`, `LockButton` and the hover plumbing in
  `WidgetRootView` (`SetHovering`, `UpdateLockIcon`, the `HideRequested`/`LockToggleRequested`
  events) — the buttons move to the toolbar and keep their behaviour.
- The tray's flow submenus, name-placement submenu and `Service status dial` item. `Edit layout…`
  stays.
- `31 * scale` of top padding on `NoticeStack`, which existed only to clear the hover header.

## The browser window that flashes — diagnosed as far as the evidence allows, and instrumented

A window was reported appearing in the taskbar for a fraction of a second and vanishing,
while the tray menu was open; it is not reproducible. **Not resolved.** Two mechanisms are live
and the evidence does not separate them:

1. `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })` — two call sites,
   `StatusDialControl.OpenStatusPage` and `TrayIcon`'s repository/issues items. With Chrome already
   running (17 processes at the time of the report), the launcher process starts, hands the URL to
   the live instance and exits — a taskbar button that appears and disappears, which is the
   description exactly.
2. WebView2. 21 `msedgewebview2` processes are alive, one profile per account, polled on a
   staggered five-minute cycle.

So: **an always-on line log**, one file, one line per browser launch and per login-window creation,
with a timestamp and which call site asked. The next occurrence identifies itself instead of
needing anyone to catch a flash. When the decisive measurement needs a person's hand and the
moment has passed, it belongs in the tool.

**One hazard stands regardless of what that window was**, and it is geometry, not speculation. The
panel is at 2220,916 with side 207 on a 2560×1440 screen, so it spans roughly x 2220-2563,
y 916-1378; the taskbar starts at 1392. The panel's bottom edge is 14 px above the taskbar and
covers the full width of the notification area, and with `Block.Flow = Column` and `Status` last in
`Order`, the bottom row of cells is three STATUS dials. `StatusDialControl` opens a browser on
mouse **down**, over the whole square — it deliberately draws a transparent rectangle so the corners
hit-test too. A press anywhere near the tray lands on a link.

Fix: the dial opens the page on a completed **click** — press and release on the same control — and
never while `EditMode` is on. That removes the stray-press case, not the deliberate-click case; a
click that dismisses a menu is still a click, and the honest mitigation for that is the panel not
living on top of the tray. Stated rather than engineered around.

## Verification

Everything visual is judged from `CUW_RENDER_PREVIEW`, never a screenshot — the panel sits behind
application windows. New cases: the toolbar at `MinSide` and at `MaxSide` (the width question above
is answered by looking at those two PNGs, not by arithmetic), and `StatusMode = Line` with the
service both operational and degraded.

Core carries the logic that can be tested without a window: `StatusMode` round-tripping through
`settings.json`, and the rule that `Order` follows `StatusMode` in both directions.

The click-not-press change to `StatusDialControl` is not offscreen-verifiable and joins the live
checks still open.

## Not in scope

- **Editing the accounts themselves** — add, rename, remove and the tray-bound account stay in the
  tray. This design moves LAYOUT, not account management.
- **Reordering account blocks by dragging** — still phase 2 of the cell editor, still unbuilt.
- **A settings window.** The panel is the surface; a window would be the tray menu with a title bar.

## Amendment 2026-09-04

Decision 5 — the toolbar is RESERVED space inside the panel — is superseded. The strip now floats
in a band OUTSIDE the rounded panel: entering edit mode grows the WINDOW by
`PanelMetrics.ToolbarReserve` and draws the strip in the extra space, above the panel while
`panelTop - reserve` still clears the work area and below it otherwise, with the window's `Top`
moving by the same number so the panel does not shift a pixel. The reason is the user's:
«Редактор меняет размер в режиме редактирования, а когда выходишь — уменьшается. Поставил виджет в
угол экрана, визуально идеально, вышел из режима — сверху и снизу всё съехало.» The honest feedback
decision 5 wanted is still there — a strip appears — it just no longer costs the panel its size or
its place, and `PersistGeometry` now writes the PANEL's top-left rather than the window's, so a
visit to the mode cannot walk the saved position up the screen.

The toolbar gains a seventh button, Done, first from the left (`Segoe MDL2 Assets` E73E): «Не
хватает кнопки сохранения, чтобы опять не заходить в трей». It raises `EditDoneRequested` up to
`App.SetEditingLayout(false)` — the single toggle the tray item already goes through, not a second
way out to keep in agreement with the first. Seven buttons at `16 * scale` with a `4 * scale` gap
need 136 of the default panel's 146 pt of inner width, so the row wraps nowhere between `MinSide`
and `MaxSide`; the wrap in `ToolbarMetrics` stays as a fallback that promises no fit, and
`SevenButtonsFitOnOneRowAtEverySide` is what holds the arithmetic to its claim.

An eighth button hides the model dial — «для work аккаунта он не нужен» — and it is the `StatusMode`
pattern again, not a second mechanism: `ModelDial` is the master, `WidgetSettingsData.ModelDial` is
nullable so an old file keeps its dial, and `Order`'s model cell follows through `Sanitize`, which
now takes both settings and treats `Model` as at most once, like `Status`. Only the CELL is
optional: the tray tooltip, the taskbar band and `DialModel.All` never read the layout and keep
reporting the percentage. The button's glyph is `EC4A` "SpeedHigh", the gauge of the EC48/EC49/EC4A
trio, picked off a rendered sheet of the font rather than a name list. **The pitch had to be
re-derived**: eight buttons at `16 * scale` need `8*16 + 7*gap` of the 146 pt of inner width, so the
old gap of 4 (156) wraps and even 3 (149) wraps — 2 is what fits, at every side, since both numbers
scale from the same one. That is also the ceiling: a ninth button fits at no pitch and would take
the wrap. `EightButtonsFitOnOneRowAtEverySide` replaces the seven-button claim.

The panel's vertical rhythm is rebuilt around a status BAND — «отступы сверху и снизу визуально не
идентичные; service operational прилип в самом низу, хотя по красивому было бы центровать в
свободном месте». It used to reserve a flat 16 pt strip, centre the dial grid in everything but the
padding and then draw the line 2 px off the bottom edge; measured on a real layout at side
207 that is **34 px** over the names, 21 between the last dial and the text and **3** under it.
`PanelMetrics` now reserves `StatusBand = 2 * Padding + CaptionLine`, puts the grid flush under the
top padding (`Height = Padding + content + StatusBand`) and exposes `TopGap`/`BottomGap` — the
latter DERIVED from the band, so `TopGap == BottomGap` checks how the band was built rather than
restating it. Same layout and side after: **18 / 19 / 15**; the residue is ink, not geometry (cap
top ~3.5 px inside the line box, ring ~2.5 px inside its cell, descender space under the caption).
`CaptionLine` is Consolas' 1.1709 em line spacing at the caption's 8 pt, measured through
`FormattedText`, and the view gives the TextBlock exactly that height so the reserve and the line
box cannot drift. The band is a padding on each side of the caption, **not the dial gap the first
design pass called for**: with the grid centred, `TopGap − BottomGap` is `Padding − Gap/2` = 7 for
any slack, so that formula cannot reach the equality — and the variant that does (grid offset 7,
inset 5) leaves 5 px under the text, non-identical margins again.

A ninth button shows the SUBSCRIPTION PLAN as a second line under the account name — «сверху текст
аккаунта, а под ним тир подписки», with its own show/hide button. `PlanLine` follows the `ModelDial`
pattern (nullable in settings, null = Hidden) but defaults OFF rather than on: nothing is taken away
by that, because the line has never been drawn. It is not a cell and nothing in `Order` follows it,
so there is no `Sanitize` step — only a `RebuildLayout`, because `BlockMetrics` RESERVES one
`CaptionLine` in the name row when the setting is on (Above/Below; the name cell already owns a dial
square). The reserve comes from the SETTING, never from whether a label has arrived, or the panel
would change height on the first poll. `PanelMetrics` now carries `PlanLine` so what the panel was
sized for and what it draws are one value, and the view reads its geometry through a single
`Metrics()` helper — the edit-mode path re-measures too, and a hand-written `PanelMetrics.For` there
would have dropped the reserve while the strip was up and nowhere else (`plan-line-*-on-edit.png` is
what holds that). **The eight-button ceiling was the ceiling for the SIZE, not the count**: nine at
16 pt need 160 of the default panel's 146 pt of inner width, so `ButtonSize` came down to 14
(9·14 + 8·2 = 142) while the glyph font stayed at `9 * scale` — the glyphs are unchanged, only the
box is tighter, confirmed by eye at `MinSide`. `NineButtonsFitOnOneRowAtEverySide` replaces the
eight-button claim, with a negative twin for the old size. The plan glyph is E8EC, a price tag.

The label is Core's (`SubscriptionTier.Label`), from three RAW fields stored on `AccountProfile` —
`capabilities`, `rate_limit_tier`, `raven_type` — so a mapping fix ships without asking claude.ai
again for a body already on disk. The capability names the family, the tier the multiplier, and
`raven_type` witnesses a team; capabilities are matched by MEMBERSHIP, because the three live bodies
put `chat` first in one account and second in another. Measured 2026-09-04 against three live
accounts: `Max 20x`, `Team`, `Max 5x`. **One rule is not from the brief**: research says `default_claude_ai` is the
tier of free AND pro, so that tier with no paid capability is labelled `Free` — without it the
unknown-tier fallback would prettify it into "Claude ai", which is not a plan anyone sells. Pro,
Free and Enterprise have no live sample here and are marked research-only in the tests. A null
label is "not fetched yet" and draws blank — deliberately NOT `Free`, which is what an organization
that claims nothing gets. `ClaudeWebSession` saves the three fields on every pick and backfills an
account picked before this release with ONE extra `/api/organizations` fetch, bounded by a
per-run flag and by the fields it writes; the usage endpoint, which owns the 429 budget, is untouched.
