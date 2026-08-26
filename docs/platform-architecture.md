# StreamVue cross-platform architecture

## Product boundary

StreamVue is a player and personal source manager. It does not bundle, sell, discover, or redistribute television streams. Every platform client imports a source supplied by the user and keeps credentials in that platform's private application storage.

## Platform map

| Platform | Native shell | Playback engine | Shared behavior |
| --- | --- | --- | --- |
| Windows | WPF / .NET | LibVLC | Catalog contract, parser fixtures, source behavior |
| Android phone and tablet | Kotlin / Compose | AndroidX Media3 | Catalog contract, parser fixtures, source behavior |
| Android TV / Google TV | Kotlin / Compose, 10-foot surface | AndroidX Media3 | Same Android package and data layer |
| Samsung TV | Tizen packaged web app | AVPlay | Catalog contract and JavaScript conformance parser |
| LG TV | webOS packaged web app | webOS media APIs | Catalog contract and JavaScript conformance parser |
| iPhone, iPad, Apple TV | SwiftUI | AVFoundation / AVPlayerViewController | Catalog contract and native Swift parser |

The UI and playback engine remain native to each device family. Only the catalog semantics and synthetic fixtures are portable. This avoids forcing desktop assumptions onto televisions and avoids shipping a browser-based video engine where a native decoder is available.

## Android 5.0 foundation

The first Android milestone is one adaptive application package for touch devices and Android TV. It targets stable API 36, compiles against the Android 37.1 API surface required by its AndroidX dependencies, uses Media3 1.11, and declares both normal launcher and Leanback launcher entry points. TV navigation uses remote-focusable controls and a room-scale layout rather than stretching the phone screen.

The initial source pipeline supports:

1. Local M3U/M3U8 selection through Android's document picker.
2. Direct playlist URL entry with a private on-device cache.
3. Refresh-on-launch for URL sources with last-working-copy fallback.
4. Group-preserving channel browsing and search.
5. Per-channel `User-Agent` and `Referer` playback headers.
6. HLS, progressive MPEG-TS/MP4, and RTSP through Media3.
7. Hardware-backed MediaCodec decoding with decoder fallback.
8. Seamless display frame-rate hints, multiple aspect modes, and immersive playback.

RTMP is preserved in the portable catalog for Windows compatibility but is not claimed as playable by Media3. A later signal-routing layer can offer a platform-specific fallback when a source contains RTMP-only feeds.

## Store readiness gates

Before public store submission, each client must pass its native remote/touch accessibility checks, use store-specific artwork, publish a privacy policy, explain that playlists are user supplied, and test with licensed streams owned by the tester. Signing and paid enrollment do not block source development, simulator/emulator verification, or platform-provided free personal-device workflows; physical-device requirements remain platform-specific.

## Apple 5.1 foundation

`platforms/apple` contains dependency-free `StreamVueCore` and `StreamVueUI` Swift package modules. XcodeGen creates separate `StreamVue` iPhone/iPad and `StreamVueTV` Apple TV application targets from the committed `project.yml`, keeping generated Xcode project state out of source control.

The touch client adapts through `NavigationSplitView`; compact devices move selected playback into a true fullscreen cover. Apple TV uses a remote-first, focus-aware three-column group/channel/player workspace. Both clients consume the same source-ordered catalog, exact `group-title` sections, search, favorites, playback settings, and privacy-safe repository.

The Swift parser enforces the shared 64 MiB input and 250,000-channel ceilings. URL credentials are separated from the non-secret source manifest and stored in Keychain. The last working playlist is written to protected Application Support storage and excluded from backup, then used only when launch refresh fails.

Playback uses AVURLAsset, AVPlayerItem, AVPlayer, and AVPlayerViewController directly. The supported boundary is standards-compatible HLS and progressive HTTP media that AVFoundation can decode. Unsupported schemes and channels requiring arbitrary Referer or Authorization headers are rejected with an explicit capability message rather than sent through a hidden proxy.

Current CI gates are Swift package contract/privacy tests, privacy-manifest validation, Xcode analysis of both simulator targets, and unsigned simulator packages with SHA-256 checksums. Store readiness additionally requires real-device HLS tests, Picture in Picture and AirPlay validation, iPad layout checks, Apple TV remote-focus/display-matching tests, final App Store artwork and privacy metadata, signed Release archives, and TestFlight acceptance.

## Television foundation

Samsung Tizen and LG webOS now share a TypeScript contract package and one remote-first interface, while each keeps the television vendor's playback API, lifecycle rules, remote keys, packaging tools, and store submission process.

### Shared television layer

`packages/catalog-js` is a dependency-free TypeScript implementation of the 1.0 catalog contract. It parses the committed fixtures to byte-for-byte compatible stable IDs and preserves source order, exact `group-title`, guide URLs, catch-up attributes, `User-Agent`, and `Referer`. The television shell consumes normalized catalog objects and never interprets raw M3U lines inside UI components.

Both TV shells use the same remote-first state machine:

1. Source onboarding and private IndexedDB last-working-copy cache.
2. Group rail, categorized All Channels view, search, favorites, and bounded rendering for very large lists.
3. Player surface with live status, seven aspect modes, native buffering state, and stream capability messages.
4. Deterministic focus restoration after dialogs, playback, and Back.
5. Automated parser, navigation, platform-selection, and privacy checks plus browser acceptance at 1920×1080 and 1280×720.

TV file onboarding cannot assume that every model exposes a usable file picker, so URL entry is the guaranteed path. The shared shell also provides file selection where the platform supports it. StreamVue does not upload playlist contents to a hosted conversion service.

### Samsung Tizen adapter

- Package: signed Tizen web `.wgt`.
- Playback: Samsung AVPlay, not an HTML video abstraction.
- Inputs: mandatory arrows, Enter, and Back plus registered media keys.
- Source headers: documented AVPlay streaming properties cover User-Agent and Cookie. Arbitrary Referer support is not promised, so channels requiring Referer are reported as unsupported instead of silently proxying credentials.
- Lifecycle: stop/release AVPlay on termination and restore catalog/focus state on relaunch.
- Signing: Samsung author plus distributor certificate; registered TV DUID for sideload tests.

### LG webOS adapter

- Package: webOS web-app `.ipk`.
- Playback: platform HTML5 media path with webOS HLS/HTTP capability checks.
- Inputs: pointer and complete 5-way focus navigation; Back unwinds StreamVue state before yielding to the platform.
- Source headers: the native media element does not provide a portable arbitrary-request-header contract. Header-dependent streams are capability-gated and never routed through an untrusted proxy.
- Lifecycle: persist catalog and navigation state before suspension and rebuild media state explicitly after resume.
- Testing: Developer Mode device test is mandatory because emulator playback support differs by webOS generation.

### Television acceptance gates

| Gate | Samsung | LG |
| --- | --- | --- |
| Contract fixtures and stable IDs | Shared TypeScript suite | Shared TypeScript suite |
| HLS live start/retry/stop | AVPlay device test | HTML5 media device test |
| Remote-only onboarding/browse/playback | Tizen remote suite | Magic Remote 5-way suite |
| Back/exit policy | Samsung Return/Exit policy | webOS Back behavior |
| Private URL cache and token-safe diagnostics | Required | Required |
| Store package | Signed `.wgt` | Reviewed `.ipk` |

Public store submission waits for real-device tests on at least one current and one older supported television generation. Emulator success alone is not sufficient evidence for streaming compatibility.
