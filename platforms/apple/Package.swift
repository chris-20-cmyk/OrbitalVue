// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "OrbitalVueApple",
    defaultLocalization: "en",
    platforms: [
        .iOS(.v17),
        .tvOS(.v17),
        .macOS(.v14)
    ],
    products: [
        .library(name: "OrbitalVueCore", targets: ["OrbitalVueCore"]),
        .library(name: "OrbitalVueUI", targets: ["OrbitalVueUI"])
    ],
    dependencies: [
        .package(url: "https://github.com/kingslay/KSPlayer.git", exact: "2.3.4")
    ],
    targets: [
        .target(name: "OrbitalVueCore"),
        .target(
            name: "OrbitalVueUI",
            dependencies: [
                "OrbitalVueCore",
                .product(
                    name: "KSPlayer",
                    package: "KSPlayer",
                    condition: .when(platforms: [.iOS, .tvOS])
                )
            ]
        ),
        .testTarget(
            name: "OrbitalVueCoreTests",
            dependencies: ["OrbitalVueCore"]
        )
    ]
)
