import AppKit
import SwiftUI

/// The panel: a 2x2 grid of dials, with a status line when something needs
/// saying.
public struct WidgetRootView: View {
    private let store: UsageStore
    private let statusStore: StatusStore

    @AppStorage(WidgetSettings.modelBucketKey) private var modelBucket = ""
    @AppStorage(WidgetSettings.widgetSizeKey) private var widgetSize = WidgetSettings.defaultSize
    @AppStorage(WidgetSettings.positionLockedKey) private var positionLocked = false
    @AppStorage(WidgetSettings.widgetVisibleKey) private var widgetVisible = true
    @State private var hovering = false
    @State private var dragStartSize: Double?

    /// Drives the "updated N ago" line between fetches — the snapshot only
    /// changes every five minutes, but time does not.
    @State private var now = Date()

    private static let tick = Timer.publish(every: 30, on: .main, in: .common).autoconnect()

    public init(store: UsageStore, statusStore: StatusStore) {
        self.store = store
        self.statusStore = statusStore
    }

    private var snapshot: UsageSnapshot? {
        if case let .ok(snapshot, _) = store.state { return snapshot }
        return store.lastSnapshot
    }

    private var dimmed: Bool {
        if case .ok = store.state { return false }
        return true
    }

    /// Everything scales from the 170 pt design size, so the composition is
    /// identical at every panel size.
    private var side: CGFloat { WidgetSettings.clampSize(widgetSize) }
    private var scale: Double { side / WidgetSettings.defaultSize }
    private var pad: CGFloat { 12 * scale }
    private var gap: CGFloat { 10 * scale }
    private var dialSize: CGFloat { (side - pad * 2 - gap) / 2 }

    public var body: some View {
        let status = StatusLine.text(for: store.state, now: now, retryUntil: store.retryPausedUntil)
        let notice = BlockingNotice.make(for: store.state)
        let models = DialModel.all(
            snapshot: snapshot,
            preferredModelKey: modelBucket.isEmpty ? nil : modelBucket,
            now: now
        )

        VStack(spacing: gap) {
            HStack(spacing: gap) {
                dial(models[0])
                dial(models[1])
            }
            HStack(spacing: gap) {
                dial(models[2])
                Button {
                    NSWorkspace.shared.open(URL(string: "https://status.claude.com")!)
                } label: {
                    StatusDialView(
                        status: statusStore.status,
                        size: dialSize,
                        scale: scale,
                        dimmed: false
                    )
                }
                .buttonStyle(.plain)
                .help("Claude service status — click to open status.claude.com")
            }
        }
        .frame(width: side - pad * 2, height: side - pad * 2)
        // The long-form explanation rides the dials rather than the notice on
        // top of them: the notice takes no hits, so that the panel keeps its
        // hover and its drag.
        .help(statusHelp ?? "")
        .padding(pad)
        .background(
            ZStack {
                PanelBackground()
                Theme.panel.opacity(0.35)
            }
            .clipShape(RoundedRectangle(cornerRadius: 22 * scale, style: .continuous))
        )
        .overlay { blockingNotice(notice) }
        .overlay(alignment: .top) { header }
        .overlay(alignment: .bottom) { statusLine(status) }
        .overlay { resizeGrips }
        .frame(width: side, height: side)
        .onHover { hovering = $0 }
        .onReceive(Self.tick) { now = $0 }
    }

    private func dial(_ model: DialModel) -> some View {
        DialView(
            title: model.title,
            fraction: model.fraction,
            remaining: model.remaining,
            dimmed: dimmed,
            size: dialSize
        )
    }

    /// Hide, support and lock, in a strip across the top of the panel. Hidden
    /// until the pointer is over the widget so the dials read cleanly at rest.
    private var header: some View {
        HStack(spacing: 0) {
            Button {
                widgetVisible = false
            } label: {
                Image(systemName: "eye.slash")
                    .font(.system(size: 9 * scale, weight: .medium))
                    .foregroundStyle(Theme.dim)
                    .frame(width: 16 * scale, height: 16 * scale)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help("Hide widget — bring it back from the menu bar icon")

            Spacer(minLength: 0)

            KofiButton(scale: scale)

            Spacer(minLength: 0)

            Button {
                positionLocked.toggle()
            } label: {
                Image(systemName: positionLocked ? "lock.fill" : "lock.open")
                    .font(.system(size: 9 * scale, weight: .medium))
                    .foregroundStyle(positionLocked ? Theme.warning : Theme.dim)
                    .frame(width: 16 * scale, height: 16 * scale)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help(positionLocked
                ? "Position and size are locked — click to unlock"
                : "Click to lock the widget position and size")
        }
        .padding(.horizontal, 10 * scale)
        // Top inset clears the resize grip's 10 pt band, which sits above this
        // overlay and would otherwise swallow the top of every button.
        .padding(.top, 10 * scale)
        .padding(.bottom, 5 * scale)
        .frame(maxWidth: .infinity)
        .background(Theme.panel.opacity(0.92))
        .clipShape(
            .rect(
                topLeadingRadius: 22 * scale,
                bottomLeadingRadius: 0,
                bottomTrailingRadius: 0,
                topTrailingRadius: 22 * scale,
                style: .continuous
            )
        )
        .opacity(hovering ? 1 : 0)
        // Opacity alone leaves the buttons clickable while invisible: a stray
        // click would hide the widget or toggle the lock with no visible cause,
        // and a mouse-down there would never reach the window's drag handler.
        .allowsHitTesting(hovering)
        .animation(.easeInOut(duration: 0.15), value: hovering)
    }

    /// Only speaks up when something is wrong or stale.
    @ViewBuilder
    private func statusLine(_ status: String?) -> some View {
        if let status {
            Text(status)
                .font(Theme.caption(scale: scale))
                .foregroundStyle(Theme.dim)
                .padding(.bottom, 2 * scale)
        }
    }

    /// Covers the dials once their figures stop meaning anything, leaving the
    /// instruction and nothing else to read.
    ///
    /// Takes no hits: the panel is dragged and resized through the views below
    /// and around it, and the header fades in on hover, so a scrim that
    /// swallowed the mouse would strand the widget where it stands.
    @ViewBuilder
    private func blockingNotice(_ notice: BlockingNotice?) -> some View {
        if let notice {
            VStack(spacing: 5 * scale) {
                Text(notice.title)
                    .font(Theme.value(scale: scale))
                    .foregroundStyle(Theme.text)
                Text(notice.detail)
                    .font(Theme.caption(scale: scale))
                    .foregroundStyle(Theme.dim)
                    .multilineTextAlignment(.center)
            }
            .padding(.horizontal, pad)
            // Clears the header strip, which fades in above this on hover.
            .padding(.top, 31 * scale)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            // As opaque as the header: at anything thinner the dials read
            // through, and a figure half-visible behind a "this is wrong"
            // notice is worse than no figure at all.
            .background(Theme.panel.opacity(0.92))
            .clipShape(RoundedRectangle(cornerRadius: 22 * scale, style: .continuous))
            .allowsHitTesting(false)
        }
    }

    /// The status line has room for an instruction, not an explanation. This
    /// carries the rest for anyone who hovers — including the case that cannot
    /// be fixed at all, where the honest answer is that there is nothing to
    /// show rather than something to repair.
    private var statusHelp: String? {
        switch store.state {
        case .failed(.noCredentials):
            """
            Open the widget's menu-bar menu and choose “Sign in to Claude.ai…”. \
            The widget uses that browser session to read the same subscription \
            usage page as Claude.ai; it does not send the session anywhere else.
            """
        case .failed(.unauthorized):
            """
            The saved Claude.ai browser session has expired. Open the menu-bar \
            menu, choose “Sign in to Claude.ai…”, and complete login once more.
            """
        default:
            nil
        }
    }

    /// Which edge of the frame a grip sits on, and how a drag there maps to a
    /// change in the single side length.
    private enum Grip: CaseIterable {
        case top, bottom, leading, trailing

        var alignment: Alignment {
            switch self {
            case .top: .top
            case .bottom: .bottom
            case .leading: .leading
            case .trailing: .trailing
            }
        }

        /// Outward is positive: dragging an edge away from the centre grows the
        /// panel, whichever edge it is.
        func delta(_ translation: CGSize) -> Double {
            switch self {
            case .top: -translation.height
            case .bottom: translation.height
            case .leading: -translation.width
            case .trailing: translation.width
            }
        }

        /// The same cursors mole-widget uses for its resize strip.
        var cursor: NSCursor {
            switch self {
            case .top, .bottom: .resizeUpDown
            case .leading, .trailing: .resizeLeftRight
            }
        }

        var isHorizontal: Bool {
            switch self {
            case .leading, .trailing: true
            case .top, .bottom: false
            }
        }
    }

    private var resizeGrips: some View {
        ZStack {
            ForEach(Array(Grip.allCases), id: \.self) { grip in
                gripView(grip)
            }
        }
    }

    /// An invisible strip along one edge. Ten points at the design size —
    /// wide enough to grab without hunting, narrow enough to leave the rest of
    /// the panel free for dragging the window.
    ///
    /// The modifier order is load-bearing: `contentShape` has to come while the
    /// view is still strip-sized. Applied after the expanding frame it would
    /// claim the entire panel as its hit area, and every drag anywhere — even
    /// in the middle — would resize instead of moving the window.
    private func gripView(_ grip: Grip) -> some View {
        let thickness = 10 * scale

        return Color.clear
            .frame(
                width: grip.isHorizontal ? thickness : nil,
                height: grip.isHorizontal ? nil : thickness
            )
            .contentShape(Rectangle())
            .onHover { inside in
                guard !positionLocked else { return }
                if inside { grip.cursor.push() } else { NSCursor.pop() }
            }
            .gesture(
                DragGesture(minimumDistance: 1, coordinateSpace: .global)
                    .onChanged { value in
                        guard !positionLocked else { return }
                        let start = dragStartSize ?? widgetSize
                        dragStartSize = start
                        widgetSize = WidgetSettings.clampSize(start + grip.delta(value.translation))
                    }
                    .onEnded { _ in dragStartSize = nil }
            )
            // Inset along the long axis so neighbouring strips never share the
            // corner squares. Two overlapping tracking areas there would push
            // two cursors and pop them out of order, stranding one.
            .padding(grip.isHorizontal ? .vertical : .horizontal, thickness)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: grip.alignment)
    }
}

/// "Ko-fi | Support" capsule opening the donation page, mirroring the sibling
/// mole-widget.
private struct KofiButton: View {
    let scale: Double

    @State private var hovering = false

    private let kofiRed = Color(red: 1.0, green: 0.369, blue: 0.357)   // #FF5E5B
    private let kofiBg  = Color(red: 0.10, green: 0.10, blue: 0.10)    // near-black

    var body: some View {
        Button {
            NSWorkspace.shared.open(URL(string: "https://ko-fi.com/tadel_unso")!)
        } label: {
            HStack(spacing: 0) {
                HStack(spacing: 3 * scale) {
                    Image(systemName: "cup.and.saucer.fill")
                        .font(.system(size: 7 * scale, weight: .medium))
                    Text("Ko-fi")
                        .font(.system(size: 8 * scale, weight: .semibold))
                }
                .foregroundStyle(.white)
                .padding(.horizontal, 5 * scale)
                .padding(.vertical, 2 * scale)
                .background(kofiBg)

                Text("Support")
                    .font(.system(size: 8 * scale, weight: .semibold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 5 * scale)
                    .padding(.vertical, 2 * scale)
                    .background(kofiRed)
            }
            .clipShape(Capsule())
            .opacity(hovering ? 0.80 : 1.0)
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .help("Support on Ko-fi ☕")
    }
}

/// The frosted backdrop native widgets sit on. A plain translucent fill reads
/// as a sticker on the wallpaper; this blurs what is actually behind the
/// window, which is the wallpaper itself since the widget lives at desktop
/// level.
private struct PanelBackground: NSViewRepresentable {
    func makeNSView(context: Context) -> NSVisualEffectView {
        let view = NSVisualEffectView()
        view.material = .hudWindow
        view.blendingMode = .behindWindow
        view.state = .active
        return view
    }

    func updateNSView(_ nsView: NSVisualEffectView, context: Context) {}
}
