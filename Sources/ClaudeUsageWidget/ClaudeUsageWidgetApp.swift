import AppKit
import ClaudeUsageWidgetCore
import ServiceManagement
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
            Button("Claude Usage Widget v\(CoreInfo.version)") {}
                .disabled(true)
            Button("Refresh now") { appDelegate.store.refresh() }
            Divider()
            ModelBucketPicker(store: appDelegate.store, selection: $modelBucket)
            Toggle("Lock position", isOn: $positionLocked)
            Toggle("Show on desktop", isOn: $widgetVisible)
            LaunchAtLoginToggle()
            Divider()
            Button("Quit Claude Usage Widget") { NSApplication.shared.terminate(nil) }
                .keyboardShortcut("q")
        } label: {
            Image(nsImage: Self.menuBarIcon)
        }
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
    let store = UsageStore()
    private var window: DesktopWindow?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory) // no Dock icon

        store.start()

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

        let hostingView = WidgetHostingView(rootView: WidgetRootView(store: store))
        window.contentView = hostingView
        // Fit the window to its content: extra transparent area would capture
        // clicks outside the visible panel.
        let fitting = hostingView.fittingSize
        if fitting.width > 0, fitting.height > 0 {
            window.setContentSize(fitting)
        }

        // Centre first, then attach the autosave name so a stored frame wins.
        window.center()
        window.setFrameAutosaveName("ClaudeUsageWidgetWindow")

        self.window = window
        if WidgetSettings.isVisible(in: .standard) {
            window.orderFrontRegardless()
        }

        NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.reconcileVisibility() }
        }
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
    }
}
