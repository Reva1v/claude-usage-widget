# Themes and a redesigned tray menu

Date: 2026-09-05. Status: approved for planning.

## Goal

Two things the user asked for on 2026-09-05, after v0.2.5:

1. The tray icon's context menu looks like a stock Windows 7 `ContextMenuStrip`
   ("выглядит очень по-старому"). Make it look like part of the widget: the
   panel's palette, generous spacing, icons on the top-level items, rounded
   corners on Windows 11.
2. A **light theme** for both the widget panel and the menu, with a
   three-way switch — **System** (default), **Dark**, **Light** — where System
   follows Windows' *app* theme and tracks it live.

## Non-goals

- Acrylic/blur behind the panel. The panel stays a translucent solid.
- Restyling the sign-in window's WebView2 content, the rename dialog, or the
  system tray icon's *shape*. The rename dialog and the sign-in window's own
  chrome (the paste box) take the palette because they are plain WPF, but no
  further design work goes into them.
- Per-account or per-dial colours. One palette per theme.
- A WPF-drawn replacement for the WinForms menu. The menu stays a
  `ContextMenuStrip` with a custom renderer (decided with the user, option 3
  of three).

## Settings

`WidgetSettingsData` gains one nullable field:

```csharp
/// Null — the default — is System: follow Windows' app theme.
public ThemeChoice? Theme { get; init; }
```

`ThemeChoice { System, Dark, Light }` lives in Core with a resolver:

```csharp
public enum ThemeKind { Dark, Light }
public static class ThemeChoices
{
    public static ThemeKind Resolve(ThemeChoice? saved, ThemeKind system) =>
        (saved ?? ThemeChoice.System) switch
        {
            ThemeChoice.Dark => ThemeKind.Dark,
            ThemeChoice.Light => ThemeKind.Light,
            _ => system,
        };
}
```

Tray menu: a **Theme** submenu with three radio items, System / Dark / Light,
placed right after **Layout** (it is a look setting, like Layout). Picking one
saves the field and applies the theme immediately, no restart.

## Reading the system theme

Two registry values under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`:

- `AppsUseLightTheme` — what **System** means for the widget panel and the
  menu (Windows' "Default app mode").
- `SystemUsesLightTheme` — the taskbar's own mode. The taskbar band's text and
  the tray icon's digits follow THIS value regardless of the theme setting
  (decided with the user): white on a dark taskbar, near-black on a light one.
  Nothing else in the app reads it.

A missing value means dark (Windows 10 before 1903 had no light mode).

Live tracking: `SystemEvents.UserPreferenceChanged` with category `General`
fires when either value changes (the app already subscribes to `SystemEvents`
for power events). On it, re-read both values; if the resolved kind changed,
apply the theme. The band and icon re-render on the same event.

`SystemTheme` (App, `Windows/SystemTheme.cs`) owns both reads and the event,
exposing `AppsKind`, `TaskbarKind` and `Changed`.

## The palette

`Theme` stops being a bag of static readonly brushes and becomes:

- `Palette` — a sealed record of colours and frozen brushes for ONE theme
  kind: `Panel`, `Track`, `Text`, `Dim`, `Accent`, `Warning`, `Danger`, `Info`,
  the derived `PanelBackgroundBrush` / `OverlayBackgroundBrush` (alphas as
  today: 0.82 and 0.92), `TrackBrush`, `TextBrush`, `DimBrush`, `WarningBrush`,
  and `ColorFor(ThresholdLevel)` / `ColorFor(ServiceStatus)`.
- `Theme.Current` — the active `Palette`; `Theme.Changed` — raised after it is
  swapped. `Theme.Apply(ThemeKind)` swaps it.
- Everything that is not a colour — font family, weights, size functions,
  padding, gap, corner radius — stays static on `Theme` exactly as it is.

### Dark (today's values, unchanged)

| Role    | Value              |
|---------|--------------------|
| Panel   | `#1E2230`          |
| Track   | `#404557`          |
| Text    | `#C7CCDE`          |
| Dim     | `#73788C`          |
| Accent  | `#A6D189`          |
| Warning | `#E5C890`          |
| Danger  | `#E78284`          |
| Info    | `#8AB4E6`          |

### Light (starting values, tuned on the preview PNGs before merge)

| Role    | Value     | Why                                                   |
|---------|-----------|-------------------------------------------------------|
| Panel   | `#F3F5F9` | Cool off-white; the same alpha keeps wallpaper visible |
| Track   | `#D3D8E3` | The unfilled ring: visible on the panel, quieter than text |
| Text    | `#262B3A` | Near-black with the dark theme's blue cast            |
| Dim     | `#6C7388` | Captions and idle glyphs                              |
| Accent  | `#4E9A4C` | The dark pastels wash out on white — one step more saturated and darker, same hue family |
| Warning | `#C9962A` |                                                       |
| Danger  | `#D2565A` |                                                       |
| Info    | `#3877C8` |                                                       |

Contrast targets: text on panel ≥ 7:1, dim on panel ≥ 4.5:1, each dial colour
on panel ≥ 3:1. Checked with the dataviz-style contrast arithmetic during
tuning; the numbers above already pass on paper.

## Applying the theme at runtime

Three kinds of consumer, three mechanisms:

1. **XAML** (`WidgetRootView.xaml`, 15 `x:Static views:Theme.*` references,
   `LoginWindow`/`RenameWindow` if any). `x:Static` is read once at load and
   never again. Replace with `{DynamicResource}` against keys
   (`Theme.PanelBackgroundBrush`, `Theme.DimBrush`, …) that live in
   `Application.Resources`. `Theme.Apply` writes the new brushes into that
   dictionary; WPF re-renders every DynamicResource consumer by itself.
2. **Code that assigns colours once** (`WidgetRootView.xaml.cs` toolbar and
   header, `DialControl`, `StatusDialControl`, `DialText` callers). Each such
   class subscribes to `Theme.Changed` and re-applies its colours; custom-drawn
   controls additionally `InvalidateVisual()`. The widget window's existing
   `RebuildLayout` path is NOT used for this — a theme change must not move
   anything, only repaint.
3. **Cached bitmaps** (`TrayIconRenderer` output, the tray menu's item icons).
   Regenerated on `Theme.Changed` (menu icons) or `SystemTheme.Changed` (tray
   icon digits, band text).

The offscreen preview (`CUW_RENDER_PREVIEW`) renders every case twice, into
`dark/` and `light/` subfolders, so both palettes are judged the same way and
the light values can be tuned without a desktop.

## The tray menu

Still a `ContextMenuStrip`; the look comes from a `WidgetMenuRenderer :
ToolStripProfessionalRenderer` fed by the current `Palette`, plus a little
Win32.

- **Colours.** Background = `Panel` at full alpha (a menu cannot be
  translucent). Text = `Text`; disabled and shortcut text = `Dim`. Hover =
  `Track` at 60 % over the background, full-width, with a 4 px radius.
  Separators = `Track`, 1 px, inset by the item padding. Submenu arrows and
  check glyphs in `Dim`, turning `Text` on hover.
- **Spacing.** Item height 30 px at 100 % DPI (`ToolStripMenuItem.Padding`
  and `ContextMenuStrip.Padding` set from a `MenuMetrics` helper that scales
  with the menu's DPI). 12 px horizontal padding, an 18 px icon column, 10 px
  between icon and text. The menu's own padding 6 px so the first hover
  highlight does not touch the border.
- **Check marks.** The default boxed tick goes; a Segoe MDL2 `` (E73E) glyph
  in `Accent` sits in the icon column for checked items, and radio-style
  groups (Tray shows, Band position, Band shows, Theme, Accounts) use the same
  glyph — one language for "this one is on".
- **Icons.** Top-level items only, Segoe MDL2 glyphs rendered to 16 px bitmaps
  in `Dim` (`Text` on hover, via a second bitmap) at the menu's DPI:
  Refresh `E72C`, Sign in `E77B`, Tray shows `E7C4`, Accounts `E716`, Layout
  `E80A`, Theme `E790`, Show on desktop `E7F4`, Taskbar band `E90E`, Band
  position `E8A0`, Band shows `E7B3`, Lock position `E72E`/`E785`, Launch at
  login `E7E8`, Quit `E8BB`. The version/GitHub and Report an issue lines get
  `E774` and `EBE8`. Submenu items carry no icons.
- **Corners and border.** On Windows 11 (build ≥ 22000) every dropdown —
  the root and each submenu — gets `DwmSetWindowAttribute(
  DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)` when it opens (the handle
  exists only then: hook `Opening`/`DropDownOpening`). A 1 px border in
  `Track` drawn by the renderer replaces the system's. On Windows 10 the
  corners stay square and everything else applies.
- **Drop shadow.** Left to the system (`DropShadowEnabled` stays true).
- **Fonts.** Segoe UI Variable Text 9 pt when installed (Windows 11),
  Segoe UI 9 pt otherwise — the system menu font, not Consolas: the menu is
  chrome, not data.

The personal branch's **Telegram bot** submenu picks the styling up for free;
it needs an icon (`E8BD`) added on that branch only.

## What follows the theme, and what does not

| Surface                        | Follows                          |
|--------------------------------|----------------------------------|
| Widget panel, header, notice, edit strip, status line | Theme setting (System = app theme) |
| Tray context menu              | Theme setting                    |
| Rename dialog, sign-in paste box | Theme setting (palette only)   |
| Tray icon digits and ring      | Taskbar mode (`SystemUsesLightTheme`) |
| Taskbar band text              | Taskbar mode                     |
| Sign-in window's web page      | claude.ai's own                  |

## Error handling

- Registry read fails or the value is missing → dark. Never throws.
- `DwmSetWindowAttribute` fails (Windows 10, or a future build) → ignored;
  square corners.
- The glyph font is missing → the icon column stays empty; text does not
  shift because the column is reserved anyway.

## Testing

- Core: `ThemeChoices.Resolve` — the six (saved, system) combinations.
- Core: `WidgetSettings` round-trips `Theme` and reads an old file as null.
- App, offscreen: `CUW_RENDER_PREVIEW` writes `dark/` and `light/` sets; the
  light PNGs are looked at before merge (contrast, ring visibility, dimmed
  dials, edit-mode outlines).
- App, manual checklist (the user, one Windows 11 machine): switch Theme
  between the three values with the panel visible; flip Windows' app mode
  with System selected; flip the taskbar mode and check the band and icon;
  open every submenu and confirm corners, hover, checks, icons; DPI 100 % and
  150 %.

## Risks

- `UserPreferenceChanged` is delivered on a thread-pool thread. Every handler
  marshals to the dispatcher (the existing `OnPowerModeChanged` shows the
  pattern) — forgetting it is a cross-thread WPF exception.
- WinForms `ToolStripProfessionalRenderer` draws the check background on its
  own; overriding `OnRenderItemCheck` AND `OnRenderImageMargin` is needed to
  fully remove the boxed look — verify at 150 % DPI where the box is most
  visible.
- The menu's `Font` must be set before the first `Show` or item heights are
  measured with the old font.
