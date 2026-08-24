using System.IO;
using System.Net.Http;
using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public enum PlaylistSourceLoadMode
{
    Live,
    CachedFallback,
    CachedOnly,
    Failed
}

public sealed record PlaylistSourceRefreshProgress(
    Guid SourceId,
    string SourceName,
    int SourceNumber,
    int SourceCount,
    string Message);

public sealed record PlaylistSourceRefreshOutcome(
    PlaylistSourceDefinition Source,
    PlaylistSourceLoadMode Mode,
    PlaylistResult? Playlist,
    string? Error = null);

public sealed record PlaylistSourceRefreshSummary(
    PlaylistMergeSummary? Merge,
    IReadOnlyList<PlaylistSourceRefreshOutcome> Outcomes)
{
    public int LiveSourceCount => Outcomes.Count(outcome => outcome.Mode == PlaylistSourceLoadMode.Live);
    public int CachedSourceCount => Outcomes.Count(outcome => outcome.Mode is PlaylistSourceLoadMode.CachedFallback or PlaylistSourceLoadMode.CachedOnly);
    public int FallbackSourceCount => Outcomes.Count(outcome => outcome.Mode == PlaylistSourceLoadMode.CachedFallback);
    public int FailedSourceCount => Outcomes.Count(outcome => outcome.Mode == PlaylistSourceLoadMode.Failed);
    public bool HasPlaylist => Merge is { Playlist.Channels.Count: > 0 };
}

public sealed class PlaylistSourceRefreshService
{
    private readonly Func<PlaylistSourceDefinition, IProgress<PlaylistProgress>?, CancellationToken, Task<PlaylistResult>> _liveLoader;
    private readonly Func<string, string, CancellationToken, Task<CachedPlaylist?>> _cacheLoader;
    private readonly Func<string, string, PlaylistResult, CancellationToken, Task> _cacheWriter;

    public PlaylistSourceRefreshService(
        Func<PlaylistSourceDefinition, IProgress<PlaylistProgress>?, CancellationToken, Task<PlaylistResult>> liveLoader,
        Func<string, string, CancellationToken, Task<CachedPlaylist?>> cacheLoader,
        Func<string, string, PlaylistResult, CancellationToken, Task> cacheWriter)
    {
        _liveLoader = liveLoader;
        _cacheLoader = cacheLoader;
        _cacheWriter = cacheWriter;
    }

    public async Task<PlaylistSourceRefreshSummary> RefreshAsync(
        IEnumerable<PlaylistSourceDefinition> sources,
        Func<PlaylistSourceDefinition, bool>? shouldRefreshLive = null,
        IProgress<PlaylistSourceRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ordered = sources
            .Where(source => source.IsEnabled)
            .OrderBy(source => source.SortOrder)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var outcomes = new List<PlaylistSourceRefreshOutcome>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ordered[index];
            var sourceNumber = index + 1;
            source.LastAttemptUtc = DateTimeOffset.UtcNow;
            if (shouldRefreshLive?.Invoke(source) == false)
            {
                progress?.Report(new PlaylistSourceRefreshProgress(
                    source.Id,
                    source.Name,
                    sourceNumber,
                    ordered.Count,
                    $"Opening the encrypted copy for {source.Name}…"));
                var cachedOnly = await TryLoadCacheAsync(source, cancellationToken);
                if (cachedOnly is null)
                {
                    const string noCache = "Startup refresh is off and this source does not have an encrypted copy yet.";
                    source.LastError = noCache;
                    source.UsedCachedFallback = false;
                    outcomes.Add(new PlaylistSourceRefreshOutcome(source, PlaylistSourceLoadMode.Failed, null, noCache));
                    continue;
                }

                source.LastSuccessUtc ??= cachedOnly.Playlist.LoadedAt;
                source.LastError = null;
                source.ChannelCount = cachedOnly.Playlist.Channels.Count;
                source.UsedCachedFallback = false;
                outcomes.Add(new PlaylistSourceRefreshOutcome(source, PlaylistSourceLoadMode.CachedOnly, cachedOnly.Playlist));
                continue;
            }

            progress?.Report(new PlaylistSourceRefreshProgress(
                source.Id,
                source.Name,
                sourceNumber,
                ordered.Count,
                $"Refreshing {source.Name}…"));
            try
            {
                var sourceProgress = new Progress<PlaylistProgress>(value =>
                    progress?.Report(new PlaylistSourceRefreshProgress(
                        source.Id,
                        source.Name,
                        sourceNumber,
                        ordered.Count,
                        value.Message)));
                var playlist = await _liveLoader(source, sourceProgress, cancellationToken);
                source.LastSuccessUtc = DateTimeOffset.UtcNow;
                source.LastError = null;
                source.ChannelCount = playlist.Channels.Count;
                source.UsedCachedFallback = false;
                try
                {
                    await _cacheWriter(source.SourceType, source.SourceValue, playlist, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // A cache-write problem must not hide a fresh provider response.
                }
                outcomes.Add(new PlaylistSourceRefreshOutcome(source, PlaylistSourceLoadMode.Live, playlist));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var failure = SafeFailureMessage(exception);
                var cached = await TryLoadCacheAsync(source, cancellationToken);
                if (cached is null)
                {
                    source.LastError = failure;
                    source.UsedCachedFallback = false;
                    outcomes.Add(new PlaylistSourceRefreshOutcome(source, PlaylistSourceLoadMode.Failed, null, failure));
                    continue;
                }

                source.LastSuccessUtc ??= cached.Playlist.LoadedAt;
                source.LastError = failure;
                source.ChannelCount = cached.Playlist.Channels.Count;
                source.UsedCachedFallback = true;
                outcomes.Add(new PlaylistSourceRefreshOutcome(source, PlaylistSourceLoadMode.CachedFallback, cached.Playlist, failure));
            }
        }

        var snapshots = outcomes
            .Where(outcome => outcome.Playlist is not null)
            .Select(outcome => new PlaylistSourceSnapshot(
                outcome.Source,
                outcome.Playlist!,
                outcome.Mode == PlaylistSourceLoadMode.CachedFallback))
            .ToList();
        var merge = snapshots.Count == 0 ? null : PlaylistMergePolicy.Merge(snapshots);
        return new PlaylistSourceRefreshSummary(merge, outcomes);
    }

    private async Task<CachedPlaylist?> TryLoadCacheAsync(
        PlaylistSourceDefinition source,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _cacheLoader(source.SourceType, source.SourceValue, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string SafeFailureMessage(Exception exception) => exception switch
    {
        FileNotFoundException => "The playlist file could not be found.",
        UnauthorizedAccessException => "Windows denied access to the playlist file.",
        HttpRequestException http when http.StatusCode is not null => $"The provider returned HTTP {(int)http.StatusCode.Value}.",
        HttpRequestException => "The provider could not be reached.",
        ArgumentException => "The saved source address or account details need attention.",
        InvalidDataException invalidData => invalidData.Message,
        _ => "The source could not be refreshed."
    };
}
