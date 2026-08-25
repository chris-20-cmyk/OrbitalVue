using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public sealed partial class M3uPlaylistParser
{
    [GeneratedRegex("(?<key>[A-Za-z0-9_-]+)=(?:\"(?<quoted>[^\"]*)\"|'(?<single>[^']*)'|(?<plain>[^\\s,]+))", RegexOptions.Compiled)]
    private static partial Regex AttributePattern();

    public async Task<PlaylistResult> ParseFileAsync(
        string path,
        IProgress<PlaylistProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 128 * 1024,
            useAsync: true);

        return await ParseAsync(
            stream,
            Path.GetFileNameWithoutExtension(path),
            path,
            progress,
            cancellationToken);
    }

    public async Task<PlaylistResult> ParseAsync(
        Stream stream,
        string displayName,
        string source,
        IProgress<PlaylistProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var channels = new List<ChannelItem>(16_384);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, bufferSize: 128 * 1024, leaveOpen: true);

        PendingChannel? pending = null;
        string? guideSource = null;
        var lineNumber = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } rawLine)
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                guideSource ??= ParseGuideSource(line);
                continue;
            }

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                pending = ParseMetadata(line);
                continue;
            }

            if (pending is not null && line.StartsWith("#EXTVLCOPT:http-user-agent=", StringComparison.OrdinalIgnoreCase))
            {
                pending.UserAgent = line[(line.IndexOf('=') + 1)..].Trim();
                continue;
            }

            if (pending is not null &&
                (line.StartsWith("#EXTVLCOPT:http-referrer=", StringComparison.OrdinalIgnoreCase) ||
                 line.StartsWith("#EXTHTTP:", StringComparison.OrdinalIgnoreCase)))
            {
                pending.Referrer = ExtractReferrer(line);
                continue;
            }

            if (line.StartsWith('#')) continue;
            if (!LooksLikePlayableSource(line)) continue;

            pending ??= new PendingChannel { Name = $"Channel {channels.Count + 1}" };
            var group = string.IsNullOrWhiteSpace(pending.Group) ? "Uncategorized" : pending.Group.Trim();
            var name = string.IsNullOrWhiteSpace(pending.Name) ? $"Channel {channels.Count + 1}" : pending.Name.Trim();

            channels.Add(new ChannelItem
            {
                Number = channels.Count + 1,
                Name = name,
                Url = line,
                Group = group,
                LogoUrl = EmptyToNull(pending.LogoUrl),
                TvgId = EmptyToNull(pending.TvgId),
                TvgName = EmptyToNull(pending.TvgName),
                UserAgent = EmptyToNull(pending.UserAgent),
                Referrer = EmptyToNull(pending.Referrer),
                Kind = InferKind(group, line),
                CatchupMode = EmptyToNull(pending.CatchupMode),
                CatchupSource = EmptyToNull(pending.CatchupSource),
                CatchupDays = Math.Max(0, pending.CatchupDays),
                CatchupCorrectionMinutes = pending.CatchupCorrectionMinutes
            });

            pending = null;

            if (channels.Count % 2_000 == 0)
            {
                progress?.Report(new PlaylistProgress(channels.Count, $"Indexed {channels.Count:N0} channels"));
                await Task.Yield();
            }
        }

        if (channels.Count == 0)
        {
            throw new InvalidDataException($"No playable entries were found in {displayName}. The file was read through line {lineNumber:N0}.");
        }

        progress?.Report(new PlaylistProgress(channels.Count, $"Ready — {channels.Count:N0} channels"));
        return new PlaylistResult(channels, displayName, source, DateTimeOffset.Now, guideSource);
    }

    private static string? ParseGuideSource(string line)
    {
        foreach (Match match in AttributePattern().Matches(line))
        {
            var key = match.Groups["key"].Value;
            if (!key.Equals("url-tvg", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("x-tvg-url", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("tvg-url", StringComparison.OrdinalIgnoreCase)) continue;

            var value = match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value
                : match.Groups["single"].Success
                    ? match.Groups["single"].Value
                    : match.Groups["plain"].Value;
            var candidate = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file")
                return uri.ToString();
        }

        return null;
    }

    private static PendingChannel ParseMetadata(string line)
    {
        var separator = FindNameSeparator(line);
        var metadata = separator >= 0 ? line[..separator] : line;
        var name = separator >= 0 && separator + 1 < line.Length ? line[(separator + 1)..].Trim() : string.Empty;
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in AttributePattern().Matches(metadata))
        {
            var value = match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value
                : match.Groups["single"].Success
                    ? match.Groups["single"].Value
                    : match.Groups["plain"].Value;
            attributes[match.Groups["key"].Value] = value;
        }

        attributes.TryGetValue("tvg-name", out var tvgName);
        attributes.TryGetValue("tvg-id", out var tvgId);
        attributes.TryGetValue("tvg-logo", out var logo);
        attributes.TryGetValue("group-title", out var group);
        attributes.TryGetValue("http-user-agent", out var userAgent);
        attributes.TryGetValue("http-referrer", out var referrer);
        attributes.TryGetValue("catchup", out var catchupMode);
        attributes.TryGetValue("catchup-source", out var catchupSource);
        attributes.TryGetValue("catchup-days", out var catchupDaysText);
        if (string.IsNullOrWhiteSpace(catchupDaysText)) attributes.TryGetValue("timeshift", out catchupDaysText);
        attributes.TryGetValue("catchup-correction", out var correctionText);
        _ = int.TryParse(catchupDaysText, out var catchupDays);
        var correctionMinutes = 0;
        if (double.TryParse(correctionText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var correctionHours))
            correctionMinutes = (int)Math.Round(correctionHours * 60d);

        return new PendingChannel
        {
            Name = string.IsNullOrWhiteSpace(name) ? tvgName ?? string.Empty : name,
            TvgName = tvgName,
            TvgId = tvgId,
            LogoUrl = logo,
            Group = group,
            UserAgent = userAgent,
            Referrer = referrer,
            CatchupMode = catchupMode,
            CatchupSource = catchupSource,
            CatchupDays = Math.Max(0, catchupDays),
            CatchupCorrectionMinutes = correctionMinutes
        };
    }

    private static int FindNameSeparator(string line)
    {
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character is '\"' or '\'')
            {
                quote = quote == '\0' ? character : quote == character ? '\0' : quote;
            }
            else if (character == ',' && quote == '\0')
            {
                return i;
            }
        }

        return -1;
    }

    private static string? ExtractReferrer(string line)
    {
        var equalsIndex = line.IndexOf('=');
        if (equalsIndex >= 0) return line[(equalsIndex + 1)..].Trim();

        var referrerMatch = Regex.Match(line, "[\\\"]Referer[\\\"]\\s*:\\s*[\\\"](?<value>[^\\\"]+)", RegexOptions.IgnoreCase);
        return referrerMatch.Success ? referrerMatch.Groups["value"].Value : null;
    }

    private static bool LooksLikePlayableSource(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" or "rtsp" or "rtmp" or "udp" or "file";

    private static ChannelKind InferKind(string group, string url)
    {
        var value = $"{group} {url}".ToLowerInvariant();
        if (value.Contains("/series/") || value.Contains("series") || value.Contains("shows")) return ChannelKind.Series;
        if (value.Contains("/movie/") || value.Contains("movie") || value.Contains("vod") || value.Contains("cinema")) return ChannelKind.Movie;
        return ChannelKind.Live;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class PendingChannel
    {
        public string Name { get; set; } = string.Empty;
        public string? Group { get; set; }
        public string? LogoUrl { get; set; }
        public string? TvgId { get; set; }
        public string? TvgName { get; set; }
        public string? UserAgent { get; set; }
        public string? Referrer { get; set; }
        public string? CatchupMode { get; set; }
        public string? CatchupSource { get; set; }
        public int CatchupDays { get; set; }
        public int CatchupCorrectionMinutes { get; set; }
    }
}
