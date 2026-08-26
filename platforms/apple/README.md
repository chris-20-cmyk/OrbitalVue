# StreamVue for iPhone, iPad, and Apple TV

This directory contains the native SwiftUI and AVFoundation StreamVue 5.1 foundation. It is one dependency-free Swift package plus two generated application targets:

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

## Native playback boundary

StreamVue uses `AVURLAsset`, `AVPlayerItem`, `AVPlayer`, and `AVPlayerViewController` directly. It supports standards-compliant HLS and progressive HTTP media that AVFoundation can decode. Hardware decoding, alternate audio/subtitles, spatial audio, Atmos-capable output, Picture in Picture, AirPlay, and Apple TV display matching remain system-managed features and depend on the source, device, receiver, and user settings.

Apple does not support arbitrary AVURLAsset header injection. StreamVue supports a source-provided User-Agent and appropriately scoped cookies. Channels requiring Referer, Authorization, RTSP, RTMP, or UDP are capability-gated with a clear message rather than routed through a hidden proxy.

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

## Signing

CI produces unsigned simulator packages without an Apple Developer Program membership. Installing on a personal iPhone, iPad, or Apple TV requires Xcode signing with either a free Personal Team or paid Apple Developer Program team. App Store and TestFlight distribution require the paid annual membership and final App Store assets/provisioning.
