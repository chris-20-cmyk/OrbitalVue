# StreamVue 4.0.0 Production Resilience

StreamVue 4.0 protects the viewing session from bad application updates, playlist-matching mistakes, missing channel metadata, and unexpected Windows interruptions while adding provider-backed catch-up TV.

## Safer in-place updates

- Choose Stable releases or opt into Preview feature builds from the Update screen
- Preserve one local last-known-good package before installing a new version
- Require the newly installed app to complete a responsive launch check
- Automatically reinstall the preserved version if that launch check never succeeds
- Keep playlists, guide data, recordings, accounts, and settings outside the application package throughout update and rollback

## Manual Signal Route editor

- Search every connected live feed from the existing Smart signal routing workspace
- Link a feed that automatic matching missed into the selected logical channel
- Mark an incorrectly grouped feed Keep separate and restore automatic matching later
- Reapply manual route decisions whenever one or more playlist sources refresh
- Continue using private per-feed scores, preferences, failover, and unified Guide data

## Catch-up and replay TV

- Parse M3U catch-up mode, source template, replay window, timeshift, and time-correction attributes
- Detect Xtream TV Archive availability and construct native timeshift playback requests
- Mark replay-ready past programmes directly in the six-hour Guide timeline
- Start a past programme with its advertised headers, referrer, artwork, and source identity
- Return to live television with the existing previous-channel control

## Channel Health Center

- Search and filter the full logical live-channel library without probing tens of thousands of provider URLs
- Find missing guide listings and open manual XMLTV mapping directly
- Find missing logos, unreliable observed playback, duplicate routes, and replay-ready channels
- Tune a result immediately or open its Signal Route for feed-level action
- Keep analysis local and exclude provider addresses and credentials from the display

## Interrupted-session recovery

- Distinguish a clean close from a crash, power loss, or forced restart with an atomic local session journal
- Restore the active logical channel and previous workspace after the playlist becomes available
- Restore Watch, Favorites, Guide, or Multiview mode plus searches, Guide filters, timeline position, layout assignments, and fullscreen presentation
- Keep normal startup behavior unchanged after a clean shutdown
- Use thread-safe single-instance coordination for the background DVR window

## Verification

- Zero-warning .NET 10 solution build
- Full feature-probe coverage for manual route linking/separation, M3U replay expansion, Channel Health analysis, session interruption/clean-close behavior, and update preferences
- Existing playback, DVR, timeshift, Guide, fullscreen, Multiview, Cast, backup, diagnostics, multi-source, and signal-routing regressions remain covered

This prerelease installs in place through StreamVue's UPDATE button. Existing settings, source catalog, encrypted playlists, Xtream logins, favorites, guide data, mappings, reminders, Playback IQ profiles, schedules, series rules, recordings, backups, Cast behavior, and Multiview assignments are preserved.
