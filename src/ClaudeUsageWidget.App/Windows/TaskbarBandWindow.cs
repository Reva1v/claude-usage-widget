using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageWidget.App.Views;
using ClaudeUsageWidget.Core;
// UseWindowsForms makes System.Drawing globally visible (see
// ClaudeUsageWidget.App.GlobalUsings.g.cs) — Point/Color/Brushes/Size
// exist there too under the same name (the same trick as in
// Windows/DesktopWidgetWindow.cs and Views/*.cs).
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;

namespace ClaudeUsageWidget.App.Windows;

/// <summary>
/// The taskbar band: a small window to the left of the tray overflow area
/// (or at the left edge of the taskbar — see <see cref="SetPosition"/>),
/// drawing "label above value" columns like the macOS menu bar.
///
/// The technique is a top-level owner window (owned window) of the taskbar,
/// NOT a child (WS_CHILD) window and not a bare Topmost. The first version
/// of this task tried both other options:
/// - SetParent-embedding as WS_CHILD into Shell_TrayWnd (the TrafficMonitor
///   technique) technically succeeds (non-zero return, exact positioning),
///   but live testing on Windows 11 showed that the taskbar's Mica
///   compositing makes the content of such a child window unreadable —
///   pixel measurements (round 2, task-17-report.md) showed that neither
///   reordering WS_CHILD/SetParent, nor WS_EX_LAYERED, nor Z-order fix
///   this.
/// - A bare Topmost overlay without an owner renders sharply (the same
///   WS_EX_LAYERED worked there), but periodically fell back behind the
///   shell's Mica layer for no visible reason (round 4) and had no
///   mechanism to stay above the taskbar during shell activity (context
///   menus, focus switching).
/// Owned window (SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, Shell_TrayWnd))
/// solves both: the window stays a normal top-level window (no child
/// compositing — real WPF transparency, AllowsTransparency=true, can be
/// used, with no black rectangles and no falling behind), and Windows
/// itself maintains the invariant "an owned window is always above its
/// owner". HWND_TOPMOST on top of that is needed only to rise above ALL
/// regular windows in general (not just specifically above the taskbar) —
/// and it is set EXACTLY ONCE, on (re)docking, not on every repositioning
/// tick: periodically re-applying HWND_TOPMOST drags the whole
/// "owner+owned" cluster (i.e. the taskbar itself) above any shell context
/// menu open at that moment and clips it — documented behavior,
/// independently rediscovered by NetSpeedTray (their issue #200: 23 out of
/// 23 attempts with periodic SetWindowPos(HWND_TOPMOST, ...) clipped the
/// menu, 0 out of 23 without it).
/// </summary>
public sealed class TaskbarBandWindow : Window
{
    private const string TrayClassName = "Shell_TrayWnd";
    private const string TrayNotifyClassName = "TrayNotifyWnd";

    /// The band's gap from the tray overflow area — task-17-brief.md: "sit to
    /// the left of it, offset by the window's width plus an 8 px gap".
    private const double GapDip = 8;

    private const double OuterPaddingDip = 8;

    /// The offset from the taskbar's left edge for BandPosition="left" — live
    /// testing (task-17-report.md, round 5) showed an extra ~130px offset
    /// instead of the expected "flush against the edge": it used to be 160
    /// DIP, inherited from an earlier idea of sitting right after
    /// Start/Search/Task View/Widgets when the taskbar alignment is "Left".
    /// The user meant literally the left edge — the same gap as GapDip for
    /// the "tray" position (8 px from TrayNotifyWnd there), just on the other
    /// side of the screen, not an offset trailing the hidden system buttons.
    private const double LeftPositionOffsetDip = GapDip;

    /// The default Windows 10/11 taskbar height at 100% scale — used only as
    /// a placeholder value until the first call to <see cref="Reposition"/>
    /// (which is called earlier than the window becomes visible, so the real
    /// size is usually substituted in before it is even shown).
    private const double DefaultHeightDip = 40;

    /// Fractions of the taskbar's width at which the visibility probe
    /// (<see cref="IsTaskbarObscured"/>) samples. Spread out along the band:
    /// one point may be legitimately covered (the volume flyout above the
    /// clock, our own band on the left or by the tray) — only a window that
    /// truly lies OVER the entire taskbar strip covers all three at once.
    private static readonly double[] ProbeFractions = [0.35, 0.55, 0.8];

    private readonly TaskbarBandContent _content;
    private readonly DispatcherTimer _repositionTimer;

    /// The delegate for SetWinEventHook — MUST live in a field, not be a
    /// temporary value at the call site: native code holds only a function
    /// pointer, with no managed reference, so without this field the GC is
    /// free to collect the delegate at any moment between installing the hook
    /// and the first event — the classic silent P/Invoke trap (the callback
    /// is invoked through already-freed memory → a crash, or silently broken
    /// event delivery).
    private readonly Win32.WinEventDelegate _winEventProc;

    /// EVENT_SYSTEM_FOREGROUND — the active window changed.
    private nint _foregroundHook;

    /// EVENT_OBJECT_LOCATIONCHANGE — the foreground window moved/resized
    /// (catches F11/borderless fullscreen without the active window changing).
    private nint _locationHook;

    /// EVENT_SYSTEM_MINIMIZESTART..MINIMIZEEND — windows being
    /// minimized/restored: after "Minimize" the foreground change does not
    /// always arrive, or not right away, and the probe must recompute
    /// immediately (a live bug: the band flickering on the "Minimize"
    /// button).
    private nint _minimizeHook;

    /// EVENT_OBJECT_REORDER — the moment the z-order gets reshuffled: the
    /// only signal that arrives BEFORE the eye would see the band under the
    /// raised taskbar (the foreground event arrives only afterward).
    private nint _reorderHook;

    /// Debounce for EVENT_OBJECT_LOCATIONCHANGE — it fires in bursts during
    /// ordinary window dragging/animation, not only when entering/leaving
    /// fullscreen; we actually re-check fullscreen state only after ~200 ms
    /// of silence since the last such event.
    private readonly DispatcherTimer _locationDebounceTimer;

    /// The hysteresis for applying visibility itself — separate from the
    /// LOCATIONCHANGE debounce above (that one decides WHEN to re-check, this
    /// one decides whether to already ACT on the check's result). Live testing
    /// (round 7) showed that a fleeting foreground-window change (a random
    /// alt-tab, a popup over a game for a fraction of a second) would
    /// otherwise make the band flicker back and forth — the "flicker as
    /// little as possible" goal requires not applying Hide()/Show()
    /// immediately on every raw determination, but only once the desired
    /// state has held stable for ~300 ms. See RequestFullscreenVisibility.
    private readonly DispatcherTimer _visibilityStabilityTimer;

    /// The visibility hysteresis, per direction. Showing is fast: the band is
    /// already invisible, so it should be returned to the user as early as
    /// possible. Hiding is deliberately slow: short-lived fullscreen overlays
    /// (ShareX covers the taskbar with an invisible window for ~0.3-1s on
    /// every other window's maximize/restore) live noticeably shorter than
    /// this threshold and should never live long enough to trigger a real
    /// Hide() at all; genuine fullscreen (a game) lasts minutes, and one extra
    /// second of the band on top of it is an acceptable price for a complete
    /// absence of flicker.
    private const int ShowStabilityMs = 150;
    private const int HideStabilityMs = 1200;

    /// Hiding when the taskbar is covered by the ACTIVE window (see
    /// TaskbarCover.ObscuredByForeground): the verdict is reliable — the user
    /// entered fullscreen themselves — so only a token quarantine is needed,
    /// against debounce in the frames of the transition itself. 150, not 0:
    /// immediate application on the very first verdict would catch
    /// intermediate frames of the maximize animation.
    private const int FastHideStabilityMs = 150;

    /// The state currently awaiting application via
    /// _visibilityStabilityTimer — null if nothing is pending (the last
    /// requested state already matches the applied one).
    private bool? _pendingHiddenForFullscreen;

    /// The fullscreen state has not been determined even once yet for this
    /// Dock() — see the why-comment in RequestFullscreenVisibility: the very
    /// first determination is applied immediately, bypassing the 300ms
    /// hysteresis (which protects an ALREADY shown band from flicker, rather
    /// than delaying the single, not-yet-visible-to-anyone initial state
    /// setup).
    private bool _fullscreenStateEstablished;

    /// "tray" (the default) or "left" — see <see cref="SetPosition"/>.
    private string _position = "tray";

    /// The band is hidden because of a fullscreen application over its
    /// monitor — see IsTaskbarObscured()/RepositionCore(). Separate from
    /// regular Visibility: we don't want the ordinary show/hide logic to
    /// confuse this state with "the band was turned off by the user" — this
    /// is just a temporary suspension of display.
    private bool _hiddenForFullscreen;

    /// The last geometry actually applied via SetWindowPos in
    /// RepositionCore() — see the why-comment there: used to skip SetWindowPos
    /// on ticks where nothing changed (perf). int.MinValue — deliberately
    /// unlike any real x/y/size, so the very first call always goes through
    /// without a special branch for "there was none yet".
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;
    private int _lastWidthPx = int.MinValue;
    private int _lastHeightPx = int.MinValue;

    /// <summary>
    /// This window's native HWND was destroyed other than through our own
    /// Detach()/Close(). An owned window (unlike the previous WS_CHILD
    /// variant) is not destroyed automatically together with its owner —
    /// Windows cascades destruction only to actual CHILDREN (WS_CHILD), not
    /// owned windows, so this scenario became noticeably less likely than in
    /// the task's first rounds, but not impossible (WPF is capable of closing
    /// a Window for other reasons too) — left in as a defensive backstop. WPF
    /// does not support calling Show()/EnsureHandle() again on a Window whose
    /// HWND disappeared this way — the only working recovery path is a new
    /// TaskbarBandWindow instance, so this is just a signal here; recreation
    /// is the owner's responsibility (App.xaml.cs).
    /// </summary>
    public event Action? Lost;

    public TaskbarBandWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;

        // Real WPF transparency: the window no longer moves into a foreign
        // process (neither SetParent nor WS_CHILD), so the cross-process
        // layered-window bug ("draws a black rectangle") that made the task's
        // first version keep the window opaque does not apply here — this is
        // an ordinary top-level window, just with an owner.
        // AllowsTransparency must be set before the HWND is created (i.e.
        // here, in the constructor, not later).
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        _content = new TaskbarBandContent();
        var root = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(OuterPaddingDip, 0, OuterPaddingDip, 0),
            Child = _content,
        };
        Content = root;

        Height = DefaultHeightDip;

        _repositionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _repositionTimer.Tick += (_, _) => Reposition();

        _winEventProc = OnWinEvent;
        // 100ms: LOCATIONCHANGE debounce needs to be smoothed out (events pour
        // in on every frame of window dragging), but entering/leaving
        // fullscreen for the same window (a YouTube player) is detected
        // PRECISELY through this path — every extra 100ms here directly
        // lengthens the visible delay before the band disappears/appears.
        _locationDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _locationDebounceTimer.Tick += (_, _) =>
        {
            _locationDebounceTimer.Stop();
            Reposition();
        };

        _visibilityStabilityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ShowStabilityMs) };
        _visibilityStabilityTimer.Tick += (_, _) =>
        {
            _visibilityStabilityTimer.Stop();
            if (_pendingHiddenForFullscreen is not { } hidden) return;
            _pendingHiddenForFullscreen = null;
            try
            {
                // A fresh probe AT THE MOMENT the timer expires, not the verdict at
                // the moment it started. A live example from this machine: ShareX, on
                // every restore/maximize of some other window, lays an invisible
                // WinForms window over the whole taskbar strip for a fraction of a
                // second — the "covered" verdict is honest at the moment it is taken,
                // but by the time the timer expires the overlay has already
                // disappeared, and there are no events that would restart the probe in
                // that interval (a window being destroyed does not arrive as
                // LOCATIONCHANGE). Applying a stale verdict would make the band
                // flicker for no reason; if it isn't confirmed, the transition is
                // simply cancelled, and the next one starts with a clean slate.
                var ownHwnd = new WindowInteropHelper(this).Handle;
                var tray = Win32.FindWindow(TrayClassName, null);
                if (ownHwnd == nint.Zero || tray == nint.Zero) return;
                var fresh = ProbeTaskbarCover(ownHwnd, tray) != TaskbarCover.Visible;
                if (fresh != hidden)
                {
                    Diag($"stability expired: verdict flipped ({hidden} -> {fresh}), transition cancelled");
                    return;
                }
                ApplyFullscreenVisibility(hidden);
            }
            catch (InvalidOperationException)
            {
                // The same zombie scenario as in Reposition()/Detach() (see
                // their comments): unlike a call from RepositionCore(), this Tick
                // doesn't go through Reposition()'s try/catch — the window could
                // have been closed while the transition was waiting out the 300ms
                // hysteresis, and an unhandled InvalidOperationException here would
                // bring down the whole process.
                _repositionTimer.Stop();
                UnhookFullscreenEvents();
                Lost?.Invoke();
            }
        };
    }

    /// <summary>
    /// Shows the band and (re)docks it to the taskbar — the owner
    /// (GWLP_HWNDPARENT) is set inside Reposition()/RepositionCore(), which
    /// itself detects "owner is wrong/not set" as a special case of a stale
    /// state (see its comment) — here it is enough to create the HWND and
    /// call it once. The only entry point for App.xaml.cs.
    /// </summary>
    public void Dock()
    {
        new WindowInteropHelper(this).EnsureHandle();
        _hiddenForFullscreen = false;
        _fullscreenStateEstablished = false;

        HookFullscreenEvents();
        Reposition();

        // Conditionally, not unconditionally: the Reposition() call above
        // could already have synchronously hidden the window (the very first
        // fullscreen-state determination is applied immediately — see
        // RequestFullscreenVisibility), and an unconditional Show() here,
        // before round 7, defeated exactly this case — a window just hidden as
        // fullscreen would immediately be shown again.
        if (!_hiddenForFullscreen) Show();
        _repositionTimer.Start();
    }

    /// <summary>
    /// Changes "tray"/"left" and repositions immediately, without waiting
    /// for the next tick — a noticeable delay when switching live from the
    /// tray menu would look like a bug. Does not touch the owner/HWND_TOPMOST:
    /// this is only a change of X, the ordinary (not "stale") path in
    /// RepositionCore().
    /// </summary>
    public void SetPosition(string position)
    {
        if (_position == position) return;
        _position = position;
        if (_repositionTimer.IsEnabled) Reposition();
    }

    /// <summary>
    /// Clears the owner and hides the window. Idempotent: safe to call
    /// even if the window has never been shown yet.
    /// </summary>
    public void Detach()
    {
        _repositionTimer.Stop();
        UnhookFullscreenEvents();

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero || !Win32.IsWindow(hwnd))
        {
            // Zombie — the same case as in RepositionCore(): touching
            // Visibility/Hide() further on a closed WPF Window would mean
            // catching an InvalidOperationException. The timer was already
            // stopped on the line above, so Reposition() will no longer raise
            // Lost by itself — we signal from here, otherwise the owner
            // (App.xaml.cs) would be left with a dead instance that would crash
            // on the next Dock().
            Lost?.Invoke();
            return;
        }

        Win32.SetWindowLongPtr(hwnd, Win32.GwlpHwndParent, nint.Zero);

        try
        {
            Hide();
        }
        catch (InvalidOperationException)
        {
            // A defensive backstop for the race between the IsWindow check
            // above and the Hide() call below (WPF managed to mark the Window
            // closed in exactly that interval) — same conclusion: the instance is
            // dead.
            Lost?.Invoke();
        }
    }

    /// <summary>Redraws the columns from fresh data — one
    /// <see cref="BandEntry"/> per account, from BandText.Entries.</summary>
    public void Render(IReadOnlyList<BandEntry> entries, ThemeKind taskbar)
    {
        _content.SetMetrics(entries, taskbar);

        // Fresh data almost always changes a column's WIDTH — "—" becomes
        // "0% 1d 3h" — and the window's width is set only by RepositionCore,
        // which until now ran on the 5-second timer alone. Between the data
        // arriving and the next tick the band sat with content wider than its
        // own window, and the rightmost column was cut (the user, 2026-08-26,
        // «low cut off»). Only ask for it once the HWND exists: before
        // EnsureHandle() there is nothing to measure in and nowhere to place
        // (see the comment below). RepositionCore recomputes DesiredSize
        // itself and short-circuits when the geometry has not moved a pixel,
        // so calling it per render costs nothing.
        if (new WindowInteropHelper(this).Handle != nint.Zero) Reposition();

        // Content measurement is NOT done here (it used to be — right after
        // SetMetrics) — it has been deliberately moved entirely into
        // RepositionCore() (see its comment about the "F"/"5" bug): App.
        // SetTaskbarBandVisible calls Render() BEFORE Dock()/EnsureHandle(),
        // i.e. at a moment when the window may still have no HWND and no
        // PresentationSource — DialText.PixelsPerDip(_content) at that
        // moment does not know the real DPI of the monitor the window will
        // end up on, and a deliberate synchronous Measure() here would
        // compute the width using the wrong DPI. RepositionCore() always
        // runs after EnsureHandle() and re-measures the content itself right
        // before reading DesiredSize — the only place where measurement is
        // actually needed.
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        // Not activated and does not show up in alt-tab. We do NOT set
        // WS_EX_LAYERED manually and do NOT call SetLayeredWindowAttributes:
        // AllowsTransparency=true already made the window layered by itself
        // (this is exactly how WPF implements per-pixel transparency on
        // Win32), and its own UpdateLayeredWindow pipeline breaks if
        // LWA_ALPHA is additionally called on top of it — two independent
        // mechanisms for managing the same layered surface conflict.
        var exStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GwlExStyle);
        exStyle |= Win32.WsExNoActivate | Win32.WsExToolWindow;
        Win32.SetWindowLongPtr(hwnd, Win32.GwlExStyle, (nint)exStyle);

        var source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("HwndSource is not available after SourceInitialized.");
        source.AddHook(WndProc);
    }

    /// <summary>WM_DPICHANGED — reposition immediately, without waiting
    /// for the next timer tick.</summary>
    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32.WmDpiChanged) Reposition();
        return nint.Zero;
    }

    /// <summary>
    /// Installs system-wide (idProcess=0/idThread=0 — the whole machine, not
    /// just our process) WinEvent hooks so we react to entering/leaving
    /// fullscreen instantly, instead of waiting for the next 5-second tick —
    /// live testing (task-17-report.md, round 6) showed a noticeable delay of
    /// up to 5 s in both directions with pure tick-based detection.
    /// EVENT_SYSTEM_FOREGROUND — the active window changed (a regular
    /// Alt-Tab/launching a game). EVENT_OBJECT_LOCATIONCHANGE — a window
    /// changed size/position WITHOUT the active window changing (F11 in the
    /// same window, switching exclusive/borderless fullscreen for the same
    /// game) — the only way to catch this case, since the foreground window
    /// does not change. WINEVENT_OUTOFCONTEXT — without injecting a DLL into
    /// other processes, events are delivered to the thread that installed the
    /// hook (our UI thread) through the same message pump that drives the WPF
    /// Dispatcher. Idempotent: Dock() can be called again (rare, after
    /// Detach) — the hook is installed only if it isn't already installed.
    /// </summary>
    private void HookFullscreenEvents()
    {
        if (_foregroundHook != nint.Zero) return;

        _foregroundHook = Win32.SetWinEventHook(
            Win32.EventSystemForeground, Win32.EventSystemForeground,
            nint.Zero, _winEventProc, 0, 0, Win32.WinEventOutOfContext);
        _locationHook = Win32.SetWinEventHook(
            Win32.EventObjectLocationChange, Win32.EventObjectLocationChange,
            nint.Zero, _winEventProc, 0, 0, Win32.WinEventOutOfContext);
        _minimizeHook = Win32.SetWinEventHook(
            Win32.EventSystemMinimizeStart, Win32.EventSystemMinimizeEnd,
            nint.Zero, _winEventProc, 0, 0, Win32.WinEventOutOfContext);
        _reorderHook = Win32.SetWinEventHook(
            Win32.EventObjectReorder, Win32.EventObjectReorder,
            nint.Zero, _winEventProc, 0, 0, Win32.WinEventOutOfContext);
    }

    /// <summary>Removes both hooks (if installed) and stops both auxiliary
    /// timers (the LOCATIONCHANGE debounce and the visibility hysteresis) —
    /// called from Detach() and from every place where the instance is
    /// declared dead (see Lost), so as not to leave behind a hook delivering
    /// events to a delegate that nobody needs any more, and not to apply
    /// deferred visibility on an instance that is no longer current.</summary>
    private void UnhookFullscreenEvents()
    {
        if (_foregroundHook != nint.Zero)
        {
            Win32.UnhookWinEvent(_foregroundHook);
            _foregroundHook = nint.Zero;
        }
        if (_locationHook != nint.Zero)
        {
            Win32.UnhookWinEvent(_locationHook);
            _locationHook = nint.Zero;
        }
        if (_minimizeHook != nint.Zero)
        {
            Win32.UnhookWinEvent(_minimizeHook);
            _minimizeHook = nint.Zero;
        }
        if (_reorderHook != nint.Zero)
        {
            Win32.UnhookWinEvent(_reorderHook);
            _reorderHook = nint.Zero;
        }
        _locationDebounceTimer.Stop();
        _visibilityStabilityTimer.Stop();
        _pendingHiddenForFullscreen = null;
    }

    /// <summary>Requests the desired visibility based on a fresh fullscreen
    /// detection — does not apply it directly (except the very first time,
    /// see below):
    /// - if <paramref name="hidden"/> already matches the applied state
    ///   (<see cref="_hiddenForFullscreen"/>), cancels any pending transition
    ///   and does nothing — "immediate application is fine when desired ==
    ///   current".
    /// - if this is a NEW transition (not the one already pending
    ///   application), (re)starts the stability timer
    ///   (ShowStabilityMs/HideStabilityMs — see their comment about the
    ///   asymmetry) — the actual Hide()/Show() will happen only if a FRESH
    ///   probe at the moment the timer expires confirms the same desired
    ///   state (see the Tick in the constructor). A fleeting foreground-window
    ///   change or a short-lived overlay over the taskbar are therefore
    ///   filtered out here and never reach the point of making the band
    ///   flicker.
    /// - the very first state determination for this Dock()
    ///   (<see cref="_fullscreenStateEstablished"/> still false) is applied
    ///   immediately, bypassing the hysteresis: that protects an already
    ///   shown band from flickering between two states, not the single,
    ///   not-yet-visible-to-anyone setting of the initial state.
    /// </summary>
    private void RequestFullscreenVisibility(bool hidden, bool coveredByForeground = false)
    {
        if (!_fullscreenStateEstablished)
        {
            _fullscreenStateEstablished = true;
            ApplyFullscreenVisibility(hidden);
            return;
        }

        if (hidden == _hiddenForFullscreen)
        {
            _pendingHiddenForFullscreen = null;
            _visibilityStabilityTimer.Stop();
            return;
        }

        if (_pendingHiddenForFullscreen == hidden) return; // already waiting for exactly this transition

        _pendingHiddenForFullscreen = hidden;
        _visibilityStabilityTimer.Stop();
        // Asymmetry of directions: hiding is an "expensive" decision (the
        // user loses sight of the band), fleeting service overlays must be
        // filtered out entirely — a long hold-off; but if the taskbar was
        // covered by the ACTIVE window, the user entered fullscreen
        // themselves (a YouTube player, a game) — hiding needs to happen
        // almost immediately, a long delay here reads as lag. Showing it back
        // is harmless — we keep that fast always.
        var delay = !hidden ? ShowStabilityMs
            : coveredByForeground ? FastHideStabilityMs
            : HideStabilityMs;
        _visibilityStabilityTimer.Interval = TimeSpan.FromMilliseconds(delay);
        _visibilityStabilityTimer.Start();
    }

    /// <summary>The actual Hide()/Show() — the only place that calls them
    /// for a fullscreen-related reason (see the calls from
    /// RequestFullscreenVisibility and from the hysteresis timer in the
    /// constructor).</summary>
    private void ApplyFullscreenVisibility(bool hidden)
    {
        if (hidden == _hiddenForFullscreen) return;
        Diag($"apply: hiddenForFullscreen {_hiddenForFullscreen} -> {hidden}");
        _hiddenForFullscreen = hidden;
        if (hidden) Hide(); else Show();
    }

    /// <summary>The system WinEvent hook callback — invoked by native code
    /// from inside our own UI thread's message pump, but we don't rely on
    /// this as a documented guarantee: we marshal through
    /// Dispatcher.BeginInvoke, rather than doing anything substantial right
    /// in the frame of a low-level system callback. BeginInvoke, not Invoke —
    /// the callback must return control to the OS as fast as
    /// possible.</summary>
    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint idEventTime)
    {
        if (eventType is Win32.EventSystemForeground
            or Win32.EventSystemMinimizeStart
            or Win32.EventSystemMinimizeEnd)
        {
            Dispatcher.BeginInvoke(Reposition);
            return;
        }

        if (eventType == Win32.EventObjectReorder)
        {
            // Not a full Reposition: a z-order reshuffle does not change the
            // geometry, and REORDER events fire noticeably more often than the
            // others — a cheap burial check is enough (a dozen or so GetWindow
            // calls per invocation). This is exactly the path that removes the
            // last visible frame of the band under the taskbar: the foreground
            // event arrives only after the reshuffle, while this one arrives at
            // the very moment of it. SYNCHRONOUSLY, since we are already on the
            // UI thread anyway (the normal case for an OUTOFCONTEXT hook): a
            // BeginInvoke queue added a lag of a frame or two, and it was exactly
            // that frame the user still had time to notice.
            if (Dispatcher.CheckAccess()) CheckBuriedNow();
            else Dispatcher.BeginInvoke(CheckBuriedNow);
            return;
        }

        if (eventType != Win32.EventObjectLocationChange) return;

        // OBJID_WINDOW/CHILDID_SELF — the event is about the window as a
        // whole, not about one of its internal controls (the hook is
        // system-wide and fires events for all windows in all processes —
        // without this filter this would drown in noise).
        if (idObject != Win32.ObjIdWindow || idChild != Win32.ChildIdSelf) return;

        Dispatcher.BeginInvoke(() =>
        {
            // We only care about the CURRENTLY active window — the hook is
            // system-wide, the event could have arrived about any window
            // anywhere.
            if (hwnd != Win32.GetForegroundWindow()) return;

            // Debounce: during ordinary window dragging/animation, dozens of
            // such events fire — we push the timer out to 200 ms from each new
            // one, and actually check fullscreen state only once the window has
            // been still for ~200 ms.
            _locationDebounceTimer.Stop();
            _locationDebounceTimer.Start();
        });
    }

    /// <summary>Recomputes position/size and (if needed) re-docks to the
    /// taskbar — looks up the taskbar and the tray area freshly on every
    /// call (does not cache handles for the lookup itself): explorer.exe can
    /// recreate Shell_TrayWnd, and idle time/adding tray icons move
    /// TrayNotifyWnd — task-17-brief.md: "the taskbar rebuilds itself".
    /// First — a self-check for our own HWND being destroyed, then — a
    /// guard for a fullscreen application over our monitor, then — a cheap
    /// check of "is our owner still the current Shell_TrayWnd" (see the
    /// comment on RepositionCore below).</summary>
    private void Reposition()
    {
        try
        {
            RepositionCore();
        }
        catch (InvalidOperationException)
        {
            // WPF itself already considers this Window closed — some operation
            // below (for example Show() after leaving fullscreen) threw an
            // InvalidOperationException("...after the window was closed"). The
            // instance is irreversibly dead — we signal outward instead of
            // trying to continue.
            _repositionTimer.Stop();
            UnhookFullscreenEvents();
            Lost?.Invoke();
        }
    }

    private void RepositionCore()
    {
        var ownHwnd = new WindowInteropHelper(this).Handle;
        if (ownHwnd == nint.Zero || !Win32.IsWindow(ownHwnd))
        {
            // Our own HWND was destroyed externally — see the doc-comment on
            // Lost. ownHwnd == Zero also lands here: EnsureHandle() was never
            // called at all (shouldn't happen once the timer is already ticking
            // — but it's safer to treat this the same as "nothing to recover"
            // than to crash a bit further down on SetWindowPos with a null
            // hwnd).
            _repositionTimer.Stop();
            UnhookFullscreenEvents();
            Lost?.Invoke();
            return;
        }

        var tray = Win32.FindWindow(TrayClassName, null);
        if (tray == nint.Zero) return; // the taskbar is temporarily unavailable (explorer between exiting and starting) — keep the previous geometry and visibility until the next tick

        // The band's visibility = the taskbar's actual visibility, and
        // nothing more. Three generations of "is the foreground window
        // fullscreen?" heuristics (rect vs rcMonitor, SW_SHOWMAXIMIZED,
        // comparing monitors — round 6-8) kept catching false positives on
        // real applications (maximized JetBrains, Toggle Full Screen Mode,
        // the "Minimize" button) over and over again. The ProbeTaskbarCover
        // probe asks the OS itself which windows really lie at points along
        // the taskbar strip — a determination of visibility, not a
        // prediction of it. Applied via RequestFullscreenVisibility
        // (hysteresis against flicker on fleeting changes; being covered by
        // the active window is a reliable fullscreen, so we hide fast).
        var cover = ProbeTaskbarCover(ownHwnd, tray);
        RequestFullscreenVisibility(cover != TaskbarCover.Visible, cover == TaskbarCover.ObscuredByForeground);
        if (_hiddenForFullscreen) return; // no point repositioning a hidden window (including one not yet released back by hysteresis)

        // A cheap check of "are we stale" — instead of caching the
        // taskbar's handle as before, we read the CURRENT owner directly
        // from the window: an obviously fresh source of truth, and it also
        // correctly catches the very first call (a freshly created HWND has
        // no owner yet, i.e. GWLP_HWNDPARENT=0 != tray — naturally read as
        // "stale", with no separate branch for "the very first time").
        var currentOwner = Win32.GetWindowLongPtr(ownHwnd, Win32.GwlpHwndParent);
        var stale = currentOwner != tray;

        if (stale)
        {
            // explorer.exe recreated Shell_TrayWnd (a restart) — or this is
            // the very first Dock(). We re-bind the owner. HWND_TOPMOST is
            // re-applied below, together with the positioning, EXACTLY ONCE —
            // not on every subsequent tick (see the class doc-comment about why
            // periodic re-application breaks the shell's context menus).
            Win32.SetWindowLongPtr(ownHwnd, Win32.GwlpHwndParent, tray);
        }

        var notify = Win32.FindWindowEx(tray, nint.Zero, TrayNotifyClassName, null);
        var dpi = Win32.GetDpiForWindow(tray);
        if (dpi == 0) dpi = 96;

        if (!Win32.GetWindowRect(tray, out var trayRect)) return;
        var bandHeightPx = trayRect.Bottom - trayRect.Top;

        // We measure the content HERE, rather than relying on Render()
        // (which may run before EnsureHandle() — see its comment): this
        // guarantees that DesiredSize is always computed in the same DPI
        // context that OnRender then actually uses to draw the text. Without
        // this, on the first Dock() the window's width was computed using
        // the "before docking" DPI (a fallback to the system DPI was
        // possible), while the text was drawn using the monitor's real DPI —
        // at a scale other than 100% these diverged, and the third column
        // was truncated down to almost one character ("FAB"/"53%" → "F"/"5",
        // task-17-report.md round 5, live-finding #2). Cheap — recomputing
        // the width of three FormattedText columns, not a repaint.
        _content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var contentWidthDip = _content.DesiredSize.Width + OuterPaddingDip * 2;
        var bandWidthPx = ToPhysical(contentWidthDip, dpi);
        var gapPx = ToPhysical(GapDip, dpi);

        var x = _position == "left"
            ? trayRect.Left + ToPhysical(LeftPositionOffsetDip, dpi)
            : ComputeTrayPositionX(trayRect, notify, bandWidthPx, gapPx);

        // We skip SetWindowPos entirely if the geometry hasn't changed by
        // even a pixel (except for the "stale" case — there the owner/Z-order
        // must be re-bound via SetWindowPos regardless, even if x/y/size
        // themselves match the previous time): every SetWindowPos call is a
        // WM_WINDOWPOSCHANGED/WM_SIZE and, for a layered window
        // (AllowsTransparency=true), another DWM composition — cheap once,
        // but no reason to pay that cost every 5 seconds forever when almost
        // always nothing has changed (task-17-report.md round 5,
        // live-finding #3: "very sluggish").
        // Anti-burial — STRICTLY before the geometric short-circuit below:
        // burial (the shell raised the taskbar/some other window above us)
        // happens precisely when the geometry hasn't changed by a pixel, and
        // the detection that used to sit after this return was, in real
        // life, never invoked at all — the band lay under the taskbar for
        // hours with a pristine, empty fullscreen log. Not needed for the
        // stale branch: it re-applies HWND_TOPMOST itself, together with
        // re-binding the owner.
        if (!stale) EnsureNotBuried(ownHwnd, tray);

        if (!stale
            && x == _lastX && trayRect.Top == _lastY
            && bandWidthPx == _lastWidthPx && bandHeightPx == _lastHeightPx)
        {
            return;
        }

        var insertAfter = stale ? Win32.HwndTopMost : nint.Zero;
        var flags = stale ? Win32.SwpNoActivate : (Win32.SwpNoZOrder | Win32.SwpNoActivate);
        Win32.SetWindowPos(ownHwnd, insertAfter, x, trayRect.Top, bandWidthPx, bandHeightPx, flags);
        _lastX = x;
        _lastY = trayRect.Top;
        _lastWidthPx = bandWidthPx;
        _lastHeightPx = bandHeightPx;
    }

    /// <summary>
    /// Brings the band back to the top if it got buried in the Z-order —
    /// and ONLY then (not periodically: see the class doc-comment about
    /// NetSpeedTray #200).
    ///
    /// Live scenario (2026-08-06): the user maximizes an application
    /// (maximized, or AWT fullscreen with the taskbar visible) — the shell
    /// drops the topmost layer during the "rude" state, then raises the
    /// taskbar with a SetWindowPos using SWP_NOOWNERZORDER, i.e. WITHOUT
    /// owned windows. The invariant "owned is always above its owner" holds
    /// when the owner is raised normally, but not with NOOWNERZORDER — the
    /// band stays under the application window while the taskbar is
    /// visible. Confirming symptom: clicking the taskbar (a normal raise,
    /// this time WITH owned windows) brought the band back before the
    /// user's eyes.
    ///
    /// Detection without a hit-test: WindowFromPoint won't do — the band is
    /// transparent, and on a transparent pixel it honestly returns whatever
    /// is underneath, even when the band is on top. Instead we walk the
    /// GW_HWNDPREV chain (windows STRICTLY above us): any visible foreign
    /// window that intersects our rectangle means "we've been covered".
    /// Exceptions: context menus (#32768) and flyouts — legitimate
    /// temporary windows; re-asserting over them is precisely bug #200, so
    /// we skip them (they will close on their own).
    /// </summary>
    /// <summary>A lightweight entry point into EnsureNotBuried for
    /// EVENT_OBJECT_REORDER: only handle resolution and the check itself,
    /// without the geometry/probe — a z-order reshuffle doesn't touch those,
    /// and there are a lot of these events.</summary>
    private void CheckBuriedNow()
    {
        if (_hiddenForFullscreen) return;
        var ownHwnd = new WindowInteropHelper(this).Handle;
        if (ownHwnd == nint.Zero || !Win32.IsWindow(ownHwnd)) return;
        var tray = Win32.FindWindow(TrayClassName, null);
        if (tray == nint.Zero) return;
        EnsureNotBuried(ownHwnd, tray);
    }

    private void EnsureNotBuried(nint ownHwnd, nint tray)
    {
        if (!Win32.GetWindowRect(ownHwnd, out var own)) return;

        var above = Win32.GetWindow(ownHwnd, Win32.GwHwndPrev);
        // A traversal limiter: the topmost layer is usually only a handful
        // of windows; 64 is generous, and guarantees against an infinite loop
        // on a broken chain.
        for (var i = 0; above != nint.Zero && i < 64; i++, above = Win32.GetWindow(above, Win32.GwHwndPrev))
        {
            if (above == tray)
            {
                // The owner is ABOVE the owned window — the owned-order invariant
                // is broken: the shell raised the taskbar with SWP_NOOWNERZORDER, and
                // the opaque Shell_TrayWnd is now drawn on top of the band — to the
                // eye it "disappeared", although it is formally visible and not
                // Hide()-d (this is exactly how the band went dark when
                // opening/closing windows from the tray — the fullscreen path in the
                // log stayed pristine and empty the whole time). This is the same
                // burial as under an application window, just that the owner itself
                // does the burying — and it is fixed with the same single re-assert.
                Diag("buried under the taskbar itself — re-asserting topmost");
                Win32.SetWindowPos(ownHwnd, Win32.HwndTopMost, 0, 0, 0, 0,
                    Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoActivate);
                return;
            }
            if (!Win32.IsWindowVisible(above)) continue;
            if (!Win32.GetWindowRect(above, out var r)) continue;

            var overlaps = r.Left < own.Right && r.Right > own.Left
                && r.Top < own.Bottom && r.Bottom > own.Top;
            if (!overlaps) continue;

            var cls = Win32.GetClassName(above);
            if (cls is "#32768" or "Xaml_WindowedPopupClass") continue; // menus/flyouts — temporary, not our case

            Diag($"buried under {above} cls={cls} — re-asserting topmost");
            Win32.SetWindowPos(ownHwnd, Win32.HwndTopMost, 0, 0, 0, 0,
                Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoActivate);
            return;
        }
    }

    private static int ComputeTrayPositionX(Win32.RECT trayRect, nint notify, int bandWidthPx, int gapPx)
    {
        if (notify != nint.Zero && Win32.GetWindowRect(notify, out var notifyRect))
            return notifyRect.Left - gapPx - bandWidthPx;

        // TrayNotifyWnd wasn't found (a non-standard explorer build) — the
        // taskbar's right edge as a coarser, but safe, estimate of the same
        // spot.
        return trayRect.Right - bandWidthPx - gapPx;
    }

    /// <summary>
    /// Is the taskbar covered by a foreign window — ground truth instead of
    /// predictions: the probe takes three points inside the taskbar strip
    /// (see <see cref="ProbeFractions"/>) and asks the OS which top-level
    /// window actually sits at each of them (WindowFromPoint →
    /// GetAncestor(GA_ROOT)). Rules:
    /// - a point "behind" the taskbar (root is Shell_TrayWnd) or behind our
    ///   own band (it legitimately hangs above the strip) → the taskbar is
    ///   visible at this point;
    /// - the taskbar is considered covered only when ALL points are covered
    ///   by foreign windows: a single point may legitimately be covered by
    ///   the volume/calendar flyout above the clock — the band shouldn't be
    ///   hidden because of that, while a real fullscreen application covers
    ///   the entire strip.
    /// This definition automatically resolves every case that broke the
    /// heuristics: a maximized window (of any application) doesn't touch the
    /// strip → the band is visible; fullscreen on ANOTHER monitor doesn't
    /// touch OUR taskbar → visible; "Minimize"/Win+D → the taskbar is on top
    /// → visible; real fullscreen on our monitor covers the strip → we hide.
    /// WindowFromPoint passes straight through the transparent pixels of
    /// layered windows, so the transparent areas of our own band don't
    /// confuse the probe.
    /// </summary>
    /// The probe's diagnostic log — enabled by the environment variable
    /// CLAUDE_BAND_DIAG=1, writes to %TEMP%\claude-band-diag.log. Left in
    /// deliberately: the band's visibility has glitched a few times only on
    /// the user's live machine, and every time the main thing lacking was
    /// facts.
    private static readonly bool DiagEnabled =
        Environment.GetEnvironmentVariable("CLAUDE_BAND_DIAG") == "1";

    private static void Diag(string message)
    {
        if (!DiagEnabled) return;
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "claude-band-diag.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (IOException) { /* the log is not more important than the band working */ }
    }

    /// The probe's verdict: the taskbar is visible; covered by the active
    /// (foreground) window — this is genuine fullscreen, hiding can be
    /// fast; covered by something INACTIVE — suspicious of a fleeting
    /// service overlay (ShareX and the like), we confirm with a long
    /// hold-off.
    private enum TaskbarCover { Visible, ObscuredByForeground, Obscured }

    private static TaskbarCover ProbeTaskbarCover(nint ownHwnd, nint tray)
    {
        if (!Win32.GetWindowRect(tray, out var trayRect)) return TaskbarCover.Visible;

        var width = trayRect.Right - trayRect.Left;
        var midY = (trayRect.Top + trayRect.Bottom) / 2;
        nint firstRoot = 0;
        var allSameRoot = true;

        foreach (var fraction in ProbeFractions)
        {
            var point = new Win32.POINT
            {
                X = trayRect.Left + (int)(width * fraction),
                Y = midY,
            };

            var hit = Win32.WindowFromPoint(point);
            if (hit == nint.Zero)
            {
                Diag($"probe f={fraction} pt=({point.X},{point.Y}) hit=0 -> visible");
                return TaskbarCover.Visible; // emptiness — definitely not a window over the taskbar
            }

            var root = Win32.GetAncestor(hit, Win32.GaRoot);
            Diag($"probe f={fraction} pt=({point.X},{point.Y}) hit={hit} root={root} cls={Win32.GetClassName(root)} tray={tray} own={ownHwnd}");
            if (root == tray || root == ownHwnd || root == nint.Zero) return TaskbarCover.Visible;

            // A cloaked window is a ghost: DWM doesn't paint it, the user sees
            // the taskbar, but WindowFromPoint still returns it. Live example:
            // the Deadlock (SDL_app) window, after leaving exclusive fullscreen,
            // hangs cloaked ABOVE the taskbar in the z-order for hours, and any
            // focus reshuffle (opening Telegram) raises it above Shell_TrayWnd
            // again — without this check the band would hide while the taskbar
            // was visible to the eye. A ghost obscures nothing — the point reads
            // as "the taskbar is visible".
            if (Win32.IsCloaked(root))
            {
                Diag($"probe f={fraction}: root {root} is DWM-cloaked ghost -> visible");
                return TaskbarCover.Visible;
            }

            if (firstRoot == 0) firstRoot = root;
            else if (root != firstRoot) allSameRoot = false;
        }

        // The same ACTIVE window at all points — the user themselves
        // maximized something to fill the screen (a YouTube player, a game):
        // the "hide" decision here is reliable, a long quarantine isn't
        // needed. Service overlays (ShareX) are never active.
        if (allSameRoot && firstRoot == Win32.GetForegroundWindow())
        {
            Diag("probe verdict: OBSCURED by foreground window (real fullscreen)");
            return TaskbarCover.ObscuredByForeground;
        }

        Diag("probe verdict: OBSCURED (all points foreign)");
        return TaskbarCover.Obscured;
    }

    private static int ToPhysical(double dip, uint dpi) => (int)Math.Round(dip * dpi / 96.0);
}

/// <summary>
/// The band's content: a horizontal row of "label above value" columns,
/// drawn by hand via <see cref="OnRender"/> — the same approach as the
/// widget's dials (DialControl/StatusDialControl), reusing
/// DialText.Format/DrawStackCentered, rather than a StackPanel of
/// TextBlocks (it's simpler to precisely compute each column's width for
/// Reposition than to add extra aliases for StackPanel/TextBlock, which
/// have namesakes in System.Windows.Forms).
/// White text with a dark 1 px drop-shadow outline — on a transparent
/// background over an arbitrary (light or dark) taskbar color, plain
/// white alone would blend into the background in places; the shadow
/// reads on any background.
/// </summary>
internal sealed class TaskbarBandContent : FrameworkElement
{
    private const double LabelFontSize = 10;
    private const double ValueFontSize = 14;
    private const double LineSpacingDip = 1;
    private const double ColumnSpacingDip = 14;
    private const double ShadowOffsetDip = 1;

    /// Between the percentage and the reset time on the bottom line. Smaller
    /// than the space between two columns, or the two halves of one account
    /// stop reading as one thing.
    private const double PairSpacingDip = 5;

    // White on Windows' dark taskbar, near-black on its light one — the same
    // taskbar-mode rule TrayIconRenderer follows, kept in sync via
    // App.OnSystemThemeChanged.
    private static readonly SolidColorBrush DarkInk = Freeze(new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B)));
    private static readonly SolidColorBrush DarkShadow = Freeze(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)));
    private static readonly SolidColorBrush LightShadow = Freeze(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)));

    private IReadOnlyList<BandEntry> _entries = Array.Empty<BandEntry>();
    private double[] _columnWidths = Array.Empty<double>();
    private ThemeKind _taskbar = ThemeKind.Dark;

    private SolidColorBrush Ink => _taskbar == ThemeKind.Light ? DarkInk : (SolidColorBrush)Brushes.White;

    /// Ink on a light taskbar needs a light shadow to stay visible (the same
    /// reason DarkShadow exists for the dark taskbar's white ink) — otherwise
    /// the shadow becomes indistinguishable from the near-black text it sits
    /// behind.
    private SolidColorBrush Shadow => _taskbar == ThemeKind.Light ? LightShadow : DarkShadow;

    public void SetMetrics(IReadOnlyList<BandEntry> entries, ThemeKind taskbar)
    {
        _entries = entries;
        _taskbar = taskbar;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// One account's column: the name alone on top, the percentage and the
    /// reset time together underneath — the percentage large, the time in the
    /// label size beside it, both sitting on one baseline.
    private readonly record struct Column(
        FormattedText Name, FormattedText Percent, FormattedText? ResetsIn)
    {
        public double BottomWidth =>
            Percent.Width + (ResetsIn is { } time ? PairSpacingDip + time.Width : 0);

        public double Width => Math.Max(Name.Width, BottomWidth);

        public double Height => Name.Height + LineSpacingDip +
            Math.Max(Percent.Height, ResetsIn?.Height ?? 0);
    }

    // System.Drawing.Brush is a namesake here — UseWindowsForms makes it visible.
    private Column Build(BandEntry entry, System.Windows.Media.Brush brush, double pixelsPerDip) => new(
        DialText.Format(entry.Name, LabelFontSize, Theme.LabelWeight, brush, pixelsPerDip),
        DialText.Format(entry.Percent, ValueFontSize, Theme.ValueWeight, brush, pixelsPerDip),
        entry.ResetsIn is null
            ? null
            : DialText.Format(entry.ResetsIn, LabelFontSize, Theme.LabelWeight, brush, pixelsPerDip));

    protected override Size MeasureOverride(Size availableSize)
    {
        var pixelsPerDip = DialText.PixelsPerDip(this);
        var widths = new double[_entries.Count];
        double totalWidth = 0;

        for (var i = 0; i < _entries.Count; i++)
        {
            var width = Build(_entries[i], Brushes.White, pixelsPerDip).Width;
            widths[i] = width;

            totalWidth += width;
            if (i > 0) totalWidth += ColumnSpacingDip;
        }

        _columnWidths = widths;

        var height = double.IsInfinity(availableSize.Height)
            ? LabelFontSize + LineSpacingDip + ValueFontSize
            : availableSize.Height;
        return new Size(totalWidth, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var pixelsPerDip = DialText.PixelsPerDip(this);
        var y = ActualHeight / 2;
        var x = 0.0;

        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var width = i < _columnWidths.Length ? _columnWidths[i] : 0;
            var center = new Point(x + width / 2, y);

            // The shadow is the same column, same sizes, one pixel down and
            // right, drawn FIRST — the ink lands on top of it.
            Draw(dc, Build(entry, Shadow, pixelsPerDip),
                new Point(center.X + ShadowOffsetDip, center.Y + ShadowOffsetDip));
            Draw(dc, Build(entry, Ink, pixelsPerDip), center);

            x += width + ColumnSpacingDip;
        }
    }

    private static void Draw(DrawingContext dc, Column column, Point center)
    {
        var top = center.Y - column.Height / 2;

        dc.DrawText(column.Name, new Point(center.X - column.Name.Width / 2, top));

        var bottom = top + column.Name.Height + LineSpacingDip;
        var startX = center.X - column.BottomWidth / 2;

        dc.DrawText(column.Percent, new Point(startX, bottom));

        if (column.ResetsIn is not { } time) return;

        // Centred on the percentage's box, NOT sharing its baseline. A common
        // baseline is right for two runs of the same size; here the time is
        // four points smaller, so it hangs at the bottom of the big digits and
        // reads as sitting too low — which is what it looked like on screen.
        dc.DrawText(time, new Point(
            startX + column.Percent.Width + PairSpacingDip,
            bottom + (column.Percent.Height - time.Height) / 2));
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
