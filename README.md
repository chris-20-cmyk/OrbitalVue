# StreamVue Native

StreamVue is a Windows-native IPTV player built with .NET, WPF, LibVLCSharp, the VideoLAN playback engine, and Velopack packaging.

## Current preview

- Native in-app playback for saved DVR recordings with a compact seek bar and original-quality local-file rendering
- Automatic per-recording resume positions, a visible watched-progress indicator, and completed-playback cleanup
- Safe confirmed recording deletion constrained to the selected recordings folder
- DVR storage reporting with free space, total capacity, library size, and recording count
- Advance warnings and visible conflict labels when TV Guide recording schedules overlap
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
- Playlist health center with refresh history, channel additions/removals, fallback status, and manual refresh
- Programme reminders from the guide with tune, snooze, and dismiss actions
- Multiview 2.0 with drag/drop assignment, tile swapping, and named reusable layouts
- Large M3U/M3U8 file and URL indexing
- Xtream-compatible live channel login
- Automatic startup refresh for the last connected M3U file, M3U URL, or Xtream account
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
- Prominent in-app UPDATE button with release checks, download progress, verified in-place installation, and automatic restart
- Silent startup release check with an UPDATE READY title-bar indicator
- Modal-safe player layering so Settings, Update, and playlist dialogs always cover native video controls
- Installer-aware Velopack startup and GitHub Releases update channel

## Build

```powershell
dotnet restore StreamVue.Native.slnx --configfile NuGet.Config
dotnet build StreamVue.Native.slnx -c Release --no-restore
```

The application targets Windows x64 and the current .NET 10 LTS release.

## Verification tools

- `StreamVue.PlaylistProbe` validates large-list parsing and favorite-key uniqueness.
- `StreamVue.FeatureProbe` checks DVR transport-stream capture and native playback, resume persistence, storage reporting, safe deletion, schedule conflicts, privacy-safe favorite identity, settings persistence, encrypted backup/restore, diagnostic redaction, nearby-display casting entry points, and Multiview policies.
- `StreamVue.PlaybackProbe` checks native live playback and bounded reconnect behavior.

## Privacy

StreamVue connects directly to playlist and guide providers and does not upload playlist contents or credentials. Miracast casting mirrors the rendered picture and compatible system audio rather than sending playlist addresses to the display. The last-known-good playlist cache, XMLTV cache, saved guide configuration, Xtream credentials, and StreamVue backup settings payload are protected with Windows per-user encryption. Diagnostic exports omit provider addresses, credentials, channel names, and guide titles.
