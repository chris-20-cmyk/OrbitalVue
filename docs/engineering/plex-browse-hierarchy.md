# Media-library browse hierarchy

This follow-up keeps the stable media-center identity introduced in PR #15 while making the Windows catalog easier to browse.

## Behavior

- `All` continues to group protected Plex/Emby items by `Source • Library`.
- `Series` keeps the library grouping and adds a second level by `SeriesTitle`.
- Episodes sort by series, season, episode, then item name.
- Episode metadata now includes `SxxEyy` when season/episode numbers are available.
- `Music` is a dedicated browse filter instead of being hidden inside `All`.
- `Continue` and `Recent` keep their editorial ordering and are not re-grouped by series.

## Identity safety

`SeriesBrowseGroup` and `SeriesEpisodeLabel` are presentation-only (`JsonIgnore`). `StableKey` and `GuideMappingKey` continue to use the persisted raw group so the browse hierarchy does not invalidate favorites, playback progress, cached media, or guide mappings.
