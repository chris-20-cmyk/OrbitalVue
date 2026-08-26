# StreamVue Native

StreamVue is a native IPTV player. The Windows 4.0 preview uses .NET, WPF, LibVLCSharp, the VideoLAN playback engine, and Velopack packaging. The Android 5.0 line uses Kotlin, Compose, and AndroidX Media3 for phones, tablets, Android TV, and Google TV.

## Android 5.0 foundation

- M3U/M3U8 file and URL import with a private last-working-copy cache and automatic URL refresh at launch
- Exact playlist grouping, categorized All Channels browsing, fast search, and remote-friendly 10-foot navigation
- Native Media3 HLS, progressive MPEG-TS/MP4, and RTSP playback with per-channel request headers
- Hardware-backed MediaCodec decoding, decoder fallback, seamless frame-rate hints, six aspect modes, and immersive full screen
- A versioned portable catalog contract and synthetic conformance fixtures for future Samsung Tizen, LG webOS, and Apple clients
- Repeatable cloud builds that produce a personal-test APK and an unsigned Google Play AAB without requiring a paid certificate

The Android Studio project lives in `platforms/android`; the shared contract lives in `contracts`. StreamVue does not ship or discover channels. Users connect sources they are authorized to use.

See [Android build instructions](platforms/android/README.md), the [cross-platform architecture](docs/platform-architecture.md), and the [distribution and signing choices](docs/distribution-and-signing.md).

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
- Automatic startup refresh for every enabled M3U file, M3U URL, and Xtream account
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

### Android and Google TV

```powershell
.\platforms\android\gradlew.bat -p platforms\android testDebugUnitTest lintDebug assembleDebug bundleRelease
```

This produces a personal-test APK and a Google Play AAB. No paid certificate is required for the development build.

## Verification tools

- `StreamVue.PlaylistProbe` validates large-list parsing and favorite-key uniqueness.
- `StreamVue.FeatureProbe` checks update preferences, manual routes, catch-up URL expansion, channel health, interrupted sessions, background wake timers, episode-aware series rules, duplicate prevention, recovery timing, retention, timeshift policy, storage guards, transport-stream capture and playback, encrypted backup/restore, diagnostics, casting, and Multiview policies.
- `StreamVue.PlaybackProbe` checks native live playback and bounded reconnect behavior.

## Privacy

StreamVue connects directly to playlist and guide providers and does not upload playlist contents or credentials. Miracast casting mirrors the rendered picture and compatible system audio rather than sending playlist addresses to the display. The last-known-good playlist cache, XMLTV cache, saved guide configuration, Xtream credentials, and StreamVue backup settings payload are protected with Windows per-user encryption. Diagnostic exports omit provider addresses, credentials, channel names, and guide titles.
