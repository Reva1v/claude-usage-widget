import AppKit
import ClaudeUsageWidgetCore
import ServiceManagement
import Sparkle
import SwiftUI

@main
struct ClaudeUsageWidgetApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    @AppStorage(WidgetSettings.positionLockedKey) private var positionLocked = false
    @AppStorage(WidgetSettings.widgetVisibleKey) private var widgetVisible = true
    @AppStorage(WidgetSettings.modelBucketKey) private var modelBucket = ""

    /// A monochrome ring glyph. Template images get tinted by macOS to match
    /// the menu bar, light or dark.
    private static let menuBarIcon: NSImage = {
        let size = NSSize(width: 16, height: 16)
        let image = NSImage(size: size, flipped: false) { _ in
            let inset: CGFloat = 2.5
            let rect = NSRect(x: inset, y: inset, width: size.width - inset * 2, height: size.height - inset * 2)
            NSColor.black.setStroke()
            let ring = NSBezierPath(ovalIn: rect)
            ring.lineWidth = 2
            ring.stroke()
            return true
        }
        image.isTemplate = true
        return image
    }()

    var body: some Scene {
        MenuBarExtra {
            Button("Claude Usage Widget v\(CoreInfo.version) — GitHub") {
                NSWorkspace.shared.open(UpdateChecker.repoPageURL)
            }
            Button("Report an Issue") {
                NSWorkspace.shared.open(UpdateChecker.issuesPageURL)
            }
            Button("Check for Updates…") {
                appDelegate.updaterController.updater.checkForUpdates()
            }
            .disabled(!appDelegate.updaterController.updater.canCheckForUpdates)
            Divider()
            Button("Refresh now") { appDelegate.store.refresh() }
            Button("Sign in to Claude.ai…") { appDelegate.signInToClaudeAi() }
            Divider()
            ModelBucketPicker(store: appDelegate.store, selection: $modelBucket)
            Toggle("Lock position", isOn: $positionLocked)
            Toggle("Show on desktop", isOn: $widgetVisible)
            LaunchAtLoginToggle()
            Divider()
            Button("Quit Claude Usage Widget") { NSApplication.shared.terminate(nil) }
                .keyboardShortcut("q")
        } label: {
            MenuBarLabel(store: appDelegate.store, fallback: Self.menuBarIcon)
        }
    }
}

/// Draws the live usage figures into the menu bar.
///
/// `MenuBarExtra` squeezes a custom SwiftUI label onto one line and clips its
/// width, so the two-line layout is rasterised into an `NSImage` instead — the
/// menu bar renders images at full size reliably. Reading the `@Observable`
/// store's properties in `body` subscribes this view to its updates.
private struct MenuBarLabel: View {
    let store: UsageStore
    let fallback: NSImage

    @AppStorage(WidgetSettings.modelBucketKey) private var modelBucket = ""

    var body: some View {
        let snapshot: UsageSnapshot? = {
            if case let .ok(snapshot, _) = store.state { return snapshot }
            return store.lastSnapshot
        }()

        let metrics = MenuBarText.metrics(for: DialModel.all(
            snapshot: snapshot,
            preferredModelKey: modelBucket.isEmpty ? nil : modelBucket,
            now: Date()
        ))

        // The fallback stands in until the first snapshot arrives — before
        // that, or once a failure has left nothing to show, the ring icon
        // reads better than three dashed columns.
        Image(nsImage: snapshot == nil ? fallback : Self.image(for: metrics))
    }

    /// Each metric is a column: a small label on top, the figure below.
    ///
    /// Rows are packed to cap height rather than to the font's full line
    /// height, so that when the image is scaled down to the menu bar's
    /// thickness the glyphs render as large as they can.
    private static func image(for metrics: [MenuBarMetric]) -> NSImage {
        let labelFont = NSFont.monospacedSystemFont(ofSize: 10, weight: .medium)
        let valueFont = NSFont.monospacedSystemFont(ofSize: 14, weight: .semibold)

        // Drawn opaque black; the image is a template, so macOS ignores the hue
        // and tints the glyphs to match the menu bar itself — dark on light
        // bars, light on dark — exactly like the clock and Wi-Fi icons.
        func attributes(_ font: NSFont) -> [NSAttributedString.Key: Any] {
            [.font: font, .foregroundColor: NSColor.black]
        }

        let cells = metrics.map { metric -> (top: NSAttributedString, bottom: NSAttributedString, width: CGFloat) in
            let top = NSAttributedString(string: metric.label, attributes: attributes(labelFont))
            let bottom = NSAttributedString(string: metric.value, attributes: attributes(valueFont))
            return (top, bottom, ceil(max(top.size().width, bottom.size().width)))
        }

        let columnGap: CGFloat = 10
        let rowGap: CGFloat = 3
        let verticalPadding: CGFloat = 2 // so cap tops and bottoms are not clipped

        let width = cells.reduce(0) { $0 + $1.width } + columnGap * CGFloat(max(0, cells.count - 1))
        let height = ceil(valueFont.capHeight + rowGap + labelFont.capHeight + verticalPadding * 2)
        let bottomBaseline = verticalPadding
        let topBaseline = valueFont.capHeight + rowGap + verticalPadding

        let image = NSImage(size: NSSize(width: width, height: height), flipped: false) { _ in
            var x: CGFloat = 0
            for cell in cells {
                cell.bottom.draw(at: NSPoint(x: x, y: bottomBaseline + valueFont.descender))
                cell.top.draw(at: NSPoint(x: x, y: topBaseline + labelFont.descender))
                x += cell.width + columnGap
            }
            return true
        }
        image.isTemplate = true

        // Scale to the bar's thickness so both rows stay visible instead of the
        // top one being clipped.
        let thickness = NSStatusBar.system.thickness
        if height > thickness {
            image.size = NSSize(width: width * thickness / height, height: thickness)
        }
        return image
    }
}

/// Lists the per-model buckets the server actually returned. Hidden entirely
/// when there is nothing to choose between.
private struct ModelBucketPicker: View {
    let store: UsageStore
    @Binding var selection: String

    private var keys: [String] {
        guard let snapshot = store.lastSnapshot else { return [] }
        return ModelBuckets.available(in: snapshot)
    }

    var body: some View {
        if keys.count > 1 {
            Picker("Model limit", selection: $selection) {
                ForEach(keys, id: \.self) { key in
                    Text(ModelBuckets.label(for: key)).tag(key)
                }
            }
        }
    }
}

/// "Launch at login" backed by SMAppService. Registration only works from a
/// real .app bundle; from a bare `swift run` binary register() throws and the
/// toggle reverts.
private struct LaunchAtLoginToggle: View {
    @State private var enabled = SMAppService.mainApp.status == .enabled

    var body: some View {
        Toggle("Launch at login", isOn: Binding(
            get: { enabled },
            set: { newValue in
                do {
                    if newValue {
                        try SMAppService.mainApp.register()
                    } else {
                        try SMAppService.mainApp.unregister()
                    }
                    enabled = newValue
                } catch {
                    enabled = SMAppService.mainApp.status == .enabled
                }
            }
        ))
    }
}

private var isDraggingAllowed: Bool {
    !UserDefaults.standard.bool(forKey: WidgetSettings.positionLockedKey)
}

/// mouseDownCanMoveWindow == false disables AppKit's built-in auto-drag, which
/// would ignore the lock; dragging goes only through DesktopWindow.mouseDown.
final class WidgetHostingView<Content: View>: NSHostingView<Content> {
    override var mouseDownCanMoveWindow: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

/// Borderless desktop-level window: never steals focus, draggable unless locked.
final class DesktopWindow: NSWindow {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    override func mouseDown(with event: NSEvent) {
        if event.type == .leftMouseDown, isDraggingAllowed {
            performDrag(with: event)
        } else {
            super.mouseDown(with: event)
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let webSession = ClaudeWebSession()
    lazy var store = UsageStore(
        cachedSnapshot: { nil },
        fetch: { [webSession] _ in try await webSession.fetchUsage() },
        // UsageStore's injected fetch API predates the web-session transport.
        // The real credential check happens inside ClaudeWebSession.
        tokenProvider: { "claude.ai-web-session" }
    )
    let statusStore = StatusStore()
    private var window: DesktopWindow?
    private var loginWindowController: ClaudeLoginWindowController?

    /// Sparkle updater. `startingUpdater: true` kicks off the background check
    /// on launch (gated by SUEnableAutomaticChecks in Info.plist); the menu's
    /// "Check for Updates…" item drives it manually.
    let updaterController = SPUStandardUpdaterController(
        startingUpdater: true,
        updaterDelegate: nil,
        userDriverDelegate: nil
    )

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory) // no Dock icon

        store.start()
        statusStore.start()

        let window = DesktopWindow(
            contentRect: NSRect(x: 0, y: 0, width: 340, height: 150),
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        // One level above the Finder desktop icon window: below it, Finder's
        // transparent full-screen window swallows every click and the widget
        // cannot be dragged.
        window.level = NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.desktopIconWindow)) + 1)
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        window.backgroundColor = .clear
        window.isOpaque = false
        window.hasShadow = false

        let hostingView = WidgetHostingView(rootView: WidgetRootView(store: store, statusStore: statusStore))
        window.contentView = hostingView

        // Centre first, then attach the autosave name so a stored frame wins.
        window.center()
        window.setFrameAutosaveName("ClaudeUsageWidgetWindow")

        self.window = window
        // The stored side is authoritative, not SwiftUI's fitting size — a
        // fresh launch with nothing stored lands at the 170 pt default.
        syncWindowSize()
        if WidgetSettings.isVisible(in: .standard) {
            window.orderFrontRegardless()
        }

        // A fresh install has no WebKit cookie jar yet. Put the one required
        // action in front of the user immediately instead of making them hunt
        // through a menu while the widget says only “Not signed in”.
        Task { [weak self] in
            guard let self, !(await webSession.hasSessionCookie()) else { return }
            signInToClaudeAi()
        }

        NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated {
                self?.reconcileVisibility()
                self?.syncWindowSize()
            }
        }
    }

    /// Keeps the window frame matching the stored side length. The widget is a
    /// square, so one number drives both axes.
    ///
    /// AppKit anchors a resize to the window's bottom-left, which would make the
    /// widget appear to crawl up the screen as it grows. Re-pinning the top-left
    /// keeps it where the user put it, whichever edge they dragged.
    private func syncWindowSize() {
        guard let window else { return }
        let side = WidgetSettings.size(in: .standard)
        let frame = window.frame
        guard abs(frame.width - side) > 0.5 || abs(frame.height - side) > 0.5 else { return }

        let top = frame.maxY
        window.setContentSize(NSSize(width: side, height: side))
        var moved = window.frame
        moved.origin.y = top - moved.height
        window.setFrameOrigin(moved.origin)
    }

    /// Shows or hides the window to match the stored flag. Idempotent, so
    /// unrelated defaults changes do not re-order the window. Polling keeps
    /// running while hidden.
    private func reconcileVisibility() {
        guard let window else { return }
        let shouldBeVisible = WidgetSettings.isVisible(in: .standard)
        if shouldBeVisible, !window.isVisible {
            window.orderFrontRegardless()
        } else if !shouldBeVisible, window.isVisible {
            window.orderOut(nil)
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        store.stop()
        statusStore.stop()
    }

    func signInToClaudeAi() {
        if let loginWindowController {
            loginWindowController.showWindow(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }

        let controller = ClaudeLoginWindowController(dataStore: .default()) { [weak self] in
            guard let self else { return }
            webSession.clearCachedOrganization()
            loginWindowController = nil
            store.refresh()
        }
        loginWindowController = controller
        controller.showWindow(nil)
        controller.window?.center()
        NSApp.activate(ignoringOtherApps: true)
    }
}
