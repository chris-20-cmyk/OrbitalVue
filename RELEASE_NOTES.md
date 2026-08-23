# StreamVue 3.5.0 Smart DVR

StreamVue 3.5 upgrades the in-app DVR into a guide-aware recording system with recurring rules, precise padding, deterministic conflict handling, and low-storage protection.

## Record a full series

- Right-click any live TV Guide program to start or stop a recurring series rule
- Match future airings by channel and normalized program title whenever guide data refreshes
- Review every active rule, its priority, padding, and upcoming-airing count in the DVR center
- Remove a rule without interrupting an airing that is already being recorded
- Skip or restore a single series airing without deleting the recurring rule

## Capture the complete program

- Start each new recording 0–30 minutes early and end it 0–30 minutes late
- Keep original guide airtimes separate from padded capture windows for accurate schedule identity
- Choose Low, Normal, or High as the default priority for new schedules
- Promote an individual airing or an entire series directly from its DVR row

## Resolve conflicts predictably

- Prefer the highest-priority due recording when overlapping programs need the single DVR connection
- Use earliest scheduled start and creation order as stable tie-breakers
- Preempt a lower-priority scheduled recording when a higher-priority program begins
- Hand off consecutive programs at the guide boundary when only their padding overlaps
- Label future winners and at-risk recordings, then retain a clear reason for skipped conflicts
- Preserve an explicitly started manual recording over automatically scheduled work

## Protect drive space

- Reserve 0, 2, 5, 10, 20, or 50 GB on the selected recordings drive
- Check the reserve before manual and scheduled capture begins
- Recheck during long recordings and stop safely before the protected free-space threshold is crossed
- Show the active reserve alongside live capacity and library storage reporting

## Verification

- Verify persistent series rules, padded guide identity, priority selection, and reserve thresholds
- Pass native transport-stream recording/playback, seeking, resume, storage, safe-delete, guide, updater, fullscreen, Multiview, multi-monitor, backup, casting, playlist, and Playback IQ regression suites
- Visually verify Smart DVR controls, populated series rules, schedule priority metadata, and the scroll-safe window-sized modal

This build installs in place through StreamVue's UPDATE button. Existing settings, playlists, favorites, guide data, reminders, channel profiles, schedules, recordings, playback progress, backups, Cast behavior, and Multiview assignments are preserved.
