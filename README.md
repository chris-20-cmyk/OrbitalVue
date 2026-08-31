# StreamVue Native

StreamVue is a native IPTV player. The Windows 5.5 preview uses .NET, WPF, LibVLCSharp, the VideoLAN playback engine, and Velopack packaging. The Android 5.2 line uses Kotlin, Compose, and AndroidX Media3 for phones, tablets, Android TV, and Google TV. Samsung Tizen and LG webOS share a lightweight remote-first TypeScript surface while retaining each vendor's native television playback path. Apple 5.1 uses SwiftUI with selectable KSPlayer/Metal and AVFoundation/AVKit engines for iPhone, iPad, and Apple TV.

## Android 5.2 foundation

- M3U/M3U8 file and URL import with a private last-working-copy cache and automatic URL refresh at launch
- Exact playlist grouping, categorized All Channels browsing, fast search, and remote-friendly 10-foot navigation
- Native Media3 HLS, progressive MPEG-TS/MP4, and RTSP playback with per-channel request headers
- Hardware-backed MediaCodec decoding, decoder fallback, seamless frame-rate hints, six aspect modes, and immersive full screen
- A versioned portable catalog contract and synthetic conformance fixtures shared with Samsung Tizen, LG webOS, and Apple clients
- Repeatable cloud builds that produce a personal-test APK, an explicitly locked unsigned foundation AAB, and a readiness-gated upload-key-signed Google Play candidate without requiring a paid certificate
- Signed Plex PIN login with QR/browser approval, automatic secure/local server discovery, a Keystore-only Ed25519 identity, token-free Compose state, identity-bound credential storage, and cancellation rollback

The Android Studio project lives in `platforms/android`; the shared contract lives in `contracts`. StreamVue does not ship or discover channels. Users connect sources they are authorized to use.

See [Android build instructions](platforms/android/README.md), the [cross-platform architecture](docs/platform-architecture.md), and the [distribution and signing choices](docs/distribution-and-signing.md).

## Samsung and LG TV foundation

- Shared contract-compatible TypeScript M3U parser with exact Windows/Android stable IDs
- Premium 10-foot browsing with exact playlist groups, categorized All Channels sections, search, and private favorites
- Directional-pad, OK, Back, channel-key, media-key, and Magic Remote behavior
- Samsung AVPlay and LG/native HTML5 player adapters with honest per-platform request-header capability messages
- Private IndexedDB last-working catalog with automatic URL refresh at launch
- Separate Tizen and webOS package directories, store metadata, icons, and LG splash artwork

See [TV build and sideload instructions](platforms/tv-web/README.md) and the [television design system](docs/design/tv-shell-design-system.md).

## Apple 5.1 foundation

- Native SwiftUI clients for iOS, iPadOS, and tvOS 17 or later, with an adaptive `NavigationSplitView` on touch devices and a focus-aware three-column Apple TV workspace
- Playlist URL onboarding everywhere plus security-scoped M3U/M3U8 file import on iPhone and iPad
- Exact playlist groups, categorized All Channels sections, search, favorites, 14 aspect modes, and true fullscreen playback
- KSPlayer 2.3.4 as the default Metal/FFmpeg engine, with hardware decoding, adaptive frame cadence, deinterlacing, configurable buffering/subtitle size, request headers, and transparent AVKit fallback
- Direct AVFoundation/AVKit fallback with system-managed hardware decoding, compatible spatial audio/Atmos paths, Picture in Picture, AirPlay, and Apple TV display matching
- Keychain-protected URL secrets, complete file protection for the cached playlist, launch refresh, and last-working-copy recovery
- Signed Plex PIN login with QR approval, automatic secure/local server discovery, and token-free SwiftUI state
- Xcode 26.6 CI that tests the shared Swift catalog, validates Apple TV layered artwork, and analyzes both app targets without publishing a license-incompatible binary

The Apple milestone is a source/simulator foundation plus a locked signed-candidate lane, not a published App Store package. Physical-device installation requires Xcode signing. Personal source builds include pinned GPL-3.0 KSPlayer, while the audited Store package graph is AVKit-only and excludes that dependency. Adding KSPlayer to a public binary would still require a GPL-compatible or separately licensed route. App Store and TestFlight distribution also require Apple Developer Program enrollment, privacy answers, and signed archives. The candidate lane uses one bundle ID and StoreKit product across iOS and tvOS, validates protected signing material, and stops before upload. See [Apple build instructions](platforms/apple/README.md) and [KSPlayer licensing gate](docs/ksplayer-licensing.md).

## 5.1 protected personal libraries

- Plex token and Emby account connections alongside M3U and Xtream sources on Windows, Android/Google TV, Samsung/LG TV, and the Apple foundation; Windows, Apple, and Android/Google TV also support signed Plex account discovery
- A shared `streamvue-media://` catalog contract that stores provider, verified server identity, and item identity—but never a playable token URL
- Public server-identity verification before credentials are sent, explicit consent for cleartext local HTTP, same-origin playback-path enforcement, and bounded provider responses
- Platform-protected credential storage with a separate encrypted last-working catalog; passwords are exchanged for provider tokens and are not retained
- Just-in-time Plex direct-play and Emby direct-play/direct-stream/transcode resolution, so the active native player is the only component that briefly receives a credentialed URL
- Windows VOD resume and seek controls using server-provided playback positions, with normal source-manager refresh, offline fallback, and safe credential cleanup
- Windows 5.4 sends Plex timeline and Emby session check-ins on play, pause, seek, ten-second progress, stop, source changes, entitlement revocation, and shutdown so resume/watched state follows the selected media-center account; reporting is serialized, bounded, cancellable, and never interrupts local video
- Windows 5.5 renders Plex and Emby posters in the virtualized channel library and Signal desk through token-free artwork locators, same-server identity verification, header-only credentials, four-request concurrency, strict image/size validation, and a 160-item decoded-image memory ceiling
- A cross-platform premium-access boundary: personal builds include Plex/Emby, while store builds collect no credentials and make no media-server requests until a native one-time purchase is verified
- Native one-time purchase foundations: StoreKit 2 on iOS/tvOS, Google Play Billing 9.1.0 on Android/Google TV, a Microsoft Store durable-license adapter on Windows, and Samsung Checkout with a protected DPI verifier, including localized pricing, Buy/Restore actions, live entitlement changes, and fail-closed verification. The shared server package verifies Google ProductPurchaseV2 state and Samsung product/history HMACs without returning tokens or account identifiers. Its route-disabled Cloudflare Worker adapter adds required secret bindings, exact-host/origin controls, opaque purchaser-key rate limiting, identifier-free logs, Workers-runtime tests, and a no-deploy dry run. LG is explicitly unavailable until its required third-party billing route is contracted and verified.
- Reproducible x64 Windows MSIX packaging with exact Partner Center identity injection, Store-owned updates, no Velopack runtime in Store packages, exact tile assets, independent package inspection, and a readiness-gated candidate workflow.
- A protected Google Play upload-key lane with explicit versioning, exact product/verifier readiness, registered-certificate fingerprint checks, and a signed AAB/checksum artifact for manual Play Console upload.

These integrations connect only to a server entered by the user and do not include a hosted relay, public-media discovery, or third-party content.

The intended store product is a one-time lifetime unlock, not a subscription. Store product IDs and native receipt providers are intentionally unconfigured until they are created in each seller console. Android and Samsung additionally require the hosting/secrets/device evidence in `store/premium-verifier-readiness.json`; the checked Worker adapter is not a production deployment. Public Store candidates also stay locked until the [technical privacy inventory and Store disclosures](docs/privacy-and-store-disclosures.md) have a published policy/support page, the [Store listing production contract](docs/store-listing-production.md) has approved copy, rights/rating decisions, and real-device artwork, and the [accessibility validation matrix](docs/accessibility-validation.md) has evidence for the exact build. Generate one current blocker dossier with `pnpm release:report`; see [release readiness reporting](docs/release-readiness-report.md), [premium access and store readiness](docs/premium-entitlements.md), the [Worker deployment runbook](packages/entitlement-verifier-worker/README.md), and the [cross-platform release-control matrix](docs/cross-platform-release-control.md).

## 4.0 production resilience

- Stable and Preview release channels with one-package last-known-good rollback if a newly installed build cannot open successfully
- A manual Signal Route editor that links missed duplicate feeds or permanently keeps an incorrect match separate across playlist refreshes
- M3U `catchup`, `catchup-source`, `catchup-days`, `timeshift`, and correction parsing plus Xtream TV Archive replay from past Guide programmes
- A searchable Channel Health Center for guide gaps, missing artwork, observed weak feeds, duplicate routes, and replay-ready channels
- Interrupted-session recovery for the active channel, Watch/Guide/Favorites/Multiview workspace, searches, guide window, layout, and fullscreen state
- Thread-safe single-instance coordination so the background recorder remains authoritative across app activation and async shutdown paths

## 3.9 smart signal routing

- Equivalent live feeds from different providers appear as one logical channel while every underlying stream remains available privately
- Automatic signal scoring learns startup speed, successful tunes, buffering, reconnects, dropped frames, resolution, and bitrate
- Backup-feed switching begins only after native reconnect, smart buffering, watchdog, and decoder recovery have been exhausted
- A polished feed chooser provides Use now, Prefer, Never use, per-feed quality details, and private signal history
- Guide listings are unified across equivalent feeds so a missing provider mapping can fall back to another feed's XMLTV match
- Signal preferences and measurements stay on the PC and never expose playlist addresses or account details

## 3.8 source manager

- A polished My sources workspace for saved M3U files, M3U URLs, and protected Xtream accounts
- Enable, pause, reorder, refresh, use, or safely remove each provider without rebuilding the rest of the library
- Per-source launch-refresh controls with encrypted cache-only startup for providers that should stay offline
- Coordinated startup refresh that keeps successful providers live, falls back failed providers independently, and merges every available source
- Clear per-source status, channel count, last-refresh time, and privacy-safe location display

## 3.7 multi-source foundation

- Automatic migration of the existing 3.6 playlist connection into an ordered multi-source catalog
- Source-aware channel identity and deterministic unified-library merging with exact duplicate suppression
- Independent Windows-user-encrypted offline playlist caches for every M3U file, M3U URL, and Xtream source
- A protected multi-account Xtream vault that migrates the existing single account without asking for credentials again
- Per-source refresh health, channel counts, fallback state, and last-error history
- Backup and restore coverage for the complete multi-source cache set, including automatic cleanup of stale restored entries

## Current preview

- Background DVR mode that keeps schedules and active recordings running from the Windows notification area after the main window closes
- Resumable Windows wake timers that can bring the PC out of supported sleep states two minutes before a scheduled recording
- Single-instance activation so reopening StreamVue restores the background window instead of starting a competing recorder
- Automatic interrupted-recording recovery with 2, 5, and 10 second backoff, preserved playable segments, and up to five attempts
- Safe Windows shutdown handling that finalizes an active transport stream before the app exits
- Live TV disk timeshift with pause, a configurable 15–120 minute window, live-edge return, and rewind controls on seekable streams
- Advanced series rules for new episodes only or all airings, this channel or any matching channel, and keep-latest retention
- XMLTV season, episode, new-airing, and repeat metadata used for cross-channel duplicate prevention
- Seven-day DVR calendar filters, next-recording summary, recording health, time remaining, and estimated hours of drive capacity
- Native in-app playback for saved DVR recordings with a compact seek bar and original-quality local-file rendering
- Automatic per-recording resume positions, a visible watched-progress indicator, and completed-playback cleanup
- Safe confirmed recording deletion constrained to the selected recordings folder
- DVR storage reporting with free space, total capacity, library size, and recording count
- Smart DVR series rules that automatically materialize matching future guide airings
- Configurable start/end recording padding and per-schedule Low, Normal, or High priority
- Automatic one-tuner conflict resolution with clear winner, at-risk, skipped, and padding-handoff feedback
- Configurable free-space protection checked before and throughout live recording
- One-click live DVR recording from the player controls or Ctrl+Shift+R, with elapsed-time, file-size, and save status
- Independent direct-stream capture that records original-quality MPEG-TS without retuning or interrupting the channel being watched
- TV Guide scheduling with automatic start and stop while StreamVue is open
- Configurable recordings folder plus a playable built-in recordings library
- Recording-safe updates and shutdown confirmation so active transport-stream files close cleanly
- Native CAST control that opens Windows nearby-display discovery for powered-on Miracast TVs, projectors, adapters, and receiving PCs—even when they have not been paired before
- Privacy-preserving screen-and-audio mirroring that keeps playlist addresses, decoding, subtitles, aspect ratio, and Playback IQ on the PC
- Compact Mini Player with exact full-workspace restoration and an optional always-on-top mode for multi-monitor work
- Session sleep timer with 30, 60, and 90 minute presets that stops both single-channel and Multiview playback
- Optional last-channel resume after the saved playlist completes its automatic startup refresh
- Windows media-key support for play/pause, stop, channel navigation, mute, and volume
- Current-user-encrypted backups for settings, playlists, guide data, favorites, reminders, Multiview layouts, and Playback IQ learning
- Recovery-safe restore with an automatic rollback copy before any saved data is replaced
- Privacy-filtered diagnostic ZIP export that excludes provider addresses, credentials, channel names, and guide titles
- Playback IQ planner that chooses Fast tune, Learned fast tune, Smart tune, Stable recovery, or Software safe mode per channel
- Faster healthy-channel startup with an automatically expanding cache when a provider or network becomes unstable
- Startup-deadline recovery for streams that remain stuck in Opening without delivering playable media
- Staged recovery that tries a larger smart buffer before graduating repeated failures to software decoding
- Visible tune-strategy and startup-time telemetry in the Signal desk
- Per-channel buffer and decoder controls with one-click Apply & retune and resettable learning
- Fullscreen live-TV HUD with channel identity, current/next programme, progress, clock, and stream telemetry
- Quick Tune overlay for number, name, group, favorites, and recently watched channels
- Remote-style Up/Down channel surfing, Page Up/Down group surfing, and Backspace previous-channel recall
- Per-channel playback profiles that remember buffer, decoder, aspect, deinterlace, track, and A/V sync choices
- Channel-aware recovery panel with immediate retry and a one-click stable-buffer override
- Playlist source health with refresh history, channel additions/removals, fallback status, and manual refresh
- Full Channel Health Center with metadata gaps, observed reliability, duplicate routes, and replay coverage
- Programme reminders from the guide with tune, snooze, and dismiss actions
- Multiview 2.0 with drag/drop assignment, tile swapping, and named reusable layouts
- Large M3U/M3U8 file and URL indexing
- Xtream-compatible live channel login
- Protected Plex and Emby personal-library sources with token-free catalogs and just-in-time native playback
- Signed Plex browser approval with automatic secure/local server discovery, a DPAPI-protected Ed25519 device identity, a revocable token-free selection lease, and an advanced manual-token fallback
- Automatic startup refresh for every enabled M3U file, M3U URL, Xtream account, Plex server, and Emby server
- Professional Multiview workspace with 2-up, 4-up, and focused viewing layouts
- Four persistent channel assignments with keyboard switching and one-click tile controls
- Single-audio focus so only the chosen Multiview tile is audible at a time
- Resource-aware Focus and 2-up modes that suspend hidden native streams
- Window-safe native child video surfaces that stay clipped inside their assigned tiles
- Uninterrupted picture on multi-monitor setups when StreamVue loses keyboard focus
- True borderless fullscreen that occupies every pixel of the selected display, including the taskbar area
- Video-only Watch fullscreen and an edge-to-edge Multiview signal grid
- F11, Alt+Enter, Escape, and Watch-video double-click fullscreen controls
- Exact restoration to the previous normal or maximized window state
- Fullscreen cursor auto-hide with a brief on-screen exit hint
- Automatic XMLTV guide discovery for supported playlists plus a verified US guide pack fallback
- Searchable native TV Guide with current and upcoming programmes, progress, and one-click tuning
- Six-hour timeline guide with proportional programme blocks, a live now marker, and earlier/now/later navigation
- Guide filters for favorites, sports, movies, news, and stable channels that still need mapping
- Manual playlist-to-XMLTV channel mapping with ranked candidates and Windows-user-encrypted persistence
- Lightweight XMLTV channel catalog that keeps mapping responsive without caching every provider programme
- Manual XML/XML.GZ guide files or URLs, including multiple merged sources
- Windows-user-encrypted guide source configuration and compressed offline programme cache
- Coverage reporting and mapping that separate temporary event feeds from stable unmatched channels
- Compressed, Windows-user-encrypted last-known-good playlist fallback when a source is temporarily unavailable
- Visible playlist refresh and offline-cache status beside the connected source
- Direct MPEG-TS and HLS playback through LibVLC
- Hardware decoding with automatic per-channel software fallback
- Smart per-channel caching plus responsive, balanced, and stable profiles
- Frozen-stream watchdog that verifies video/audio progress rather than trusting the nominal player state
- Zero-video decoder detection that falls back only the affected stream when audio starts without video frames
- Audio-track, subtitle-track, deinterlacing, A/V sync, volume, and persistent aspect-ratio controls
- Display framing for Auto, Fill, 4:3, 5:4, 3:2, 14:9, 16:10, 16:9, 18:9, 21:9, 2.35:1, 2.39:1, and 32:9
- Codec, resolution, frame-rate, bitrate, dropped-frame, cache, decoder, and recovery telemetry
- Optional Windows display cadence matching with automatic restoration
- HDMI Dolby/DTS and source-provided Atmos passthrough when the Windows audio path and receiver support it
- Search, content-type filtering, category filtering, and a virtualized channel library
- Visible `group-title` section headers when All groups is selected
- Every playlist category in original source order, with per-group channel counts
- Local settings persistence
- Persistent favorites with privacy-safe channel identities
- Functional favorites navigation and quick favorite controls
- Automatic live-stream recovery with bounded retry attempts
- One-click manual reconnect and live buffer/reconnect telemetry
- One-click stream stabilization that switches to the 8-second cache and retunes
- Buffer overlays automatically dismiss at completed buffering and Playing states
- Ctrl+Up/Down channel zapping and Ctrl+D favorite shortcut
- Prominent in-app UPDATE button with Stable/Preview channels, release checks, download progress, verified in-place installation, automatic restart, and failed-launch rollback
- Silent startup release check with an UPDATE READY title-bar indicator
- Modal-safe player layering so Settings, Update, and playlist dialogs always cover native video controls
- Installer-aware Velopack startup and GitHub Releases update channel

## Build

### Windows

```powershell
dotnet restore StreamVue.Native.slnx --configfile NuGet.Config
dotnet build StreamVue.Native.slnx -c Release --no-restore
```

The application targets Windows x64 and the current .NET 10 LTS release.

The personal build uses Velopack and GitHub Releases for in-app updates. The Microsoft Store lane uses an unsigned MSIX that Partner Center signs and updates; run `tools/build-windows-msix.ps1` with the exact identity and durable add-on values from Partner Center. See [distribution and signing choices](docs/distribution-and-signing.md) and [premium access readiness](docs/premium-entitlements.md).

### Android and Google TV

```powershell
.\platforms\android\gradlew.bat -p platforms\android testDebugUnitTest lintDebug assembleDebug bundleRelease
```

This produces a personal-test APK and an unsigned, locked Google Play foundation AAB. No paid certificate is required for development. The separate manual **Build Google Play candidate** workflow uses a protected self-generated upload key and cannot run until the real Play product, verifier, and readiness manifest agree; see the [Android upload-signing instructions](platforms/android/README.md).

### Samsung Tizen and LG webOS

```powershell
pnpm install
pnpm tv:check
pnpm tv:test
pnpm tv:build
```

This produces unsigned Tizen project contents and the webOS app directory. Samsung requires a local TV certificate profile for a sideloaded `.wgt`; LG Developer Mode accepts an IPK produced by the pinned official webOS CLI without a Samsung-style signing certificate. A separate manual **Build Samsung TV Store candidate** workflow creates an author/Partner-distributor-signed `.wgt` only after the Seller Office, DPI, real-TV checkout, premium-product, and author-continuity gates agree. **Build LG webOS Seller Lounge candidate** creates and independently audits a free, premium-locked IPK only after its Seller account, terms, UX, checklist, privacy, and real-TV gates agree. Both workflows stop at temporary manual-upload artifacts.

### iPhone, iPad, and Apple TV

On a Mac with Xcode 26.6 and XcodeGen 2.46.0:

```bash
cd platforms/apple
xcodegen generate --spec project.yml
swift test
xcodebuild -project StreamVueApple.xcodeproj -scheme StreamVue -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO build
xcodebuild -project StreamVueApple.xcodeproj -scheme StreamVueTV -destination 'generic/platform=tvOS Simulator' CODE_SIGNING_ALLOWED=NO build
```

CI compiles and analyzes unsigned KSPlayer-enabled personal simulator products, then regenerates and verifies separate AVKit-only Store simulator products with no KSPlayer package resolution. These simulator products are not IPA files and cannot be installed on physical devices. A separate manual candidate workflow can create AVKit-only signed iOS/tvOS IPAs only after the premium, owner-review, bundle, profile, and certificate gates all pass; it deliberately does not upload them.

### Privacy and support site

```powershell
pnpm site:check
pnpm site:dev
pnpm site:build
```

The static site in `site/` provides the StreamVue overview, plain-language privacy policy, and support guidance needed by every Store lane. It is intentionally marked as a draft. The manual **Build public site candidate** workflow stops at an artifact and remains locked until `store/public-site-readiness.json` contains verified owner approval, a monitored privacy contact, reviewed copy, canonical HTTPS URLs, and a verified live deployment. Every Store candidate also requires this shared site gate.

The visual source concepts and the implementation comparison are recorded in [the public-site fidelity ledger](docs/design/streamvue-support-site-fidelity.md).

## Verification tools

- `StreamVue.PlaylistProbe` validates large-list parsing and favorite-key uniqueness.
- `StreamVue.FeatureProbe` checks update preferences, manual routes, catch-up URL expansion, channel health, interrupted sessions, background wake timers, episode-aware series rules, duplicate prevention, recovery timing, retention, timeshift policy, storage guards, transport-stream capture and playback, protected Plex/Emby contracts, signed Plex account discovery and revocation, encrypted backup/restore, diagnostics, casting, and Multiview policies.
- `StreamVue.PlaybackProbe` checks native live playback and bounded reconnect behavior.

## Privacy

StreamVue connects directly to playlist, guide, Plex, and Emby servers and does not upload library contents or credentials. Miracast casting mirrors the rendered picture and compatible system audio rather than sending source addresses to the display. The last-known-good playlist cache, XMLTV cache, saved guide configuration, Xtream credentials, media-center tokens, and StreamVue backup settings payload are protected with Windows per-user encryption. On Apple devices, URL and media-center secrets live in Keychain and cached catalogs use complete file protection and are excluded from device backup. Diagnostic exports omit provider addresses, credentials, channel names, and guide titles.
