// swift-tools-version: 6.0

import PackageDescription

// This manifest is copied over Package.swift only inside the audited Apple Store
// build lane. Personal source builds keep the pinned public KSPlayer dependency;
// Store builds compile the same UI through its AVKit-only canImport boundary.
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
    targets: [
        .target(name: "OrbitalVueCore"),
        .target(
            name: "OrbitalVueUI",
            dependencies: ["OrbitalVueCore"]
        ),
        .testTarget(
            name: "OrbitalVueCoreTests",
            dependencies: ["OrbitalVueCore"]
        )
    ]
)
