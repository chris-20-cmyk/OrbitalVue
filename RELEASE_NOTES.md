# StreamVue 3.2.0 Wireless Cast

StreamVue 3.2 adds a native Cast experience for nearby Windows wireless displays without weakening playback compatibility or exposing IPTV provider details to the receiving screen.

## Nearby display discovery

- Open Cast directly from the StreamVue title bar or with Ctrl+Shift+C
- Discover powered-on Miracast TVs, projectors, adapters, and receiving Windows PCs
- Connect to compatible displays even when they have not previously been paired with this PC
- Hand off to the official Windows Cast panel for trusted device selection and connection prompts
- Open Windows display settings directly when projection configuration or troubleshooting is needed

## Playback-safe mirroring

- Mirror StreamVue's rendered picture instead of sending the IPTV source URL to the receiving device
- Keep LibVLC decoding, Playback IQ, hardware fallback, subtitles, aspect ratio, and on-screen controls active on the PC
- Avoid receiver-side failures caused by unsupported MPEG-TS, HLS variants, provider authentication, or private request details
- Continue using standard or Multiview playback while Windows manages the wireless display connection

## Professional Cast panel

- Explain device requirements and connection steps before leaving StreamVue
- Clarify that the target must be powered on with Screen Mirroring or Miracast enabled
- Recommend Windows Duplicate mode for a direct StreamVue mirror
- Document the separate protocol requirement for Chromecast-only and AirPlay-only receivers
- Note that wireless audio capability and maximum resolution depend on the receiving device

## Verification

- Release builds with zero warnings or errors
- Feature probes verify the Windows nearby-display entry point and display-settings fallback
- Visual checks cover the complete Cast panel at standard and maximized window sizes
- Existing playback, updater, fullscreen, Multiview, multi-monitor, backup, guide, playlist, and Playback IQ probes remain covered

This build installs in place through StreamVue's UPDATE button. Existing settings, favorites, playlists, guide data, reminders, channel profiles, backups, and Multiview assignments are preserved.
