import Foundation

/// UserDefaults keys shared by the app shell and the views.
public enum WidgetSettings {
    /// Pins the widget in place: blocks dragging.
    public static let positionLockedKey = "positionLocked"

    /// Whether the desktop window is on screen. Polling keeps running while it
    /// is hidden, so bringing it back shows fresh dials.
    public static let widgetVisibleKey = "widgetVisible"

    /// The per-model bucket the third dial is pinned to. Empty or absent means
    /// "let ModelBuckets choose".
    public static let modelBucketKey = "modelBucket"

    /// Side length of the square panel, in points. One value drives both axes —
    /// the widget is always a square, so resizing cannot distort it.
    public static let widgetSizeKey = "widgetSize"

    /// The layout is designed at 170 pt (the macOS small desktop widget
    /// footprint) and scales linearly from there.
    public static let defaultSize: Double = 170
    public static let minSize: Double = 150
    public static let maxSize: Double = 340

    public static func isVisible(in defaults: UserDefaults) -> Bool {
        defaults.object(forKey: widgetVisibleKey) as? Bool ?? true
    }

    public static func modelBucket(in defaults: UserDefaults) -> String? {
        let stored = defaults.string(forKey: modelBucketKey)
        return (stored?.isEmpty ?? true) ? nil : stored
    }

    public static func clampSize(_ size: Double) -> Double {
        min(max(size, minSize), maxSize)
    }

    public static func size(in defaults: UserDefaults) -> Double {
        guard let stored = defaults.object(forKey: widgetSizeKey) as? Double else { return defaultSize }
        return clampSize(stored)
    }
}
