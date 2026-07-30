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

    @Test("the default side is used when nothing is stored")
    func defaultSize() {
        #expect(WidgetSettings.size(in: defaults()) == WidgetSettings.defaultSize)
    }

    @Test("a stored side is returned")
    func storedSize() {
        let store = defaults()
        store.set(240.0, forKey: WidgetSettings.widgetSizeKey)
        #expect(WidgetSettings.size(in: store) == 240)
    }

    @Test("a stored side outside the bounds is clamped on read")
    func clampsStoredSize() {
        let small = defaults()
        small.set(10.0, forKey: WidgetSettings.widgetSizeKey)
        #expect(WidgetSettings.size(in: small) == WidgetSettings.minSize)

        let large = defaults()
        large.set(9000.0, forKey: WidgetSettings.widgetSizeKey)
        #expect(WidgetSettings.size(in: large) == WidgetSettings.maxSize)
    }

    @Test("clampSize pins values to the allowed range")
    func clampSize() {
        #expect(WidgetSettings.clampSize(WidgetSettings.minSize - 1) == WidgetSettings.minSize)
        #expect(WidgetSettings.clampSize(WidgetSettings.maxSize + 1) == WidgetSettings.maxSize)
        #expect(WidgetSettings.clampSize(200) == 200)
    }
}
