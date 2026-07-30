import AppKit
import SwiftUI

/// The panel: a 2x2 grid of dials, with a status line when something needs
/// saying.
public struct WidgetRootView: View {
    private let store: UsageStore

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

    public init(store: UsageStore) {
        self.store = store
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
    private var dialSize: CGFloat { 68 * scale }
    private var gap: CGFloat { 10 * scale }
    private var pad: CGFloat { 12 * scale }

    public var body: some View {
        let status = StatusLine.text(for: store.state, now: now)
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
                PlaceholderDial(size: dialSize, scale: scale)
            }
        }
        .frame(width: side - pad * 2, height: side - pad * 2)
        .padding(pad)
        .background(
            RoundedRectangle(cornerRadius: 22 * scale, style: .continuous)
                .fill(Theme.panel.opacity(0.86))
        )
        .overlay(alignment: .topTrailing) { controls }
        .overlay(alignment: .bottom) { footer(status: status) }
        .overlay(alignment: .bottomTrailing) { resizeHandle }
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

    /// Lock and hide, revealed on hover so the dials stay unobstructed at rest.
    private var controls: some View {
        HStack(spacing: 2 * scale) {
            Button {
                positionLocked.toggle()
            } label: {
                Image(systemName: positionLocked ? "lock.fill" : "lock.open")
                    .font(.system(size: 9 * scale, weight: .medium))
                    .foregroundStyle(positionLocked ? Theme.warning : Theme.dim)
                    .frame(width: 18 * scale, height: 18 * scale)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help(positionLocked
                ? "Position and size are locked — click to unlock"
                : "Click to lock the widget position and size")

            Button {
                widgetVisible = false
            } label: {
                Image(systemName: "eye.slash")
                    .font(.system(size: 9 * scale, weight: .medium))
                    .foregroundStyle(Theme.dim)
                    .frame(width: 18 * scale, height: 18 * scale)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help("Hide widget — bring it back from the menu bar icon")
        }
        .padding(4 * scale)
        // The lock stays visible while engaged, so its state is never a surprise.
        .opacity(hovering || positionLocked ? 1 : 0)
        .animation(.easeInOut(duration: 0.15), value: hovering)
    }

    /// The status line when something needs saying, the Ko-fi badge otherwise
    /// while hovering.
    @ViewBuilder
    private func footer(status: String?) -> some View {
        if let status {
            Text(status)
                .font(Theme.caption(scale: scale))
                .foregroundStyle(Theme.dim)
                .padding(.bottom, 4 * scale)
        } else {
            KofiButton(scale: scale)
                .padding(.bottom, 4 * scale)
                .opacity(hovering ? 1 : 0)
                .animation(.easeInOut(duration: 0.15), value: hovering)
        }
    }

    /// Drag the bottom-right corner to resize. One delta drives the single side
    /// length, so the panel can only ever be a square.
    private var resizeHandle: some View {
        Color.clear
            .frame(width: 16 * scale, height: 16 * scale)
            .contentShape(Rectangle())
            .onHover { inside in
                guard !positionLocked else { return }
                // NSCursor.frameResize(position:directions:) needs macOS 15;
                // the deployment target is 14, so crosshair stands in for it.
                if inside { NSCursor.crosshair.push() }
                else { NSCursor.pop() }
            }
            .gesture(
                DragGesture(minimumDistance: 1, coordinateSpace: .global)
                    .onChanged { value in
                        guard !positionLocked else { return }
                        let start = dragStartSize ?? widgetSize
                        dragStartSize = start
                        // The larger of the two deltas wins, so a diagonal drag
                        // feels natural even though only one number changes.
                        let delta = max(value.translation.width, value.translation.height)
                        widgetSize = WidgetSettings.clampSize(start + delta)
                    }
                    .onEnded { _ in dragStartSize = nil }
            )
    }
}

/// The empty fourth cell: a bare ring holding the spot until it has a job.
private struct PlaceholderDial: View {
    let size: CGFloat
    let scale: Double

    var body: some View {
        Circle()
            .strokeBorder(Theme.track.opacity(0.5), lineWidth: 5 * scale)
            .padding((4 - 5 / 2) * scale)
            .frame(width: size, height: size)
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
