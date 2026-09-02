using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrbitalVue.Player.Models;
using OrbitalVue.Player.Services;

var metadataOnly = args.Length == 3 && args[2].Equals("--metadata-only", StringComparison.OrdinalIgnoreCase);
var sourceOverride = args.Length == 4 && args[2].Equals("--source", StringComparison.OrdinalIgnoreCase)
    ? args[3]
    : null;
var sourceOverrides = args.Length == 4 && args[2].Equals("--sources", StringComparison.OrdinalIgnoreCase)
    ? args[3].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    : [];
if (args.Length is < 2 or > 4 || !File.Exists(args[0]) ||
    args.Length == 3 && !metadataOnly || args.Length == 4 && string.IsNullOrWhiteSpace(sourceOverride) && sourceOverrides.Length == 0)
{
    Console.Error.WriteLine("Usage: OrbitalVue.GuideCoverageProbe <playlist.m3u> <output-directory> [--metadata-only | --source <xmltv-url> | --sources <url1|url2>]");
    return 2;
}

var playlistPath = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(outputDirectory);

var stopwatch = Stopwatch.StartNew();
var playlist = await new M3uPlaylistParser().ParseFileAsync(playlistPath);
var liveChannels = playlist.Channels.Where(channel => channel.Kind == ChannelKind.Live).ToList();
if (metadataOnly)
{
    await WriteMetadataAuditAsync(outputDirectory, playlistPath, liveChannels);
    stopwatch.Stop();
    Console.WriteLine($"Local metadata audit: {liveChannels.Count:N0} live channels");
    Console.WriteLine($"Reports: {outputDirectory}");
    Console.WriteLine($"Elapsed: {stopwatch.Elapsed.TotalSeconds:0.0}s");
    return 0;
}

var sources = sourceOverrides.Length > 0
    ? sourceOverrides
    : [sourceOverride ?? EpgSourceResolver.Resolve(playlist) ?? string.Empty];
if (sources.Any(string.IsNullOrWhiteSpace))
{
    Console.Error.WriteLine("No XMLTV source was advertised by or could be safely inferred from this playlist.");
    return 3;
}

Console.WriteLine($"Playlist ready: {liveChannels.Count:N0} live channels");
Console.WriteLine("Loading the provider guide (credentials are intentionally hidden)…");
var progress = new Progress<PlaylistProgress>(value =>
{
    if (value.ChannelsParsed > 0 && value.ChannelsParsed % 10_000 == 0)
        Console.WriteLine(value.Message);
});
var sourceService = new EpgSourceService();
var loadedSchedules = new List<EpgSchedule>();
foreach (var source in sources)
    loadedSchedules.Add(await sourceService.LoadAsync(source, liveChannels, progress));
var schedule = loadedSchedules.Count == 1
    ? loadedSchedules[0]
    : EpgSchedule.Merge(loadedSchedules, $"{loadedSchedules.Count} supplemental guides");
var now = DateTimeOffset.UtcNow;

var rows = liveChannels.Select(channel =>
{
    var programmes = schedule.GetProgrammes(channel);
    var nowNext = schedule.GetNowNext(channel, now);
    var status = programmes.Count == 0
        ? "Unmatched"
        : nowNext.Current is not null
            ? "On now"
            : nowNext.Next is not null
                ? "Upcoming only"
                : "Matched, no current window";
    return new CoverageRow(channel, programmes.Count, nowNext, status);
}).ToList();

var covered = rows.Where(row => row.ProgrammeCount > 0).ToList();
var onNow = rows.Where(row => row.NowNext.Current is not null).ToList();
var upcomingOnly = rows.Where(row => row.Status == "Upcoming only").ToList();
var dormant = rows.Where(row => row.Status == "Matched, no current window").ToList();
var unmatched = rows.Where(row => row.ProgrammeCount == 0).ToList();
var unmatchedEventFeeds = unmatched.Where(row => IsTemporaryEventFeed(row.Channel)).ToList();
var unmatchedStableChannels = unmatched.Where(row => !IsTemporaryEventFeed(row.Channel)).ToList();

await WriteChannelCsvAsync(Path.Combine(outputDirectory, "guide-covered.csv"), covered);
await WriteChannelCsvAsync(Path.Combine(outputDirectory, "guide-on-now.csv"), onNow);
await WriteChannelCsvAsync(Path.Combine(outputDirectory, "guide-upcoming-only.csv"), upcomingOnly);
await WriteChannelCsvAsync(Path.Combine(outputDirectory, "guide-matched-no-current-window.csv"), dormant);
await WriteChannelCsvAsync(Path.Combine(outputDirectory, "guide-unmatched.csv"), unmatched);
await WriteChannelCsvAsync(Path.Combine(outputDirectory, "guide-unmatched-event-feeds.csv"), unmatchedEventFeeds);
await WriteChannelCsvAsync(Path.Combine(outputDirectory, "guide-unmatched-stable-channels.csv"), unmatchedStableChannels);

var groups = rows
    .GroupBy(row => row.Channel.Group, StringComparer.OrdinalIgnoreCase)
    .Select(group => new GroupCoverage(
        group.Key,
        group.Count(),
        group.Count(row => row.ProgrammeCount > 0),
        group.Count(row => row.NowNext.Current is not null),
        group.Count(row => row.ProgrammeCount == 0)))
    .OrderByDescending(group => group.Unmatched)
    .ThenBy(group => group.Group, StringComparer.OrdinalIgnoreCase)
    .ToList();
await WriteGroupCsvAsync(Path.Combine(outputDirectory, "guide-coverage-by-group.csv"), groups);

var summary = new
{
    generatedAt = DateTimeOffset.Now,
    playlist = Path.GetFileName(playlistPath),
    guide = schedule.DisplayName,
    liveChannels = liveChannels.Count,
    matchedChannels = covered.Count,
    coveragePercent = liveChannels.Count == 0 ? 0 : Math.Round(covered.Count * 100d / liveChannels.Count, 2),
    channelsWithCurrentProgramme = onNow.Count,
    upcomingOnlyChannels = upcomingOnly.Count,
    matchedWithoutCurrentWindow = dormant.Count,
    unmatchedChannels = unmatched.Count,
    unmatchedTemporaryEventFeeds = unmatchedEventFeeds.Count,
    unmatchedStableChannels = unmatchedStableChannels.Count,
    guideProgrammes = schedule.ProgramCount,
    files = new[]
    {
        "guide-covered.csv",
        "guide-on-now.csv",
        "guide-upcoming-only.csv",
        "guide-matched-no-current-window.csv",
        "guide-unmatched.csv",
        "guide-unmatched-event-feeds.csv",
        "guide-unmatched-stable-channels.csv",
        "guide-coverage-by-group.csv"
    },
    privacy = "Stream URLs and provider credentials are excluded from every report."
};
await File.WriteAllTextAsync(
    Path.Combine(outputDirectory, "guide-coverage-summary.json"),
    JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }),
    new UTF8Encoding(false));

stopwatch.Stop();
Console.WriteLine($"Coverage: {covered.Count:N0}/{liveChannels.Count:N0} ({summary.coveragePercent:0.00}%)");
Console.WriteLine($"On now: {onNow.Count:N0} / Upcoming only: {upcomingOnly.Count:N0} / Unmatched: {unmatched.Count:N0}");
Console.WriteLine($"Reports: {outputDirectory}");
Console.WriteLine($"Elapsed: {stopwatch.Elapsed.TotalSeconds:0.0}s");
return 0;

static async Task WriteChannelCsvAsync(string path, IReadOnlyList<CoverageRow> rows)
{
    var builder = new StringBuilder("number,name,group,tvg_id,status,programme_count,current_programme,current_time,next_programme,next_time\r\n");
    foreach (var row in rows.OrderBy(row => row.Channel.Group, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Channel.Number))
    {
        var current = row.NowNext.Current;
        var next = row.NowNext.Next;
        builder.Append(Csv(row.Channel.Number.ToString(CultureInfo.InvariantCulture))).Append(',')
            .Append(Csv(row.Channel.Name)).Append(',')
            .Append(Csv(row.Channel.Group)).Append(',')
            .Append(Csv(row.Channel.TvgId)).Append(',')
            .Append(Csv(row.Status)).Append(',')
            .Append(Csv(row.ProgrammeCount.ToString(CultureInfo.InvariantCulture))).Append(',')
            .Append(Csv(current?.Title)).Append(',')
            .Append(Csv(current?.LocalTimeRange)).Append(',')
            .Append(Csv(next?.Title)).Append(',')
            .Append(Csv(next?.LocalTimeRange)).Append("\r\n");
    }
    await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false));
}

static async Task WriteGroupCsvAsync(string path, IReadOnlyList<GroupCoverage> groups)
{
    var builder = new StringBuilder("group,total,matched,on_now,unmatched,coverage_percent\r\n");
    foreach (var group in groups)
    {
        var percentage = group.Total == 0 ? 0 : group.Matched * 100d / group.Total;
        builder.Append(Csv(group.Group)).Append(',')
            .Append(group.Total).Append(',')
            .Append(group.Matched).Append(',')
            .Append(group.OnNow).Append(',')
            .Append(group.Unmatched).Append(',')
            .Append(percentage.ToString("0.00", CultureInfo.InvariantCulture)).Append("\r\n");
    }
    await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false));
}

static async Task WriteMetadataAuditAsync(string outputDirectory, string playlistPath, IReadOnlyList<ChannelItem> channels)
{
    var withId = channels.Where(channel => !string.IsNullOrWhiteSpace(channel.TvgId)).ToList();
    var missingId = channels.Where(channel => string.IsNullOrWhiteSpace(channel.TvgId)).ToList();
    var duplicateIds = withId
        .GroupBy(channel => channel.TvgId!.Trim(), StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Count() > 1)
        .SelectMany(group => group)
        .ToList();

    await WriteMetadataCsvAsync(Path.Combine(outputDirectory, "guide-id-present.csv"), withId);
    await WriteMetadataCsvAsync(Path.Combine(outputDirectory, "guide-id-missing.csv"), missingId);
    await WriteMetadataCsvAsync(Path.Combine(outputDirectory, "guide-id-duplicates.csv"), duplicateIds);

    var groups = channels
        .GroupBy(channel => channel.Group, StringComparer.OrdinalIgnoreCase)
        .Select(group => new
        {
            Group = group.Key,
            Total = group.Count(),
            WithId = group.Count(channel => !string.IsNullOrWhiteSpace(channel.TvgId)),
            MissingId = group.Count(channel => string.IsNullOrWhiteSpace(channel.TvgId))
        })
        .OrderByDescending(group => group.MissingId)
        .ThenBy(group => group.Group, StringComparer.OrdinalIgnoreCase)
        .ToList();
    var groupCsv = new StringBuilder("group,total,with_tvg_id,missing_tvg_id,metadata_percent\r\n");
    foreach (var group in groups)
    {
        var percentage = group.Total == 0 ? 0 : group.WithId * 100d / group.Total;
        groupCsv.Append(Csv(group.Group)).Append(',')
            .Append(group.Total).Append(',')
            .Append(group.WithId).Append(',')
            .Append(group.MissingId).Append(',')
            .Append(percentage.ToString("0.00", CultureInfo.InvariantCulture)).Append("\r\n");
    }
    await File.WriteAllTextAsync(Path.Combine(outputDirectory, "guide-id-coverage-by-group.csv"), groupCsv.ToString(), new UTF8Encoding(false));

    var summary = new
    {
        generatedAt = DateTimeOffset.Now,
        playlist = Path.GetFileName(playlistPath),
        liveChannels = channels.Count,
        channelsWithTvgId = withId.Count,
        channelsMissingTvgId = missingId.Count,
        metadataPercent = channels.Count == 0 ? 0 : Math.Round(withId.Count * 100d / channels.Count, 2),
        duplicateTvgIdEntries = duplicateIds.Count,
        privacy = "Local metadata-only audit. No network request was made, and stream URLs and credentials are excluded."
    };
    await File.WriteAllTextAsync(
        Path.Combine(outputDirectory, "guide-id-summary.json"),
        JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }),
        new UTF8Encoding(false));
}

static async Task WriteMetadataCsvAsync(string path, IReadOnlyList<ChannelItem> channels)
{
    var builder = new StringBuilder("number,name,group,tvg_id,tvg_name\r\n");
    foreach (var channel in channels.OrderBy(channel => channel.Group, StringComparer.OrdinalIgnoreCase).ThenBy(channel => channel.Number))
    {
        builder.Append(Csv(channel.Number.ToString(CultureInfo.InvariantCulture))).Append(',')
            .Append(Csv(channel.Name)).Append(',')
            .Append(Csv(channel.Group)).Append(',')
            .Append(Csv(channel.TvgId)).Append(',')
            .Append(Csv(channel.TvgName)).Append("\r\n");
    }
    await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false));
}

static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

static bool IsTemporaryEventFeed(ChannelItem channel) =>
    channel.Group is "NFL" or "MLB Baseball league" or "WNBA League Pass" ||
    Regex.IsMatch(channel.Name, @"\bvs\b|\s@\s|\bMILB\b", RegexOptions.IgnoreCase);

sealed record CoverageRow(ChannelItem Channel, int ProgrammeCount, EpgNowNext NowNext, string Status);
sealed record GroupCoverage(string Group, int Total, int Matched, int OnNow, int Unmatched);
