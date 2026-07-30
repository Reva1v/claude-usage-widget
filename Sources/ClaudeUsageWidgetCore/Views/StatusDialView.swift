import SwiftUI

/// The fourth dial: Claude's own service status. The ring is filled solid
/// rather than to a fraction — there is no percentage here, only a state — so
/// the cell reads as part of the same family without pretending to be a gauge.
public struct StatusDialView: View {
    private let status: ServiceStatus
    private let size: CGFloat
    private let scale: Double
    private let dimmed: Bool

    private var arcInset: CGFloat { 4 * scale }
    private var arcWidth: CGFloat { 5 * scale }

    public init(status: ServiceStatus, size: CGFloat, scale: Double, dimmed: Bool) {
        self.status = status
        self.size = size
        self.scale = scale
        self.dimmed = dimmed
    }

    private var ringColor: Color {
        dimmed || status == .unknown ? Theme.dim : Theme.color(for: status)
    }

    public var body: some View {
        ZStack {
            Circle()
                .strokeBorder(Theme.track, lineWidth: arcWidth)
                .padding(arcInset - arcWidth / 2)

            Circle()
                .strokeBorder(ringColor, lineWidth: arcWidth)
                .padding(arcInset - arcWidth / 2)
                .opacity(status == .unknown ? 0.4 : 1)

            VStack(spacing: 1) {
                Text("STATUS")
                    .font(Theme.label(scale: scale))
                    .foregroundStyle(Theme.dim)
                Text(status.label)
                    .font(Theme.value(scale: scale))
                    .foregroundStyle(dimmed ? Theme.dim : Theme.text)
                    .minimumScaleFactor(0.6)
                    .lineLimit(1)
            }
            .padding(.horizontal, 6 * scale)
        }
        .frame(width: size, height: size)
        .contentShape(Rectangle())
    }
}
