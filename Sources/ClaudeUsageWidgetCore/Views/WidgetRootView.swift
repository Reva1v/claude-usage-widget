import SwiftUI

/// The panel: a 2x2 grid of dials, with a status line when something needs
/// saying.
public struct WidgetRootView: View {
    private let store: UsageStore

    @AppStorage(WidgetSettings.modelBucketKey) private var modelBucket = ""

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

    public var body: some View {
        let status = StatusLine.text(for: store.state, now: now)
        let models = DialModel.all(
            snapshot: snapshot,
            preferredModelKey: modelBucket.isEmpty ? nil : modelBucket,
            now: now
        )

        VStack(spacing: 8) {
            VStack(spacing: 10) {
                HStack(spacing: 10) {
                    dial(models[0])
                    dial(models[1])
                }
                HStack(spacing: 10) {
                    dial(models[2])
                    PlaceholderDial()
                }
            }

            if let status {
                Text(status)
                    .font(Theme.caption)
                    .foregroundStyle(Theme.dim)
            }
        }
        .padding(12)
        .background(
            RoundedRectangle(cornerRadius: 22, style: .continuous)
                .fill(Theme.panel.opacity(0.86))
        )
        .onReceive(Self.tick) { now = $0 }
    }

    private func dial(_ model: DialModel) -> some View {
        DialView(
            title: model.title,
            fraction: model.fraction,
            remaining: model.remaining,
            dimmed: dimmed
        )
    }
}

/// The empty fourth cell: a bare ring holding the spot until it has a job.
private struct PlaceholderDial: View {
    var body: some View {
        Circle()
            .strokeBorder(Theme.track.opacity(0.5), lineWidth: 5)
            .padding(4 - 5 / 2)
            .frame(width: DialView.size, height: DialView.size)
    }
}
