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
| iPhone, iPad, Apple TV | SwiftUI | AVPlayer | Catalog contract and native Swift parser |

The UI and playback engine remain native to each device family. Only the catalog semantics and synthetic fixtures are portable. This avoids forcing desktop assumptions onto televisions and avoids shipping a browser-based video engine where a native decoder is available.

## Android 5.0 foundation

The first Android milestone is one adaptive application package for touch devices and Android TV. It targets API 36, compiles with API 37, uses Media3 1.11, and declares both normal launcher and Leanback launcher entry points. TV navigation uses remote-focusable controls and a room-scale layout rather than stretching the phone screen.

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

Before public store submission, each client must pass its native remote/touch accessibility checks, use store-specific artwork, publish a privacy policy, explain that playlists are user supplied, and test with licensed streams owned by the tester. Release signing and developer-account enrollment are distribution gates, not blockers for local development or unsigned personal testing.

## Television sequence

Samsung Tizen and LG webOS follow after the Android player proves the catalog and playback-state model. They will share a TypeScript contract package and the same fixture suite, but each will use the television vendor's playback API, lifecycle rules, remote keys, packaging tools, and store submission process.

### Shared television layer

The next client milestone is `packages/catalog-js`, a dependency-light TypeScript implementation of the 1.0 catalog contract. It must parse the committed fixtures to byte-for-byte compatible stable IDs and preserve source order, exact `group-title`, guide URLs, catch-up attributes, `User-Agent`, and `Referer`. The television shells consume normalized catalog objects and never interpret raw M3U lines inside UI components.

Both TV shells use the same remote-first state machine:

1. Source onboarding and private last-working-copy cache.
2. Group rail, categorized All Channels view, search, and favorites.
3. Player surface with live status, aspect menu, retry, and stream capability message.
4. Deterministic focus restoration after dialogs, playback, app suspension, and Back.
5. A no-mouse acceptance pass from cold launch through source connection and playback.

TV file onboarding cannot assume a desktop file picker. URL entry is the first guaranteed path. Local-file parity will use a privacy-preserving same-network transfer flow or removable-storage capability only after it is verified on each vendor's supported models; StreamVue will not upload playlist contents to a hosted conversion service.

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
