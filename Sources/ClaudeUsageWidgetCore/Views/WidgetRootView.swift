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
    /// identical at every panel size. The gap is wide enough to open a cross
    /// through the middle; the controls live in its horizontal arm.
    private var side: CGFloat { WidgetSettings.clampSize(widgetSize) }
    private var scale: Double { side / WidgetSettings.defaultSize }
    private var pad: CGFloat { 8 * scale }
    private var gap: CGFloat { 32 * scale }
    private var dialSize: CGFloat { (side - pad * 2 - gap) / 2 }

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
        .overlay { controls }
        .overlay(alignment: .bottom) { statusLine(status) }
        .overlay { resizeGrips }
        .frame(width: side, height: side)
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

    /// Hide, support, lock — sitting in the horizontal arm of the cross, where
    /// they obstruct nothing.
    private var controls: some View {
        HStack(spacing: 6 * scale) {
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

            KofiButton(scale: scale)

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

    /// Which part of the frame a grip sits on, and how a drag there maps to a
    /// change in the single side length.
    private enum Grip: CaseIterable {
        case top, bottom, leading, trailing
        case topLeading, topTrailing, bottomLeading, bottomTrailing

        var alignment: Alignment {
            switch self {
            case .top: .top
            case .bottom: .bottom
            case .leading: .leading
            case .trailing: .trailing
            case .topLeading: .topLeading
            case .topTrailing: .topTrailing
            case .bottomLeading: .bottomLeading
            case .bottomTrailing: .bottomTrailing
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
            case .topLeading: max(-translation.width, -translation.height)
            case .topTrailing: max(translation.width, -translation.height)
            case .bottomLeading: max(-translation.width, translation.height)
            case .bottomTrailing: max(translation.width, translation.height)
            }
        }

        var cursor: NSCursor {
            switch self {
            case .top, .bottom: .resizeUpDown
            case .leading, .trailing: .resizeLeftRight
            default: .crosshair
            }
        }

        var isCorner: Bool {
            switch self {
            case .top, .bottom, .leading, .trailing: false
            default: true
            }
        }
    }

    /// Invisible grips around the whole frame. Disabled while locked. Corners
    /// are declared after edges so they win the hit test where they overlap.
    private var resizeGrips: some View {
        ZStack {
            ForEach(Grip.allCases.filter { !$0.isCorner }, id: \.self) { grip in
                gripView(grip)
            }
            ForEach(Grip.allCases.filter { $0.isCorner }, id: \.self) { grip in
                gripView(grip)
            }
        }
    }

    private func gripView(_ grip: Grip) -> some View {
        let thickness = 6 * scale

        let width: CGFloat?
        let height: CGFloat?
        if grip.isCorner {
            width = thickness
            height = thickness
        } else {
            switch grip.alignment {
            case .leading, .trailing:
                width = thickness
                height = nil
            default:
                width = nil
                height = thickness
            }
        }

        return Color.clear
            .frame(width: width, height: height)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: grip.alignment)
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
