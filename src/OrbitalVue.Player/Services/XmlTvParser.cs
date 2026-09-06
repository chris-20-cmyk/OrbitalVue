using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using OrbitalVue.Player.Models;

namespace OrbitalVue.Player.Services;

public sealed class XmlTvParser
{
    public Task<EpgSchedule> ParseAsync(
        Stream stream,
        string displayName,
        IReadOnlyList<ChannelItem> playlistChannels,
        IProgress<PlaylistProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        ParseAsync(stream, displayName, playlistChannels, null, progress, cancellationToken);

    public async Task<EpgSchedule> ParseAsync(
        Stream stream,
        string displayName,
        IReadOnlyList<ChannelItem> playlistChannels,
        IReadOnlyCollection<string>? additionalChannelIds,
        IProgress<PlaylistProgress>? progress,
        CancellationToken cancellationToken)
    {
        var targetKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var channel in playlistChannels.Where(channel => channel.Kind == ChannelKind.Live))
        {
            foreach (var key in EpgSchedule.CandidateKeys(channel.TvgId, channel.TvgName, channel.Name))
                targetKeys.Add(key);
        }

        var acceptedChannelIds = new HashSet<string>(StringComparer.Ordinal);
        var requestedChannelIds = new HashSet<string>(
            (additionalChannelIds ?? []).Select(EpgSchedule.NormalizeKey).Where(value => value.Length > 0),
            StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var channelCatalog = new Dictionary<string, string>(StringComparer.Ordinal);
        var programmes = new Dictionary<string, List<EpgProgram>>(StringComparer.Ordinal);
        var xmlTvChannels = 0;
        var parsedProgrammes = 0;
        var programmesInWindow = 0;
        var matchedProgrammes = 0;
        var now = DateTimeOffset.UtcNow;
        var earliest = now.AddHours(-3);
        var latest = now.AddDays(2);

        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CloseInput = false
        };

        using var reader = XmlReader.Create(stream, settings);
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element) continue;

            if (reader.LocalName.Equals("channel", StringComparison.OrdinalIgnoreCase))
            {
                xmlTvChannels++;
                var id = reader.GetAttribute("id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                var normalizedId = EpgSchedule.NormalizeKey(id);
                var displayNames = await ReadChannelDisplayNamesAsync(reader, cancellationToken);
                channelCatalog[normalizedId] = displayNames.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))?.Trim() ?? id.Trim();
                var keys = EpgSchedule.CandidateKeys([id, .. displayNames]);
                if (!requestedChannelIds.Contains(normalizedId) && !keys.Any(targetKeys.Contains)) continue;

                acceptedChannelIds.Add(normalizedId);
                foreach (var key in keys) aliases[key] = normalizedId;
                continue;
            }

            if (!reader.LocalName.Equals("programme", StringComparison.OrdinalIgnoreCase)) continue;
            parsedProgrammes++;
            var channelId = EpgSchedule.NormalizeKey(reader.GetAttribute("channel"));
            var start = ParseXmlTvTimestamp(reader.GetAttribute("start"));
            var stop = ParseXmlTvTimestamp(reader.GetAttribute("stop"));
            var isMatched = acceptedChannelIds.Contains(channelId) || requestedChannelIds.Contains(channelId) || targetKeys.Contains(channelId);
            var isInWindow = start is not null && stop is not null && stop > earliest && start < latest;
            if (isInWindow) programmesInWindow++;

            if (!isMatched || !isInWindow)
            {
                if (!reader.IsEmptyElement) await reader.SkipAsync();
                continue;
            }

            var details = await ReadProgrammeDetailsAsync(reader, cancellationToken);
            if (!programmes.TryGetValue(channelId, out var channelProgrammes))
            {
                channelProgrammes = [];
                programmes[channelId] = channelProgrammes;
            }

            channelProgrammes.Add(new EpgProgram(
                channelId,
                string.IsNullOrWhiteSpace(details.Title) ? "Untitled programme" : details.Title,
                NullIfBlank(details.Description),
                NullIfBlank(details.Category),
                start!.Value,
                stop!.Value,
                NullIfBlank(details.EpisodeId),
                details.SeasonNumber,
                details.EpisodeNumber,
                details.IsNewEpisode));
            matchedProgrammes++;

            if (matchedProgrammes % 2_000 == 0)
                progress?.Report(new PlaylistProgress(matchedProgrammes, $"Matched {matchedProgrammes:N0} guide programmes"));
        }

        var sorted = programmes.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<EpgProgram>)pair.Value.OrderBy(programme => programme.Start).ToList(),
            StringComparer.Ordinal);

        if (xmlTvChannels == 0 || parsedProgrammes == 0)
            throw new InvalidDataException(
                $"{displayName} did not contain usable XMLTV channel/programme data " +
                $"({xmlTvChannels:N0} guide channels, {parsedProgrammes:N0} programmes).");

        if (programmesInWindow == 0)
            throw new InvalidDataException(
                $"{displayName} contained XMLTV data, but no programmes overlapped the current guide window " +
                $"({xmlTvChannels:N0} guide channels, {parsedProgrammes:N0} programmes). The feed may be stale.");

        if (sorted.Count == 0)
        {
            progress?.Report(new PlaylistProgress(
                0,
                $"Guide downloaded — no automatic channel matches yet • {channelCatalog.Count:N0} XMLTV channels available for mapping"));
            return new EpgSchedule(
                new Dictionary<string, IReadOnlyList<EpgProgram>>(StringComparer.Ordinal),
                aliases,
                displayName,
                DateTimeOffset.UtcNow,
                channelCatalog);
        }

        progress?.Report(new PlaylistProgress(matchedProgrammes, $"Guide ready — {matchedProgrammes:N0} programmes"));
        return new EpgSchedule(sorted, aliases, displayName, DateTimeOffset.UtcNow, channelCatalog);
    }

    private static async Task<List<string>> ReadChannelDisplayNamesAsync(XmlReader reader, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        if (reader.IsEmptyElement) return names;
        using var subtree = reader.ReadSubtree();
        if (!await subtree.ReadAsync() || !await subtree.ReadAsync()) return names;
        while (!subtree.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (subtree.NodeType == XmlNodeType.Element && subtree.LocalName.Equals("display-name", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(await subtree.ReadElementContentAsStringAsync());
                continue;
            }
            if (!await subtree.ReadAsync()) break;
        }
        return names;
    }

    private static async Task<ProgrammeDetails> ReadProgrammeDetailsAsync(XmlReader reader, CancellationToken cancellationToken)
    {
        var details = new ProgrammeDetails();
        if (reader.IsEmptyElement) return details;
        using var subtree = reader.ReadSubtree();
        if (!await subtree.ReadAsync() || !await subtree.ReadAsync()) return details;
        while (!subtree.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (subtree.NodeType != XmlNodeType.Element)
            {
                if (!await subtree.ReadAsync()) break;
                continue;
            }
            if (subtree.LocalName.Equals("title", StringComparison.OrdinalIgnoreCase))
            {
                var value = await subtree.ReadElementContentAsStringAsync();
                details.Title ??= value;
                continue;
            }
            else if (subtree.LocalName.Equals("desc", StringComparison.OrdinalIgnoreCase))
            {
                var value = await subtree.ReadElementContentAsStringAsync();
                details.Description ??= value;
                continue;
            }
            else if (subtree.LocalName.Equals("category", StringComparison.OrdinalIgnoreCase))
            {
                var value = await subtree.ReadElementContentAsStringAsync();
                details.Category ??= value;
                continue;
            }
            else if (subtree.LocalName.Equals("new", StringComparison.OrdinalIgnoreCase))
                details.IsNewEpisode = true;
            else if (subtree.LocalName.Equals("previously-shown", StringComparison.OrdinalIgnoreCase))
                details.IsNewEpisode ??= false;
            else if (subtree.LocalName.Equals("episode-num", StringComparison.OrdinalIgnoreCase))
            {
                var system = subtree.GetAttribute("system")?.Trim() ?? string.Empty;
                var value = (await subtree.ReadElementContentAsStringAsync()).Trim();
                if (value.Length == 0) continue;
                details.EpisodeId ??= $"{system}:{value}";
                ParseEpisodeNumber(system, value, details);
                continue;
            }
            if (!await subtree.ReadAsync()) break;
        }
        return details;
    }

    private static void ParseEpisodeNumber(string system, string value, ProgrammeDetails details)
    {
        if (system.Equals("xmltv_ns", StringComparison.OrdinalIgnoreCase))
        {
            var parts = value.Split('.');
            if (parts.Length > 0 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zeroBasedSeason))
                details.SeasonNumber = zeroBasedSeason + 1;
            if (parts.Length > 1)
            {
                var episodePart = parts[1].Split('/')[0];
                if (int.TryParse(episodePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zeroBasedEpisode))
                    details.EpisodeNumber = zeroBasedEpisode + 1;
            }
            return;
        }

        var match = Regex.Match(value, @"S(?<season>\d{1,3})\s*E(?<episode>\d{1,4})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            details.SeasonNumber = int.Parse(match.Groups["season"].Value, CultureInfo.InvariantCulture);
            details.EpisodeNumber = int.Parse(match.Groups["episode"].Value, CultureInfo.InvariantCulture);
        }
    }

    public static DateTimeOffset? ParseXmlTvTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var datePart = parts[0];
        if (datePart.Length < 12) return null;

        var dateFormat = datePart.Length >= 14 ? "yyyyMMddHHmmss" : "yyyyMMddHHmm";
        datePart = datePart[..dateFormat.Length];
        if (!DateTime.TryParseExact(datePart, dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;

        if (parts.Length > 1)
        {
            var offsetText = parts[1];
            if (offsetText.Length == 5 && (offsetText[0] == '+' || offsetText[0] == '-'))
                offsetText = offsetText.Insert(3, ":");
            if (TimeSpan.TryParse(offsetText, CultureInfo.InvariantCulture, out var offset))
                return new DateTimeOffset(date, offset);
        }

        return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc));
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class ProgrammeDetails
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public string? EpisodeId { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public bool? IsNewEpisode { get; set; }
    }
}
