using OrbitalVue.Player.Models;

namespace OrbitalVue.Player.Services;

public enum MediaLibraryBrowseMode
{
    All,
    ContinueWatching,
    RecentlyAdded,
    Live,
    Movies,
    Series
}

public sealed record MediaLibraryBrowseSummary(
    bool IsMediaCenterLibrary,
    int ContinueWatchingCount,
    int RecentlyAddedCount,
    int MovieCount,
    int SeriesCount);

public static class MediaLibraryBrowsePolicy
{
    public static readonly TimeSpan RecentlyAddedWindow = TimeSpan.FromDays(30);

    public static MediaLibraryBrowseSummary Summarize(
        IEnumerable<ChannelItem> items,
        DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var list = items as IReadOnlyCollection<ChannelItem> ?? items.ToList();
        return new MediaLibraryBrowseSummary(
            list.Any(item => item.IsProtectedMedia),
            list.Count(item => Matches(item, MediaLibraryBrowseMode.ContinueWatching, timestamp)),
            list.Count(item => Matches(item, MediaLibraryBrowseMode.RecentlyAdded, timestamp)),
            list.Count(item => item.Kind == ChannelKind.Movie),
            list.Count(item => item.Kind == ChannelKind.Series));
    }

    public static bool Matches(
        ChannelItem item,
        MediaLibraryBrowseMode mode,
        DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        return mode switch
        {
            MediaLibraryBrowseMode.All => true,
            MediaLibraryBrowseMode.ContinueWatching => item.IsProtectedMedia && item.CanResume,
            MediaLibraryBrowseMode.RecentlyAdded =>
                item.IsProtectedMedia &&
                item.AddedAtUtc is { } addedAt &&
                addedAt <= timestamp.AddDays(1) &&
                addedAt >= timestamp - RecentlyAddedWindow,
            MediaLibraryBrowseMode.Live => item.Kind == ChannelKind.Live,
            MediaLibraryBrowseMode.Movies => item.Kind == ChannelKind.Movie,
            MediaLibraryBrowseMode.Series => item.Kind == ChannelKind.Series,
            _ => false
        };
    }

    public static bool UsesEditorialOrder(MediaLibraryBrowseMode mode) =>
        mode is MediaLibraryBrowseMode.ContinueWatching or MediaLibraryBrowseMode.RecentlyAdded;
}
