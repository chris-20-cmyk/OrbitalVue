# StreamVue 5.5.0 Protected Plex & Emby Library Artwork

StreamVue 5.5 gives Plex and Emby libraries a polished visual catalog on Windows. Posters and thumbnails now appear in the virtualized channel list and Signal desk while provider credentials remain isolated inside the protected media-center service. Existing M3U, Xtream, Plex account discovery, watch-progress sync, DVR, casting, playback-resilience, and in-place updates remain available.

## Premium library presentation

- Shows Plex and Emby artwork in the channel library with initials retained as a clean fallback
- Shows the selected item's artwork in Signal desk without delaying tune or playback
- Adds `RESUME` and `WATCHED` status badges for protected media-center items
- Loads only visible, requested artwork instead of downloading an entire large library at once
- Keeps decoded-image retention to 160 items and reclaims older posters while browsing very large catalogs

## Credential and network protection

- Stores only canonical `streamvue-artwork://` locators in playlists and encrypted caches; no provider token or raw authenticated image URL reaches WPF
- Revalidates the selected provider, normalized server origin, public server identity, protected credential binding, item identifier, and optional Plex artwork version before download
- Uses Plex and Emby credential headers only; tokens are never materialized in artwork URLs
- Blocks redirects, cross-provider locators, non-image responses, and responses larger than 8 MB
- Limits artwork network and decode work to four concurrent requests and cancels it on source replacement, credential deletion, entitlement revocation, or shutdown
- Fails open so missing or unavailable artwork always falls back to initials and never interrupts browsing or playback

## Verification and release controls

- Protocol tests cover Plex image transcoding and Emby primary-image routes with header-only authentication
- Security tests prove locked entitlements and forged locators never reach the network
- Response tests reject non-image and oversized payloads
- Cache tests prove token-free artwork locators survive protected offline catalog storage
- A dedicated structural gate covers canonical locators, identity binding, bounded concurrency, cancellation, image validation, memory retention, privacy disclosure, and WPF presentation
- Personal and Microsoft Store build modes remain independently compiled and checked

## Updating from Windows 4.0 through 5.4

The personal Windows build remains on the existing Velopack update lane. StreamVue 4.0 through 5.4 can install the 5.5 preview in place; uninstalling first is not required. Microsoft Store builds remain Store-managed and do not contact the GitHub updater.

This is a prerelease intended for personal testing. Store submission remains locked until the real product, privacy, listing, accessibility, public-site, and Partner Center owner-review gates are complete.
