# Layout editor design (2026-08-26)

Arrange the cells of the panel by dragging them on the panel itself, instead of picking from lists
in a tray menu.

What shipped with the layout record is configuration: `WidgetLayout` in `settings.json` plus a tray `Layout`
submenu for the two flows and the name placement. You pick a value, close the menu, and look at what
happened. The user asked for the other thing — «я хочу именно редактор layout прямо в этом
виджете»: take a dial, put it where you want it, watch the panel follow.

The engine is not wasted, and that is the point of this design. `WidgetLayout` is already the
complete description of a layout, `Sanitize` already validates it, and `PanelMetrics.For` →
`BuildGrid` already renders from it. An editor is a UI that writes that record.

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | Edit in place on the panel, not in a separate editor window | The user's choice 2026-08-26 over a window showing one block. The preview is the panel; a second window would be the same configuration dialogue with a picture |
| 2 | Drag swaps two cells; `Order` keeps exactly the items it had | The user's choice over free placement with holes. A swap is a permutation of the list, so it preserves what `Sanitize` already checks — the three dials present once each, the name and the status at most once — and `FlowGrid.Shape` derives the grid from the count regardless. Free placement needs a new record shape, new validation and a rule for collapsing empty rows |
| 3 | Enter and leave from the same tray item | The user's choice. Nothing is drawn over the panel to leave with — a `Done` button costs a cell's worth of space on a 170-px panel, and click-outside is unreliable for a window that never takes focus. Esc was the planned second exit; measured 2026-08-26 as unreachable — see Leaving |
| 4 | Core owns the list operation, WPF owns the hit test | **Reversed on a second pass; the first version put both in Core.** `SwapCells` belongs in Core: `LaidOut` is not `Order`, the mistake is easy and a test catches it. `CellAt` does not: `DialGrid` is `HorizontalAlignment=Center VerticalAlignment=Center` inside a panel that reserves `gap + StatusLineHeight * scale` at the bottom, so the grid's origin is not the padding — it sits half that reserve lower, ~17 px at the default scale. A pure function would have to copy that out of the XAML and would drift silently the next time the markup changes. Wrapping each cell in a transparent `Border` and asking `InputHitTest` costs one wrapper and cannot be wrong |
| 5 | One `BlockLayout` for all accounts — a drag in any block rearranges every block | It is already one record shared by every block, and that is the property that keeps the panel a table rather than four unrelated widgets. The live rearrangement of all blocks IS the feedback that says so |
| 6 | The name cell participates only when `NamePlacement.Cell` | Above/Below/Hidden take the name out of the flow, so there is nothing in the grid to drag. Changing the placement stays a tray decision |
| 7 | Accounts keep their order in v1 | Reordering blocks means reordering `WidgetSettingsData.Accounts`, a different record with different consequences (the tray-bound account, the export paths). Phase 2, below |

## What the editor edits

```csharp
WidgetLayout(LayoutFlow PanelFlow, BlockLayout Block)
BlockLayout(LayoutFlow Flow, NamePlacement Name, IReadOnlyList<BlockItem> Order)
```

Exactly one field: `Block.Order`. Everything else the tray already sets and the editor does not
touch. A drag is `Order` with two indices exchanged, run through `Sanitize`, saved, re-rendered —
the same path the tray's `LayoutSelected` already takes, so the editor adds no new save seam.

## Naming the cell under the cursor

Each cell is wrapped in a `Border` with a transparent background — transparent rather than absent,
because a panel with no background does not hit-test its own empty pixels, and the corner between a
dial's ring and its square is exactly where a drag gets aimed. Every host is registered against the
block and the laid-out position it carries:

```csharp
private readonly record struct CellHit(int Block, int Cell);
private readonly Dictionary<UIElement, CellHit> _cells = [];
```

A point becomes a cell by `RootGrid.InputHitTest(point)` followed by a walk up the visual parents
until one is a key in that dictionary. The padding, the gaps, the holes of a 3- or 5-item grid and
the status line all fall out as "no cell" without a single line about them, because none of them is
a host.

The host adds no thickness and no padding, so it cannot move what it wraps; that claim is checked
rather than asserted — the preview PNGs must hash identically before and after the wrapper lands.

## Core: the list operation

```csharp
/// `Order` with the items at two positions in `LaidOut` exchanged. Positions
/// index the laid-out list, not `Order`, because that is what the eye sees.
public BlockLayout SwapCells(int a, int b);
```

**`LaidOut` is not `Order` and the mapping is where this gets written wrong.** `LaidOut` drops
`BlockItem.Name` whenever the placement is not `Cell`, so with the name Above, cell 0 on screen is
`Order[1]`. `SwapCells` translates by identity — find each laid-out item in `Order` and exchange
*those* positions — rather than by index arithmetic, which is correct for every placement and does
not have to be re-derived when a sixth item appears.

`SwapCells` gets unit tests: a swap in both directions, a swap with itself, a position outside the
laid-out cells, a swap with the name Above (where the two index spaces differ), and the invariant
that `Sanitize` accepts every result of a swap — including from a layout carrying the status cell,
since a swap the save path rejects would silently reset the layout to default.

The hit test gets no unit test and does not need one: it asks WPF where the cursor is, and the thing
that could go wrong — a wrapper that shifts the layout — is caught by the PNG hashes instead.

## App: what a drag looks like

`WidgetRootView` gains `EditMode` (bool) and `LayoutEdited` (event carrying the new `WidgetLayout`).

**Entering.** Tray → Layout → `Edit layout…` sets `EditMode`. Each laid-out cell gets a dashed
one-pixel outline in `Theme.DimBrush`, and the status line gains `drag a cell to swap · Edit layout
finishes` — appended after the service notice and the usual status text with the same ` · `
separator, not substituted for them. As first written here the hint replaced the status line
outright; a ruling during task 4 changed that, because it would have hidden a service outage or a
rate-limit notice for as long as edit mode stayed on. The hint sits last in the joined list, so a
notice still leads when there is one — `edit-mode-drag.png` renders exactly this case, with
`ServiceStatus.Degraded` on: `service SLOW · drag a cell to swap · Edit layout finishes`. Nothing
moves and nothing resizes. The hint belongs inside `SetContent`, not only at the moment edit mode is
entered: `SetContent` rewrites `StatusLineText` on every poll, so a hint written once is erased by
the next refresh a minute later.

**Suppressing the window.** `DesktopWidgetWindow` starts a manual drag or a resize on
`MouseLeftButtonDown` at window level, and `StatusDialControl` opens status.claude.com on a click.
Both must be inert while editing. `WidgetRootView` handles `PreviewMouseLeftButtonDown` on
`RootGrid` and marks it handled when `EditMode` is on: preview tunnels down before the bubbling
event climbs back to the window, so neither the panel drag nor the dial's own handler ever sees it.
Verified against the code rather than assumed — `DesktopWidgetWindow`'s constructor subscribes with
plain `+=` (`OnWindowMouseLeftButtonDown`, `OnWindowMouseMove`, `OnWindowMouseLeftButtonUp`,
`OnLostMouseCapture`), not `AddHandler(…, handledEventsToo: true)`, so `Handled` genuinely stops all
four. Named rather than cited by line because this exact reference has already been repaired twice
in this work: once in the code comment that points at the same four handlers (in the commit whose
subject is that lesson) and once here, from `:124-127` to `:144-147`. The reverse —
checking `EditMode` inside the window and inside the dial — spreads one rule over three files and
will be forgotten by the fourth.

**Dragging.** Mouse down over a cell records the hit and the point; the drag starts once the pointer
passes `SystemParameters.MinimumHorizontalDragDistance`, so a click that opens nothing is not a
half-swap. While dragging: the mouse is captured by the view, a ghost of the source cell (a
`VisualBrush` of the element at 60% opacity) follows the cursor, and the cell under the cursor is
outlined in `Theme.TextBrush`. The ghost is the only source-side signal: this paragraph also
promised a dimmed source cell, nothing ever implemented it, and the ruling on finding it (final
review, 2026-08-26) was to drop the clause rather than build a second signal the user never asked
for.

The ghost lives on a transparent `Canvas` overlaying `RootGrid`, **not** in the `AdornerLayer`. Two
reasons, and the second is the one that decides it: an adorner layer needs an `AdornerDecorator` in
the tree, which this panel does not have — and `RenderTargetBitmap.Render(view)` does not draw
adorners at all, so the offscreen preview would render every edit-mode PNG with the ghost missing
and prove nothing about the thing hardest to get right.

**Dropping.** On mouse up, the hit test again; a null hit or the source cell is a no-op, anything else
raises `LayoutEdited` with the swapped layout. `App` saves it through `SettingsStore` and re-renders
— the tray's existing path. Losing capture (Alt+Tab, a taskbar click) cancels the drag and changes
nothing, the same way `OnLostMouseCapture` already cancels a window drag.

**Leaving.** The tray item only — `Edit layout…` toggles edit mode both ways. Every drop was already
saved, so leaving saves nothing and cancels nothing — there is no dialogue to confirm and no state
to lose. **Measured 2026-08-26**: a temporary `HwndSource` hook was installed on the panel's own
HWND (`WS_EX_NOACTIVATE`, `ShowActivated = false`) and logged every `WM_KEYDOWN`/`WM_SYSKEYDOWN` it
saw. Conditions: the panel visible on the desktop, one Esc sent to the foreground window five
seconds after launch (never the panel — it cannot be the focused window), log read two seconds
later. The hook installed (confirmed by its own marker line) and observed neither message. Esc
cannot reach this window; there is no `KeyDown` route to build. The tray item is the only way to
leave edit mode.

**Ruling (taken while implementing, 2026-08-26 — not yet put to the user): no
`WH_KEYBOARD_LL` fallback.** A system-wide low-level keyboard hook would see the key regardless of
focus — it is not that no route exists, only that this one does not. Installing a hook that
observes every keystroke on the machine, for every app the user is in, just to close an edit mode
on this panel, reads as out of proportion to the feature. Taken as a call, not a fact, because the
plan asked to measure and act without stopping for every branch; the user has not seen it and can
overrule it. Flag it rather than reading this paragraph as settled.

## Verification

The panel sits behind application windows, so nothing here is judged from a screenshot.
`CUW_RENDER_PREVIEW` gains four edit-mode cases, each a PNG of the whole chrome offscreen:

| PNG | What it renders | What it proves |
|---|---|---|
| `edit-mode.png` | `EditMode` on over `WidgetLayout.Default`, nothing being dragged | The resting chrome: one dashed outline per cell in every block, the name cell outlined like a dial, and the hint on the status line |
| `edit-mode-drag.png` | A drag in flight — the ghost parked between two cells, the target outlined — with `ServiceStatus.Degraded` on | The shipped ghost and highlight, through `PreviewDrag` calling what the mouse handlers call; and that the hint is *appended* to a service notice rather than hiding it |
| `edit-mode-after-swap.png` | Edit mode on, then `ApplyLayout` again from `BlockLayout.SwapCells(0, 3)` — the save path `App` takes after a drop | The chrome is rebuilt against a grid built *after* the mode was entered, when `BuildGrid` has replaced every host the `_chromeBounds` cache is keyed on; and that the swap reaches every block (decision 5). It cannot show outlines *offset* from their dials — a swap permutes cell contents, not the uniform cell rectangles — so the signature it discriminates is chrome missing, doubled or orphaned |
| `edit-mode-drag-name-above.png` | The same drag with `NamePlacement.Above`, ghost on laid-out cell 0, target on cell 2 | The `LaidOut` ≠ `Order` mapping *through the cell registry*, which the other three cases cannot reach because `NamePlacement.Cell` makes the two index spaces coincide. With the name above, the laid-out cells are the three dials: the ghost must be a copy of 5H, the target must sit on OPUS, and the name line must carry no outline at all |

The drag arithmetic itself is Core's, so it is judged by `SwapCells`'s unit tests, not by the render.

Whether a `WS_EX_NOACTIVATE` window sees `WM_KEYDOWN` could not be judged by either and had to be
measured live — done 2026-08-26, see Leaving. It does not; there is nothing left to verify here.

What no render reaches, because it needs a hand on a mouse: `CaptureMouse` under
`WS_EX_NOACTIVATE`, the window drag and the status dial's link actually being suppressed, the two
cancels (lost capture, and a release the view never saw), and `settings.json` changing after a real
drop. Open for the user on the running build — unverified, not assumed.

## Not in scope

- **Free placement with holes** — decision 2.
- **Per-account layouts** — decision 5; the panel is a table.
- **Resizing a cell** — dial size, gap and padding come from the theme and scale together on
  purpose (`PanelMetrics`, "letting them be set independently is how a panel ends up unreadable
  with no way back").
- **Undo history** — a swap is undone by the reverse swap, in the tool that made it.
- **Reordering accounts (phase 2)** — dragging a whole block reorders
  `WidgetSettingsData.Accounts`. `CellHit` already carries the block index, so the addition is a
  drag that starts on a block's empty area rather than on a cell, plus a settings write that has to
  leave `TrayAccountId` and the per-account export paths pointing at the same accounts. Worth doing
  once the cell editor has been used for a week; not worth guessing at now.

## Plan

The implementation plan (not included here) breaks it into five tasks: `SwapCells` in Core, the cell
hosts, edit mode and its chrome, the drag, and Esc (measured before it is built on).
