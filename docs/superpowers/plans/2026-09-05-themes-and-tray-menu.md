# Themes and Tray Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A System / Dark / Light theme switch (System by default, tracking Windows live) for the widget panel and the tray menu, and a tray menu that looks like part of the widget: panel palette, spacing, icons, Windows 11 rounded corners.

**Architecture:** `Theme` becomes a swappable `Palette` (`Theme.Current` + `Theme.Changed`) whose brushes are also published into `Application.Resources` so XAML consumes them through `DynamicResource`; custom-drawn controls repaint on `Changed`. `SystemTheme` reads the two registry values and raises on `SystemEvents.UserPreferenceChanged`. The WinForms `ContextMenuStrip` keeps its structure and gets a `WidgetMenuRenderer`, glyph icons, and DWM rounded corners.

**Tech Stack:** .NET 8, WPF + WinForms (`UseWindowsForms`), System.Drawing for menu glyphs, `dwmapi.dll` P/Invoke, xUnit for Core.

**Spec:** `docs/superpowers/specs/2026-09-05-themes-and-tray-menu-design.md`

## Global Constraints

- All code comments in English (project convention since 2026-09-04).
- Commit messages in English with the trailer `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Release build must stay at 0 warnings; Core tests all green after every task.
- Dark palette values are unchanged from today: Panel `#1E2230`, Track `#404557`, Text `#C7CCDE`, Dim `#73788C`, Accent `#A6D189`, Warning `#E5C890`, Danger `#E78284`, Info `#8AB4E6`.
- Light palette starting values: Panel `#F3F5F9`, Track `#D3D8E3`, Text `#262B3A`, Dim `#6C7388`, Accent `#4E9A4C`, Warning `#C9962A`, Danger `#D2565A`, Info `#3877C8`.
- A theme change repaints; it never rebuilds the layout or moves the panel.
- Tray icon digits and taskbar band text follow the TASKBAR mode (`SystemUsesLightTheme`), not the theme setting.
- Build: `dotnet build -c Release -nologo -v q`; tests: `dotnet test Tests/ClaudeUsageWidget.Core.Tests -c Release -nologo -v q`. Do not run the App while the user's personal build is running (single-instance mutex); use `CUW_RENDER_PREVIEW` for visual checks.

---

## File map

| File | Responsibility |
|---|---|
| `src/ClaudeUsageWidget.Core/Views/ThemeChoice.cs` (new) | `ThemeChoice`, `ThemeKind`, `ThemeChoices.Resolve` |
| `src/ClaudeUsageWidget.Core/Settings/WidgetSettings.cs` | `Theme` field |
| `src/ClaudeUsageWidget.App/Views/Palette.cs` (new) | One theme's colours and frozen brushes; `Palette.Dark`, `Palette.Light` |
| `src/ClaudeUsageWidget.App/Views/Theme.cs` | `Current`, `Changed`, `Apply`; colour members forward to `Current`; publishes resource keys |
| `src/ClaudeUsageWidget.App/Windows/SystemTheme.cs` (new) | Registry reads, `AppsKind`, `TaskbarKind`, `Changed` |
| `src/ClaudeUsageWidget.App/Views/WidgetRootView.xaml(.cs)` | DynamicResource keys; repaint on `Theme.Changed` |
| `src/ClaudeUsageWidget.App/Views/DialControl.cs`, `StatusDialControl.cs` | `InvalidateVisual` on `Theme.Changed` |
| `src/ClaudeUsageWidget.App/Tray/TrayIconRenderer.cs` | `Render(valueText, ThemeKind taskbar)` |
| `src/ClaudeUsageWidget.App/Windows/TaskbarBandWindow.cs` | text brush from taskbar mode |
| `src/ClaudeUsageWidget.App/Tray/MenuGlyphs.cs` (new) | Segoe MDL2 glyph → 16 px `Bitmap` |
| `src/ClaudeUsageWidget.App/Tray/WidgetMenuRenderer.cs` (new) | `ToolStripProfessionalRenderer` for the palette; check glyph; border |
| `src/ClaudeUsageWidget.App/Tray/MenuChrome.cs` (new) | Font, padding, DWM rounded corners per dropdown |
| `src/ClaudeUsageWidget.App/Tray/TrayIcon.cs` | Theme submenu, icons, renderer wiring |
| `src/ClaudeUsageWidget.App/Windows/Win32.cs` | `DwmSetWindowAttribute` |
| `src/ClaudeUsageWidget.App/App.xaml.cs` | Startup apply, setting handler, system tracking |
| `src/ClaudeUsageWidget.App/LayoutPreview.cs` | `dark/` and `light/` output |
| `Tests/ClaudeUsageWidget.Core.Tests/ThemeChoiceTests.cs` (new), `WidgetSettingsTests.cs` | Core tests |
| `README.md` | Theme submenu, menu look |

---

### Task 1: ThemeChoice in Core and the settings field

**Files:**
- Create: `src/ClaudeUsageWidget.Core/Views/ThemeChoice.cs`
- Modify: `src/ClaudeUsageWidget.Core/Settings/WidgetSettings.cs` (after the `BandView` property)
- Test: `Tests/ClaudeUsageWidget.Core.Tests/ThemeChoiceTests.cs` (new)

**Interfaces:**
- Produces: `enum ThemeChoice { System, Dark, Light }`, `enum ThemeKind { Dark, Light }`, `static ThemeKind ThemeChoices.Resolve(ThemeChoice? saved, ThemeKind system)`, `WidgetSettingsData.Theme : ThemeChoice?`.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace ClaudeUsageWidget.Core.Tests;

public class ThemeChoiceTests
{
    [Theory]
    [InlineData(null, ThemeKind.Dark, ThemeKind.Dark)]
    [InlineData(null, ThemeKind.Light, ThemeKind.Light)]
    [InlineData(ThemeChoice.System, ThemeKind.Light, ThemeKind.Light)]
    [InlineData(ThemeChoice.Dark, ThemeKind.Light, ThemeKind.Dark)]
    [InlineData(ThemeChoice.Light, ThemeKind.Dark, ThemeKind.Light)]
    [InlineData(ThemeChoice.Light, ThemeKind.Light, ThemeKind.Light)]
    public void ResolveFollowsTheSystemOnlyForSystem(ThemeChoice? saved, ThemeKind system, ThemeKind expected) =>
        Assert.Equal(expected, ThemeChoices.Resolve(saved, system));

    [Fact]
    public void ThemeRoundTripsThroughSettingsAndReadsAsNullFromAnOldFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cuw-theme-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            Assert.Null(store.Load().Theme);

            store.Save(new WidgetSettingsData { Theme = ThemeChoice.Light });
            Assert.Equal(ThemeChoice.Light, store.Load().Theme);
            Assert.Contains("\"Theme\": \"Light\"", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Tests/ClaudeUsageWidget.Core.Tests -c Release -nologo -v q`
Expected: build error `ThemeChoice` not found.

- [ ] **Step 3: Implement**

`src/ClaudeUsageWidget.Core/Views/ThemeChoice.cs`:

```csharp
namespace ClaudeUsageWidget.Core;

/// What the user picked in the tray's Theme submenu. System — the default,
/// and what a null setting means — follows Windows' app theme.
public enum ThemeChoice
{
    System,
    Dark,
    Light,
}

/// The two palettes the app can actually draw.
public enum ThemeKind
{
    Dark,
    Light,
}

public static class ThemeChoices
{
    /// The palette a choice means, given what Windows currently says. Null is
    /// System: a file written before the setting existed follows the OS,
    /// which is also the default for a fresh install.
    public static ThemeKind Resolve(ThemeChoice? saved, ThemeKind system) =>
        (saved ?? ThemeChoice.System) switch
        {
            ThemeChoice.Dark => ThemeKind.Dark,
            ThemeChoice.Light => ThemeKind.Light,
            _ => system,
        };
}
```

In `WidgetSettings.cs`, after the `BandView` property:

```csharp
    /// The panel's and the tray menu's palette. NULLABLE: null is System —
    /// follow Windows' app theme — through <see cref="ThemeChoices.Resolve"/>.
    /// Never default it here.
    public ThemeChoice? Theme { get; init; }
```

- [ ] **Step 4: Run tests**

Run: `dotnet test Tests/ClaudeUsageWidget.Core.Tests -c Release -nologo -v q`
Expected: all pass (previous count + 7).

- [ ] **Step 5: Commit**

```bash
git add src/ClaudeUsageWidget.Core/Views/ThemeChoice.cs src/ClaudeUsageWidget.Core/Settings/WidgetSettings.cs Tests/ClaudeUsageWidget.Core.Tests/ThemeChoiceTests.cs
git commit -m "feat(core): ThemeChoice setting and resolver"
```

---

### Task 2: Palette and a swappable Theme

**Files:**
- Create: `src/ClaudeUsageWidget.App/Views/Palette.cs`
- Modify: `src/ClaudeUsageWidget.App/Views/Theme.cs`
- Modify: `src/ClaudeUsageWidget.App/Views/WidgetRootView.xaml` (15 `x:Static views:Theme.*` references at lines 30, 35, 68, 85, 89, 90, 93, 94, 107, 119, 125, 131, 156, 165, 166)

**Interfaces:**
- Produces: `sealed record Palette` with `Color Panel, Track, Text, Dim, Accent, Warning, Danger, Info`; `SolidColorBrush TrackBrush, TextBrush, DimBrush, WarningBrush, AccentBrush, PanelBackgroundBrush, OverlayBackgroundBrush, PanelOpaqueBrush`; `Color ColorFor(ThresholdLevel)`, `Color ColorFor(ServiceStatus)`; `static Palette Dark`, `static Palette Light`, `static Palette For(ThemeKind)`.
- Produces: `static Palette Theme.Current`, `static event Action? Theme.Changed`, `static void Theme.Apply(ThemeKind kind)`, `static ThemeKind Theme.Kind`. Every existing colour/brush member of `Theme` (`Panel`, `Track`, `Text`, `Dim`, `Accent`, `Warning`, `Danger`, `Info`, `TrackBrush`, `TextBrush`, `DimBrush`, `WarningBrush`, `PanelBackgroundBrush`, `OverlayBackgroundBrush`, `ColorFor(...)`, `PanelBrush(double)`) keeps its name and type but reads from `Current`, so no other code changes to compile.
- Resource keys in `Application.Current.Resources`: `"Theme.TextBrush"`, `"Theme.DimBrush"`, `"Theme.PanelBackgroundBrush"`, `"Theme.OverlayBackgroundBrush"`.

- [ ] **Step 1: Write `Palette.cs`**

```csharp
using System.Windows.Media;
using ClaudeUsageWidget.Core;
using Color = System.Windows.Media.Color;

namespace ClaudeUsageWidget.App.Views;

/// The colours of ONE theme, with the brushes made once and frozen. Two
/// instances exist for the life of the process; Theme.Current points at
/// whichever is on.
public sealed record Palette(
    Color Panel,
    Color Track,
    Color Text,
    Color Dim,
    Color Accent,
    Color Warning,
    Color Danger,
    Color Info)
{
    /// Theme.swift's alpha was 0.35 over a desktop blur; without blur the
    /// digits need more behind them.
    public const double PanelAlpha = 0.82;

    /// Header and BlockingNotice — almost opaque, as in Theme.swift.
    public const double OverlayAlpha = 0.92;

    public SolidColorBrush TrackBrush { get; } = Freeze(new SolidColorBrush(Track));
    public SolidColorBrush TextBrush { get; } = Freeze(new SolidColorBrush(Text));
    public SolidColorBrush DimBrush { get; } = Freeze(new SolidColorBrush(Dim));
    public SolidColorBrush WarningBrush { get; } = Freeze(new SolidColorBrush(Warning));
    public SolidColorBrush AccentBrush { get; } = Freeze(new SolidColorBrush(Accent));

    public SolidColorBrush PanelBackgroundBrush { get; } = Freeze(new SolidColorBrush(WithAlpha(Panel, PanelAlpha)));
    public SolidColorBrush OverlayBackgroundBrush { get; } = Freeze(new SolidColorBrush(WithAlpha(Panel, OverlayAlpha)));

    /// The panel colour with no transparency — the tray menu, which cannot be
    /// translucent, and the rename dialog.
    public SolidColorBrush PanelOpaqueBrush { get; } = Freeze(new SolidColorBrush(Panel));

    public Color ColorFor(ThresholdLevel level) => level switch
    {
        ThresholdLevel.Ok => Accent,
        ThresholdLevel.Warning => Warning,
        ThresholdLevel.Danger => Danger,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    public Color ColorFor(ServiceStatus status) => status switch
    {
        ServiceStatus.Operational => Accent,
        ServiceStatus.Degraded or ServiceStatus.PartialOutage => Warning,
        ServiceStatus.MajorOutage => Danger,
        ServiceStatus.Maintenance => Info,
        ServiceStatus.Unknown => Dim,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public SolidColorBrush PanelBrush(double alpha) => new(WithAlpha(Panel, alpha));

    /// Today's palette, unchanged: the port of Theme.swift.
    public static readonly Palette Dark = new(
        Panel: Color.FromRgb(0x1E, 0x22, 0x30),
        Track: Color.FromRgb(0x40, 0x45, 0x57),
        Text: Color.FromRgb(0xC7, 0xCC, 0xDE),
        Dim: Color.FromRgb(0x73, 0x78, 0x8C),
        Accent: Color.FromRgb(0xA6, 0xD1, 0x89),
        Warning: Color.FromRgb(0xE5, 0xC8, 0x90),
        Danger: Color.FromRgb(0xE7, 0x82, 0x84),
        Info: Color.FromRgb(0x8A, 0xB4, 0xE6));

    /// The same hues one step darker and more saturated: the dark pastels
    /// wash out on an off-white panel. Starting values from the spec; tuned
    /// on the preview PNGs.
    public static readonly Palette Light = new(
        Panel: Color.FromRgb(0xF3, 0xF5, 0xF9),
        Track: Color.FromRgb(0xD3, 0xD8, 0xE3),
        Text: Color.FromRgb(0x26, 0x2B, 0x3A),
        Dim: Color.FromRgb(0x6C, 0x73, 0x88),
        Accent: Color.FromRgb(0x4E, 0x9A, 0x4C),
        Warning: Color.FromRgb(0xC9, 0x96, 0x2A),
        Danger: Color.FromRgb(0xD2, 0x56, 0x5A),
        Info: Color.FromRgb(0x38, 0x77, 0xC8));

    public static Palette For(ThemeKind kind) => kind == ThemeKind.Light ? Light : Dark;

    private static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B);

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
```

- [ ] **Step 2: Rewrite the colour half of `Theme.cs`**

Replace everything in `Theme` from `public static readonly Color Panel` through `ColorFor(ServiceStatus)`, plus `PanelAlpha`, `OverlayAlpha`, `PanelBackgroundBrush`, `OverlayBackgroundBrush`, `PanelBrush` and the private `Freeze`, with:

```csharp
    /// The palette being drawn. Swapped by <see cref="Apply"/>; everything
    /// below that is a colour reads through here, so a consumer that asks on
    /// every paint is already theme-aware, and one that cached a brush
    /// subscribes to <see cref="Changed"/>.
    public static Palette Current { get; private set; } = Palette.Dark;

    public static ThemeKind Kind { get; private set; } = ThemeKind.Dark;

    /// Raised after Current has changed, on the thread Apply was called on —
    /// always the dispatcher; see App.ApplyTheme.
    public static event Action? Changed;

    public const string TextBrushKey = "Theme.TextBrush";
    public const string DimBrushKey = "Theme.DimBrush";
    public const string PanelBackgroundBrushKey = "Theme.PanelBackgroundBrush";
    public const string OverlayBackgroundBrushKey = "Theme.OverlayBackgroundBrush";

    /// Swaps the palette, republishes the XAML resource keys and tells the
    /// code consumers. Idempotent: the same kind twice is a no-op, so a
    /// system-theme event that changed nothing costs nothing.
    public static void Apply(ThemeKind kind)
    {
        if (kind == Kind && Current == Palette.For(kind)) return;

        Kind = kind;
        Current = Palette.For(kind);
        PublishResources();
        Changed?.Invoke();
    }

    /// The four brushes WidgetRootView.xaml binds with DynamicResource. Called
    /// at startup too, before the first window is created, so the keys exist.
    public static void PublishResources()
    {
        var resources = System.Windows.Application.Current?.Resources;
        if (resources is null) return;
        resources[TextBrushKey] = Current.TextBrush;
        resources[DimBrushKey] = Current.DimBrush;
        resources[PanelBackgroundBrushKey] = Current.PanelBackgroundBrush;
        resources[OverlayBackgroundBrushKey] = Current.OverlayBackgroundBrush;
    }

    public static Color Panel => Current.Panel;
    public static Color Track => Current.Track;
    public static Color Text => Current.Text;
    public static Color Dim => Current.Dim;
    public static Color Accent => Current.Accent;
    public static Color Warning => Current.Warning;
    public static Color Danger => Current.Danger;
    public static Color Info => Current.Info;

    public static SolidColorBrush TrackBrush => Current.TrackBrush;
    public static SolidColorBrush TextBrush => Current.TextBrush;
    public static SolidColorBrush DimBrush => Current.DimBrush;
    public static SolidColorBrush WarningBrush => Current.WarningBrush;
    public static SolidColorBrush PanelBackgroundBrush => Current.PanelBackgroundBrush;
    public static SolidColorBrush OverlayBackgroundBrush => Current.OverlayBackgroundBrush;

    public static Color ColorFor(ThresholdLevel level) => Current.ColorFor(level);
    public static Color ColorFor(ServiceStatus status) => Current.ColorFor(status);
    public static SolidColorBrush PanelBrush(double alpha) => Current.PanelBrush(alpha);

    public const double PanelAlpha = Palette.PanelAlpha;
    public const double OverlayAlpha = Palette.OverlayAlpha;
```

Keep `FontFamily`, the weights, the size functions, `Padding`, `Gap`, `CornerRadius` exactly as they are.

- [ ] **Step 3: Switch the XAML to DynamicResource**

In `WidgetRootView.xaml`, replace every brush reference (keep the `Weight` ones as `x:Static`, they do not change with theme):

- `{x:Static views:Theme.TextBrush}` → `{DynamicResource Theme.TextBrush}`
- `{x:Static views:Theme.DimBrush}` → `{DynamicResource Theme.DimBrush}`
- `{x:Static views:Theme.PanelBackgroundBrush}` → `{DynamicResource Theme.PanelBackgroundBrush}`
- `{x:Static views:Theme.OverlayBackgroundBrush}` → `{DynamicResource Theme.OverlayBackgroundBrush}`

Run: `grep -c 'x:Static views:Theme\.\(Text\|Dim\|Panel\|Overlay\)' src/ClaudeUsageWidget.App/Views/WidgetRootView.xaml` → expected `0`.

- [ ] **Step 4: Publish the keys before any window exists**

In `App.xaml.cs` `OnStartup`, right after `base.OnStartup(e);` and before `LayoutPreview.RunIfRequested();`, add:

```csharp
        // The XAML binds the palette with DynamicResource; the keys must exist
        // before the first view loads (the preview included).
        Theme.PublishResources();
```

`LayoutPreview` constructs views without a running App window but `Application.Current` exists by then, so the keys resolve.

- [ ] **Step 5: Build, run the preview, compare**

Run: `dotnet build -c Release -nologo -v q` → 0 warnings, 0 errors.
Run: `CUW_RENDER_PREVIEW=<scratch>/pv dotnet run --project src/ClaudeUsageWidget.App -c Release --no-build` and open `1-classic.png` — must look exactly as before this task (dark).

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeUsageWidget.App/Views/Palette.cs src/ClaudeUsageWidget.App/Views/Theme.cs src/ClaudeUsageWidget.App/Views/WidgetRootView.xaml src/ClaudeUsageWidget.App/App.xaml.cs
git commit -m "refactor(app): swappable Palette behind Theme; XAML reads it through DynamicResource"
```

---

### Task 3: SystemTheme and applying the setting

**Files:**
- Create: `src/ClaudeUsageWidget.App/Windows/SystemTheme.cs`
- Modify: `src/ClaudeUsageWidget.App/App.xaml.cs` (startup after `BuildAccounts();`; `OnExit`; a new handler)
- Modify: `src/ClaudeUsageWidget.App/Tray/TrayIcon.cs` (`TrayMenuState`, a Theme submenu, event)

**Interfaces:**
- Consumes: `ThemeChoices.Resolve`, `Theme.Apply`, `ThemeKind`.
- Produces: `static class SystemTheme { ThemeKind AppsKind { get; } ThemeKind TaskbarKind { get; } event Action? Changed; void Start(); void Stop(); }` — `Changed` is raised ON THE DISPATCHER (SystemTheme marshals itself).
- Produces: `TrayIcon.ThemeSelected : event Action<ThemeChoice>?`; `TrayMenuState` gains `ThemeChoice Theme` (last parameter).
- Produces: `App.ApplyTheme()` — the one place that resolves the setting against the system and calls `Theme.Apply`.

- [ ] **Step 1: Write `SystemTheme.cs`**

```csharp
using System.Windows;
using System.Windows.Threading;
using ClaudeUsageWidget.Core;
using Microsoft.Win32;

namespace ClaudeUsageWidget.App.Windows;

/// Windows' two light/dark switches, read from the registry, and a Changed
/// event when either flips.
///
/// AppsUseLightTheme is "Default app mode" — what the panel and the menu
/// follow under Theme = System. SystemUsesLightTheme is the taskbar's own
/// colour — what the tray icon's digits and the band's text follow ALWAYS,
/// because they are drawn onto the taskbar and the theme setting has no say
/// over its colour. A missing value is dark: Windows 10 before 1903 had no
/// light mode at all.
public static class SystemTheme
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static ThemeKind AppsKind { get; private set; } = Read("AppsUseLightTheme");
    public static ThemeKind TaskbarKind { get; private set; } = Read("SystemUsesLightTheme");

    /// Raised on the dispatcher after either kind changed.
    public static event Action? Changed;

    private static Dispatcher? _dispatcher;

    public static void Start(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static void Stop() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    /// UserPreferenceChanged arrives on a thread-pool thread. Everything that
    /// listens to Changed touches WPF, so the hop happens once, here.
    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;

        var apps = Read("AppsUseLightTheme");
        var taskbar = Read("SystemUsesLightTheme");
        if (apps == AppsKind && taskbar == TaskbarKind) return;

        _dispatcher?.BeginInvoke(() =>
        {
            AppsKind = apps;
            TaskbarKind = taskbar;
            Changed?.Invoke();
        });
    }

    private static ThemeKind Read(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(valueName) is int value && value != 0 ? ThemeKind.Light : ThemeKind.Dark;
        }
        catch
        {
            return ThemeKind.Dark;
        }
    }
}
```

- [ ] **Step 2: Tray menu — Theme submenu**

In `TrayIcon.cs`:

`TrayMenuState`: append `ThemeChoice Theme` after `BandView BandView`.

Fields, next to `_bandViewAccountsItem`:

```csharp
    private ToolStripMenuItem _themeSystemItem = null!;
    private ToolStripMenuItem _themeDarkItem = null!;
    private ToolStripMenuItem _themeLightItem = null!;
```

Event, after `BandViewSelected`:

```csharp
    /// Theme — System / Dark / Light.
    public event Action<ThemeChoice>? ThemeSelected;
```

In `SyncMenuState`, after the band-view checks:

```csharp
        _themeSystemItem.Checked = state.Theme == ThemeChoice.System;
        _themeDarkItem.Checked = state.Theme == ThemeChoice.Dark;
        _themeLightItem.Checked = state.Theme == ThemeChoice.Light;
```

In `BuildMenu`, right after `menu.Items.Add(_layoutMenu);`:

```csharp
        // A look setting, like Layout, so it sits beside it. System follows
        // Windows' "Default app mode" live.
        var themeMenu = new ToolStripMenuItem("Theme");
        _themeSystemItem = new ToolStripMenuItem("System", null, (_, _) => ThemeSelected?.Invoke(ThemeChoice.System))
        {
            ToolTipText = "Follow Windows' app theme.",
        };
        _themeDarkItem = new ToolStripMenuItem("Dark", null, (_, _) => ThemeSelected?.Invoke(ThemeChoice.Dark));
        _themeLightItem = new ToolStripMenuItem("Light", null, (_, _) => ThemeSelected?.Invoke(ThemeChoice.Light));
        themeMenu.DropDownItems.Add(_themeSystemItem);
        themeMenu.DropDownItems.Add(_themeDarkItem);
        themeMenu.DropDownItems.Add(_themeLightItem);
        menu.Items.Add(themeMenu);
```

- [ ] **Step 3: App wiring**

In `App.xaml.cs`:

After `_trayIcon.BandViewSelected += OnBandViewSelected;`:

```csharp
        _trayIcon.ThemeSelected += OnThemeSelected;
```

After `BuildAccounts();` (before `_statusStore = ...`):

```csharp
        // The palette before any window exists, and live tracking of Windows'
        // switch for as long as the app runs.
        ApplyTheme();
        SystemTheme.Changed += OnSystemThemeChanged;
        SystemTheme.Start(Dispatcher);
```

In `RefreshTrayMenuState`, append `data.Theme ?? ThemeChoice.System` as the last `TrayMenuState` argument.

New methods, after `OnBandViewSelected`:

```csharp
    /// The one place the theme setting meets the system: everything that
    /// draws reads Theme.Current, so this is a resolve and an Apply.
    private void ApplyTheme()
    {
        var choice = _settings!.Load().Theme;
        Theme.Apply(ThemeChoices.Resolve(choice, SystemTheme.AppsKind));
    }

    private void OnThemeSelected(ThemeChoice choice)
    {
        _settings!.Save(_settings.Load() with { Theme = choice });
        ApplyTheme();
        RefreshTrayMenuState();
    }

    /// Already on the dispatcher — SystemTheme hops before raising. The
    /// taskbar-mode consumers (icon digits, band text) redraw here too; the
    /// theme itself only changes when the setting is System.
    private void OnSystemThemeChanged()
    {
        ApplyTheme();
        RefreshTrayIcon();
        RenderTaskbarBand();
    }
```

In `OnExit`, next to `SystemEvents.PowerModeChanged -= OnPowerModeChanged;`:

```csharp
        SystemTheme.Stop();
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet build -c Release -nologo -v q` → 0 warnings. Tests still green (nothing in Core changed).

- [ ] **Step 5: Commit**

```bash
git add src/ClaudeUsageWidget.App/Windows/SystemTheme.cs src/ClaudeUsageWidget.App/App.xaml.cs src/ClaudeUsageWidget.App/Tray/TrayIcon.cs
git commit -m "feat(app): Theme submenu (System/Dark/Light) with live system tracking"
```

---

### Task 4: Repaint the panel on Theme.Changed

**Files:**
- Modify: `src/ClaudeUsageWidget.App/Views/WidgetRootView.xaml.cs` (constructor; a new `ApplyThemeColours()`; the places at lines ~206, ~309, ~333, ~724, ~894, ~901, ~1018 that assign `Theme.*Brush`)
- Modify: `src/ClaudeUsageWidget.App/Views/DialControl.cs`, `src/ClaudeUsageWidget.App/Views/StatusDialControl.cs` (constructors)
- Modify: `src/ClaudeUsageWidget.App/Windows/RenameWindow.cs` (constructor)

**Interfaces:**
- Consumes: `Theme.Changed`, `Theme.Current`.
- Produces: nothing new; the invariant that a theme change repaints without `RebuildLayout`.

- [ ] **Step 1: Dials repaint themselves**

`DialControl` and `StatusDialControl` both derive from `DialControlBase`. In `DialControlBase`'s constructor (or add one if there is none) subscribe with a weak-ish pattern that unsubscribes on unload:

```csharp
    protected DialControlBase()
    {
        Loaded += (_, _) => Theme.Changed += InvalidateVisual;
        Unloaded += (_, _) => Theme.Changed -= InvalidateVisual;
    }
```

The dials read `Theme.*` inside `OnRender` on every paint, so `InvalidateVisual` is the whole change.

- [ ] **Step 2: The view re-applies the brushes it assigned in code**

In `WidgetRootView.xaml.cs`, add:

```csharp
    /// Everything this class assigned from Theme by hand — toolbar glyphs,
    /// the header's lock, the edit-mode outlines, the account-name labels —
    /// reads the palette again. The XAML parts follow DynamicResource on
    /// their own, and the dials repaint themselves; nothing here moves.
    private void ApplyThemeColours()
    {
        foreach (var button in _toolbarButtons) button.Foreground = Theme.DimBrush;
        RefreshToolbar();       // re-applies the lock's warning colour
        RefreshHeaderLock();
        EyeButton.Foreground = Theme.DimBrush;
        PencilButton.Foreground = Theme.DimBrush;
        foreach (var label in DialGrid.Children.OfType<TextBlock>())
            label.Foreground = label.Tag is "plan" ? Theme.DimBrush : Theme.TextBrush;
        if (_editMode) RefreshEditChrome();
    }
```

Where the name and plan labels are created (`NewLabel(...)` at ~894 and ~901), set `Tag = "name"` on the name label and `Tag = "plan"` on the plan label so the loop above can tell them apart. `RefreshEditChrome` already rebuilds the outlines from `Theme.DimBrush`/`Theme.TextBrush` (~206, ~724) — confirm by reading those lines; if a brush is cached in a field, replace the field with a call to `Theme.*Brush` at use.

In the constructor, after `RefreshHeaderLock();`:

```csharp
        Loaded += (_, _) => Theme.Changed += ApplyThemeColours;
        Unloaded += (_, _) => Theme.Changed -= ApplyThemeColours;
```

- [ ] **Step 3: Rename dialog takes the palette**

In `RenameWindow`'s constructor, after `ShowInTaskbar = false;`:

```csharp
        Background = Views.Theme.Current.PanelOpaqueBrush;
        Foreground = Views.Theme.Current.TextBrush;
```

and give `_input` `Background = Views.Theme.Current.PanelOpaqueBrush, Foreground = Views.Theme.Current.TextBrush, BorderBrush = Views.Theme.Current.TrackBrush, CaretBrush = Views.Theme.Current.TextBrush`. The dialog is modal and short-lived, so no `Changed` subscription.

- [ ] **Step 4: Verify with the preview in both palettes**

Temporarily (do not commit) set `CUW_PREVIEW_THEME=light` handling — no: Task 7 adds the two-folder preview. For this task, verify by building (`0 warnings`) and by a code read: `grep -n 'Theme\.' src/ClaudeUsageWidget.App/Views/WidgetRootView.xaml.cs` — every brush assignment must be either inside `ApplyThemeColours`/`RefreshToolbar`/`RefreshHeaderLock`/`RefreshEditChrome`/`BuildGrid`, or a size/font (not a colour).

- [ ] **Step 5: Commit**

```bash
git add src/ClaudeUsageWidget.App/Views src/ClaudeUsageWidget.App/Windows/RenameWindow.cs
git commit -m "feat(app): panel repaints on theme change without a layout rebuild"
```

---

### Task 5: Tray icon and taskbar band follow the taskbar's mode

**Files:**
- Modify: `src/ClaudeUsageWidget.App/Tray/TrayIconRenderer.cs` (`Render` signature, `DrawRing`, `DrawDigits`)
- Modify: `src/ClaudeUsageWidget.App/Tray/TrayIcon.cs` (constructor's `SetIcon(TrayIconRenderer.Render(null))`)
- Modify: `src/ClaudeUsageWidget.App/App.xaml.cs` (`RefreshTrayIcon`'s call to `TrayIconRenderer.Render`)
- Modify: `src/ClaudeUsageWidget.App/Windows/TaskbarBandWindow.cs` (`TaskbarBandContent`: `Brushes.White` at ~1048 and ~1079, `ShadowBrush`)

**Interfaces:**
- Produces: `static Icon TrayIconRenderer.Render(string? valueText, ThemeKind taskbar)`; `TaskbarBandContent.SetMetrics(IReadOnlyList<BandEntry> entries, ThemeKind taskbar)`; `TaskbarBandWindow.Render(IReadOnlyList<BandEntry> entries, ThemeKind taskbar)`.

- [ ] **Step 1: Icon renderer takes the taskbar kind**

```csharp
    public static Icon Render(string? valueText, ThemeKind taskbar)
    {
        // White on Windows' dark taskbar, near-black on its light one — the
        // taskbar's own mode, never the widget's theme: the icon is drawn onto
        // the taskbar and readability there is the only thing that matters.
        var ink = taskbar == ThemeKind.Light ? Color.FromArgb(0x1B, 0x1B, 0x1B) : Color.White;
        ...
            if (digits is null) DrawRing(g, size, ink); else DrawDigits(g, size, digits, ink);
```

`DrawRing(Graphics g, Size size, Color ink)` uses `new Pen(ink, 2f)`; `DrawDigits(..., Color ink)` uses `using var brush = new SolidBrush(ink); g.DrawString(digits, font, brush, rect, format);`.

Callers: `TrayIcon` constructor → `TrayIconRenderer.Render(null, SystemTheme.TaskbarKind)`; `App.RefreshTrayIcon` → `TrayIconRenderer.Render(value, SystemTheme.TaskbarKind)`.

- [ ] **Step 2: Band text colour**

In `TaskbarBandContent`:

```csharp
    private ThemeKind _taskbar = ThemeKind.Dark;

    private static readonly SolidColorBrush DarkInk = Freeze(new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B)));
    private static readonly SolidColorBrush DarkShadow = Freeze(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)));
    private static readonly SolidColorBrush LightShadow = Freeze(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)));

    private SolidColorBrush Ink => _taskbar == ThemeKind.Light ? DarkInk : (SolidColorBrush)Brushes.White;
    private SolidColorBrush Shadow => _taskbar == ThemeKind.Light ? LightShadow : DarkShadow;

    public void SetMetrics(IReadOnlyList<BandEntry> entries, ThemeKind taskbar)
    {
        _entries = entries;
        _taskbar = taskbar;
        ...  // unchanged body
```

Replace `ShadowBrush` with `Shadow` and `Brushes.White` with `Ink` in `OnRender`; `MeasureOverride` keeps `Brushes.White` (it measures only). `TaskbarBandWindow.Render(IReadOnlyList<BandEntry> entries, ThemeKind taskbar)` passes it through to `_content.SetMetrics(entries, taskbar)`. `App.RenderTaskbarBand` calls `_bandWindow.Render(entries, SystemTheme.TaskbarKind)`.

- [ ] **Step 3: Build**

Run: `dotnet build -c Release -nologo -v q` → 0 warnings. `OnSystemThemeChanged` (Task 3) already re-renders both.

- [ ] **Step 4: Commit**

```bash
git add src/ClaudeUsageWidget.App/Tray/TrayIconRenderer.cs src/ClaudeUsageWidget.App/Tray/TrayIcon.cs src/ClaudeUsageWidget.App/App.xaml.cs src/ClaudeUsageWidget.App/Windows/TaskbarBandWindow.cs
git commit -m "feat(app): tray icon digits and band text follow the taskbar's light/dark mode"
```

---

### Task 6: The tray menu renderer, chrome and icons

**Files:**
- Create: `src/ClaudeUsageWidget.App/Tray/MenuGlyphs.cs`
- Create: `src/ClaudeUsageWidget.App/Tray/WidgetMenuRenderer.cs`
- Create: `src/ClaudeUsageWidget.App/Tray/MenuChrome.cs`
- Modify: `src/ClaudeUsageWidget.App/Windows/Win32.cs` (add `DwmSetWindowAttribute`)
- Modify: `src/ClaudeUsageWidget.App/Tray/TrayIcon.cs` (`BuildMenu`: renderer, chrome, icons; re-theme on `Theme.Changed`)

**Interfaces:**
- Produces: `static Bitmap MenuGlyphs.Render(string glyph, System.Drawing.Color ink, int sizePx)`; `sealed class WidgetMenuRenderer : ToolStripProfessionalRenderer` with `Palette Palette { get; set; }`; `static class MenuChrome { void Attach(ContextMenuStrip menu); void ApplyTheme(ContextMenuStrip menu, Palette palette); }`; `Win32.DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size)`.

- [ ] **Step 1: DWM import**

In `Win32.cs`, alongside the other imports:

```csharp
    public const int DwmwaWindowCornerPreference = 33;
    public const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
```

- [ ] **Step 2: `MenuGlyphs.cs`**

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ClaudeUsageWidget.App.Tray;

/// Segoe MDL2 glyphs as menu images. WinForms menus take bitmaps, not
/// fonts, so each glyph is drawn once per colour and size and cached.
internal static class MenuGlyphs
{
    private static readonly Dictionary<(string, int, int), Bitmap> Cache = [];

    public static Bitmap Render(string glyph, Color ink, int sizePx)
    {
        var key = (glyph, ink.ToArgb(), sizePx);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var bitmap = new Bitmap(sizePx, sizePx);
        using (var g = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe MDL2 Assets", sizePx * 0.72f, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(ink))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            g.DrawString(glyph, font, brush, new RectangleF(0, 0, sizePx, sizePx), format);
        }

        Cache[key] = bitmap;
        return bitmap;
    }
}
```

- [ ] **Step 3: `WidgetMenuRenderer.cs`**

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using ClaudeUsageWidget.App.Views;
using Color = System.Drawing.Color;

namespace ClaudeUsageWidget.App.Tray;

/// The tray menu in the panel's palette: flat background, a rounded
/// full-width hover, thin separators, a tick glyph instead of the boxed
/// check, and a 1 px border in the track colour in place of the system's.
internal sealed class WidgetMenuRenderer : ToolStripProfessionalRenderer
{
    private Palette _palette;

    public WidgetMenuRenderer(Palette palette) : base(new ColorTable(palette))
    {
        _palette = palette;
        RoundedEdges = false;
    }

    public Palette Palette
    {
        get => _palette;
        set => _palette = value;
    }

    private static Color Gdi(System.Windows.Media.Color c) => Color.FromArgb(c.R, c.G, c.B);

    private Color Background => Gdi(_palette.Panel);
    private Color Text => Gdi(_palette.Text);
    private Color Dim => Gdi(_palette.Dim);
    private Color Track => Gdi(_palette.Track);
    private Color Accent => Gdi(_palette.Accent);

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Same colour as the menu: no separate gutter strip.
        using var brush = new SolidBrush(Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Track);
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            base.OnRenderMenuItemBackground(e);
            return;
        }

        // 60 % track over the panel, inset by the menu's own padding, 4 px radius.
        var rect = new Rectangle(4, 0, e.Item.Width - 8, e.Item.Height);
        using var path = Rounded(rect, 4);
        using var brush = new SolidBrush(Color.FromArgb(153, Track));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : Dim;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item.Selected ? Text : Dim;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Track);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // A tick in the accent colour where the icon would be — no box.
        var glyph = MenuGlyphs.Render("", Accent, e.ImageRectangle.Height);
        var x = e.ImageRectangle.X + (e.ImageRectangle.Width - glyph.Width) / 2;
        var y = e.ImageRectangle.Y + (e.ImageRectangle.Height - glyph.Height) / 2;
        e.Graphics.DrawImage(glyph, x, y);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// Only the colours the base renderer still paints on its own (the
    /// gutter line and the check background) — everything visible is drawn
    /// by the overrides above.
    private sealed class ColorTable(Palette palette) : ProfessionalColorTable
    {
        private readonly Color _panel = Gdi(palette.Panel);
        public override Color ToolStripDropDownBackground => _panel;
        public override Color ImageMarginGradientBegin => _panel;
        public override Color ImageMarginGradientMiddle => _panel;
        public override Color ImageMarginGradientEnd => _panel;
        public override Color CheckBackground => _panel;
        public override Color CheckSelectedBackground => _panel;
        public override Color CheckPressedBackground => _panel;
        public override Color SeparatorDark => Gdi(palette.Track);
        public override Color SeparatorLight => Gdi(palette.Track);
        public override Color MenuBorder => Gdi(palette.Track);
    }
}
```

- [ ] **Step 4: `MenuChrome.cs`**

```csharp
using System.Drawing;
using ClaudeUsageWidget.App.Views;
using ClaudeUsageWidget.App.Windows;

namespace ClaudeUsageWidget.App.Tray;

/// Font, spacing and Windows 11 corners for the tray menu and every submenu.
internal static class MenuChrome
{
    /// The system menu font, not Consolas: the menu is chrome, not data.
    /// Segoe UI Variable exists on Windows 11 only; GDI falls back silently.
    private static readonly Font MenuFont = new(
        FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI",
        9f, GraphicsUnit.Point);

    /// Font, padding and the renderer, once, before the first Show — item
    /// heights are measured with whatever font is set at that moment.
    public static void Attach(ContextMenuStrip menu, WidgetMenuRenderer renderer)
    {
        menu.Renderer = renderer;
        menu.Font = MenuFont;
        menu.ShowImageMargin = true;
        menu.ShowCheckMargin = false;
        menu.Padding = new Padding(6, 6, 6, 6);
        menu.DropShadowEnabled = true;
        Style(menu.Items, renderer);
        menu.Opening += (_, _) => RoundCorners(menu);
    }

    private static void Style(ToolStripItemCollection items, WidgetMenuRenderer renderer)
    {
        foreach (var item in items.OfType<ToolStripMenuItem>())
        {
            item.Padding = new Padding(12, 6, 12, 6);
            item.ImageScaling = ToolStripItemImageScaling.None;
            if (!item.HasDropDownItems) continue;

            var drop = item.DropDown;
            drop.Renderer = renderer;
            drop.Font = MenuFont;
            drop.Padding = new Padding(6, 6, 6, 6);
            drop.Opening += (_, _) => RoundCorners(drop);
            Style(item.DropDownItems, renderer);
        }
    }

    /// DWMWA_WINDOW_CORNER_PREFERENCE exists from Windows 11 (22000). The
    /// handle exists only while the dropdown is open, hence at Opening. A
    /// failure is a square menu, nothing else.
    private static void RoundCorners(ToolStripDropDown drop)
    {
        if (Environment.OSVersion.Version.Build < 22000) return;
        try
        {
            var pref = Win32.DwmwcpRound;
            Win32.DwmSetWindowAttribute(drop.Handle, Win32.DwmwaWindowCornerPreference, ref pref, sizeof(int));
        }
        catch
        {
            // Square corners.
        }
    }
}
```

- [ ] **Step 5: Wire it in `TrayIcon`**

In `BuildMenu`, replace `var menu = new ContextMenuStrip();` with:

```csharp
        var menu = new ContextMenuStrip();
        _renderer = new WidgetMenuRenderer(Theme.Current);
```

Add the field `private WidgetMenuRenderer _renderer = null!;` and, at the END of `BuildMenu` before `return menu;`:

```csharp
        MenuChrome.Attach(menu, _renderer);
        ApplyMenuIcons();
        Theme.Changed += OnThemeChanged;
```

Icons — add to `TrayIcon`:

```csharp
    /// Top-level items only; submenus stay text. Dim glyphs at the menu's
    /// DPI, regenerated with the palette.
    private void ApplyMenuIcons()
    {
        var ink = System.Drawing.Color.FromArgb(Theme.Current.Dim.R, Theme.Current.Dim.G, Theme.Current.Dim.B);
        var px = (int)Math.Round(16 * Menu.DeviceDpi / 96.0);
        System.Drawing.Bitmap G(string glyph) => MenuGlyphs.Render(glyph, ink, px);

        var byText = Menu.Items.OfType<ToolStripMenuItem>().ToDictionary(i => i.Text ?? "", i => i);
        void Set(string text, string glyph) { if (byText.TryGetValue(text, out var item)) item.Image = G(glyph); }

        Set($"Claude Usage Widget v{CoreInfo.Version} — GitHub", "");
        Set("Report an Issue", "");
        Set("Refresh now", "");
        Set("Sign in to Claude.ai…", "");
        Set("Tray shows", "");
        Set("Accounts", "");
        Set("Layout", "");
        Set("Theme", "");
        Set("Show on desktop", "");
        Set("Taskbar band", "");
        Set("Band position", "");
        Set("Band shows", "");
        Set("Lock position", "");
        Set("Launch at login", "");
        Set("Quit Claude Usage Widget", "");
    }

    private void OnThemeChanged()
    {
        _renderer.Palette = Theme.Current;
        ApplyMenuIcons();
        Menu.Invalidate();
    }
```

Checked top-level items (`Show on desktop`, `Taskbar band`, `Lock position`, `Launch at login`) show the tick INSTEAD of the icon while checked — WinForms draws the check in the image slot; that is the intended "this one is on" language. Confirm `_showOnDesktopItem` etc. are created with `CheckOnClick = false` (they are toggled by the App) so nothing changes there.

`Dispose`: add `Theme.Changed -= OnThemeChanged;`.

- [ ] **Step 6: Build; visual check**

Run: `dotnet build -c Release -nologo -v q` → 0 warnings.

The menu cannot be rendered offscreen; the visual check is manual by the user after Task 8 (merge to personal + relaunch). Before that, a smoke check on this branch: the user's instance must be stopped first, then `dotnet run --project src/ClaudeUsageWidget.App -c Release --no-build`, right-click the tray icon, open each submenu, switch Theme to Light and back, quit. Restore the personal instance afterwards (see the release skill's step 7).

- [ ] **Step 7: Commit**

```bash
git add src/ClaudeUsageWidget.App/Tray src/ClaudeUsageWidget.App/Windows/Win32.cs
git commit -m "feat(tray): menu in the widget's palette with icons, tick glyphs and Win11 rounded corners"
```

---

### Task 7: Preview renders both palettes

**Files:**
- Modify: `src/ClaudeUsageWidget.App/LayoutPreview.cs` (`RunIfRequested`)

**Interfaces:**
- Consumes: `Theme.Apply(ThemeKind)`.

- [ ] **Step 1: Loop the whole render over both kinds**

In `RunIfRequested`, after `Directory.CreateDirectory(dir);`, wrap everything that follows (the cases loop and every `Render*` call) in:

```csharp
        foreach (var kind in new[] { ThemeKind.Dark, ThemeKind.Light })
        {
            Theme.Apply(kind);
            var themeDir = Path.Combine(dir, kind.ToString().ToLowerInvariant());
            Directory.CreateDirectory(themeDir);
            RenderAll(themeDir, accounts, snapshots, cases, now);
        }
```

Extract the existing body into `private static void RenderAll(string dir, AccountProfile[] accounts, Dictionary<string, UsageSnapshot?> snapshots, (string Name, WidgetLayout Layout, int Accounts)[] cases, DateTimeOffset now)` with no other change. `Theme.PublishResources()` (Task 2, Step 4) already ran in `OnStartup` before this.

- [ ] **Step 2: Render and look**

Run: `CUW_RENDER_PREVIEW=<scratch>/pv dotnet run --project src/ClaudeUsageWidget.App -c Release` then open `light/1-classic.png`, `light/3-grid-cell.png`, `light/edit-mode.png`, `light/status-line-degraded.png`. Check: text readable, rings visible against the panel, dimmed dials distinguishable from live ones, edit outlines visible. Adjust `Palette.Light` values if not (keep the spec's contrast targets) and re-render.

- [ ] **Step 3: Commit**

```bash
git add src/ClaudeUsageWidget.App/LayoutPreview.cs src/ClaudeUsageWidget.App/Views/Palette.cs
git commit -m "chore(preview): render every layout case in both palettes"
```

---

### Task 8: README, personal branch, release

**Files:**
- Modify: `README.md` (tray menu list: add **Theme**; a sentence on the menu's look; test count)

- [ ] **Step 1: README**

In the tray menu bullet list, after the **Layout** bullet:

```markdown
- **Theme** — **System** (follows Windows' app theme, the default), **Dark**
  or **Light**. The panel and this menu switch at once, no restart. The tray
  icon's digits and the taskbar band follow the taskbar's own colour instead,
  so they stay readable whatever the theme.
```

Update the `N tests across` line to the new count from `dotnet test`.

- [ ] **Step 2: Full verification**

Run: `dotnet build -c Release -nologo -v q` → 0 warnings; `dotnet test ...` → all green; `git grep -n -P '[А-Яа-яЁё]' -- 'src/*' 'Tests/*'` → nothing.

- [ ] **Step 3: Commit and hand over**

```bash
git add README.md
git commit -m "docs: Theme submenu and the redesigned tray menu"
```

Then the user looks at it live: merge `main` into `personal` (the bot submenu on that branch gets `Set("Telegram bot", "")` added to `ApplyMenuIcons` there), Debug build, relaunch. Push and the `release` skill only after the user's OK.

---

## Self-review

- **Spec coverage.** Settings + resolver → T1. Palette values and swap mechanism (DynamicResource, Changed, cached bitmaps) → T2, T4, T6. System theme reading and live tracking → T3. Taskbar-mode following for icon and band → T5. Menu colours, spacing, checks, icons, corners, font → T6. Preview in both palettes → T7. Rename dialog palette → T4. Sign-in paste box: `LoginWindow` has no hard-coded colours today (grep found none), so it inherits system chrome; listed in the spec as palette-only and left for T4's pattern if a colour is ever added — no task needed. README → T8. Manual checklist → T6 step 6 and T8.
- **Placeholders.** None; every step has code or an exact command.
- **Type consistency.** `ThemeKind`/`ThemeChoice` (T1) used by T2–T7 unchanged; `Palette.PanelOpaqueBrush` (T2) used in T4; `WidgetMenuRenderer.Palette` setter (T6) used in `OnThemeChanged`; `TaskbarBandWindow.Render(entries, taskbar)` (T5) matches `App.RenderTaskbarBand`; `SystemTheme.Start(Dispatcher)` (T3) matches the call in `OnStartup`.
