import SwiftUI

/// Where things sit on the face. Pulled out of the view so the angles are
/// assertable without rendering anything.
public enum DialGeometry {
    /// Dials start at twelve o'clock and run clockwise, like a watch.
    public static func angle(forFraction fraction: Double) -> Angle {
        .degrees(-90 + 360 * fraction)
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

/// One limit: an arc for how much is spent and the numbers in the middle.
public struct DialView: View {
    private let title: String
    private let fraction: Double?
    private let remaining: String?
    private let dimmed: Bool
    private let size: CGFloat

    /// The dial size the inner metrics were drawn against.
    public static let designSize: CGFloat = 68

    private var scale: Double { size / Self.designSize }
    private var arcInset: CGFloat { 4 * scale }
    private var arcWidth: CGFloat { 5 * scale }

    public init(title: String, fraction: Double?, remaining: String?, dimmed: Bool, size: CGFloat) {
        self.title = title
        self.fraction = fraction
        self.remaining = remaining
        self.dimmed = dimmed
        self.size = size
    }

    private var arcColor: Color {
        guard let fraction, !dimmed else { return Theme.dim }
        return Theme.color(for: ThresholdLevel.level(for: fraction))
    }

    public var body: some View {
        ZStack {
            Circle()
                .strokeBorder(Theme.track, lineWidth: arcWidth)
                .padding(arcInset - arcWidth / 2)

            if let fraction {
                DialArc(fraction: fraction, inset: arcInset)
                    .stroke(arcColor, style: StrokeStyle(lineWidth: arcWidth, lineCap: .round))
                    .animation(.easeOut(duration: 0.4), value: fraction)
            }

            VStack(spacing: 1) {
                Text(title)
                    .font(Theme.label(scale: scale))
                    .foregroundStyle(Theme.dim)
                Text(fraction.map { "\(Int(($0 * 100).rounded()))%" } ?? "n/a")
                    .font(Theme.value(scale: scale))
                    .foregroundStyle(dimmed ? Theme.dim : Theme.text)
                Text(remaining ?? "—")
                    .font(Theme.caption(scale: scale))
                    .foregroundStyle(Theme.dim)
            }
        }
        .frame(width: size, height: size)
    }
}
