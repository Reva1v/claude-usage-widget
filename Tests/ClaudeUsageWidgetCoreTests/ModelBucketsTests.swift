import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("ModelBuckets")
struct ModelBucketsTests {
    private func snapshot(_ keys: [String]) -> UsageSnapshot {
        UsageSnapshot(buckets: Dictionary(uniqueKeysWithValues: keys.map {
            ($0, UsageBucket(utilization: 1, resetsAt: nil))
        }))
    }

    @Test("lists only per-model weekly buckets")
    func filtersToModelBuckets() {
        let keys = ModelBuckets.available(in: snapshot([
            "five_hour", "seven_day", "seven_day_opus", "seven_day_sonnet",
            "seven_day_oauth_apps", "seven_day_overage_included", "extra_usage",
        ]))
        #expect(keys == ["seven_day_opus", "seven_day_sonnet"])
    }

    @Test("orders known models fable, opus, sonnet")
    func ordersKnownModels() {
        let keys = ModelBuckets.available(in: snapshot([
            "seven_day_sonnet", "seven_day_fable", "seven_day_opus",
        ]))
        #expect(keys == ["seven_day_fable", "seven_day_opus", "seven_day_sonnet"])
    }

    @Test("puts unknown model buckets after the known ones, alphabetically")
    func appendsUnknownModels() {
        let keys = ModelBuckets.available(in: snapshot([
            "seven_day_zebra", "seven_day_opus", "seven_day_aardvark",
        ]))
        #expect(keys == ["seven_day_opus", "seven_day_aardvark", "seven_day_zebra"])
    }

    @Test("prefers fable when it exists")
    func defaultsToFable() {
        let key = ModelBuckets.resolve(preferred: nil, in: snapshot([
            "seven_day_opus", "seven_day_fable",
        ]))
        #expect(key == "seven_day_fable")
    }

    @Test("falls back to the first available model when fable is absent")
    func fallsBackWhenFableMissing() {
        let key = ModelBuckets.resolve(preferred: nil, in: snapshot([
            "seven_day_sonnet", "seven_day_opus",
        ]))
        #expect(key == "seven_day_opus")
    }

    @Test("honours the user's pick while it is still present")
    func honoursPreference() {
        let key = ModelBuckets.resolve(preferred: "seven_day_sonnet", in: snapshot([
            "seven_day_fable", "seven_day_sonnet",
        ]))
        #expect(key == "seven_day_sonnet")
    }

    @Test("ignores a pick the server no longer returns")
    func dropsStalePreference() {
        let key = ModelBuckets.resolve(preferred: "seven_day_haiku", in: snapshot([
            "seven_day_opus",
        ]))
        #expect(key == "seven_day_opus")
    }

    @Test("resolves to nil when there is no model bucket at all")
    func noModelBuckets() {
        #expect(ModelBuckets.resolve(preferred: nil, in: snapshot(["five_hour", "seven_day"])) == nil)
    }

    @Test("labels strip the prefix and upcase")
    func labels() {
        #expect(ModelBuckets.label(for: "seven_day_fable") == "FABLE")
        #expect(ModelBuckets.label(for: "seven_day_opus") == "OPUS")
    }
}
