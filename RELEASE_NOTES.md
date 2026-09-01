# OrbitalVue 5.7.0 Personal Media Progress Sync

OrbitalVue 5.7 completes provider-synced Plex and Emby playback progress across Windows, Android and Google TV, iPhone, iPad, Apple TV, Samsung Tizen, and LG webOS. It also brings the dedicated Continue Watching and Recently Added library lanes to every supported platform foundation.

## Cross-platform Plex and Emby browsing

- Adds dedicated **Continue Watching** and **Recently Added** groups across Apple, Android/Google TV, Samsung, and LG alongside the existing Windows experience
- Preserves provider-supplied resume position, watched state, recency, episode metadata, and artwork-safe internal locators
- Starts Apple playback at the saved Plex or Emby position with either AVKit or KSPlayer
- Keeps Live, Movies, Series, and provider library groups available beside the editorial lanes

## Provider-synced playback progress

- Sends Plex timeline and Emby session lifecycle reports from every native player foundation
- Reports start, pause, resume, buffering, ten-second progress, completion, stop, and source changes where the platform exposes those events
- Uses real AVKit, KSPlayer, Media3, HTML5, and Samsung AVPlay position and duration data rather than estimated time
- Preserves a reporting session during safe decoder retries so fallback does not falsely mark a title stopped
- Clamps invalid position, duration, volume, and tick values before they reach a provider

## Credential and playback isolation

- Keeps provider tokens in platform-secure storage and sends them only in protected headers
- Keeps cached catalogs, UI state, and public playback locators free of Plex and Emby credentials
- Binds progress reports to short-lived server, item, media-source, and playback-session records held behind each platform repository
- Serializes report delivery and treats provider check-in failures as non-fatal so local playback continues
- Retains the Store-mode premium boundary: locked builds make no Plex or Emby credential, refresh, artwork, playback, or reporting requests

## Verification

- Adds Plex playing/stopped timeline tests with session headers and token-free URLs
- Adds Emby start/progress/pause/stopped payload tests, including tick conversion and value clamping
- Exercises Android unit tests, lint, APK/AAB packaging, Google Play fail-closed mode, Apple Swift tests, iPhone/iPad and Apple TV builds, television tests, and the cross-platform Store contract
- Preserves the existing Windows updater identity so current personal installations can update in place without uninstalling

This is a prerelease intended for personal testing. Store submission remains locked until the real product, privacy, listing, accessibility, public-site, commerce, licensing, and platform-owner review gates are complete.
