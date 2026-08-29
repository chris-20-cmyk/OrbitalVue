# StreamVue for iPhone, iPad, and Apple TV

This directory contains the native SwiftUI StreamVue 5.1 foundation. It is one Swift package with pinned KSPlayer 2.3.4 and AVFoundation/AVKit playback plus two generated application targets:

- `StreamVue` for iPhone and iPad
- `StreamVueTV` for Apple TV

The minimum deployment target is iOS, iPadOS, and tvOS 17. Store builds use Xcode 26.6 and the iOS/tvOS 26.5 SDKs. The shared parser also supports macOS 14 so its contract and privacy tests can run directly with Swift Package Manager.

## Build on a Mac

Requirements:

- macOS 26.2 or later
- Xcode 26.6
- XcodeGen 2.46.0

```bash
cd platforms/apple
xcodegen generate --spec project.yml
swift test
xcodebuild -project StreamVueApple.xcodeproj -scheme StreamVue -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO build
xcodebuild -project StreamVueApple.xcodeproj -scheme StreamVueTV -destination 'generic/platform=tvOS Simulator' CODE_SIGNING_ALLOWED=NO build
```

The Xcode project is generated and ignored. `project.yml`, the Swift package, and the locked CI workflow remain the authoritative build inputs.

Both app targets intentionally use `com.streamvue.player`. Apple requires an added tvOS platform to share the iOS app's bundle ID when the platforms belong to one App Store Connect record, and this lets the same non-consumable purchase serve iPhone, iPad, and Apple TV. A Personal Team may substitute its own available identifier for local device testing, but a Store candidate must use the exact shared ID recorded in `store/apple-distribution.json`.

## Selectable playback engines

KSPlayer's KSMEPlayer is the default engine. It combines FFmpeg demuxing, Metal rendering, VideoToolbox hardware decode, request-header support, adaptive frame presentation, deinterlacing, broader containers, embedded tracks, and text/image subtitles. StreamVue owns the player chrome and keeps one KSPlayer surface alive while moving between inline and true fullscreen presentation. Users can choose AVKit as primary or allow automatic fallback in either direction.

The AVKit path uses `AVURLAsset`, `AVPlayerItem`, `AVPlayer`, and `AVPlayerViewController` directly for standards-compliant HLS and progressive HTTP media. AVKit handles compatible hardware decoding, spatial audio/Atmos, Picture in Picture, AirPlay, HDR, and Apple TV display matching. Apple does not support arbitrary AVURLAsset header injection, so Referer/Authorization streams can fall back to KSPlayer instead of passing credentials through a hidden proxy.

KSPlayer's public package is GPL-3.0. StreamVue pins version 2.3.4 for reproducible personal source builds. `Package.store.swift` is a second, dependency-free package graph: CI copies it over `Package.swift` only for the Store configuration, compiles the same UI through `canImport(KSPlayer)`, exposes AVKit as the only engine, and verifies that the resolved Store graph and app bundles contain no KSPlayer artifact. Including KSPlayer in a public binary still requires either a GPL-compatible StreamVue release or KSPlayer's separately licensed LGPL/commercial package. See [the licensing gate](../../docs/ksplayer-licensing.md).

The normal Debug and Release configurations are personal builds and include Plex/Emby. The `Store` Xcode configuration injects `StreamVueDistributionMode=store` into each app's Info.plist so the shared Swift package reads the correct runtime mode; until StoreKit 2 verifies a real one-time product, that configuration hides credential entry and blocks saved refresh, artwork, and playback at the repository boundary. This purchase gate is separate from both Apple code signing and the KSPlayer distribution license. See [premium access and store readiness](../../docs/premium-entitlements.md).

The iOS and tvOS clients now include a StoreKit 2 purchase controller. Set the exact App Store Connect non-consumable identifier as the `STREAMVUE_PREMIUM_PRODUCT_ID` Xcode build setting for a Store build. The controller loads localized product information, listens for transaction changes, accepts only StoreKit-verified current entitlements, and exposes explicit **Buy once** and **Restore purchase** actions. `AppStore.sync()` is called only from the user's restore action.

Because IPTV providers are user-selected and some still expose only HTTP endpoints, both app targets include the global ATS exception intended for apps that connect to arbitrary user-specified servers. StreamVue defaults an address without a scheme to HTTPS, warns before connecting to explicit HTTP sources, keeps normal server-trust evaluation for HTTPS, and refuses HTTPS-to-HTTP redirects. Public App Store submissions must include this provider-compatibility justification in review notes.

## Playlist and privacy behavior

- M3U/M3U8 URL onboarding on every Apple platform
- Security-scoped M3U file import on iPhone and iPad
- Exact `group-title` browsing in source order
- Automatic URL refresh at launch and protected last-working-copy recovery
- URL credentials stored in Keychain, never in `source.json`
- Protected Application Support cache excluded from device backup
- 64 MiB source limit and 250,000-channel parser limit
- Contract-compatible IDs, guide metadata, catch-up data, User-Agent, and Referer parsing
- No built-in channel catalog and an explicit content-rights confirmation

`PrivacyInfo.xcprivacy` declares no tracking or collected data and documents the settings-only UserDefaults access reason. Store privacy answers must still match the final shipping feature set.

The Apple TV target includes an opaque back plate, two transparent parallax layers, 400×240 and 1280×768 icon stacks, and standard plus wide Top Shelf artwork at both scale factors. `node tools/verify-apple-brand-assets.mjs` checks the role declarations, filenames, PNG dimensions, and alpha-channel boundaries before either Apple workflow reaches Xcode.

## Signing and distribution

CI compiles and analyzes unsigned personal simulator products with KSPlayer and separate AVKit-only Store simulator products without an Apple Developer Program membership. It does not publish either. Installing a personal build on an iPhone, iPad, or Apple TV requires Xcode signing with either a free Personal Team or paid Apple Developer Program team. App Store and TestFlight distribution use the AVKit-only graph unless the owner later records a compatible KSPlayer/StreamVue license; either route still requires the paid Apple membership plus final App Store metadata and provisioning.

The manual **Build Apple Store candidates** workflow is the only signed Store archive lane. It remains fail-closed until both `store/premium-products.json` and `store/apple-distribution.json` are explicitly ready. Before running it, create the shared iOS/tvOS app record and non-consumable in App Store Connect, create an Apple Distribution certificate plus separate App Store provisioning profiles for iOS and tvOS, then configure these GitHub repository values:

- Variables: `STREAMVUE_APPLE_BUNDLE_ID`, `STREAMVUE_APPLE_TEAM_ID`, and `STREAMVUE_APPLE_PREMIUM_PRODUCT_ID`
- Secrets: `STREAMVUE_APPLE_DISTRIBUTION_CERTIFICATE_BASE64`, `STREAMVUE_APPLE_DISTRIBUTION_CERTIFICATE_PASSWORD`, `STREAMVUE_APPLE_IOS_APP_STORE_PROFILE_BASE64`, and `STREAMVUE_APPLE_TVOS_APP_STORE_PROFILE_BASE64`

The workflow resolves and tests dependencies before importing signing material, records the exact Swift package graph, validates both profiles and the distribution identity, builds two signed Store archives with the same bundle/product/version, exports audited IPAs, removes the temporary keychain and profiles, and uploads only a 30-day workflow artifact. It never uploads to App Store Connect automatically. Apple requires a new build number for every upload; the operator must confirm that history in App Store Connect.

The certificate and profiles should be base64-encoded as single-line secret values. Keep their original files and passwords in a separate encrypted backup; never commit them. See Apple's [distribution guide](https://developer.apple.com/documentation/xcode/distributing-your-app-for-beta-testing-and-releases), [App Store profile instructions](https://developer.apple.com/help/account/provisioning-profiles/create-an-app-store-provisioning-profile), and [build upload guide](https://developer.apple.com/help/app-store-connect/manage-builds/upload-builds/).
