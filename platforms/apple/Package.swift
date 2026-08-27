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
    dependencies: [
        .package(url: "https://github.com/kingslay/KSPlayer.git", exact: "2.3.4")
    ],
    targets: [
        .target(name: "StreamVueCore"),
        .target(
            name: "StreamVueUI",
            dependencies: [
                "StreamVueCore",
                .product(
                    name: "KSPlayer",
                    package: "KSPlayer",
                    condition: .when(platforms: [.iOS, .tvOS])
                )
            ]
        ),
        .testTarget(
            name: "StreamVueCoreTests",
            dependencies: ["StreamVueCore"]
        )
    ]
)
