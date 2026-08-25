using System.Diagnostics;
using StreamVue.Player.Models;
using StreamVue.Player.Services;

if (args is ["--routing-scale", var countText] && int.TryParse(countText, out var requestedCount))
{
    var channelCount = Math.Clamp(requestedCount, 2, 250_000);
    var scaleChannels = Enumerable.Range(0, channelCount)
        .Select(index => new ChannelItem
        {
            Number = index + 1,
            Name = $"Scale Network {index / 2:D5} {(index % 2 == 0 ? "HD" : "FHD")}",
            Group = "Routing scale",
            Url = $"https://provider-{index % 2}.invalid/live/{index / 2}.ts",
            TvgId = $"scale.network.{index / 2}",
            Kind = ChannelKind.Live,
            SourceName = index % 2 == 0 ? "Primary" : "Backup"
        })
        .ToList();
    var scaleStopwatch = Stopwatch.StartNew();
    var scaleRoutes = SmartSignalRoutingPolicy.BuildRoutes(scaleChannels);
    scaleStopwatch.Stop();
    var expectedRoutes = (channelCount + 1) / 2;
    Console.WriteLine($"Signal routing scale: {channelCount:N0} feeds -> {scaleRoutes.Count:N0} logical channels in {scaleStopwatch.Elapsed.TotalSeconds:0.00}s");
    return scaleRoutes.Count == expectedRoutes && scaleRoutes.All(route => route.FeedCount is 1 or 2) ? 0 : 1;
}

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: StreamVue.PlaylistProbe <playlist.m3u>");
    return 2;
}

var stopwatch = Stopwatch.StartNew();
var parser = new M3uPlaylistParser();
var result = await parser.ParseFileAsync(args[0]);
stopwatch.Stop();

var blankNames = result.Channels.Count(channel => string.IsNullOrWhiteSpace(channel.Name));
var blankUrls = result.Channels.Count(channel => string.IsNullOrWhiteSpace(channel.Url));
var groups = result.Channels.Select(channel => channel.Group).Distinct(StringComparer.OrdinalIgnoreCase).Count();
var live = result.Channels.Count(channel => channel.Kind == ChannelKind.Live);
var movies = result.Channels.Count(channel => channel.Kind == ChannelKind.Movie);
var series = result.Channels.Count(channel => channel.Kind == ChannelKind.Series);
var uniqueFavoriteKeys = result.Channels.Select(channel => channel.StableKey).Distinct(StringComparer.Ordinal).Count();
var duplicateFavoriteKeys = result.Channels.Count - uniqueFavoriteKeys;
var routeStopwatch = Stopwatch.StartNew();
var signalRoutes = SmartSignalRoutingPolicy.BuildRoutes(result.Channels);
routeStopwatch.Stop();
var redundantRoutes = signalRoutes.Count(route => route.HasAlternates);
var backupFeeds = signalRoutes.Sum(route => Math.Max(0, route.FeedCount - 1));
var largestRoute = signalRoutes.Count == 0 ? 0 : signalRoutes.Max(route => route.FeedCount);
var cachePath = Path.Combine(Path.GetTempPath(), $"streamvue-playlist-probe-{Guid.NewGuid():N}.bin");
var cacheStopwatch = Stopwatch.StartNew();
var cacheStore = new PlaylistCacheStore(cachePath);
await cacheStore.SaveAsync("file", args[0], result);
var cached = await cacheStore.TryLoadAsync("file", args[0]);
cacheStopwatch.Stop();
var cacheRoundTripPassed = cached?.Playlist.Channels.Count == result.Channels.Count;
var cacheSize = File.Exists(cachePath) ? new FileInfo(cachePath).Length : 0;
if (File.Exists(cachePath)) File.Delete(cachePath);

Console.WriteLine($"Source: {result.DisplayName}");
Console.WriteLine($"Channels: {result.Channels.Count:N0}");
Console.WriteLine($"Groups: {groups:N0}");
Console.WriteLine($"Kinds: {live:N0} live / {movies:N0} movies / {series:N0} series");
Console.WriteLine($"Integrity: {blankNames} blank names / {blankUrls} blank URLs");
Console.WriteLine($"Favorite identities: {uniqueFavoriteKeys:N0} unique / {duplicateFavoriteKeys:N0} duplicate entries");
Console.WriteLine($"Signal routes: {signalRoutes.Count:N0} logical channels / {redundantRoutes:N0} with backups / {backupFeeds:N0} backup feeds / largest route {largestRoute:N0}");
Console.WriteLine($"Signal routing index: {routeStopwatch.Elapsed.TotalSeconds:0.00}s");
Console.WriteLine($"Encrypted cache: {(cacheRoundTripPassed ? "PASS" : "FAIL")} / {cacheSize / 1_048_576d:0.00} MiB / {cacheStopwatch.Elapsed.TotalSeconds:0.00}s");
Console.WriteLine($"Elapsed: {stopwatch.Elapsed.TotalSeconds:0.00}s");

return result.Channels.Count > 0 && blankNames == 0 && blankUrls == 0 && cacheRoundTripPassed ? 0 : 1;
