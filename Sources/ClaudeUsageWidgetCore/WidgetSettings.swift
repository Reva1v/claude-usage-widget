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

    public static func isVisible(in defaults: UserDefaults) -> Bool {
        defaults.object(forKey: widgetVisibleKey) as? Bool ?? true
    }

    public static func modelBucket(in defaults: UserDefaults) -> String? {
        let stored = defaults.string(forKey: modelBucketKey)
        return (stored?.isEmpty ?? true) ? nil : stored
    }
}
