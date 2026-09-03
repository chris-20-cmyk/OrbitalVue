using OrbitalVue.Player.Models;

namespace OrbitalVue.Player.Playback;

public static class PlaybackHealthPolicy
{
    public const int MaximumSmartCacheMilliseconds = 8_000;

    public static int SelectCacheMilliseconds(
        BufferPreset preset,
        string url,
        int instabilityScore = 0,
        bool fastTune = false,
        int successfulStarts = 0)
    {
        if (preset != BufferPreset.Smart)
        {
            return preset switch
            {
                BufferPreset.Responsive => 1_200,
                BufferPreset.Stable => 8_000,
                _ => 4_000
            };
        }

        var baseCache = Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeFile || uri.IsFile)
            ? fastTune ? 800 : 1_200
            : url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
                ? fastTune ? 1_800 : 2_400
                : fastTune ? 1_600 : 2_800;

        if (fastTune && successfulStarts >= 3)
            baseCache = Math.Max(900, baseCache - 300);

        return Math.Clamp(baseCache + Math.Clamp(instabilityScore, 0, 4) * 1_300, baseCache, MaximumSmartCacheMilliseconds);
    }

    public static bool HasOpeningTimedOut(
        DateTimeOffset now,
        DateTimeOffset playbackStarted,
        bool hasReachedPlaying,
        bool isPlaying,
        bool isBuffering,
        TimeSpan threshold)
    {
        if (hasReachedPlaying || isPlaying || isBuffering) return false;
        return now - playbackStarted >= threshold;
    }

    public static bool IsStalled(
        DateTimeOffset now,
        DateTimeOffset lastProgress,
        DateTimeOffset playbackStarted,
        bool isPlaying,
        bool isBuffering,
        TimeSpan? threshold = null)
    {
        if (!isPlaying || isBuffering) return false;
        var limit = threshold ?? TimeSpan.FromSeconds(12);
        return now - playbackStarted >= limit && now - lastProgress >= limit;
    }

    public static bool HasVideoStartupFailed(
        DateTimeOffset now,
        DateTimeOffset playbackStarted,
        bool hasVideoTrack,
        int displayedPictures,
        bool isPlaying,
        bool isBuffering,
        TimeSpan? threshold = null)
    {
        if (!hasVideoTrack || displayedPictures > 0 || !isPlaying || isBuffering) return false;
        return now - playbackStarted >= (threshold ?? TimeSpan.FromSeconds(12));
    }
}
