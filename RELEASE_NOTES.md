# StreamVue 3.3.0 Live DVR

StreamVue 3.3 adds original-quality live recording and TV Guide scheduling while preserving the native playback, casting, and multi-monitor improvements from 3.2.

## Record live TV

- Start or stop a recording from the Watch controls, the new DVR center, or Ctrl+Shift+R
- Capture the provider's live transport stream directly without retuning or interrupting playback
- Keep visible elapsed-time, file-size, channel, program, and save-status feedback
- Save recordings as broadly compatible `.ts` files without quality-reducing video transcoding
- Record the active Multiview tile while leaving the rest of the signal grid undisturbed

## Schedule from the TV Guide

- Right-click a current or upcoming guide program to schedule or cancel its recording
- Start and stop scheduled recordings automatically while StreamVue is open
- Preserve upcoming schedules across restarts and clearly label completed, missed, or failed events
- Enforce one active recording at a time to avoid uncontrolled provider connections

## Recordings center

- Choose a recordings folder or open it directly in Windows Explorer
- Review recent recordings with date, time, and file size
- Reveal any saved recording in its folder with one click
- Warn when a provider may count recording as an additional simultaneous stream

## Safe lifecycle and verification

- Prevent an in-app update restart until the active recording has been stopped and finalized
- Confirm before closing StreamVue during a recording
- Remove empty failed outputs instead of adding unusable files to the recordings library
- Verify direct MPEG-TS remuxing with a real local transport-stream fixture
- Pass the complete existing playlist, guide, updater, fullscreen, Multiview, multi-monitor, backup, casting, and Playback IQ feature suite

This build installs in place through StreamVue's UPDATE button. Existing settings, playlists, favorites, guide data, reminders, channel profiles, backups, Cast behavior, and Multiview assignments are preserved.
