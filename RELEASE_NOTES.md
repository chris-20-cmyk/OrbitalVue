# StreamVue 3.6.0 Background DVR

StreamVue 3.6 turns Smart DVR into a resilient Windows background recorder, adds live-TV timeshift, and makes recurring rules episode-aware.

## Keep recording in the background

- Closing the main window now leaves the DVR engine, schedule monitor, and wake timer running in the Windows notification area
- Double-click the tray icon to reopen StreamVue, open the DVR center directly, or stop and save the active recording
- Reopening StreamVue activates the existing background process instead of starting a second competing player
- Exiting from the tray warns when future schedules would be abandoned
- A supported sleeping PC can wake two minutes before the next scheduled program
- Active recording prevents automatic sleep, while Windows shutdown safely finalizes the current transport-stream file

## Recover interrupted streams

- Retry a dropped scheduled recording after 2, 5, and 10 second backoff intervals
- Configure one to five recovery attempts in the DVR center
- Create a fresh transport-stream segment for each retry so an earlier playable segment is never overwritten
- Resume an unfinished schedule after StreamVue or Windows restarts while the program is still airing
- Label recovered and partial files clearly in the recording library
- Preserve playable segments when the provider never returns before the program ends

## Pause live television

- Keep a private disk-backed live buffer on this PC with a 15, 30, 60, or 120 minute window
- Pause and resume live channels without affecting the independent DVR recorder
- Rewind by 10 or 60 seconds when the provider stream exposes seeking through LibVLC
- Return to the live edge with one click and automatically recover when the configured window is exhausted
- Remove stale temporary timeshift files without touching saved DVR recordings

## Record smarter series

- Choose all airings or new episodes only
- Match the original channel or any channel carrying the same program
- Read XMLTV season, episode, new-airing, and repeat metadata
- Prevent the same identified episode from being scheduled twice across alternate feeds
- Keep every recording or automatically retain only the newest 1, 3, 5, or 10 episodes
- Rebuild future schedules immediately when a series option changes

## See the week at a glance

- Filter the schedule by All or any of the next seven days
- See the next recording, background-engine state, estimated recording hours, current ingest rate, and time remaining
- Distinguish scheduled, recording, recovering, recovered, partial, expired, missed, failed, conflict, and canceled states

## Verification

- Pass a zero-warning Release build and the complete feature regression suite
- Exercise the real Windows waitable timer, power guard, settings migration, XMLTV episode parser, duplicate prevention, retention, capacity estimate, and staged recovery policy
- Record an original transport stream, reopen it in the native player, seek it, and verify the saved output is playable
- Visually verify the expanded DVR center and live timeshift controls at desktop scale

This build installs in place through StreamVue's UPDATE button. Existing settings, playlist cache, favorites, guide data, mappings, reminders, Playback IQ profiles, schedules, series rules, recordings, playback progress, backups, Cast behavior, and Multiview assignments are preserved.
