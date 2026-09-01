# OrbitalVue 5.6.0 Rebrand & Premium Library Browsing

StreamVue is becoming **OrbitalVue**. Version 5.6 introduces the new public identity across Windows, Android and Google TV, iPhone, iPad, Apple TV, Samsung Tizen, LG webOS, Store listings, support pages, and release packages. The existing Windows updater identity is deliberately retained behind the scenes so current personal installations can update in place without uninstalling.

## Premium Plex & Emby browsing

- Adds dedicated **Continue Watching** and **Recently Added** views for connected Plex and Emby libraries
- Keeps Live, Movies, and Series filters alongside provider library groups
- Shows year, series, season, episode, watch progress, and resume position where the provider supplies them
- Sorts Continue Watching by the latest activity and Recently Added by provider date
- Keeps artwork, credentials, and media-center tokens behind the protected provider service

## OrbitalVue identity

- Rebrands all customer-facing Windows screens, dialogs, diagnostics, package metadata, and recording defaults
- Rebrands Android, Google TV, Apple, Samsung, and LG presentation and future Store identities
- Uses `com.orbitalvue.player` for new Android and Apple Store identities
- Uses `OvTvPlayer.OrbitalVue` for Samsung and `com.orbitalvue.player.tv` for LG
- Updates the public website, privacy copy, support copy, Store listing contract, and distribution checks

## Safe Windows update continuity

- Keeps the existing Windows executable assembly name, Velopack package ID, application-data location, encrypted-data entropy, and legacy backup support
- Existing personal Windows installations remain on the same in-app update lane
- Users upgrading from the StreamVue line do not need to uninstall first
- Microsoft Store builds remain Store-managed and do not contact the GitHub updater

## Verification

- Extends the Windows feature probe with Plex and Emby metadata, watch-progress, recency, and filtering fixtures
- Keeps personal and Store-mode builds independently compiled and checked
- Adds release-contract enforcement for the new public platform identifiers while protecting legacy Windows continuity identifiers

This is a prerelease intended for personal testing. Store submission remains locked until the real product, privacy, listing, accessibility, public-site, commerce, licensing, and platform-owner review gates are complete.
