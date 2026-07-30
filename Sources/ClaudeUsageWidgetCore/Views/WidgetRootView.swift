import SwiftUI

/// The panel: three dials in a row, with a status line when something needs
/// saying.
public struct WidgetRootView: View {
    private let store: UsageStore

    @AppStorage(WidgetSettings.modelBucketKey) private var modelBucket = ""

    /// Drives the hands and the "updated N ago" line between fetches — the
    /// snapshot only changes every five minutes, but time does not.
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

        VStack(spacing: 8) {
            HStack(spacing: 14) {
                ForEach(DialModel.all(
                    snapshot: snapshot,
                    preferredModelKey: modelBucket.isEmpty ? nil : modelBucket,
                    now: now
                ), id: \.title) { model in
                    DialView(
                        title: model.title,
                        fraction: model.fraction,
                        elapsed: model.elapsed,
                        remaining: model.remaining,
                        dimmed: dimmed
                    )
                }
            }

            if let status {
                Text(status)
                    .font(Theme.caption)
                    .foregroundStyle(Theme.dim)
            }
        }
        .padding(16)
        .background(
            RoundedRectangle(cornerRadius: 22, style: .continuous)
                .fill(Theme.panel.opacity(0.86))
        )
        .onReceive(Self.tick) { now = $0 }
    }
}
