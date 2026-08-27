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

## Selectable playback engines

KSPlayer's KSMEPlayer is the default engine. It combines FFmpeg demuxing, Metal rendering, VideoToolbox hardware decode, request-header support, adaptive frame presentation, deinterlacing, broader containers, embedded tracks, and text/image subtitles. StreamVue owns the player chrome and keeps one KSPlayer surface alive while moving between inline and true fullscreen presentation. Users can choose AVKit as primary or allow automatic fallback in either direction.

The AVKit path uses `AVURLAsset`, `AVPlayerItem`, `AVPlayer`, and `AVPlayerViewController` directly for standards-compliant HLS and progressive HTTP media. AVKit handles compatible hardware decoding, spatial audio/Atmos, Picture in Picture, AirPlay, HDR, and Apple TV display matching. Apple does not support arbitrary AVURLAsset header injection, so Referer/Authorization streams can fall back to KSPlayer instead of passing credentials through a hidden proxy.

KSPlayer's public package is GPL-3.0. StreamVue pins version 2.3.4 for reproducibility, but CI does not upload KSPlayer-enabled binaries while this repository has no compatible software license. Personal source builds may proceed; public binary/App Store distribution requires either a GPL-compatible StreamVue release or KSPlayer's separately licensed LGPL/commercial package. See [the licensing gate](../../docs/ksplayer-licensing.md).

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

## Signing and distribution

CI compiles and analyzes unsigned simulator products without an Apple Developer Program membership, but does not publish them while the KSPlayer licensing gate is unresolved. Installing a personal build on an iPhone, iPad, or Apple TV requires Xcode signing with either a free Personal Team or paid Apple Developer Program team. App Store and TestFlight distribution require both a compatible KSPlayer/StreamVue software license and the paid Apple membership plus final App Store assets/provisioning.
