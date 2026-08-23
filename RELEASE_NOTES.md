# StreamVue 3.4.0 DVR Library

StreamVue 3.4 turns the Live DVR foundation into a complete in-app recordings library while preserving the native playback, casting, multi-monitor, guide, and Playback IQ improvements from earlier releases.

## Watch saved recordings

- Play saved `.ts` recordings directly inside StreamVue through the native LibVLC renderer
- Keep live-stream reconnect and stall recovery isolated from local recording playback
- Seek through a recording from the Watch controls with elapsed and total time feedback
- Resume each recording automatically from its last meaningful position
- Clear the saved resume point after playback reaches the end

## Manage the DVR library

- See recording date, time, file size, resume point, and watched progress in one compact row
- Review drive capacity, available space, total recording size, and recording count
- Reveal a recording in Windows File Explorer without leaving the library
- Delete recordings only after confirmation, with deletion constrained to `.ts` files inside the selected recordings folder
- Protect the active recording and the file currently being played from accidental deletion

## Catch schedule conflicts early

- Detect overlapping TV Guide recording schedules before they begin
- Warn before adding a new conflicting schedule and let the viewer cancel the change
- Label every affected schedule clearly in the DVR center
- Continue enforcing one simultaneous recorder to avoid unexpected provider connection use

## Verification

- Exercise a real recorded transport stream through native library playback and seeking
- Verify resume persistence, stable privacy-safe recording identity, storage reporting, guarded deletion, and overlap detection
- Pass the complete playlist, guide, updater, fullscreen, Multiview, multi-monitor, backup, casting, and Playback IQ feature suite
- Visually verify the window-sized DVR center with storage, conflicts, resume progress, and management controls populated

This build installs in place through StreamVue's UPDATE button. Existing settings, playlists, favorites, guide data, reminders, channel profiles, recordings, backups, Cast behavior, and Multiview assignments are preserved.
