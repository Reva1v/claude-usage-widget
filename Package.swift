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
