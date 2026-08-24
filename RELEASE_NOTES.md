# StreamVue 3.7.0 Multi-Source Foundation

StreamVue 3.7 lays the safe storage and migration foundation for combining multiple IPTV providers without disrupting the playlist already connected in 3.6.

## Preserve every source independently

- Automatically migrate the existing M3U file, M3U URL, or Xtream connection into the new ordered source catalog
- Keep a stable identity, friendly name, enabled state, startup-refresh preference, channel count, and health history for each source
- Track the originating source on merged channels so searching and future source controls remain unambiguous
- Normalize duplicate source entries while preserving the original source order and identifiers

## Stay usable when one provider is offline

- Store a separate Windows-user-encrypted last-known-good playlist for every configured source
- Continue reading the existing 3.6 encrypted cache during migration
- Remove or replace one source cache without disturbing offline copies belonging to other providers
- Keep playlist addresses, channel data, guide addresses, and provider tokens out of clear-text cache files

## Protect multiple Xtream accounts

- Retain multiple Xtream logins in one Windows-user-encrypted account vault instead of replacing the previous login
- Recognize equivalent server addresses with or without an HTTP or HTTPS prefix
- Migrate the protected 3.6 single-account format automatically after a successful read
- Serialize concurrent account updates safely so one login cannot overwrite another

## Prepare a unified library

- Merge enabled source snapshots in a deterministic order
- Preserve favorite-compatible channel identities and source provenance
- Suppress exact duplicate channel entries while leaving genuinely different feeds available
- Combine distinct XMLTV guide sources for the unified channel set

## Keep recovery dependable

- Include every per-source encrypted playlist cache in StreamVue backups
- Restore 3.7 cache collections as a complete snapshot and remove stale entries safely
- Continue restoring StreamVue 3.6 backup archives with the same Windows account
- Keep the automatic pre-restore rollback backup

## Verification

- Pass a zero-warning Release build across the complete solution
- Verify source migration, ordering, identity, provenance, merging, and duplicate suppression
- Verify independent encrypted caches, multi-account credential persistence, and legacy credential migration
- Create and restore a multi-source backup, then restore a simulated 3.6 backup into 3.7
- Pass the existing playback, DVR, timeshift, guide, fullscreen, Multiview, multi-monitor, Cast, diagnostics, and updater regression suite

This prerelease installs in place through StreamVue's UPDATE button. Existing settings, playlist connection, encrypted cache, Xtream login, favorites, guide data, mappings, reminders, Playback IQ profiles, schedules, series rules, recordings, backups, Cast behavior, and Multiview assignments are preserved.
