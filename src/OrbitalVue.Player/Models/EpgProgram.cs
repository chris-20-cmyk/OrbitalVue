using System.Net;
using System.Text.RegularExpressions;

namespace OrbitalVue.Player.Models;

public sealed record EpgProgram(
    string ChannelId,
    string Title,
    string? Description,
    string? Category,
    DateTimeOffset Start,
    DateTimeOffset Stop,
    string? EpisodeId = null,
    int? SeasonNumber = null,
    int? EpisodeNumber = null,
    bool? IsNewEpisode = null)
{
    public string LocalTimeRange => $"{Start.ToLocalTime():h:mm tt} – {Stop.ToLocalTime():h:mm tt}";
    public string? EpisodeLabel => SeasonNumber is int season && EpisodeNumber is int episode
        ? $"S{season:00}E{episode:00}"
        : EpisodeNumber is int standaloneEpisode ? $"Episode {standaloneEpisode}" : null;
}

public sealed record EpgNowNext(EpgProgram? Current, EpgProgram? Next);

public sealed record EpgChannelOption(string ChannelId, string DisplayName)
{
    public string SearchText => $"{DisplayName}\n{ChannelId}".ToUpperInvariant();
}

public sealed class EpgSchedule
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<EpgProgram>> _programsByChannel;
    private readonly IReadOnlyDictionary<string, string> _aliases;
    private readonly IReadOnlyDictionary<string, string> _channelCatalog;

    public EpgSchedule(
        IReadOnlyDictionary<string, IReadOnlyList<EpgProgram>> programsByChannel,
        IReadOnlyDictionary<string, string> aliases,
        string displayName,
        DateTimeOffset loadedAt,
        IReadOnlyDictionary<string, string>? channelCatalog = null)
    {
        _programsByChannel = programsByChannel;
        _aliases = aliases;
        _channelCatalog = channelCatalog ?? new Dictionary<string, string>(StringComparer.Ordinal);
        DisplayName = displayName;
        LoadedAt = loadedAt;
        ProgramCount = programsByChannel.Values.Sum(programs => programs.Count);
    }

    public string DisplayName { get; }
    public DateTimeOffset LoadedAt { get; }
    public int ChannelCount => _programsByChannel.Count;
    public int ProgramCount { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<EpgProgram>> ProgrammesByChannel => _programsByChannel;
    public IReadOnlyDictionary<string, string> Aliases => _aliases;
    public IReadOnlyDictionary<string, string> ChannelCatalog => _channelCatalog;
    public IReadOnlyList<EpgChannelOption> GuideChannels => _channelCatalog
        .Select(channel => new EpgChannelOption(channel.Key, channel.Value))
        .OrderBy(channel => channel.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(channel => channel.ChannelId, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static EpgSchedule Merge(IEnumerable<EpgSchedule> schedules, string displayName)
    {
        var materialized = schedules.ToList();
        var programmes = new Dictionary<string, List<EpgProgram>>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var channelCatalog = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var schedule in materialized)
        {
            foreach (var alias in schedule.Aliases) aliases.TryAdd(alias.Key, alias.Value);
            foreach (var channel in schedule.ChannelCatalog) channelCatalog.TryAdd(channel.Key, channel.Value);
            foreach (var channel in schedule.ProgrammesByChannel)
            {
                if (!programmes.TryGetValue(channel.Key, out var combined))
                {
                    combined = [];
                    programmes[channel.Key] = combined;
                }
                combined.AddRange(channel.Value);
            }
        }

        var merged = programmes.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<EpgProgram>)pair.Value
                .DistinctBy(programme => (programme.Start, programme.Stop, programme.Title, programme.EpisodeId))
                .OrderBy(programme => programme.Start)
                .ToList(),
            StringComparer.Ordinal);
        return new EpgSchedule(merged, aliases, displayName, DateTimeOffset.UtcNow, channelCatalog);
    }

    public EpgNowNext GetNowNext(ChannelItem channel, DateTimeOffset now)
        => GetNowNext(channel, now, null);

    public EpgNowNext GetNowNext(
        ChannelItem channel,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string>? manualMappings)
    {
        var programmes = GetProgrammes(channel, manualMappings);
        if (programmes.Count == 0) return new EpgNowNext(null, null);

        EpgProgram? current = null;
        EpgProgram? next = null;
        foreach (var programme in programmes)
        {
            if (programme.Start <= now && programme.Stop > now)
            {
                current = programme;
                continue;
            }

            if (programme.Start > now)
            {
                next = programme;
                break;
            }
        }

        return new EpgNowNext(current, next);
    }

    public IReadOnlyList<EpgProgram> GetProgrammes(ChannelItem channel)
        => GetProgrammes(channel, null);

    public IReadOnlyList<EpgProgram> GetProgrammes(
        ChannelItem channel,
        IReadOnlyDictionary<string, string>? manualMappings)
    {
        if (manualMappings is not null &&
            manualMappings.TryGetValue(channel.GuideMappingKey, out var mappedChannelId) &&
            _programsByChannel.TryGetValue(NormalizeKey(mappedChannelId), out var mapped))
        {
            return mapped;
        }

        foreach (var candidate in CandidateKeys(channel.TvgId, channel.TvgName, channel.Name))
        {
            if (_programsByChannel.TryGetValue(candidate, out var direct)) return direct;
            if (_aliases.TryGetValue(candidate, out var channelId) &&
                _programsByChannel.TryGetValue(channelId, out var aliased)) return aliased;
        }

        return [];
    }

    public static IReadOnlyList<string> CandidateKeys(params string?[] values)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var normalized = NormalizeKey(value);
            if (normalized.Length > 0) keys.Add(normalized);
            var canonical = CanonicalName(value);
            if (canonical.Length > 0) keys.Add(canonical);
            var signature = SignatureName(value);
            if (signature.Length >= 3) keys.Add($"SIGNATURE:{signature}");
            foreach (Match match in Regex.Matches(normalized, @"(?<![A-Z0-9])([KW][A-Z]{2,3})(?=[^A-Z]|$)"))
                keys.Add($"CALLSIGN:{match.Groups[1].Value}");
        }

        return keys.ToList();
    }

    public static string NormalizeKey(string? value) =>
        WebUtility.HtmlDecode(value ?? string.Empty).Trim().ToUpperInvariant();

    public static string CanonicalName(string? value)
    {
        var normalized = NormalizeKey(value);
        normalized = Regex.Replace(normalized, @"^(US|USA|CA|CANADA|UK|GB)\s*[:|\-]\s*", string.Empty);
        normalized = Regex.Replace(normalized, @"\s*\((D|E|W|EAST|WEST)\)\s*$", string.Empty);
        normalized = Regex.Replace(normalized, @"\b(UHD|FHD|HD|4K)\b", string.Empty);
        return Regex.Replace(normalized, @"[^A-Z0-9]", string.Empty);
    }

    public static string SignatureName(string? value)
    {
        var normalized = NormalizeKey(Regex.Replace(value ?? string.Empty, @"\([^)]*\)", " "));
        normalized = normalized.Replace("A&E", "AANDE", StringComparison.Ordinal);
        var tokens = Regex.Split(normalized, @"[^A-Z0-9]+")
            .Where(token => token.Length > 0)
            .Select(NormalizeSignatureToken)
            .Where(token => token.Length > 0 && !SignatureStopWords.Contains(token) &&
                            (!token.All(char.IsDigit) || token.Length == 1))
            .ToList();
        var signature = string.Concat(tokens);
        return signature switch
        {
            "AND" => "AANDE",
            "HOMEANDGARDEN" or "HOMEGARDEN" => "HGTV",
            "INVESTIGATIONDISCOVERY" => "DISCOVERYID",
            "HALLMARKMOVIESMYSTERIES" => "HALLMARKMYSTERY",
            "BUZZER" => "BUZZR",
            "VICELAND" => "VICE",
            "ME" => "METV",
            "SCIENCE" => "DISCOVERYSCIENCE",
            "MTVMUSIC" => "MTV",
            "MTV2MUSIC" => "MTV2",
            "FOXSPORTS1" => "FS1",
            "FOXSPORTS2" => "FS2",
            "BIGTEN" or "BTN" => "BTN",
            _ when signature.StartsWith("AWEAWEALTH", StringComparison.Ordinal) => "AWE",
            _ when signature.StartsWith("SPACECITYHOME", StringComparison.Ordinal) => "SPACECITYHOME",
            _ when signature.StartsWith("ESPNUCOLLEGE", StringComparison.Ordinal) => "ESPNU",
            _ when signature.StartsWith("ESPNSEC", StringComparison.Ordinal) => "SEC",
            _ when signature.StartsWith("MLB", StringComparison.Ordinal) && signature is "MLB" or "MLBNETWORK" or "MLBCHANNEL" => "MLB",
            _ => signature
        };
    }

    private static string NormalizeSignatureToken(string token) =>
        SignatureProtectedTokens.Contains(token)
            ? token
            : Regex.Replace(token, @"(?<=\w)(CHANNEL|NETWORK|TELEVISION|TV|UHD|FHD|HD|SD)$", string.Empty);

    private static readonly HashSet<string> SignatureStopWords = new(StringComparer.Ordinal)
    {
        "US", "US1", "US2", "US3", "USLOCALS1", "CA", "CA1", "CA2",
        "HD", "SD", "UHD", "FHD", "4K", "D", "E", "W", "H", "S", "A", "PM", "MAX",
        "CHANNEL", "NETWORK", "TELEVISION", "TV", "THE", "EAST", "WEST", "EASTERN", "PACIFIC",
        "FEED", "LIVE", "STREAM", "FPS", "MBPS"
    };

    private static readonly HashSet<string> SignatureProtectedTokens = new(StringComparer.Ordinal)
    {
        "HGTV", "MTV", "METV", "COZITV"
    };
}

public sealed record GuideChannelRow(
    ChannelItem Channel,
    string CurrentTitle,
    string CurrentTime,
    double CurrentProgress,
    string NextTitle,
    string NextTime,
    bool HasSchedule)
{
    public string ChannelName => Channel.Name;
    public string Group => Channel.Group;
    public string Initials => Channel.Initials;
}

public sealed record GuideProgrammeBlock(
    ChannelItem Channel,
    EpgProgram? Programme,
    double Left,
    double Width,
    bool IsCurrent,
    bool IsPast,
    bool IsPlaceholder)
{
    public string Title => Programme?.Title ?? "No guide listing";
    public string Time => Programme?.LocalTimeRange ?? "Assign an XMLTV channel";
    public bool CanReplay => Programme is not null && Channel.HasCatchup && IsPast &&
                             (Channel.CatchupDays <= 0 || Programme.Stop >= DateTimeOffset.UtcNow.AddDays(-Channel.CatchupDays));
    public string Category => CanReplay
        ? $"↶ REPLAY{(string.IsNullOrWhiteSpace(Programme?.Category) ? string.Empty : $"  •  {Programme.Category}")}"
        : Programme?.Category ?? string.Empty;
}

public sealed record GuideTimelineRow(
    ChannelItem Channel,
    IReadOnlyList<GuideProgrammeBlock> Blocks,
    double TimelineWidth,
    double NowMarkerLeft,
    bool ShowNowMarker,
    bool HasSchedule,
    string MappingStatus)
{
    public string ChannelName => Channel.Name;
    public string Group => Channel.Group;
    public string Initials => Channel.Initials;
    public bool CanMap => !HasSchedule;
}

public sealed record GuideTimeMarker(double Left, double Width, string TimeLabel, string DateLabel, bool IsHour);

public sealed record GuideMappingChannelRow(ChannelItem Channel, string Status, string? MappedChannelId)
{
    public string ChannelName => Channel.Name;
    public string Group => Channel.Group;
    public string Initials => Channel.Initials;
    public bool IsMapped => !string.IsNullOrWhiteSpace(MappedChannelId);
}
