# Claude Usage Widget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A macOS desktop widget showing Claude Code subscription limits as three dials — the 5-hour session limit, the 7-day weekly limit, and one model-specific weekly limit.

**Architecture:** A Swift Package with a `ClaudeUsageWidgetCore` library and a thin `ClaudeUsageWidget` app shell, mirroring the sibling project `mole-widget`. All computation lives in pure functions over a decoded snapshot and is unit-tested; the Keychain reader and the HTTP client are thin wrappers with smoke tests. The app shell is a borderless window at desktop level plus a `MenuBarExtra`.

**Tech Stack:** Swift 6 toolchain in language mode v5, SwiftUI, Observation, Security framework (Keychain), Swift Testing. No third-party dependencies.

## Global Constraints

- Minimum platform: macOS 14. Declared as `platforms: [.macOS(.v14)]` and `LSMinimumSystemVersion` `14.0`.
- Swift tools version 6.0, every target built with `.swiftLanguageMode(.v5)`.
- Zero third-party package dependencies. Sparkle is explicitly out of scope for this plan.
- Bundle identifier: `com.sbezbabnykh.claude-usage-widget`. App name: `Claude Usage Widget`. Executable name: `ClaudeUsageWidget`.
- Run tests only via `make test`. On a machine without full Xcode, a bare `swift test` silently runs zero tests and exits 0 — the Makefile passes the toolchain flags Swift Testing needs from Command Line Tools.
- The widget only ever **reads** the Keychain. It never writes, deletes, or refreshes credentials.
- API endpoint: `https://api.anthropic.com/api/oauth/usage`. Required headers: `Authorization: Bearer <token>`, `anthropic-beta: oauth-2025-04-20`, `Accept: application/json`.
- Refresh interval: 300 seconds. Also refreshes on demand from the menu and when the Mac wakes from sleep.
- Utilization arrives from the server on a 0–100 scale; every internal fraction is 0–1.
- Threshold colors: green below 0.6, amber below 0.85, red at or above 0.85.
- Reference spec: `docs/superpowers/specs/2026-07-29-claude-usage-widget-design.md`.

---

## File Structure

| File | Responsibility |
|---|---|
| `Package.swift` | Package manifest: core library, app executable, test target |
| `Makefile` | `run` / `test` / `app` targets, Swift Testing framework flags |
| `Resources/Info.plist` | Bundle metadata, `LSUIElement` |
| `Sources/ClaudeUsageWidgetCore/CoreInfo.swift` | Version string shared by the menu and the plist |
| `Sources/ClaudeUsageWidgetCore/Usage/UsageTypes.swift` | `UsageBucket`, `UsageSnapshot`, `UsageError` |
| `Sources/ClaudeUsageWidgetCore/Usage/UsageDecoder.swift` | Tolerant JSON → `UsageSnapshot` |
| `Sources/ClaudeUsageWidgetCore/Usage/UsageMath.swift` | Window length, elapsed fraction, remaining-time text, utilization fraction |
| `Sources/ClaudeUsageWidgetCore/Usage/ThresholdLevel.swift` | Fraction → ok / warning / danger |
| `Sources/ClaudeUsageWidgetCore/Usage/ModelBuckets.swift` | Which per-model keys exist, which one the third dial shows, its label |
| `Sources/ClaudeUsageWidgetCore/Auth/ClaudeCredentials.swift` | Reads the Claude Code OAuth token from the Keychain |
| `Sources/ClaudeUsageWidgetCore/Usage/UsageAPI.swift` | The HTTP request and its status-code mapping |
| `Sources/ClaudeUsageWidgetCore/Store/UsageStore.swift` | `@Observable` state machine, refresh timer, wake handling |
| `Sources/ClaudeUsageWidgetCore/WidgetSettings.swift` | UserDefaults keys and defaults |
| `Sources/ClaudeUsageWidgetCore/Views/Theme.swift` | Colors and fonts |
| `Sources/ClaudeUsageWidgetCore/Views/DialView.swift` | One dial: arc, hand, center labels |
| `Sources/ClaudeUsageWidgetCore/Views/WidgetRootView.swift` | The three-dial panel and the status line |
| `Sources/ClaudeUsageWidget/ClaudeUsageWidgetApp.swift` | App shell: desktop window, `MenuBarExtra`, launch at login |

---

### Task 1: Package skeleton that builds and tests

**Files:**
- Create: `Package.swift`
- Create: `Makefile`
- Create: `Sources/ClaudeUsageWidgetCore/CoreInfo.swift`
- Create: `Sources/ClaudeUsageWidget/main.swift`
- Test: `Tests/ClaudeUsageWidgetCoreTests/CoreInfoTests.swift`

**Interfaces:**
- Consumes: nothing.
- Produces: `CoreInfo.version: String`. Package targets `ClaudeUsageWidgetCore`, `ClaudeUsageWidget`, `ClaudeUsageWidgetCoreTests`. A working `make test`.

`Sources/ClaudeUsageWidget/main.swift` is a one-line placeholder in this task so the executable target compiles; Task 12 replaces it with the real app shell.

- [ ] **Step 1: Write `Package.swift`**

```swift
// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ClaudeUsageWidget",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "ClaudeUsageWidgetCore", targets: ["ClaudeUsageWidgetCore"]),
        .executable(name: "ClaudeUsageWidget", targets: ["ClaudeUsageWidget"]),
    ],
    targets: [
        .target(
            name: "ClaudeUsageWidgetCore",
            path: "Sources/ClaudeUsageWidgetCore",
            swiftSettings: [.swiftLanguageMode(.v5)]
        ),
        .executableTarget(
            name: "ClaudeUsageWidget",
            dependencies: ["ClaudeUsageWidgetCore"],
            path: "Sources/ClaudeUsageWidget",
            swiftSettings: [.swiftLanguageMode(.v5)]
        ),
        .testTarget(
            name: "ClaudeUsageWidgetCoreTests",
            dependencies: ["ClaudeUsageWidgetCore"],
            path: "Tests/ClaudeUsageWidgetCoreTests",
            swiftSettings: [
                .swiftLanguageMode(.v5),
                // Swift Testing in Command Line Tools ships as a separate framework
                // (these flags are harmless with a full Xcode install)
                .unsafeFlags(["-F", "/Library/Developer/CommandLineTools/Library/Developer/Frameworks"]),
            ],
            linkerSettings: [
                .unsafeFlags([
                    "-F", "/Library/Developer/CommandLineTools/Library/Developer/Frameworks",
                    "-Xlinker", "-rpath",
                    "-Xlinker", "/Library/Developer/CommandLineTools/Library/Developer/Frameworks",
                    "-Xlinker", "-rpath",
                    "-Xlinker", "/Library/Developer/CommandLineTools/Library/Developer/usr/lib",
                ])
            ]
        ),
    ]
)
```

- [ ] **Step 2: Write the Makefile**

```make
# Swift Testing in Command Line Tools (without full Xcode) ships as a separate
# framework; the SwiftPM test runner needs a global -F flag, otherwise
# canImport(Testing) == false and tests silently do not run.
FRAMEWORKS = /Library/Developer/CommandLineTools/Library/Developer/Frameworks
TESTFLAGS = -Xswiftc -F -Xswiftc $(FRAMEWORKS)

APP_NAME = Claude Usage Widget
DIST = dist/$(APP_NAME).app

.PHONY: run test app clean

run:
	swift run ClaudeUsageWidget

# make test              — run all tests
# make test FILTER=Usage — run only suites/tests matching FILTER
test:
	swift test $(TESTFLAGS) $(if $(FILTER),--filter $(FILTER))

clean:
	rm -rf .build dist
```

The `app` target is added in Task 13, once there is an app shell to package.

- [ ] **Step 3: Write `CoreInfo.swift`**

```swift
import Foundation

/// Version string shown in the menu bar. Kept in sync by hand with
/// `CFBundleShortVersionString` in Resources/Info.plist.
public enum CoreInfo {
    public static let version = "0.1.0"
}
```

- [ ] **Step 4: Write the placeholder executable**

```swift
// Placeholder so the executable target compiles before the app shell exists.
// Replaced by ClaudeUsageWidgetApp.swift in Task 12.
print("Claude Usage Widget")
```

- [ ] **Step 5: Write the failing test**

`Tests/ClaudeUsageWidgetCoreTests/CoreInfoTests.swift`:

```swift
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("CoreInfo")
struct CoreInfoTests {
    @Test("version is a non-empty dotted string")
    func versionIsPresent() {
        #expect(!CoreInfo.version.isEmpty)
        #expect(CoreInfo.version.contains("."))
    }
}
```

- [ ] **Step 6: Run the test suite**

Run: `make test`
Expected: PASS, 1 test. If it reports "0 tests" the framework flags are not reaching the compiler — check the `FRAMEWORKS` path exists.

- [ ] **Step 7: Commit**

```bash
git add Package.swift Makefile Sources Tests
git commit -m "chore: scaffold the Swift package and test harness"
```

---

### Task 2: Decode the usage response

**Files:**
- Create: `Sources/ClaudeUsageWidgetCore/Usage/UsageTypes.swift`
- Create: `Sources/ClaudeUsageWidgetCore/Usage/UsageDecoder.swift`
- Test: `Tests/ClaudeUsageWidgetCoreTests/UsageDecoderTests.swift`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `struct UsageBucket: Equatable, Sendable { let utilization: Double; let resetsAt: Date? }`
  - `struct UsageSnapshot: Equatable, Sendable { let buckets: [String: UsageBucket]; init(buckets:); subscript(String) -> UsageBucket? }`
  - `enum UsageError: Error, Equatable, Sendable { case noCredentials, unauthorized, malformedResponse, network(String) }`
  - `UsageDecoder.snapshot(from data: Data) throws -> UsageSnapshot`

- [ ] **Step 1: Write the failing tests**

`Tests/ClaudeUsageWidgetCoreTests/UsageDecoderTests.swift`:

```swift
import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("UsageDecoder")
struct UsageDecoderTests {
    /// Shaped after the real /api/oauth/usage payload: a flat object whose
    /// values are usage buckets, plus scalar members that are not buckets.
    static let payload = Data("""
    {
      "five_hour":            { "utilization": 42,   "resets_at": "2026-07-29T18:00:00Z" },
      "seven_day":            { "utilization": 17.5, "resets_at": "2026-08-02T00:00:00Z" },
      "seven_day_opus":       { "utilization": 3,    "resets_at": "2026-08-02T00:00:00Z" },
      "seven_day_oauth_apps": { "utilization": 0,    "resets_at": null },
      "currency":             "EUR"
    }
    """.utf8)

    @Test("keeps every object member that carries a utilization")
    func decodesBuckets() throws {
        let snapshot = try UsageDecoder.snapshot(from: Self.payload)
        #expect(snapshot.buckets.count == 4)
        #expect(snapshot["five_hour"]?.utilization == 42)
        #expect(snapshot["seven_day"]?.utilization == 17.5)
    }

    @Test("drops members that are not usage buckets")
    func skipsScalars() throws {
        let snapshot = try UsageDecoder.snapshot(from: Self.payload)
        #expect(snapshot["currency"] == nil)
    }

    @Test("keeps an unknown bucket key")
    func keepsUnknownKeys() throws {
        let data = Data(#"{"seven_day_fable": {"utilization": 8, "resets_at": null}}"#.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        #expect(snapshot["seven_day_fable"]?.utilization == 8)
    }

    @Test("parses an ISO 8601 reset time, with or without fractional seconds")
    func parsesDates() throws {
        let data = Data("""
        {
          "a": { "utilization": 1, "resets_at": "2026-07-29T18:00:00Z" },
          "b": { "utilization": 1, "resets_at": "2026-07-29T18:00:00.123Z" }
        }
        """.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        let expected = Date(timeIntervalSince1970: 1_785_348_000)
        #expect(snapshot["a"]?.resetsAt == expected)
        #expect(snapshot["b"]?.resetsAt?.timeIntervalSince(expected) ?? 1 < 0.5)
    }

    @Test("parses a numeric reset time as a unix timestamp")
    func parsesEpochDates() throws {
        let data = Data(#"{"a": {"utilization": 1, "resets_at": 1785348000}}"#.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        #expect(snapshot["a"]?.resetsAt == Date(timeIntervalSince1970: 1_785_348_000))
    }

    @Test("a missing reset time decodes as nil, not as an error")
    func toleratesMissingResetTime() throws {
        let data = Data(#"{"a": {"utilization": 1}}"#.utf8)
        let snapshot = try UsageDecoder.snapshot(from: data)
        #expect(snapshot["a"]?.resetsAt == nil)
    }

    @Test("a body with no buckets is malformed")
    func rejectsBucketlessBody() {
        #expect(throws: UsageError.malformedResponse) {
            try UsageDecoder.snapshot(from: Data(#"{"currency": "EUR"}"#.utf8))
        }
    }

    @Test("a non-JSON body is malformed")
    func rejectsGarbage() {
        #expect(throws: UsageError.malformedResponse) {
            try UsageDecoder.snapshot(from: Data("<html>Just a moment</html>".utf8))
        }
    }
}
```

`1_785_348_000` is `2026-07-29T18:00:00Z`. If the assertion fails, print `expected.description` once and correct the literal rather than loosening the test.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `make test FILTER=UsageDecoder`
Expected: FAIL — `cannot find 'UsageDecoder' in scope`.

- [ ] **Step 3: Write `UsageTypes.swift`**

```swift
import Foundation

/// One usage bucket as returned by /api/oauth/usage.
public struct UsageBucket: Equatable, Sendable {
    /// Percentage of the limit consumed, on the server's 0...100 scale.
    public let utilization: Double
    /// When this window rolls over. Absent for buckets the account does not use.
    public let resetsAt: Date?

    public init(utilization: Double, resetsAt: Date?) {
        self.utilization = utilization
        self.resetsAt = resetsAt
    }
}

/// A decoded /api/oauth/usage response.
///
/// Deliberately a dictionary rather than a struct with fixed properties: the
/// set of bucket keys changes as models come and go, and neither a new key nor
/// a vanished one should require a code change.
public struct UsageSnapshot: Equatable, Sendable {
    public let buckets: [String: UsageBucket]

    public init(buckets: [String: UsageBucket]) {
        self.buckets = buckets
    }

    public subscript(key: String) -> UsageBucket? { buckets[key] }
}

public enum UsageError: Error, Equatable, Sendable {
    /// No Claude Code credentials were found in the Keychain.
    case noCredentials
    /// The endpoint rejected the token — Claude Code needs a fresh login.
    case unauthorized
    /// The body was not JSON, or carried no usage buckets at all.
    case malformedResponse
    /// Transport failure or an unexpected status code.
    case network(String)
}
```

- [ ] **Step 4: Write `UsageDecoder.swift`**

```swift
import Foundation

/// Turns a raw /api/oauth/usage body into a snapshot.
///
/// Uses JSONSerialization rather than Codable because the response is an open
/// map: unknown keys must survive, and members that are not buckets (a plain
/// currency string, for instance) must be ignored instead of failing the parse.
public enum UsageDecoder {
    public static func snapshot(from data: Data) throws -> UsageSnapshot {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw UsageError.malformedResponse
        }

        var buckets: [String: UsageBucket] = [:]
        for (key, value) in root {
            guard let object = value as? [String: Any],
                  let utilization = object["utilization"] as? Double else { continue }
            buckets[key] = UsageBucket(
                utilization: utilization,
                resetsAt: resetDate(from: object["resets_at"])
            )
        }

        guard !buckets.isEmpty else { throw UsageError.malformedResponse }
        return UsageSnapshot(buckets: buckets)
    }

    /// Accepts both an ISO 8601 string and a unix timestamp. The exact wire
    /// format could not be observed while writing this, so both are handled.
    private static func resetDate(from value: Any?) -> Date? {
        if let string = value as? String {
            return fractionalFormatter.date(from: string) ?? plainFormatter.date(from: string)
        }
        if let seconds = value as? Double, seconds > 0 {
            return Date(timeIntervalSince1970: seconds)
        }
        return nil
    }

    private static let plainFormatter = ISO8601DateFormatter()

    private static let fractionalFormatter: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `make test FILTER=UsageDecoder`
Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add Sources/ClaudeUsageWidgetCore/Usage Tests/ClaudeUsageWidgetCoreTests/UsageDecoderTests.swift
git commit -m "feat: decode the oauth usage response into a snapshot"
```

---

### Task 3: Window and time math

**Files:**
- Create: `Sources/ClaudeUsageWidgetCore/Usage/UsageMath.swift`
- Test: `Tests/ClaudeUsageWidgetCoreTests/UsageMathTests.swift`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `UsageMath.windowLength(forKey: String) -> TimeInterval?`
  - `UsageMath.elapsedFraction(resetsAt: Date?, window: TimeInterval?, now: Date) -> Double?`
  - `UsageMath.remainingText(resetsAt: Date?, now: Date) -> String?`
  - `UsageMath.fraction(_ utilization: Double) -> Double`

- [ ] **Step 1: Write the failing tests**

`Tests/ClaudeUsageWidgetCoreTests/UsageMathTests.swift`:

```swift
import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("UsageMath")
struct UsageMathTests {
    static let now = Date(timeIntervalSince1970: 1_785_348_000)
    static let fiveHours: TimeInterval = 5 * 3600
    static let sevenDays: TimeInterval = 7 * 24 * 3600

    // MARK: windowLength

    @Test("the session bucket is a five hour window")
    func sessionWindow() {
        #expect(UsageMath.windowLength(forKey: "five_hour") == Self.fiveHours)
    }

    @Test("every seven_day bucket is a seven day window")
    func weeklyWindow() {
        #expect(UsageMath.windowLength(forKey: "seven_day") == Self.sevenDays)
        #expect(UsageMath.windowLength(forKey: "seven_day_fable") == Self.sevenDays)
        #expect(UsageMath.windowLength(forKey: "seven_day_opus") == Self.sevenDays)
    }

    @Test("an unrecognised key has no known window")
    func unknownWindow() {
        #expect(UsageMath.windowLength(forKey: "extra_usage") == nil)
    }

    // MARK: elapsedFraction

    @Test("halfway through the window reads as 0.5")
    func midWindow() {
        let resetsAt = Self.now.addingTimeInterval(Self.fiveHours / 2)
        let fraction = UsageMath.elapsedFraction(resetsAt: resetsAt, window: Self.fiveHours, now: Self.now)
        #expect(fraction == 0.5)
    }

    @Test("a window that just opened reads as 0")
    func freshWindow() {
        let resetsAt = Self.now.addingTimeInterval(Self.fiveHours)
        #expect(UsageMath.elapsedFraction(resetsAt: resetsAt, window: Self.fiveHours, now: Self.now) == 0)
    }

    @Test("a reset time longer away than the window clamps to 0")
    func overlongWindow() {
        let resetsAt = Self.now.addingTimeInterval(Self.fiveHours * 2)
        #expect(UsageMath.elapsedFraction(resetsAt: resetsAt, window: Self.fiveHours, now: Self.now) == 0)
    }

    @Test("a reset time in the past yields nil rather than a guessed angle")
    func expiredWindow() {
        let resetsAt = Self.now.addingTimeInterval(-60)
        #expect(UsageMath.elapsedFraction(resetsAt: resetsAt, window: Self.fiveHours, now: Self.now) == nil)
    }

    @Test("a missing reset time or window yields nil")
    func missingInputs() {
        #expect(UsageMath.elapsedFraction(resetsAt: nil, window: Self.fiveHours, now: Self.now) == nil)
        #expect(UsageMath.elapsedFraction(resetsAt: Self.now.addingTimeInterval(60), window: nil, now: Self.now) == nil)
    }

    // MARK: remainingText

    @Test("under a minute reads in seconds")
    func remainingSeconds() {
        #expect(UsageMath.remainingText(resetsAt: Self.now.addingTimeInterval(59), now: Self.now) == "59s")
    }

    @Test("under an hour reads in minutes")
    func remainingMinutes() {
        #expect(UsageMath.remainingText(resetsAt: Self.now.addingTimeInterval(600), now: Self.now) == "10m")
    }

    @Test("exactly one hour reads as hours and minutes")
    func remainingExactHour() {
        #expect(UsageMath.remainingText(resetsAt: Self.now.addingTimeInterval(3600), now: Self.now) == "1h 0m")
    }

    @Test("over a day reads as days and hours")
    func remainingDays() {
        #expect(UsageMath.remainingText(resetsAt: Self.now.addingTimeInterval(90_000), now: Self.now) == "1d 1h")
    }

    @Test("a reset time in the past has no remaining text")
    func remainingExpired() {
        #expect(UsageMath.remainingText(resetsAt: Self.now.addingTimeInterval(-1), now: Self.now) == nil)
        #expect(UsageMath.remainingText(resetsAt: nil, now: Self.now) == nil)
    }

    // MARK: fraction

    @Test("utilization converts from the server's 0-100 scale and clamps")
    func fractionScaling() {
        #expect(UsageMath.fraction(0) == 0)
        #expect(UsageMath.fraction(42) == 0.42)
        #expect(UsageMath.fraction(100) == 1)
        #expect(UsageMath.fraction(140) == 1)
        #expect(UsageMath.fraction(-5) == 0)
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `make test FILTER=UsageMath`
Expected: FAIL — `cannot find 'UsageMath' in scope`.

- [ ] **Step 3: Write `UsageMath.swift`**

```swift
import Foundation

/// Pure arithmetic behind the dials. No I/O, no clock reads — `now` is always
/// passed in so every case is reproducible in a test.
public enum UsageMath {
    /// How long the window behind a bucket key runs.
    ///
    /// The API returns only `resets_at`, never the length of the window, so the
    /// length is derived from the key: `five_hour` is a rolling five hours and
    /// every `seven_day*` bucket is a rolling seven days.
    public static func windowLength(forKey key: String) -> TimeInterval? {
        if key == "five_hour" { return 5 * 3600 }
        if key.hasPrefix("seven_day") { return 7 * 24 * 3600 }
        return nil
    }

    /// How far the current window has already run, as 0...1 — the angle of the
    /// dial's hand.
    ///
    /// Returns nil when the answer is unknowable: no reset time, no window
    /// length, or a reset time that has already passed. A stale snapshot must
    /// hide the hand rather than draw it at a guessed angle.
    public static func elapsedFraction(resetsAt: Date?, window: TimeInterval?, now: Date) -> Double? {
        guard let resetsAt, let window, window > 0 else { return nil }
        let remaining = resetsAt.timeIntervalSince(now)
        guard remaining > 0 else { return nil }
        return min(max((window - remaining) / window, 0), 1)
    }

    /// Time left in the window: "45s", "10m", "1h 0m", "1d 1h".
    /// Nil when there is no reset time or it has already passed.
    public static func remainingText(resetsAt: Date?, now: Date) -> String? {
        guard let resetsAt else { return nil }
        let seconds = Int(resetsAt.timeIntervalSince(now).rounded(.down))
        guard seconds > 0 else { return nil }

        let days = seconds / 86_400
        let hours = (seconds % 86_400) / 3600
        let minutes = (seconds % 3600) / 60

        if days > 0 { return "\(days)d \(hours)h" }
        if hours > 0 { return "\(hours)h \(minutes)m" }
        if minutes > 0 { return "\(minutes)m" }
        return "\(seconds)s"
    }

    /// The server reports utilization on a 0...100 scale; everything inside the
    /// widget works in 0...1.
    public static func fraction(_ utilization: Double) -> Double {
        min(max(utilization / 100, 0), 1)
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `make test FILTER=UsageMath`
Expected: PASS, 14 tests.

- [ ] **Step 5: Commit**

```bash
git add Sources/ClaudeUsageWidgetCore/Usage/UsageMath.swift Tests/ClaudeUsageWidgetCoreTests/UsageMathTests.swift
git commit -m "feat: add window progress and remaining-time math"
```

---

### Task 4: Threshold levels

**Files:**
- Create: `Sources/ClaudeUsageWidgetCore/Usage/ThresholdLevel.swift`
- Test: `Tests/ClaudeUsageWidgetCoreTests/ThresholdLevelTests.swift`

**Interfaces:**
- Consumes: nothing.
- Produces: `enum ThresholdLevel: Equatable, Sendable { case ok, warning, danger }` with `static func level(for fraction: Double) -> ThresholdLevel`.

The level is a separate type from the color so the thresholds can be asserted directly. Comparing two `SwiftUI.Color` values in a test is fragile; comparing two enum cases is not.

- [ ] **Step 1: Write the failing test**

`Tests/ClaudeUsageWidgetCoreTests/ThresholdLevelTests.swift`:

```swift
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("ThresholdLevel")
struct ThresholdLevelTests {
    @Test("below 60 percent is ok")
    func okBand() {
        #expect(ThresholdLevel.level(for: 0) == .ok)
        #expect(ThresholdLevel.level(for: 0.59) == .ok)
    }

    @Test("60 percent up to 85 is a warning")
    func warningBand() {
        #expect(ThresholdLevel.level(for: 0.6) == .warning)
        #expect(ThresholdLevel.level(for: 0.84) == .warning)
    }

    @Test("85 percent and above is danger")
    func dangerBand() {
        #expect(ThresholdLevel.level(for: 0.85) == .danger)
        #expect(ThresholdLevel.level(for: 1) == .danger)
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `make test FILTER=ThresholdLevel`
Expected: FAIL — `cannot find 'ThresholdLevel' in scope`.

- [ ] **Step 3: Write `ThresholdLevel.swift`**

```swift
/// How alarming a utilization fraction is. Kept separate from the palette so
/// the thresholds are testable without touching SwiftUI.
public enum ThresholdLevel: Equatable, Sendable {
    case ok
    case warning
    case danger

    public static func level(for fraction: Double) -> ThresholdLevel {
        switch fraction {
        case ..<0.6: .ok
        case ..<0.85: .warning
        default: .danger
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `make test FILTER=ThresholdLevel`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add Sources/ClaudeUsageWidgetCore/Usage/ThresholdLevel.swift Tests/ClaudeUsageWidgetCoreTests/ThresholdLevelTests.swift
git commit -m "feat: map a utilization fraction to a threshold level"
```

---

### Task 5: Pick the third dial's bucket

**Files:**
- Create: `Sources/ClaudeUsageWidgetCore/Usage/ModelBuckets.swift`
- Test: `Tests/ClaudeUsageWidgetCoreTests/ModelBucketsTests.swift`

**Interfaces:**
- Consumes: `UsageSnapshot` from Task 2.
- Produces:
  - `ModelBuckets.available(in: UsageSnapshot) -> [String]`
  - `ModelBuckets.resolve(preferred: String?, in: UsageSnapshot) -> String?`
  - `ModelBuckets.label(for key: String) -> String`

- [ ] **Step 1: Write the failing tests**

`Tests/ClaudeUsageWidgetCoreTests/ModelBucketsTests.swift`:

```swift
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `make test FILTER=ModelBuckets`
Expected: FAIL — `cannot find 'ModelBuckets' in scope`.

- [ ] **Step 3: Write `ModelBuckets.swift`**

```swift
import Foundation

/// Decides which per-model weekly limit the third dial shows.
public enum ModelBuckets {
    /// Known per-model keys in display preference order. Fable first: it is the
    /// limit this widget was built to watch.
    private static let preference = ["seven_day_fable", "seven_day_opus", "seven_day_sonnet"]

    /// `seven_day_*` keys that are not per-model limits and must never land on
    /// the dial.
    private static let excluded: Set<String> = ["seven_day_oauth_apps", "seven_day_overage_included"]

    /// Every per-model bucket in a snapshot: the known ones in preference
    /// order, then any unrecognised model key alphabetically.
    public static func available(in snapshot: UsageSnapshot) -> [String] {
        let all = snapshot.buckets.keys.filter(isModelKey)
        let known = preference.filter(all.contains)
        let rest = all.filter { !preference.contains($0) }.sorted()
        return known + rest
    }

    static func isModelKey(_ key: String) -> Bool {
        key.hasPrefix("seven_day_") && !excluded.contains(key)
    }

    /// The bucket the dial should show: the user's pick while the server still
    /// returns it, otherwise the first available model bucket.
    public static func resolve(preferred: String?, in snapshot: UsageSnapshot) -> String? {
        let available = available(in: snapshot)
        if let preferred, available.contains(preferred) { return preferred }
        return available.first
    }

    /// "seven_day_fable" -> "FABLE"
    public static func label(for key: String) -> String {
        key.hasPrefix("seven_day_")
            ? String(key.dropFirst("seven_day_".count)).uppercased()
            : key.uppercased()
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `make test FILTER=ModelBuckets`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add Sources/ClaudeUsageWidgetCore/Usage/ModelBuckets.swift Tests/ClaudeUsageWidgetCoreTests/ModelBucketsTests.swift
git commit -m "feat: select and label the per-model dial bucket"
```

---

### Task 6: Read the Claude Code token from the Keychain

**Files:**
- Create: `Sources/ClaudeUsageWidgetCore/Auth/ClaudeCredentials.swift`
- Test: `Tests/ClaudeUsageWidgetCoreTests/ClaudeCredentialsTests.swift`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `ClaudeCredentials.accessToken() -> String?`
  - `ClaudeCredentials.accessToken(fromItemData: Data) -> String?` (internal, the tested half)

**This task starts with a manual verification step.** The spec flags the exact Keychain item as the one unverified assumption; the strings in the Claude Code binary confirm the stored blob carries `claudeAiOauth`, `accessToken` and `refreshToken`, and that the CLI reads it with `security find-generic-password -a "<account>" -w -s "<service>"`, but the service name itself is built at runtime and could not be read out.

- [ ] **Step 1: Confirm the Keychain item exists**

Run, and read the output rather than the secret:

```bash
security find-generic-password -s "Claude Code-credentials" 2>&1 | grep -E '"(svce|acct)"'
```

Expected: two lines naming the service and the account. If instead it prints `SecKeychainSearch... The specified item could not be found`, find the real service name with:

```bash
security dump-keychain 2>/dev/null | grep -i -A1 '"svce".*[Cc]laude' | head -20
```

Record the exact service string. If it differs from `Claude Code-credentials`, use it as the `service` constant in Step 4 and note the difference in the commit message.

- [ ] **Step 2: Write the failing test**

Only the JSON extraction is unit-tested — the Keychain query itself needs a real login keychain and is covered by the smoke test in Step 6.

`Tests/ClaudeUsageWidgetCoreTests/ClaudeCredentialsTests.swift`:

```swift
import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("ClaudeCredentials")
struct ClaudeCredentialsTests {
    @Test("reads the token out of the claudeAiOauth blob")
    func readsNestedToken() {
        let data = Data("""
        {
          "claudeAiOauth": {
            "accessToken": "sk-ant-oat01-example",
            "refreshToken": "sk-ant-ort01-example",
            "expiresAt": 1785348000
          }
        }
        """.utf8)
        #expect(ClaudeCredentials.accessToken(fromItemData: data) == "sk-ant-oat01-example")
    }

    @Test("accepts a flat blob that carries the token at the top level")
    func readsFlatToken() {
        let data = Data(#"{"accessToken": "sk-ant-oat01-flat"}"#.utf8)
        #expect(ClaudeCredentials.accessToken(fromItemData: data) == "sk-ant-oat01-flat")
    }

    @Test("returns nil for a blob without a token")
    func rejectsTokenlessBlob() {
        #expect(ClaudeCredentials.accessToken(fromItemData: Data(#"{"claudeAiOauth": {}}"#.utf8)) == nil)
    }

    @Test("returns nil for a blob that is not JSON")
    func rejectsGarbage() {
        #expect(ClaudeCredentials.accessToken(fromItemData: Data("not json".utf8)) == nil)
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `make test FILTER=ClaudeCredentials`
Expected: FAIL — `cannot find 'ClaudeCredentials' in scope`.

- [ ] **Step 4: Write `ClaudeCredentials.swift`**

Substitute the service string confirmed in Step 1 if it differs.

```swift
import Foundation
import Security

/// Reads the Claude Code OAuth access token from the login keychain.
///
/// Read-only by design: Claude Code owns these credentials and refreshes them
/// itself. The widget never writes, deletes, or refreshes the item — when the
/// token has expired the fetch fails with `.unauthorized` and the next Claude
/// Code session puts a fresh one in place.
public enum ClaudeCredentials {
    /// Generic-password service under which Claude Code stores its credentials.
    public static let service = "Claude Code-credentials"

    public static func accessToken() -> String? {
        guard let data = itemData() else { return nil }
        return accessToken(fromItemData: data)
    }

    private static func itemData() -> Data? {
        // Queried by service alone: the account is the macOS user name, and
        // matching on it adds a failure mode without adding precision — the
        // service is already unique to Claude Code.
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var result: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess else { return nil }
        return result as? Data
    }

    /// Pulls the access token out of the stored JSON blob. Handles both the
    /// nested `claudeAiOauth` shape Claude Code writes today and a flat blob.
    static func accessToken(fromItemData data: Data) -> String? {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        if let oauth = root["claudeAiOauth"] as? [String: Any],
           let token = oauth["accessToken"] as? String {
            return token
        }
        return root["accessToken"] as? String
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `make test FILTER=ClaudeCredentials`
Expected: PASS, 4 tests.

- [ ] **Step 6: Smoke-test against the real Keychain**

Temporarily replace the body of `Sources/ClaudeUsageWidget/main.swift` with:

```swift
import ClaudeUsageWidgetCore

// Temporary probe — reverted at the end of this step.
print("token found:", ClaudeCredentials.accessToken() != nil)
```

Run `swift run ClaudeUsageWidget`, approve the Keychain access prompt, and
confirm it prints `token found: true`. Print only the boolean, never the token.

Then restore `main.swift` to its Task 1 placeholder before committing:

```swift
// Placeholder so the executable target compiles before the app shell exists.
// Replaced by ClaudeUsageWidgetApp.swift in Task 12.
print("Claude Usage Widget")
```

- [ ] **Step 7: Commit**

```bash
git add Sources/ClaudeUsageWidgetCore/Auth Tests/ClaudeUsageWidgetCoreTests/ClaudeCredentialsTests.swift
git commit -m "feat: read the Claude Code oauth token from the keychain"
```

---

### Task 7: The usage request

**Files:**
- Create: `Sources/ClaudeUsageWidgetCore/Usage/UsageAPI.swift`
- Test: `Tests/ClaudeUsageWidgetCoreTests/UsageAPITests.swift`

**Interfaces:**
- Consumes: `UsageSnapshot`, `UsageError`, `UsageDecoder` from Task 2.
- Produces:
  - `struct UsageAPI: Sendable` with `init(session: URLSession = .shared)` and `func fetch(token: String) async throws -> UsageSnapshot`
  - `UsageAPI.request(token: String) -> URLRequest` (internal, tested)
  - `UsageAPI.validate(statusCode: Int) throws` (internal, tested)

- [ ] **Step 1: Write the failing tests**

`Tests/ClaudeUsageWidgetCoreTests/UsageAPITests.swift`:

```swift
import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("UsageAPI")
struct UsageAPITests {
    @Test("builds the request the endpoint expects")
    func buildsRequest() {
        let request = UsageAPI.request(token: "sk-ant-oat01-example")
        #expect(request.url?.absoluteString == "https://api.anthropic.com/api/oauth/usage")
        #expect(request.httpMethod == "GET")
        #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer sk-ant-oat01-example")
        #expect(request.value(forHTTPHeaderField: "anthropic-beta") == "oauth-2025-04-20")
        #expect(request.value(forHTTPHeaderField: "Accept") == "application/json")
    }

    @Test("a 2xx status passes")
    func acceptsSuccess() throws {
        try UsageAPI.validate(statusCode: 200)
        try UsageAPI.validate(statusCode: 204)
    }

    @Test("401 and 403 mean the token is no longer good")
    func rejectsUnauthorized() {
        #expect(throws: UsageError.unauthorized) { try UsageAPI.validate(statusCode: 401) }
        #expect(throws: UsageError.unauthorized) { try UsageAPI.validate(statusCode: 403) }
    }

    @Test("any other status is a network error naming the code")
    func rejectsOtherStatuses() {
        #expect(throws: UsageError.network("HTTP 500")) { try UsageAPI.validate(statusCode: 500) }
        #expect(throws: UsageError.network("HTTP 429")) { try UsageAPI.validate(statusCode: 429) }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `make test FILTER=UsageAPI`
Expected: FAIL — `cannot find 'UsageAPI' in scope`.

- [ ] **Step 3: Write `UsageAPI.swift`**

```swift
import Foundation

/// The one HTTP call this widget makes.
public struct UsageAPI: Sendable {
    public static let endpoint = URL(string: "https://api.anthropic.com/api/oauth/usage")!

    /// The beta opt-in Claude Code itself sends with oauth-authenticated calls.
    static let betaHeader = "oauth-2025-04-20"

    private let session: URLSession

    public init(session: URLSession = .shared) {
        self.session = session
    }

    public func fetch(token: String) async throws -> UsageSnapshot {
        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: Self.request(token: token))
        } catch {
            throw UsageError.network(error.localizedDescription)
        }

        guard let http = response as? HTTPURLResponse else { throw UsageError.malformedResponse }
        try Self.validate(statusCode: http.statusCode)
        return try UsageDecoder.snapshot(from: data)
    }

    /// Split out from `fetch` so the header set is assertable without a network
    /// round trip.
    static func request(token: String) -> URLRequest {
        var request = URLRequest(url: endpoint)
        request.httpMethod = "GET"
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        request.setValue(betaHeader, forHTTPHeaderField: "anthropic-beta")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        return request
    }

    /// Maps a status code onto the widget's error vocabulary.
    static func validate(statusCode: Int) throws {
        switch statusCode {
        case 200..<300: return
        case 401, 403: throw UsageError.unauthorized
        default: throw UsageError.network("HTTP \(statusCode)")
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `make test FILTER=UsageAPI`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add Sources/ClaudeUsageWidgetCore/Usage/UsageAPI.swift Tests/ClaudeUsageWidgetCoreTests/UsageAPITests.swift
git commit -m "feat: add the oauth usage request"
```

---

### Task 8: The store

**Files:**
- Create: `Sources/ClaudeUsageWidgetCore/Store/UsageStore.swift`
- Test: `Tests/ClaudeUsageWidgetCoreTests/UsageStoreTests.swift`

**Interfaces:**
- Consumes: `UsageSnapshot`, `UsageError` (Task 2), `UsageAPI` (Task 7), `ClaudeCredentials` (Task 6).
- Produces:
  - `@MainActor @Observable final class UsageStore`
  - `UsageStore.State: Equatable` with `.loading`, `.ok(UsageSnapshot, fetchedAt: Date)`, `.failed(UsageError)`
  - `init(fetch:tokenProvider:now:)` — all three injectable
  - `var state: State`, `var lastSnapshot: UsageSnapshot?`
  - `func start()`, `func stop()`, `func refresh()`, `func load() async`
  - `static let refreshInterval: TimeInterval = 300`

The fetch is injected as a closure rather than a `UsageAPI` value so tests never touch `URLSession`.

- [ ] **Step 1: Write the failing tests**

`Tests/ClaudeUsageWidgetCoreTests/UsageStoreTests.swift`:

```swift
import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@MainActor
@Suite("UsageStore")
struct UsageStoreTests {
    static let now = Date(timeIntervalSince1970: 1_785_348_000)

    private static func snapshot(_ utilization: Double) -> UsageSnapshot {
        UsageSnapshot(buckets: ["five_hour": UsageBucket(utilization: utilization, resetsAt: nil)])
    }

    private func store(
        token: String? = "token",
        fetch: @escaping @Sendable (String) async throws -> UsageSnapshot
    ) -> UsageStore {
        UsageStore(fetch: fetch, tokenProvider: { token }, now: { Self.now })
    }

    @Test("starts out loading")
    func startsLoading() {
        let store = store { _ in Self.snapshot(1) }
        #expect(store.state == .loading)
    }

    @Test("a successful load publishes the snapshot and the fetch time")
    func publishesSnapshot() async {
        let store = store { _ in Self.snapshot(42) }
        await store.load()
        #expect(store.state == .ok(Self.snapshot(42), fetchedAt: Self.now))
        #expect(store.lastSnapshot == Self.snapshot(42))
    }

    @Test("a missing token fails without calling the endpoint")
    func failsWithoutToken() async {
        let store = store(token: nil) { _ in
            Issue.record("fetch must not run without a token")
            return Self.snapshot(1)
        }
        await store.load()
        #expect(store.state == .failed(.noCredentials))
    }

    @Test("a rejected token surfaces as unauthorized")
    func surfacesUnauthorized() async {
        let store = store { _ in throw UsageError.unauthorized }
        await store.load()
        #expect(store.state == .failed(.unauthorized))
    }

    @Test("an unexpected error surfaces as a network error")
    func wrapsUnknownErrors() async {
        struct Boom: Error {}
        let store = store { _ in throw Boom() }
        await store.load()
        if case .failed(.network) = store.state {} else {
            Issue.record("expected a network failure, got \(store.state)")
        }
    }

    @Test("a failure after a success keeps the last snapshot on screen")
    func keepsLastSnapshotOnFailure() async {
        final class Box: @unchecked Sendable { var shouldFail = false }
        let box = Box()
        let store = store { _ in
            if box.shouldFail { throw UsageError.unauthorized }
            return Self.snapshot(42)
        }

        await store.load()
        box.shouldFail = true
        await store.load()

        #expect(store.state == .failed(.unauthorized))
        #expect(store.lastSnapshot == Self.snapshot(42))
    }

    @Test("the token is passed through to the fetch")
    func passesToken() async {
        final class Box: @unchecked Sendable { var seen: String? }
        let box = Box()
        let store = store(token: "sk-ant-oat01-example") { token in
            box.seen = token
            return Self.snapshot(1)
        }
        await store.load()
        #expect(box.seen == "sk-ant-oat01-example")
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `make test FILTER=UsageStore`
Expected: FAIL — `cannot find 'UsageStore' in scope`.

- [ ] **Step 3: Write `UsageStore.swift`**

```swift
import AppKit
import Foundation
import Observation

/// Owns the usage snapshot and the refresh cycle.
///
/// The fetch, the token lookup and the clock are all injected so the whole
/// state machine is testable without a network, a keychain, or real time.
@MainActor
@Observable
public final class UsageStore {
    public enum State: Equatable {
        case loading
        case ok(UsageSnapshot, fetchedAt: Date)
        case failed(UsageError)
    }

    /// The server's figures move slowly; a faster poll would only add requests.
    public static let refreshInterval: TimeInterval = 300

    public private(set) var state: State = .loading

    /// The most recent successful snapshot. Kept so a failing refresh dims the
    /// dials instead of blanking them.
    public private(set) var lastSnapshot: UsageSnapshot?

    private let fetch: @Sendable (String) async throws -> UsageSnapshot
    private let tokenProvider: @Sendable () -> String?
    private let now: @Sendable () -> Date
    private var timer: Timer?
    private var wakeObserver: NSObjectProtocol?

    public init(
        fetch: @escaping @Sendable (String) async throws -> UsageSnapshot = { try await UsageAPI().fetch(token: $0) },
        tokenProvider: @escaping @Sendable () -> String? = { ClaudeCredentials.accessToken() },
        now: @escaping @Sendable () -> Date = { Date() }
    ) {
        self.fetch = fetch
        self.tokenProvider = tokenProvider
        self.now = now
    }

    /// Idempotent: stops any previous cycle first, so it is safe to call again.
    public func start() {
        stop()
        refresh()
        timer = Timer.scheduledTimer(withTimeInterval: Self.refreshInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
        // A machine asleep for hours wakes with a stale snapshot; refresh at once
        // rather than waiting out the rest of the interval.
        wakeObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
    }

    public func stop() {
        timer?.invalidate()
        timer = nil
        if let wakeObserver {
            NSWorkspace.shared.notificationCenter.removeObserver(wakeObserver)
            self.wakeObserver = nil
        }
    }

    public func refresh() {
        Task { await load() }
    }

    func load() async {
        guard let token = tokenProvider() else {
            state = .failed(.noCredentials)
            return
        }
        do {
            let snapshot = try await fetch(token)
            lastSnapshot = snapshot
            state = .ok(snapshot, fetchedAt: now())
        } catch let error as UsageError {
            state = .failed(error)
        } catch {
            state = .failed(.network(error.localizedDescription))
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `make test FILTER=UsageStore`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add Sources/ClaudeUsageWidgetCore/Store Tests/ClaudeUsageWidgetCoreTests/UsageStoreTests.swift
git commit -m "feat: add the usage store with its refresh cycle"
```

---

### Task 9: Settings and palette

**Files:**
- Create: `Sources/ClaudeUsageWidgetCore/WidgetSettings.swift`
- Create: `Sources/ClaudeUsageWidgetCore/Views/Theme.swift`
- Test: `Tests/ClaudeUsageWidgetCoreTests/WidgetSettingsTests.swift`

**Interfaces:**
- Consumes: `ThresholdLevel` from Task 4.
- Produces:
  - `WidgetSettings.positionLockedKey`, `.widgetVisibleKey`, `.modelBucketKey` (all `String`)
  - `WidgetSettings.isVisible(in: UserDefaults) -> Bool`
  - `WidgetSettings.modelBucket(in: UserDefaults) -> String?`
  - `Theme.panel`, `.track`, `.hand`, `.text`, `.dim`, `.accent`, `.warning`, `.danger` (all `Color`)
  - `Theme.color(for: ThresholdLevel) -> Color`
  - `Theme.label`, `Theme.value`, `Theme.caption` (all `Font`)

- [ ] **Step 1: Write the failing test**

`Tests/ClaudeUsageWidgetCoreTests/WidgetSettingsTests.swift`:

```swift
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `make test FILTER=WidgetSettings`
Expected: FAIL — `cannot find 'WidgetSettings' in scope`.

- [ ] **Step 3: Write `WidgetSettings.swift`**

```swift
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
```

- [ ] **Step 4: Write `Theme.swift`**

```swift
import SwiftUI

/// The widget palette: a dark glass panel with pastel dials, in the spirit of
/// the sibling mole-widget.
public enum Theme {
    public static let panel = Color(red: 0.118, green: 0.133, blue: 0.188)
    public static let track = Color(red: 0.250, green: 0.270, blue: 0.340)
    public static let hand = Color(red: 0.780, green: 0.800, blue: 0.870)
    public static let text = Color(red: 0.780, green: 0.800, blue: 0.870)
    public static let dim = Color(red: 0.450, green: 0.470, blue: 0.550)

    public static let accent = Color(red: 0.651, green: 0.820, blue: 0.537)
    public static let warning = Color(red: 0.898, green: 0.784, blue: 0.565)
    public static let danger = Color(red: 0.906, green: 0.510, blue: 0.518)

    public static func color(for level: ThresholdLevel) -> Color {
        switch level {
        case .ok: accent
        case .warning: warning
        case .danger: danger
        }
    }

    /// Monospaced digits everywhere, so numbers do not jitter as they tick.
    public static let label = Font.system(size: 9, weight: .semibold).monospacedDigit()
    public static let value = Font.system(size: 17, weight: .semibold).monospacedDigit()
    public static let caption = Font.system(size: 9, weight: .medium).monospacedDigit()
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `make test FILTER=WidgetSettings`
Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add Sources/ClaudeUsageWidgetCore/WidgetSettings.swift Sources/ClaudeUsageWidgetCore/Views/Theme.swift Tests/ClaudeUsageWidgetCoreTests/WidgetSettingsTests.swift
git commit -m "feat: add widget settings keys and the palette"
```

---

### Task 10: The dial

**Files:**
- Create: `Sources/ClaudeUsageWidgetCore/Views/DialView.swift`
- Test: `Tests/ClaudeUsageWidgetCoreTests/DialGeometryTests.swift`

**Interfaces:**
- Consumes: `ThresholdLevel` (Task 4), `Theme` (Task 9).
- Produces:
  - `struct DialView: View` with `init(title: String, fraction: Double?, elapsed: Double?, remaining: String?, dimmed: Bool)`
  - `enum DialGeometry` with `static func angle(forFraction: Double) -> Angle` and `static func handPoint(forFraction: Double, in rect: CGRect, inset: CGFloat) -> CGPoint`

`fraction: nil` renders the dial as `n/a` — that is the no-model-bucket case from Task 5.

- [ ] **Step 1: Write the failing test**

`Tests/ClaudeUsageWidgetCoreTests/DialGeometryTests.swift`:

```swift
import CoreGraphics
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("DialGeometry")
struct DialGeometryTests {
    @Test("zero points at twelve o'clock")
    func startsAtTop() {
        #expect(DialGeometry.angle(forFraction: 0).degrees == -90)
    }

    @Test("a quarter turn points at three o'clock")
    func quarterTurn() {
        #expect(DialGeometry.angle(forFraction: 0.25).degrees == 0)
    }

    @Test("a full turn comes back to twelve o'clock")
    func fullTurn() {
        #expect(DialGeometry.angle(forFraction: 1).degrees == 270)
    }

    @Test("the hand at zero sits above the centre")
    func handAtTop() {
        let rect = CGRect(x: 0, y: 0, width: 100, height: 100)
        let point = DialGeometry.handPoint(forFraction: 0, in: rect, inset: 20)
        #expect(abs(point.x - 50) < 0.001)
        #expect(abs(point.y - 20) < 0.001)
    }

    @Test("the hand at a quarter turn sits right of the centre")
    func handAtRight() {
        let rect = CGRect(x: 0, y: 0, width: 100, height: 100)
        let point = DialGeometry.handPoint(forFraction: 0.25, in: rect, inset: 20)
        #expect(abs(point.x - 80) < 0.001)
        #expect(abs(point.y - 50) < 0.001)
    }
}
```

The hand length is `min(width, height) / 2 - inset`, so with a 100×100 rect and an inset of 20 the tip is 30 points from the centre.

- [ ] **Step 2: Run the test to verify it fails**

Run: `make test FILTER=DialGeometry`
Expected: FAIL — `cannot find 'DialGeometry' in scope`.

- [ ] **Step 3: Write `DialView.swift`**

```swift
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

/// The hand: how far the current window has run.
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

/// One limit: an arc for how much is spent, a hand for how far the window has
/// run, and the numbers in the middle.
public struct DialView: View {
    private let title: String
    private let fraction: Double?
    private let elapsed: Double?
    private let remaining: String?
    private let dimmed: Bool

    private static let size: CGFloat = 92
    private static let arcInset: CGFloat = 5
    private static let arcWidth: CGFloat = 6
    private static let handInset: CGFloat = 16

    public init(title: String, fraction: Double?, elapsed: Double?, remaining: String?, dimmed: Bool) {
        self.title = title
        self.fraction = fraction
        self.elapsed = elapsed
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

            if let elapsed, !dimmed {
                DialHand(fraction: elapsed, inset: Self.handInset)
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
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `make test FILTER=DialGeometry`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add Sources/ClaudeUsageWidgetCore/Views/DialView.swift Tests/ClaudeUsageWidgetCoreTests/DialGeometryTests.swift
git commit -m "feat: add the dial view"
```

---

### Task 11: The panel

**Files:**
- Create: `Sources/ClaudeUsageWidgetCore/Views/WidgetRootView.swift`
- Create: `Sources/ClaudeUsageWidgetCore/Views/DialModel.swift`
- Test: `Tests/ClaudeUsageWidgetCoreTests/DialModelTests.swift`

**Interfaces:**
- Consumes: `UsageStore` (Task 8), `UsageSnapshot`/`UsageError` (Task 2), `UsageMath` (Task 3), `ModelBuckets` (Task 5), `WidgetSettings`/`Theme` (Task 9), `DialView` (Task 10).
- Produces:
  - `struct DialModel: Equatable { let title: String; let fraction: Double?; let elapsed: Double?; let remaining: String? }`
  - `DialModel.make(key: String, title: String, snapshot: UsageSnapshot?, now: Date) -> DialModel`
  - `DialModel.all(snapshot: UsageSnapshot?, preferredModelKey: String?, now: Date) -> [DialModel]`
  - `StatusLine.text(for state: UsageStore.State, now: Date) -> String?`
  - `struct WidgetRootView: View` with `init(store: UsageStore)`

`DialModel` is the seam between the store and SwiftUI: it turns a snapshot into exactly what three `DialView`s need, and it is a plain value so it can be asserted in a test.

- [ ] **Step 1: Write the failing tests**

`Tests/ClaudeUsageWidgetCoreTests/DialModelTests.swift`:

```swift
import Foundation
import Testing
@testable import ClaudeUsageWidgetCore

@Suite("DialModel")
struct DialModelTests {
    static let now = Date(timeIntervalSince1970: 1_785_348_000)

    private static func snapshot(_ pairs: [String: (Double, TimeInterval?)]) -> UsageSnapshot {
        UsageSnapshot(buckets: pairs.mapValues { utilization, offset in
            UsageBucket(utilization: utilization, resetsAt: offset.map { now.addingTimeInterval($0) })
        })
    }

    @Test("builds a dial from a bucket")
    func buildsFromBucket() {
        let model = DialModel.make(
            key: "five_hour",
            title: "SESSION",
            snapshot: Self.snapshot(["five_hour": (42, 2.5 * 3600)]),
            now: Self.now
        )
        #expect(model.title == "SESSION")
        #expect(model.fraction == 0.42)
        #expect(model.elapsed == 0.5)
        #expect(model.remaining == "2h 30m")
    }

    @Test("a missing bucket yields an empty dial rather than nothing")
    func missingBucket() {
        let model = DialModel.make(key: "five_hour", title: "SESSION", snapshot: Self.snapshot([:]), now: Self.now)
        #expect(model.fraction == nil)
        #expect(model.elapsed == nil)
        #expect(model.remaining == nil)
    }

    @Test("a nil snapshot yields an empty dial")
    func noSnapshot() {
        let model = DialModel.make(key: "five_hour", title: "SESSION", snapshot: nil, now: Self.now)
        #expect(model.fraction == nil)
    }

    @Test("always produces exactly three dials, session then week then model")
    func alwaysThreeDials() {
        let models = DialModel.all(
            snapshot: Self.snapshot([
                "five_hour": (10, 3600),
                "seven_day": (20, 86_400),
                "seven_day_fable": (30, 86_400),
            ]),
            preferredModelKey: nil,
            now: Self.now
        )
        #expect(models.count == 3)
        #expect(models.map(\.title) == ["SESSION", "WEEK", "FABLE"])
        #expect(models[2].fraction == 0.3)
    }

    @Test("the third dial reads MODEL and is empty when no model bucket exists")
    func noModelBucket() {
        let models = DialModel.all(
            snapshot: Self.snapshot(["five_hour": (10, 3600), "seven_day": (20, 86_400)]),
            preferredModelKey: nil,
            now: Self.now
        )
        #expect(models[2].title == "MODEL")
        #expect(models[2].fraction == nil)
    }

    @Test("the third dial honours the pinned bucket")
    func honoursPinnedBucket() {
        let models = DialModel.all(
            snapshot: Self.snapshot([
                "seven_day_fable": (30, 86_400),
                "seven_day_opus": (60, 86_400),
            ]),
            preferredModelKey: "seven_day_opus",
            now: Self.now
        )
        #expect(models[2].title == "OPUS")
        #expect(models[2].fraction == 0.6)
    }
}
```

Append the status-line suite to the same file:

```swift
@Suite("StatusLine")
struct StatusLineTests {
    static let now = Date(timeIntervalSince1970: 1_785_348_000)

    @Test("a fresh success shows no status line")
    func silentWhenHealthy() {
        let snapshot = UsageSnapshot(buckets: ["five_hour": UsageBucket(utilization: 1, resetsAt: nil)])
        #expect(StatusLine.text(for: .ok(snapshot, fetchedAt: Self.now), now: Self.now) == nil)
    }

    @Test("a stale success says how long ago it was fetched")
    func reportsStaleness() {
        let snapshot = UsageSnapshot(buckets: ["five_hour": UsageBucket(utilization: 1, resetsAt: nil)])
        let fetchedAt = Self.now.addingTimeInterval(-2 * 3600)
        #expect(StatusLine.text(for: .ok(snapshot, fetchedAt: fetchedAt), now: Self.now) == "updated 2h 0m ago")
    }

    @Test("each failure has its own wording")
    func reportsFailures() {
        #expect(StatusLine.text(for: .failed(.noCredentials), now: Self.now) == "no Claude Code credentials found")
        #expect(StatusLine.text(for: .failed(.unauthorized), now: Self.now) == "token rejected — sign in to Claude Code")
        #expect(StatusLine.text(for: .failed(.malformedResponse), now: Self.now) == "unexpected response from the API")
        #expect(StatusLine.text(for: .failed(.network("HTTP 500")), now: Self.now) == "HTTP 500")
    }

    @Test("the first load says so")
    func reportsLoading() {
        #expect(StatusLine.text(for: .loading, now: Self.now) == "loading…")
    }
}
```

The staleness threshold is 15 minutes: three missed refreshes.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `make test FILTER=Dial`
Expected: FAIL — `cannot find 'DialModel' in scope`.

- [ ] **Step 3: Write `DialModel.swift`**

```swift
import Foundation

/// Everything one dial needs, derived from a snapshot. A plain value so the
/// derivation is testable without SwiftUI.
public struct DialModel: Equatable, Sendable {
    public let title: String
    public let fraction: Double?
    public let elapsed: Double?
    public let remaining: String?

    public static func make(key: String, title: String, snapshot: UsageSnapshot?, now: Date) -> DialModel {
        guard let bucket = snapshot?[key] else {
            return DialModel(title: title, fraction: nil, elapsed: nil, remaining: nil)
        }
        return DialModel(
            title: title,
            fraction: UsageMath.fraction(bucket.utilization),
            elapsed: UsageMath.elapsedFraction(
                resetsAt: bucket.resetsAt,
                window: UsageMath.windowLength(forKey: key),
                now: now
            ),
            remaining: UsageMath.remainingText(resetsAt: bucket.resetsAt, now: now)
        )
    }

    /// The three dials, always in the same order and always all three present —
    /// a dial with no data reads as `n/a` rather than disappearing and shifting
    /// the layout.
    public static func all(snapshot: UsageSnapshot?, preferredModelKey: String?, now: Date) -> [DialModel] {
        let modelKey = snapshot.flatMap { ModelBuckets.resolve(preferred: preferredModelKey, in: $0) }
        return [
            make(key: "five_hour", title: "SESSION", snapshot: snapshot, now: now),
            make(key: "seven_day", title: "WEEK", snapshot: snapshot, now: now),
            make(
                key: modelKey ?? "",
                title: modelKey.map(ModelBuckets.label(for:)) ?? "MODEL",
                snapshot: snapshot,
                now: now
            ),
        ]
    }
}

/// The line under the dials. Nil means everything is fine and the widget should
/// stay quiet.
public enum StatusLine {
    /// A snapshot older than three missed refreshes is worth calling out.
    static let staleAfter: TimeInterval = 15 * 60

    public static func text(for state: UsageStore.State, now: Date) -> String? {
        switch state {
        case .loading:
            return "loading…"
        case let .ok(_, fetchedAt):
            let age = now.timeIntervalSince(fetchedAt)
            guard age >= staleAfter else { return nil }
            let ago = UsageMath.remainingText(resetsAt: now, now: fetchedAt)
            return ago.map { "updated \($0) ago" }
        case .failed(.noCredentials):
            return "no Claude Code credentials found"
        case .failed(.unauthorized):
            return "token rejected — sign in to Claude Code"
        case .failed(.malformedResponse):
            return "unexpected response from the API"
        case let .failed(.network(message)):
            return message
        }
    }
}
```

`remainingText` is reused for the "ago" phrasing by swapping its arguments: the time between `fetchedAt` and `now` is the same arithmetic in the other direction.

- [ ] **Step 4: Write `WidgetRootView.swift`**

```swift
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
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `make test FILTER=DialModel` then `make test FILTER=StatusLine`
Expected: PASS — 6 tests and 4 tests. (`FILTER=Dial` would also pull in the
five `DialGeometry` tests from Task 10.)

- [ ] **Step 6: Commit**

```bash
git add Sources/ClaudeUsageWidgetCore/Views/DialModel.swift Sources/ClaudeUsageWidgetCore/Views/WidgetRootView.swift Tests/ClaudeUsageWidgetCoreTests/DialModelTests.swift
git commit -m "feat: derive the three dials and render the panel"
```

---

### Task 12: The app shell

**Files:**
- Delete: `Sources/ClaudeUsageWidget/main.swift`
- Create: `Sources/ClaudeUsageWidget/ClaudeUsageWidgetApp.swift`
- Create: `Resources/Info.plist`

**Interfaces:**
- Consumes: `UsageStore` (Task 8), `WidgetRootView` (Task 11), `WidgetSettings` (Task 9), `ModelBuckets` (Task 5), `CoreInfo` (Task 1).
- Produces: a runnable app. Nothing later depends on its symbols.

The window plumbing is lifted from `mole-widget`'s `MoleWidgetApp.swift` — the comments there explain why each choice is what it is, and the same reasoning applies here.

`main.swift` must be deleted: a file by that name and an `@main` type in the same target is a compile error.

- [ ] **Step 1: Delete the placeholder**

```bash
git rm Sources/ClaudeUsageWidget/main.swift
```

- [ ] **Step 2: Write `ClaudeUsageWidgetApp.swift`**

```swift
import AppKit
import ClaudeUsageWidgetCore
import ServiceManagement
import SwiftUI

@main
struct ClaudeUsageWidgetApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    @AppStorage(WidgetSettings.positionLockedKey) private var positionLocked = false
    @AppStorage(WidgetSettings.widgetVisibleKey) private var widgetVisible = true
    @AppStorage(WidgetSettings.modelBucketKey) private var modelBucket = ""

    /// A monochrome ring glyph. Template images get tinted by macOS to match
    /// the menu bar, light or dark.
    private static let menuBarIcon: NSImage = {
        let size = NSSize(width: 16, height: 16)
        let image = NSImage(size: size, flipped: false) { _ in
            let inset: CGFloat = 2.5
            let rect = NSRect(x: inset, y: inset, width: size.width - inset * 2, height: size.height - inset * 2)
            NSColor.black.setStroke()
            let ring = NSBezierPath(ovalIn: rect)
            ring.lineWidth = 2
            ring.stroke()
            return true
        }
        image.isTemplate = true
        return image
    }()

    var body: some Scene {
        MenuBarExtra {
            Button("Claude Usage Widget v\(CoreInfo.version)") {}
                .disabled(true)
            Button("Refresh now") { appDelegate.store.refresh() }
            Divider()
            ModelBucketPicker(store: appDelegate.store, selection: $modelBucket)
            Toggle("Lock position", isOn: $positionLocked)
            Toggle("Show on desktop", isOn: $widgetVisible)
            LaunchAtLoginToggle()
            Divider()
            Button("Quit Claude Usage Widget") { NSApplication.shared.terminate(nil) }
                .keyboardShortcut("q")
        } label: {
            Image(nsImage: Self.menuBarIcon)
        }
    }
}

/// Lists the per-model buckets the server actually returned. Hidden entirely
/// when there is nothing to choose between.
private struct ModelBucketPicker: View {
    let store: UsageStore
    @Binding var selection: String

    private var keys: [String] {
        guard let snapshot = store.lastSnapshot else { return [] }
        return ModelBuckets.available(in: snapshot)
    }

    var body: some View {
        if keys.count > 1 {
            Picker("Model limit", selection: $selection) {
                ForEach(keys, id: \.self) { key in
                    Text(ModelBuckets.label(for: key)).tag(key)
                }
            }
        }
    }
}

/// "Launch at login" backed by SMAppService. Registration only works from a
/// real .app bundle; from a bare `swift run` binary register() throws and the
/// toggle reverts.
private struct LaunchAtLoginToggle: View {
    @State private var enabled = SMAppService.mainApp.status == .enabled

    var body: some View {
        Toggle("Launch at login", isOn: Binding(
            get: { enabled },
            set: { newValue in
                do {
                    if newValue {
                        try SMAppService.mainApp.register()
                    } else {
                        try SMAppService.mainApp.unregister()
                    }
                    enabled = newValue
                } catch {
                    enabled = SMAppService.mainApp.status == .enabled
                }
            }
        ))
    }
}

private var isDraggingAllowed: Bool {
    !UserDefaults.standard.bool(forKey: WidgetSettings.positionLockedKey)
}

/// mouseDownCanMoveWindow == false disables AppKit's built-in auto-drag, which
/// would ignore the lock; dragging goes only through DesktopWindow.mouseDown.
final class WidgetHostingView<Content: View>: NSHostingView<Content> {
    override var mouseDownCanMoveWindow: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

/// Borderless desktop-level window: never steals focus, draggable unless locked.
final class DesktopWindow: NSWindow {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    override func mouseDown(with event: NSEvent) {
        if event.type == .leftMouseDown, isDraggingAllowed {
            performDrag(with: event)
        } else {
            super.mouseDown(with: event)
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    let store = UsageStore()
    private var window: DesktopWindow?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory) // no Dock icon

        store.start()

        let window = DesktopWindow(
            contentRect: NSRect(x: 0, y: 0, width: 340, height: 150),
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        // One level above the Finder desktop icon window: below it, Finder's
        // transparent full-screen window swallows every click and the widget
        // cannot be dragged.
        window.level = NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.desktopIconWindow)) + 1)
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        window.backgroundColor = .clear
        window.isOpaque = false
        window.hasShadow = false

        let hostingView = WidgetHostingView(rootView: WidgetRootView(store: store))
        window.contentView = hostingView
        // Fit the window to its content: extra transparent area would capture
        // clicks outside the visible panel.
        let fitting = hostingView.fittingSize
        if fitting.width > 0, fitting.height > 0 {
            window.setContentSize(fitting)
        }

        // Centre first, then attach the autosave name so a stored frame wins.
        window.center()
        window.setFrameAutosaveName("ClaudeUsageWidgetWindow")

        self.window = window
        if WidgetSettings.isVisible(in: .standard) {
            window.orderFrontRegardless()
        }

        NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.reconcileVisibility() }
        }
    }

    /// Shows or hides the window to match the stored flag. Idempotent, so
    /// unrelated defaults changes do not re-order the window. Polling keeps
    /// running while hidden.
    private func reconcileVisibility() {
        guard let window else { return }
        let shouldBeVisible = WidgetSettings.isVisible(in: .standard)
        if shouldBeVisible, !window.isVisible {
            window.orderFrontRegardless()
        } else if !shouldBeVisible, window.isVisible {
            window.orderOut(nil)
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        store.stop()
    }
}
```

- [ ] **Step 3: Write `Resources/Info.plist`**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleExecutable</key>
	<string>ClaudeUsageWidget</string>
	<key>CFBundleIdentifier</key>
	<string>com.sbezbabnykh.claude-usage-widget</string>
	<key>CFBundleName</key>
	<string>Claude Usage Widget</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleShortVersionString</key>
	<string>0.1.0</string>
	<key>CFBundleVersion</key>
	<string>1</string>
	<key>LSMinimumSystemVersion</key>
	<string>14.0</string>
	<key>LSUIElement</key>
	<true/>
	<key>NSHighResolutionCapable</key>
	<true/>
</dict>
</plist>
```

No `CFBundleIconFile`: there is no icon asset yet, and pointing at a missing one makes Finder show a broken bundle.

- [ ] **Step 4: Verify the whole suite still passes**

Run: `make test`
Expected: PASS, 70 tests.

- [ ] **Step 5: Run it and look at it**

Run: `make run`

Confirm: the panel appears centred on the desktop; three dials; the arcs fill to real percentages; hands sit somewhere sensible; a menu bar ring icon appears with a working menu. Drag the panel — it should move; toggle "Lock position" and confirm it stops moving.

If the dials read `n/a` and the status line says `no Claude Code credentials found`, revisit Task 6 Step 1 — the service name is wrong.

- [ ] **Step 6: Commit**

```bash
git add Sources/ClaudeUsageWidget Resources/Info.plist
git commit -m "feat: add the desktop window and menu bar shell"
```

---

### Task 13: Packaging and README

**Files:**
- Modify: `Makefile` (add the `app` target)
- Create: `README.md`

**Interfaces:**
- Consumes: everything.
- Produces: `make app` producing `dist/Claude Usage Widget.app`.

- [ ] **Step 1: Add the `app` target to the Makefile**

Insert before the `clean` target:

```make
app:
	swift build -c release
	rm -rf "$(DIST)"
	mkdir -p "$(DIST)/Contents/MacOS"
	cp .build/release/ClaudeUsageWidget "$(DIST)/Contents/MacOS/ClaudeUsageWidget"
	cp Resources/Info.plist "$(DIST)/Contents/Info.plist"
	codesign --force --sign - "$(DIST)"
	@echo "Done: $(DIST)"
```

- [ ] **Step 2: Build and launch the bundle**

Run:

```bash
make app && open "dist/Claude Usage Widget.app"
```

Expected: the widget appears, and the Keychain prompts for access once (the ad-hoc signature differs from the `swift run` binary, so this is a separate approval). Confirm the dials populate.

- [ ] **Step 3: Write the README**

```markdown
# Claude Usage Widget

A macOS desktop widget showing Claude Code subscription limits as three dials:
the 5-hour session limit, the 7-day weekly limit, and one per-model weekly
limit.

Each dial fills its arc with the share of the limit already spent — green,
amber past 60%, red past 85% — while the hand shows how far the current window
has run. The centre reads the percentage and the time left until the window
resets.

## How it reads your usage

The widget calls `https://api.anthropic.com/api/oauth/usage` with the OAuth
token Claude Code already stores in your login keychain. It reads that item and
nothing else, and never writes to it — Claude Code owns and refreshes those
credentials. macOS asks for permission the first time; if the token has expired,
the widget says so and the next Claude Code session refreshes it.

## Requirements

- macOS 14+
- Claude Code, signed in
- Swift 6 toolchain — Command Line Tools are enough (`xcode-select --install`)

## Build

```bash
make app
open "dist/Claude Usage Widget.app"
```

## Development

```bash
make run    # run a dev build
make test   # run the test suite
```

> **Important:** run tests only via `make test`. On a machine without full Xcode
> a bare `swift test` silently runs zero tests and exits 0 — the Makefile passes
> the toolchain flags Swift Testing needs from Command Line Tools.

## Menu bar

- **Refresh now** — fetch immediately instead of waiting out the 5-minute cycle
- **Model limit** — pick which per-model weekly limit the third dial shows
  (appears once the server returns more than one)
- **Lock position** — pin the panel so it cannot be dragged
- **Show on desktop** — hide the panel while keeping the menu bar item
- **Launch at login**

## Architecture

```
Sources/ClaudeUsageWidgetCore/
  Auth/          — keychain read
  Usage/         — decoding, pure math, bucket selection, the HTTP call
  Store/         — @Observable store, 5-minute refresh
  Views/         — SwiftUI dials and panel
Sources/ClaudeUsageWidget/  — app shell: desktop window, MenuBarExtra
```

All computation is pure functions over a decoded snapshot and is unit-tested;
the keychain reader and the HTTP client are thin wrappers with smoke tests.
```

- [ ] **Step 4: Verify the full suite one last time**

Run: `make test`
Expected: PASS, 70 tests.

- [ ] **Step 5: Commit**

```bash
git add Makefile README.md
git commit -m "chore: package the app bundle and document the widget"
```

---

## Test Count Reference

| Task | Suite | Tests |
|---|---|---|
| 1 | CoreInfo | 1 |
| 2 | UsageDecoder | 8 |
| 3 | UsageMath | 14 |
| 4 | ThresholdLevel | 3 |
| 5 | ModelBuckets | 9 |
| 6 | ClaudeCredentials | 4 |
| 7 | UsageAPI | 4 |
| 8 | UsageStore | 7 |
| 9 | WidgetSettings | 5 |
| 10 | DialGeometry | 5 |
| 11 | DialModel + StatusLine | 6 + 4 |
| | **Total** | **70** |

If the runner reports a different total, correct this table to match rather than
loosening any assertion.
