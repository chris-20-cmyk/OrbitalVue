# OrbitalVue cross-platform architecture

## Product boundary

OrbitalVue is a player and personal source manager. It does not bundle, sell, discover, or redistribute television streams. Every platform client imports a source supplied by the user and keeps credentials in that platform's private application storage.

## Platform map

| Platform | Native shell | Playback engine | Shared behavior |
| --- | --- | --- | --- |
| Windows | WPF / .NET | LibVLC | Catalog contract, parser fixtures, source behavior |
| Android phone and tablet | Kotlin / Compose | AndroidX Media3 | Catalog contract, parser fixtures, source behavior |
| Android TV / Google TV | Kotlin / Compose, 10-foot surface | AndroidX Media3 | Same Android package and data layer |
| Samsung TV | Tizen packaged web app | AVPlay | Catalog contract and JavaScript conformance parser |
| LG TV | webOS packaged web app | webOS media APIs | Catalog contract and JavaScript conformance parser |
| iPhone, iPad, Apple TV | SwiftUI | KSPlayer/Metal + AVFoundation/AVPlayerViewController | Catalog contract and native Swift parser |

The UI and playback engine remain native to each device family. Only the catalog semantics and synthetic fixtures are portable. This avoids forcing desktop assumptions onto televisions and avoids shipping a browser-based video engine where a native decoder is available.

## Protected personal media centers

Plex and Emby use the versioned `contracts/media-center-contract-v1.schema.json` boundary. A cached catalog contains only a canonical `orbitalvue-media://{provider}/{serverId}/{itemId}` locator plus non-secret presentation and resume metadata. Provider tokens, passwords, and credentialed playback URLs are forbidden from that contract.

Each client follows the same security sequence:

1. Normalize the user-entered HTTP or HTTPS base address and require explicit consent before cleartext HTTP credentials are used.
2. Read the provider's public identity endpoint without a token.
3. Bind the protected credential to the provider, normalized origin, and verified server ID.
4. Fetch and cache a token-free catalog in platform-private storage.
5. Resolve a playable provider URL only when the user selects an item, strip any credential query supplied by catalog metadata, reject cross-origin paths, and inject the locally protected token at the final playback boundary.

Apple, Android, and Windows additionally support Plex's strong signed-PIN account flow. Each platform keeps a stable device-registration Ed25519 private key locally and sends only its public JWK while creating the PIN. Apple stores the identity in Keychain; Android stores it in a Tink encrypted keyset wrapped by a verified Android Keystore key and fails closed without cleartext fallback; Windows stores only the private seed under current-user DPAPI and imports it into a non-exportable runtime key. The transient account token is used in core/data memory to verify the Plex account and fetch server-scoped resources, then discarded. SwiftUI, Compose, and WPF receive only sanitized server choices and an opaque ten-minute discovery lease. The chosen server token moves directly into the existing origin-bound secure credential record after an unauthenticated identity probe whose server ID must match the selected account resource; token-bearing resource fields are rejected from server IDs, names, and connection URLs. Cancellation and premium-entitlement loss destroy the lease before it can be reused.

Windows protects media-center credentials and its Plex device-signing seed with current-user DPAPI, offers signed browser approval with automatic server discovery plus an advanced manual-token fallback, and plays the resolved URL through LibVLC. Android uses its platform-protected credential store and Media3. Apple stores secrets in Keychain and hands the short-lived plan to KSPlayer or AVKit. Samsung and LG use the shared contract and capability-gate playback when the television API cannot safely express a provider requirement. No platform routes personal-library traffic through a OrbitalVue proxy.

## Android 5.2 foundation

The Android milestone is one adaptive application package for touch devices and Android TV. It targets stable API 36, compiles against the Android 37.1 API surface required by its AndroidX dependencies, uses Media3 1.11, and declares both normal launcher and Leanback launcher entry points. TV navigation uses remote-focusable controls and a room-scale layout rather than stretching the phone screen. Version 5.2 adds strong signed Plex PIN approval, account server discovery, and an opaque token-free selection lease with identity-bound credential activation.

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

### Android package and signing boundary

Android also has two separate release paths. Personal device testing uses the generated debug APK. Foundation CI compiles Store mode with Plex/Emby fail-closed and deliberately verifies that its AAB is unsigned. It has no product ID or verifier URL and is named as a locked verification artifact.

The manual Google Play candidate workflow is the only path that accepts OrbitalVue's upload-key environment variables. It refuses personal mode, partial signing configuration, missing product/verifier inputs, a readiness-manifest mismatch, an unregistered upload certificate, or an invalid version input. The operator must still supply a version code that has never previously been uploaded because only Play Console has authoritative version history. The workflow runs unit tests and lint, builds the optimized release AAB, verifies the JAR signature and RSA upload certificate, rejects packaged private-key files, and emits a checksum alongside the AAB. It stops before publication so the signed candidate can be reviewed and uploaded through Play Console. Google Play App Signing remains responsible for the app-signing key used on device-delivered APKs.

### Shared entitlement-verifier hosting boundary

The Google Play and Samsung clients share a provider-neutral verification core and a separately packaged Cloudflare Worker adapter. The Worker has generated binding types, required secret declarations, exact-host/origin checks, bounded streaming JSON, separate staging/production rate-limit namespaces, HMAC-derived purchaser keys, identifier-free structured logs, and Workers-runtime tests. Its committed configuration disables `workers.dev` and preview URLs and has no route, so it is buildable evidence rather than a live entitlement service. Production remains locked until a custom domain, exact seller identifiers, four environment secrets, API permissions, privacy retention, and real purchase/refund evidence are recorded.

### Windows package and update boundary

Windows has two deliberately separate distribution graphs. Personal/direct-download builds include Velopack, Stable/Preview GitHub release checks, in-place installation, and failed-launch rollback. Microsoft Store builds compile without Velopack and run under an x64 `Windows.Desktop` MSIX identity; the Store signs, installs, and updates them. The in-app update surface reports that Store-managed state instead of contacting GitHub or offering a second installer.

`packaging/windows-msix/AppxManifest.template.xml` declares the WPF process as a `packagedClassicApp` at `mediumIL` with `runFullTrust`. `tools/build-windows-msix.ps1` injects only exact non-secret Partner Center identifiers, writes a package audit, and invokes the Windows SDK MakeAppx tool. `tools/verify-windows-msix.ps1` then unpacks the result and independently verifies identity, Windows target, execution model, assets, premium product ID, and absence of Velopack. The public candidate workflow is fail-closed behind the Windows premium-readiness entry; foundation CI uses an obviously synthetic, non-publishable identity.

## Apple 5.1 foundation

`platforms/apple` contains a dependency-free `OrbitalVueCore` module and a `OrbitalVueUI` module that integrates the pinned KSPlayer package. XcodeGen creates separate `OrbitalVue` iPhone/iPad and `OrbitalVueTV` Apple TV application targets from the committed `project.yml`, keeping generated Xcode project state out of source control.

The touch client adapts through `NavigationSplitView`; compact devices move selected playback into a true fullscreen cover. Apple TV uses a remote-first, focus-aware three-column group/channel/player workspace. Both clients consume the same source-ordered catalog, exact `group-title` sections, search, favorites, playback settings, and privacy-safe repository.

The Swift parser enforces the shared 64 MiB input and 250,000-channel ceilings. URL credentials are separated from the non-secret source manifest and stored in Keychain. The last working playlist is written to protected Application Support storage and excluded from backup, then used only when launch refresh fails.

The Apple, Android, and Windows Plex connectors provide QR or external-browser approval, lifecycle-bound automatic PIN polling, account-wide server discovery, preferred secure/local connection selection, and a manual server-token fallback. Account and server tokens never become SwiftUI, Compose, or WPF state; an expiring opaque lease is the only bridge between discovery and server selection.

Playback is engine-neutral above the surface layer. KSPlayer 2.3.4 is the default KSMEPlayer/Metal path for broader demuxing, arbitrary provider headers, hardware decode, adaptive presentation, and embedded tracks; AVURLAsset, AVPlayerItem, AVPlayer, and AVPlayerViewController provide a selectable native path and two-way fallback. Neither path sends credentials through a OrbitalVue proxy.

User-entered source domains cannot be enumerated ahead of time, and some providers expose only HTTP endpoints. The Apple targets therefore carry a deliberate global ATS exception: missing schemes default to HTTPS, explicit HTTP sources display a cleartext warning, HTTPS-to-HTTP redirects are refused, and App Store review notes must justify the exception as compatibility with arbitrary user-specified third-party servers.

Foundation CI gates are Swift package contract/privacy tests, the machine-readable KSPlayer decision, exact shared bundle identity, privacy-manifest validation, layered tvOS icon/Top Shelf structure, and Xcode builds plus analysis of both simulator targets. It builds the pinned KSPlayer graph for personal evaluation, then regenerates both Store targets from `Package.store.swift` and verifies an AVKit-only resolution. KSPlayer-enabled binaries remain unpublished unless OrbitalVue later chooses GPL-compatible distribution or obtains KSPlayer's separately licensed package.

The manual Apple Store candidate is a separate graph. Premium and software-license manifests must both be ready before any signing secret is read. Dependencies are resolved first; the workflow then imports a protected Apple Distribution identity and two platform profiles into a temporary keychain, archives iOS and tvOS with the same bundle ID and StoreKit product, validates each signed app, exports IPAs, removes signing material, and emits a checksum/audit artifact without uploading it. Real-device HLS, Picture in Picture, AirPlay, iPad layout, Apple TV remote-focus/display-matching, privacy metadata, and TestFlight review remain human release checks.

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

TV file onboarding cannot assume that every model exposes a usable file picker, so URL entry is the guaranteed path. The shared shell also provides file selection where the platform supports it. OrbitalVue does not upload playlist contents to a hosted conversion service.

### Samsung Tizen adapter

- Package: signed Tizen web `.wgt`.
- Playback: Samsung AVPlay, not an HTML video abstraction.
- Inputs: mandatory arrows, Enter, and Back plus registered media keys.
- Source headers: documented AVPlay streaming properties cover User-Agent and Cookie. Arbitrary Referer support is not promised, so channels requiring Referer are reported as unsupported instead of silently proxying credentials.
- Lifecycle: stop/release AVPlay on termination and restore catalog/focus state on relaunch.
- Premium commerce: Samsung Checkout opens the native one-time purchase surface only after a protected backend confirms DPI country availability; the deprecated native production-service probe is used as a second check where the TV still exposes it. The backend owns the DPI security key and returns only the exact product offer plus purchase-history decision. Native callbacks never grant access, and foreground/history rechecks can revoke an active media-center session.
- Signing: original Samsung author plus Partner distributor certificate for the Store candidate; registered TV DUID in a local distributor certificate for sideload tests. CI validates the author fingerprint and never auto-submits the package.

### LG webOS adapter

- Package: webOS web-app `.ipk`.
- Playback: platform HTML5 media path with webOS HLS/HTTP capability checks.
- Inputs: pointer and complete 5-way focus navigation; Back unwinds OrbitalVue state before yielding to the platform.
- Source headers: the native media element does not provide a portable arbitrary-request-header contract. Header-dependent streams are capability-gated and never routed through an untrusted proxy.
- Lifecycle: persist catalog and navigation state before suspension and rebuild media state explicitly after resume.
- Premium commerce: LG's native billing service is discontinued. Store builds remain visibly unavailable—with no fake purchase action or client-side entitlement—until a reviewed third-party provider, identity/recovery flow, backend verification, and refund handling are implemented.
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
