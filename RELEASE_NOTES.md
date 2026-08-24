# StreamVue 3.8.0 Source Manager

StreamVue 3.8 turns the multi-source foundation into a complete source workspace and refreshes every enabled IPTV provider into one resilient library when the app opens.

## Manage every provider in one place

- Open the new My sources workspace to see every saved M3U file, M3U URL, and protected Xtream account
- Enable or pause a source without deleting its saved details or encrypted offline copy
- Reorder sources to control which provider wins when the same exact channel appears more than once
- Load one source immediately, refresh all enabled sources together, or safely remove a source and its private cached data
- See a privacy-safe location, connection state, channel count, and last successful refresh for every source

## Build one library automatically

- Refresh every enabled provider whenever StreamVue opens instead of checking only the most recently connected playlist
- Merge all available channels in the chosen source order and suppress exact duplicates automatically
- Preserve source identity on every merged channel so favorites and guide matching remain stable
- Keep manually connected sources in the manager automatically for future launches

## Stay online when providers struggle

- Refresh and recover each source independently so one unavailable provider cannot take the rest of the library offline
- Fall back to the affected source's Windows-user-encrypted last-known-good playlist while healthy providers remain live
- Allow startup network refresh to be disabled per source and open that source's encrypted copy instead
- Report which sources are live, using protected offline data, or need attention without exposing provider tokens

## Keep account data private

- Hide playlist query strings, tokens, and Xtream account details from the source-manager display
- Retain protected Xtream credentials only while another saved source still uses the same account
- Delete only the selected source's encrypted cache without disturbing other providers
- Preserve the complete 3.7 source catalog, caches, account vault, settings, and channel order during the in-place update

## Verification

- Pass a zero-warning Release build across the complete solution
- Verify live refresh, intentional cache-only startup, provider-failure fallback, and no-cache failure in one coordinated run
- Verify deterministic merging, source provenance, exact duplicate suppression, independent encrypted caches, and protected multi-account credentials
- Visually verify the source manager at desktop scale with no player or modal overlap
- Pass the existing playback, DVR, timeshift, guide, fullscreen, Multiview, multi-monitor, Cast, backup, diagnostics, and updater regression suite

This prerelease installs in place through StreamVue's UPDATE button. Existing settings, source catalog, encrypted playlists, Xtream logins, favorites, guide data, mappings, reminders, Playback IQ profiles, schedules, series rules, recordings, backups, Cast behavior, and Multiview assignments are preserved.
