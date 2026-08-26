// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "StreamVueApple",
    defaultLocalization: "en",
    platforms: [
        .iOS(.v17),
        .tvOS(.v17),
        .macOS(.v14)
    ],
    products: [
        .library(name: "StreamVueCore", targets: ["StreamVueCore"]),
        .library(name: "StreamVueUI", targets: ["StreamVueUI"])
    ],
    targets: [
        .target(name: "StreamVueCore"),
        .target(
            name: "StreamVueUI",
            dependencies: ["StreamVueCore"]
        ),
        .testTarget(
            name: "StreamVueCoreTests",
            dependencies: ["StreamVueCore"]
        )
    ]
)
