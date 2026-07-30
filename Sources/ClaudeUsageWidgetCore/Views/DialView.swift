import SwiftUI

/// Where things sit on the face. Pulled out of the view so the angles are
/// assertable without rendering anything.
public enum DialGeometry {
    /// Dials start at twelve o'clock and run clockwise, like a watch.
    public static func angle(forFraction fraction: Double) -> Angle {
        .degrees(-90 + 360 * fraction)
    }

    /// The tip of the hand for a given fraction of a full revolution.
    public static func handPoint(forFraction fraction: Double, in rect: CGRect, inset: CGFloat) -> CGPoint {
        let radius = min(rect.width, rect.height) / 2 - inset
        let radians = angle(forFraction: fraction).radians
        return CGPoint(
            x: rect.midX + radius * cos(radians),
            y: rect.midY + radius * sin(radians)
        )
    }
}

/// The filled arc: the share of the limit already spent.
private struct DialArc: Shape {
    let fraction: Double
    let inset: CGFloat

    func path(in rect: CGRect) -> Path {
        var path = Path()
        path.addArc(
            center: CGPoint(x: rect.midX, y: rect.midY),
            radius: min(rect.width, rect.height) / 2 - inset,
            startAngle: DialGeometry.angle(forFraction: 0),
            endAngle: DialGeometry.angle(forFraction: fraction),
            clockwise: false
        )
        return path
    }
}

/// The hand: the wall-clock position of the reset time, watch-style.
private struct DialHand: Shape {
    let fraction: Double
    let inset: CGFloat

    func path(in rect: CGRect) -> Path {
        var path = Path()
        path.move(to: CGPoint(x: rect.midX, y: rect.midY))
        path.addLine(to: DialGeometry.handPoint(forFraction: fraction, in: rect, inset: inset))
        return path
    }
}

/// One limit: an arc for how much is spent, a hand pointing at the reset
/// time, and the numbers in the middle.
public struct DialView: View {
    private let title: String
    private let fraction: Double?
    private let hand: Double?
    private let remaining: String?
    private let dimmed: Bool

    private static let size: CGFloat = 92
    private static let arcInset: CGFloat = 5
    private static let arcWidth: CGFloat = 6
    private static let handInset: CGFloat = 16

    public init(title: String, fraction: Double?, hand: Double?, remaining: String?, dimmed: Bool) {
        self.title = title
        self.fraction = fraction
        self.hand = hand
        self.remaining = remaining
        self.dimmed = dimmed
    }

    private var arcColor: Color {
        guard let fraction, !dimmed else { return Theme.dim }
        return Theme.color(for: ThresholdLevel.level(for: fraction))
    }

    public var body: some View {
        ZStack {
            Circle()
                .strokeBorder(Theme.track, lineWidth: Self.arcWidth)
                .padding(Self.arcInset - Self.arcWidth / 2)

            if let fraction {
                DialArc(fraction: fraction, inset: Self.arcInset)
                    .stroke(arcColor, style: StrokeStyle(lineWidth: Self.arcWidth, lineCap: .round))
                    .animation(.easeOut(duration: 0.4), value: fraction)
            }

            if let hand, !dimmed {
                DialHand(fraction: hand, inset: Self.handInset)
                    .stroke(Theme.hand.opacity(0.7), style: StrokeStyle(lineWidth: 1.5, lineCap: .round))
            }

            VStack(spacing: 1) {
                Text(title)
                    .font(Theme.label)
                    .foregroundStyle(Theme.dim)
                Text(fraction.map { "\(Int(($0 * 100).rounded()))%" } ?? "n/a")
                    .font(Theme.value)
                    .foregroundStyle(dimmed ? Theme.dim : Theme.text)
                Text(remaining ?? "—")
                    .font(Theme.caption)
                    .foregroundStyle(Theme.dim)
            }
        }
        .frame(width: Self.size, height: Self.size)
    }
}
