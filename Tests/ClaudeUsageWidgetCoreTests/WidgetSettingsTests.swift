import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("WidgetSettings")
struct WidgetSettingsTests {
    private func defaults() -> UserDefaults {
        let suite = UserDefaults(suiteName: "WidgetSettingsTests-\(UUID().uuidString)")!
        return suite
    }

    @Test("the widget is visible when nothing has been stored yet")
    func visibleByDefault() {
        #expect(WidgetSettings.isVisible(in: defaults()) == true)
    }

    @Test("a stored false hides the widget")
    func respectsStoredVisibility() {
        let store = defaults()
        store.set(false, forKey: WidgetSettings.widgetVisibleKey)
        #expect(WidgetSettings.isVisible(in: store) == false)
    }

    @Test("no model bucket is pinned by default")
    func noDefaultModelBucket() {
        #expect(WidgetSettings.modelBucket(in: defaults()) == nil)
    }

    @Test("a stored model bucket is returned")
    func returnsStoredModelBucket() {
        let store = defaults()
        store.set("seven_day_opus", forKey: WidgetSettings.modelBucketKey)
        #expect(WidgetSettings.modelBucket(in: store) == "seven_day_opus")
    }

    @Test("an empty stored model bucket reads as no pin")
    func treatsEmptyAsUnset() {
        let store = defaults()
        store.set("", forKey: WidgetSettings.modelBucketKey)
        #expect(WidgetSettings.modelBucket(in: store) == nil)
    }
}
