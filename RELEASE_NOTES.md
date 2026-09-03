# OrbitalVue 5.8.0 Music Libraries and OrbitalVue Identity

OrbitalVue 5.8 makes Plex and Emby music libraries playable on every platform, and completes the rename from StreamVue down to the Windows binary, data directory, and update identity.

## Fixed in alpha.4: Plex sign-in reported the wrong reason

Connecting a discovered Plex server could fail with *The source could not be read.
Verify the address or file and try again* -- a message about playlist URLs, shown while
Plex itself listed the app as connected.

The sign-in path reports expiry and approval problems as `InvalidOperationException`,
which was the one exception type `SafeErrorMessage` had no case for, so every one of them
fell through to that generic playlist wording. The real messages -- *The Plex server
selection expired. Sign in again.* among them -- are now shown.

Separately, Plex can return the same `clientIdentifier` more than once, for a server that
is both owned and shared or reachable on several networks. Connections were already
de-duplicated but servers were not, so selecting one could throw *Sequence contains more
than one matching element* and surface as that same generic message. Servers are now
de-duplicated too, and the lookups no longer fail on a duplicate.

The server-selection step is valid for ten minutes after Plex approves the app.

## Fixed in alpha.3: the header still said StreamVue

The window header renders its wordmark letter-spaced, as `S T R E A M V U E`. Because
the letters are separated, the string `StreamVue` never appears in the markup, so every
search during the rename passed straight over it and the old brand shipped in the header
of alpha.1 and alpha.2.

The wordmark now reads `O R B I T A L V U E`. The brand check strips whitespace before
matching, so no amount of letter-spacing can hide the old name again.

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
