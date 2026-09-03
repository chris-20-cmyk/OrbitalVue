# OrbitalVue 5.8.0 Music Libraries and OrbitalVue Identity

OrbitalVue 5.8 makes Plex and Emby music libraries playable on every platform, and completes the rename from StreamVue down to the Windows binary, data directory, and update identity.

## Fixed in alpha.2: crash when a playlist loaded

alpha.1 crashed as soon as a playlist produced channels, and kept crashing on every
launch afterwards because the cached playlist re-rendered the same rows.

The channel row binds a ProgressBar to `WatchProgressPercent`. WPF registers
`RangeBase.Value` with `BindsTwoWayByDefault`, so `{Binding WatchProgressPercent}`
became a TwoWay binding onto a getter-only property, and WPF threw
`XamlParseException` while applying the item template. Because templates are applied
during list virtualisation, the failure landed in the middle of rendering rather than
at startup.

The binding is now explicitly `Mode=OneWay`. This was not new in 5.8 -- the same line
shipped in 5.6 and 5.7. A new check, `tools/verify-xaml-binding-modes.mjs`, now fails
the build if any binding onto a two-way-by-default property omits an explicit `Mode`,
or points a `Mode=TwoWay` at a property with no setter.

## This build does not update in place

The Windows update identity changed from `Chris.StreamVue` to `Chris.OrbitalVue`, so an existing personal installation will **not** offer this as an update.

- Uninstall the previous build, then install `Chris.OrbitalVue-win-Setup.exe` from this release
- Application data moves from `%LocalAppData%\StreamVue` to `%LocalAppData%\OrbitalVue`, so playlists, guide sources, and Plex/Emby server logins must be entered again
- The previous folder is left untouched on disk and can be deleted once you are satisfied with the new build
- Diagnostics backups exported by an earlier build still restore: the reader falls back to the previous encryption entropy

## Plex and Emby music

- Music libraries now produce channels instead of being silently discarded on every platform
- Tracks appear badged **MUSIC** in All Channels and in their library group, alongside Live, Movies, Series, Recording, and Replay
- Plex library requests now ask for the matching item type, so a music library returns tracks rather than an unfiltered listing
- Continue Watching and Recently Added include music automatically, because those lanes filter on resume state and recency rather than on kind

Music previously reached the catalog builder as an `audio` item and was mapped to "drop this", once on each of the four platforms. Connecting a music library listed it correctly and then showed zero channels.

## Security fixes

- Session identifiers for Plex and Emby now come from a cryptographic source rather than `Math.random()`, with no silent fallback
- Removes polynomial backtracking from service-account key parsing: a 1,600-character input went from 534 ms to 0 ms, and a 20,000-character input no longer hangs
- Replaces a constructed regular expression in the Samsung packaging tool with literal string counting
- Extends CodeQL coverage to Android Kotlin sources, which no query had previously reached

## Verification

- Adds catalog coverage asserting every media-center item kind produces a channel, that audio yields `music`, and that channel numbering stays contiguous
- Adds an Android exhaustiveness test over `MediaCenterItemKind`, and extends the Apple and Windows suites for the music kind
- The diagnostics backup self-test now seals with the previous encryption entropy and restores with the current one, making it a real migration test rather than a same-value round trip
- Windows, Android and Google TV, iPhone/iPad/Apple TV, Samsung, LG, the portable catalog contract, and the cross-platform Store contract all pass

`ChannelKind` is persisted as a numeric value in the Windows playlist cache. `Music` is appended last and every member now carries an explicit value, so existing cached channels keep their meaning.

## Known gaps

- The Plex item-type behaviour is derived from the existing Windows client and has not been checked against a live Plex server
- `orbitalvue-artwork://` locators are generated on Apple, Android, and the TV platforms but only resolved on Windows

This is a prerelease intended for personal testing. Store submission remains locked until the real product, privacy, listing, accessibility, public-site, commerce, licensing, and platform-owner review gates are complete.
