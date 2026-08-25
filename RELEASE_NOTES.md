# StreamVue 3.9.0 Smart Signal Routing

StreamVue 3.9 turns duplicate live streams from multiple IPTV providers into one intelligent channel that can select and recover through the best available feed automatically.

## One channel, every available feed

- Combine equivalent live feeds into one clean library entry while keeping every underlying stream available
- Match reliable duplicate feeds by TVG identity or channel name without collapsing East, West, or numbered channel variants
- Preserve existing favorites, recent channels, startup resume, Multiview layouts, DVR schedules, and per-channel playback settings as logical channel identities migrate
- Search alternate feed names and source names without filling the channel list with duplicates

## Learn the strongest signal path

- Score feeds locally from successful tunes, startup speed, buffering, reconnects, watchdog recovery, dropped frames, resolution, frame rate, and bitrate
- Prefer a chosen provider, return it to automatic selection, or mark an unreliable feed as Never use
- Keep all provider addresses, account details, and channel URLs out of the signal-history display and saved measurements
- Index very large libraries efficiently, including a verified 70,000-feed routing test

## Recover automatically

- Switch to the next eligible feed only after native reconnect, smart buffering, frozen-stream watchdog, and decoder recovery have been exhausted
- Avoid retry loops by remembering every attempted feed during the current tune and limiting automatic switches
- Keep the logical channel selected while the underlying provider changes
- Use the same best-feed policy for regular playback, scheduled recordings, and Multiview assignments

## Stay in control

- Open the new Signal routes workspace from the Signal desk to review every feed behind a channel
- See a private signal score, quality, reliability, buffer history, reconnects, failovers, and last measurement for each feed
- Switch feeds immediately, manage preferences, reset learned history, or disable automatic failover
- Merge guide listings across equivalent feeds so Now/Next and the full guide can use the best available XMLTV match

## Verification

- Pass a zero-warning Release build across the complete solution
- Pass the full playback, DVR, timeshift, guide, fullscreen, Multiview, multi-monitor, Cast, backup, diagnostics, source-manager, and updater regression suite
- Verify grouping safeguards for different TVG labels, numbered channels, and East/West schedule variants
- Verify feed scoring, preference, exclusion, terminal-only failover, guide merging, encrypted settings persistence, and 70,000-feed routing performance
- Visually verify the Signal routes workspace at desktop scale with correct modal layering and no player overlap

This prerelease installs in place through StreamVue's UPDATE button. Existing settings, source catalog, encrypted playlists, Xtream logins, favorites, guide data, mappings, reminders, Playback IQ profiles, schedules, series rules, recordings, backups, Cast behavior, and Multiview assignments are preserved.
