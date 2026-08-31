# StreamVue 5.4.0 Windows Media-Center Progress Sync

StreamVue 5.4 keeps Plex and Emby resume position and watched state synchronized from the native Windows player. Existing M3U, Xtream, Plex account discovery, DVR, casting, playback-resilience, and in-place update features remain available.

## Plex and Emby playback lifecycle

- Sends Plex timeline updates when playback starts, pauses, resumes, seeks, stops, changes source, fails terminally, or closes
- Sends Emby `Playing`, `Progress`, and `Stopped` session check-ins with the server-issued play-session and media-source identifiers
- Refreshes active progress every ten seconds without blocking the player UI
- Reports the native player position, duration, mute state, volume, direct-play/direct-stream/transcode method, and seek capability where the provider accepts them
- Lets each selected media center update its own resume and watched state for the signed-in account

## Credential and session protection

- Exposes only a random 32-character reporting handle to WPF; tokens remain inside the DPAPI-backed media-center service
- Binds every report to the verified provider, server identity, normalized origin, credential, item, and playback session
- Serializes concurrent events and rejects stale event ordering
- Limits in-memory reporting sessions to eight and expires abandoned sessions after eighteen hours
- Cancels pending network work when a resolution is abandoned, premium access is revoked, or StreamVue closes
- Fails open: an unavailable Plex or Emby server never interrupts local playback

## Verification and release controls

- Protocol tests verify Plex playing/paused/stopped ordering and authenticated session headers
- Protocol tests verify Emby start/progress/pause/unpause/stopped endpoints, event reasons, and tick conversion
- Security tests prove forged handles never reach the network, duplicate stops are suppressed, and report bodies contain no password or provider token
- A dedicated structural gate covers play, pause, seek, periodic progress, stop, shutdown, cancellation, entitlement revocation, and fail-open behavior
- Personal and Microsoft Store build modes remain independently compiled and checked

## Updating from Windows 4.0 or 5.3

The personal Windows build remains on the existing Velopack update lane. StreamVue 4.0 and 5.3 can install the 5.4 preview in place; uninstalling first is not required. Microsoft Store builds remain Store-managed and do not contact the GitHub updater.

This is a prerelease intended for personal testing. Store submission remains locked until the real product, privacy, listing, accessibility, public-site, and Partner Center owner-review gates are complete.
