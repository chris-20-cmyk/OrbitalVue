using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public static partial class SmartSignalRoutingPolicy
{
    private static readonly HashSet<string> GenericNameSignatures = new(StringComparer.Ordinal)
    {
        "LIVE", "NEWS", "SPORT", "SPORTS", "MOVIE", "MOVIES", "MUSIC", "KIDS", "LOCAL", "EVENT", "EVENTS",
        "WEATHER", "RADIO", "PPV", "PREMIUM", "NETWORK", "CHANNEL", "TELEVISION"
    };

    public static IReadOnlyList<SignalRoute> BuildRoutes(
        IEnumerable<ChannelItem> channels,
        SignalRoutingPreferences? preferences = null)
    {
        preferences ??= new SignalRoutingPreferences();
        NormalizeManualRoutes(preferences);
        var manualMembership = preferences.ManualRoutes
            .SelectMany(route => route.FeedKeys.Select(key => (key, route)))
            .GroupBy(item => item.key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().route, StringComparer.OrdinalIgnoreCase);
        var separated = preferences.SeparatedFeedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builders = new List<RouteBuilder>();
        var buildersByAlias = new Dictionary<string, RouteBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in channels)
        {
            var aliases = separated.Contains(channel.StableKey)
                ? [$"FEED:{channel.StableKey}"]
                : manualMembership.TryGetValue(channel.StableKey, out var manualRoute)
                    ? [$"MANUAL:{manualRoute.Id:N}"]
                    : CreateLogicalAliases(channel);
            var matches = aliases
                .Select(alias => buildersByAlias.GetValueOrDefault(alias))
                .Where(builder => builder is not null && builder.IsActive)
                .Distinct()
                .OrderBy(builder => builder!.Order)
                .ToList();
            RouteBuilder target;
            if (matches.Count == 0)
            {
                var key = aliases[0].StartsWith("MANUAL:", StringComparison.Ordinal)
                    ? aliases[0]
                    : aliases[0].StartsWith("FEED:", StringComparison.Ordinal)
                        ? aliases[0]
                        : CreateLogicalChannelKey(channel);
                target = new RouteBuilder(key, builders.Count);
                builders.Add(target);
            }
            else
            {
                target = matches[0]!;
                foreach (var merged in matches.Skip(1).Cast<RouteBuilder>())
                {
                    target.Feeds.AddRange(merged.Feeds);
                    foreach (var alias in merged.Aliases)
                    {
                        target.Aliases.Add(alias);
                        buildersByAlias[alias] = target;
                    }
                    merged.IsActive = false;
                }
            }

            target.Feeds.Add(channel);
            foreach (var alias in aliases)
            {
                target.Aliases.Add(alias);
                buildersByAlias[alias] = target;
            }
        }

        var routes = new List<SignalRoute>();
        foreach (var builder in builders.Where(builder => builder.IsActive).OrderBy(builder => builder.Order))
        {
            foreach (var feed in builder.Feeds)
            {
                feed.SignalRouteKey = builder.Key;
                feed.SignalFeedCount = builder.Feeds.Count;
            }
            routes.Add(new SignalRoute(builder.Key, builder.Feeds[0], builder.Feeds));
        }
        return routes;
    }

    public static void LinkFeedToRoute(
        SignalRoutingPreferences preferences,
        SignalRoute route,
        ChannelItem candidate)
    {
        NormalizeManualRoutes(preferences);
        var keys = route.Feeds.Select(feed => feed.StableKey)
            .Append(candidate.StableKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var intersecting = preferences.ManualRoutes
            .Where(manual => manual.FeedKeys.Any(keys.Contains))
            .ToList();
        foreach (var manual in intersecting)
            foreach (var key in manual.FeedKeys) keys.Add(key);
        foreach (var manual in intersecting) preferences.ManualRoutes.Remove(manual);
        preferences.SeparatedFeedKeys.RemoveAll(keys.Contains);
        preferences.ManualRoutes.Add(new ManualSignalRoute
        {
            Id = intersecting.FirstOrDefault()?.Id ?? Guid.NewGuid(),
            Name = route.Representative.Name,
            FeedKeys = keys.Order(StringComparer.OrdinalIgnoreCase).ToList()
        });
    }

    public static bool ToggleFeedSeparation(SignalRoutingPreferences preferences, ChannelItem feed)
    {
        NormalizeManualRoutes(preferences);
        var separated = preferences.SeparatedFeedKeys.Contains(feed.StableKey, StringComparer.OrdinalIgnoreCase);
        if (separated)
        {
            preferences.SeparatedFeedKeys.RemoveAll(key => key.Equals(feed.StableKey, StringComparison.OrdinalIgnoreCase));
            return false;
        }

        preferences.SeparatedFeedKeys.Add(feed.StableKey);
        foreach (var manual in preferences.ManualRoutes)
            manual.FeedKeys.RemoveAll(key => key.Equals(feed.StableKey, StringComparison.OrdinalIgnoreCase));
        preferences.ManualRoutes.RemoveAll(manual => manual.FeedKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2);
        return true;
    }

    public static void NormalizeManualRoutes(SignalRoutingPreferences preferences)
    {
        preferences.ManualRoutes ??= [];
        preferences.SeparatedFeedKeys ??= [];
        preferences.SeparatedFeedKeys = preferences.SeparatedFeedKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var route in preferences.ManualRoutes)
        {
            route.FeedKeys ??= [];
            route.FeedKeys = route.FeedKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        preferences.ManualRoutes.RemoveAll(route => route.FeedKeys.Count < 2);
    }

    public static string CreateLogicalChannelKey(ChannelItem channel)
    {
        if (channel.Kind != ChannelKind.Live) return $"FEED:{channel.StableKey}";

        var aliases = CreateLogicalAliases(channel);
        var variant = ExtractScheduleVariant(channel.Name);
        var tvgId = NormalizeTvgId(channel.TvgId);
        var identity = tvgId.Length > 0
            ? $"TVG:{tvgId}{variant}"
            : aliases[0];

        return $"LIVE:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))}";
    }

    private static IReadOnlyList<string> CreateLogicalAliases(ChannelItem channel)
    {
        if (channel.Kind != ChannelKind.Live) return [$"FEED:{channel.StableKey}"];
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var variant = ExtractScheduleVariant(channel.Name);
        var tvgId = NormalizeTvgId(channel.TvgId);
        if (tvgId.Length > 0) aliases.Add($"TVG:{tvgId}{variant}");

        foreach (var value in new[] { channel.Name, channel.TvgName })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var signatureSource = Regex.Replace(
                value,
                @"\b(?:UHD|FHD|HD|SD|4K|\d{3,4}P|\d{2,3}\s*FPS)\b",
                " ",
                RegexOptions.IgnoreCase);
            var signature = CreateRoutingNameSignature(signatureSource);
            if (signature.Length >= 4 && !GenericNameSignatures.Contains(signature))
                aliases.Add($"NAME:{signature}{variant}");
        }

        if (aliases.Count == 0)
        {
            var canonicalName = EpgSchedule.CanonicalName(channel.Name);
            var canonicalGroup = EpgSchedule.CanonicalName(channel.Group);
            aliases.Add($"NAME:{canonicalName}|GROUP:{canonicalGroup}{variant}");
        }
        return aliases.Order(StringComparer.Ordinal).ToList();
    }

    private static string CreateRoutingNameSignature(string value)
    {
        var signature = EpgSchedule.SignatureName(value);
        var numberIdentity = Regex.Matches(
                EpgSchedule.NormalizeKey(value),
                @"(?<![A-Z0-9])\d{2,}(?![A-Z0-9])")
            .Cast<Match>()
            .Select(match => match.Value.TrimStart('0'))
            .Select(number => number.Length == 0 ? "0" : number)
            .ToList();
        return numberIdentity.Count == 0
            ? signature
            : $"{signature}|NUMBER:{string.Join('.', numberIdentity)}";
    }

    public static SignalFeedScore Score(
        ChannelItem feed,
        SignalRoutingPreferences preferences,
        DateTimeOffset? now = null)
    {
        preferences.FeedHealth ??= new Dictionary<string, SignalFeedHealth>(StringComparer.OrdinalIgnoreCase);
        preferences.FeedHealth.TryGetValue(feed.StableKey, out var health);
        health ??= new SignalFeedHealth();
        var eligible = health.Preference != SignalFeedPreference.Blocked;
        if (!eligible)
            return new SignalFeedScore(feed, -1_000, "Never use", "Excluded by your preference.", false);

        var score = 55d;
        var details = new List<string>();
        if (health.Preference == SignalFeedPreference.Preferred)
        {
            score += 28;
            details.Add("preferred");
        }

        var starts = health.CompletedStarts;
        if (starts > 0)
        {
            var reliability = (health.SuccessfulStarts + 2d) / (starts + 4d);
            score += (reliability - 0.5d) * 42d;
            details.Add($"{reliability:P0} reliable");
        }
        else
        {
            details.Add("learning history");
        }

        if (health.LastStartupMilliseconds > 0)
        {
            var seconds = health.LastStartupMilliseconds / 1000d;
            score += Math.Clamp(12d - seconds * 1.8d, -9d, 11d);
            details.Add($"{seconds:0.0}s start");
        }

        var completedSessions = Math.Max(1, starts);
        score -= Math.Min(16d, health.BufferEvents / (double)completedSessions * 2.2d);
        score -= Math.Min(14d, health.Reconnects / (double)completedSessions * 3.5d);
        score -= Math.Min(12d, health.StallRecoveries / (double)completedSessions * 4d);
        if (health.DroppedFrames > 0)
            score -= Math.Min(7d, Math.Log10(health.DroppedFrames + 1) * 1.8d);

        score += health.LastResolutionHeight switch
        {
            >= 2160 => 10,
            >= 1440 => 8,
            >= 1080 => 6,
            >= 720 => 3,
            > 0 and < 576 => -3,
            _ => 0
        };
        if (health.LastInputBitrateMbps > 0)
            score += Math.Clamp(health.LastInputBitrateMbps / 2d, 0d, 5d);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        if (health.LastFailureUtc is DateTimeOffset lastFailure && timestamp - lastFailure < TimeSpan.FromHours(6))
        {
            score -= 12;
            details.Add("recent failure");
        }
        if (health.LastSuccessUtc is DateTimeOffset lastSuccess && timestamp - lastSuccess < TimeSpan.FromDays(7))
            score += 3;

        score = Math.Clamp(score, 0d, 100d);
        var grade = starts == 0 && health.LastResolutionHeight == 0
            ? "Learning"
            : score >= 82 ? "Excellent"
            : score >= 68 ? "Strong"
            : score >= 52 ? "Fair"
            : "Weak";
        return new SignalFeedScore(feed, score, grade, string.Join(" • ", details), true);
    }

    public static ChannelItem? SelectBestFeed(
        SignalRoute route,
        SignalRoutingPreferences preferences,
        IReadOnlySet<string>? excludedFeedKeys = null,
        DateTimeOffset? now = null)
    {
        var ranked = route.Feeds
            .Where(feed => excludedFeedKeys?.Contains(feed.StableKey) != true)
            .Select((feed, index) => new { Feed = feed, Index = index, Score = Score(feed, preferences, now) })
            .Where(candidate => candidate.Score.IsEligible)
            .OrderByDescending(candidate =>
                preferences.FeedHealth.TryGetValue(candidate.Feed.StableKey, out var health) &&
                health.Preference == SignalFeedPreference.Preferred)
            .ThenByDescending(candidate => candidate.Score.Score)
            .ThenBy(candidate => candidate.Index)
            .ThenBy(candidate => candidate.Feed.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Feed.StableKey, StringComparer.Ordinal)
            .FirstOrDefault();
        return ranked?.Feed;
    }

    public static SignalFeedHealth GetOrCreateHealth(
        SignalRoutingPreferences preferences,
        ChannelItem feed,
        string routeKey)
    {
        preferences.FeedHealth ??= new Dictionary<string, SignalFeedHealth>(StringComparer.OrdinalIgnoreCase);
        if (!preferences.FeedHealth.TryGetValue(feed.StableKey, out var health) || health is null)
        {
            health = new SignalFeedHealth();
            preferences.FeedHealth[feed.StableKey] = health;
        }
        health.LogicalChannelKey = routeKey;
        health.ChannelName = feed.Name;
        health.SourceName = SourceLabel(feed);
        return health;
    }

    public static SignalFeedRow CreateRow(
        SignalRoute route,
        ChannelItem feed,
        SignalRoutingPreferences preferences,
        bool isActive,
        DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var score = Score(feed, preferences, timestamp);
        preferences.FeedHealth.TryGetValue(feed.StableKey, out var health);
        health ??= new SignalFeedHealth();
        var attempts = health.CompletedStarts;
        var reliability = attempts == 0 ? 0d : health.SuccessfulStarts / (double)attempts;
        var quality = health.LastResolutionHeight > 0
            ? $"{health.LastResolutionHeight:N0}p" +
              (health.LastFramesPerSecond > 0 ? $" • {health.LastFramesPerSecond:0.##} fps" : string.Empty) +
              (health.LastInputBitrateMbps > 0 ? $" • {health.LastInputBitrateMbps:0.00} Mbps" : string.Empty)
            : "Quality learning";
        var reliabilityText = attempts == 0
            ? "No completed tune history yet"
            : $"{reliability:P0} reliable • {health.SuccessfulStarts:N0} good / {health.FailedStarts:N0} failed";
        var history = $"{health.BufferEvents:N0} buffers • {health.Reconnects:N0} reconnects • {health.AutomaticFailovers:N0} failovers";
        var lastSeen = health.LastAttemptUtc is null
            ? "Not measured"
            : $"Last measured {FormatRelativeTime(timestamp - health.LastAttemptUtc.Value)}";
        var status = isActive ? "ACTIVE"
            : health.Preference == SignalFeedPreference.Preferred ? "PREFERRED"
            : health.Preference == SignalFeedPreference.Blocked ? "NEVER USE"
            : "AUTO";
        return new SignalFeedRow(
            feed,
            route.Key,
            SourceLabel(feed),
            feed.Name,
            score.IsEligible ? $"{score.Score:0} SIGNAL SCORE" : "EXCLUDED",
            score.Grade,
            score.Explanation,
            quality,
            reliabilityText,
            history,
            lastSeen,
            status,
            isActive ? "In use" : health.Preference == SignalFeedPreference.Blocked ? "Allow feed first" : "Use now",
            health.Preference == SignalFeedPreference.Preferred ? "Use automatic" : "Prefer",
            health.Preference == SignalFeedPreference.Blocked ? "Allow feed" : "Never use",
            isActive,
            health.Preference == SignalFeedPreference.Preferred,
            health.Preference == SignalFeedPreference.Blocked,
            !isActive && health.Preference != SignalFeedPreference.Blocked,
            preferences.SeparatedFeedKeys.Contains(feed.StableKey, StringComparer.OrdinalIgnoreCase)
                ? "Allow auto matching"
                : "Keep separate",
            feed.Kind == ChannelKind.Live);
    }

    public static IReadOnlyList<EpgProgram> MergeProgrammes(IEnumerable<IReadOnlyList<EpgProgram>> programmeSets) =>
        programmeSets
            .SelectMany(programmes => programmes)
            .DistinctBy(programme => (programme.Start, programme.Stop, programme.Title, programme.EpisodeId))
            .OrderBy(programme => programme.Start)
            .ToList();

    public static EpgNowNext GetNowNext(IReadOnlyList<EpgProgram> programmes, DateTimeOffset now)
    {
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

    public static int ParseResolutionHeight(string? resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution)) return 0;
        var match = ResolutionRegex().Match(resolution);
        var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return match.Success && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            ? Math.Clamp(height, 0, 8_640)
            : 0;
    }

    public static string SourceLabel(ChannelItem feed)
    {
        if (!string.IsNullOrWhiteSpace(feed.SourceName)) return feed.SourceName.Trim();
        return Uri.TryCreate(feed.Url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : "Playlist feed";
    }

    private static string NormalizeTvgId(string? value)
    {
        var normalized = EpgSchedule.NormalizeKey(value);
        return Regex.Replace(normalized, @"(?:[._\- ](?:UHD|FHD|HD|SD|4K))$", string.Empty);
    }

    private static string ExtractScheduleVariant(string value)
    {
        if (Regex.IsMatch(value, @"\b(?:EAST|EASTERN)\b|\(E\)", RegexOptions.IgnoreCase)) return "|SCHEDULE:EAST";
        if (Regex.IsMatch(value, @"\b(?:WEST|WESTERN)\b|\(W\)", RegexOptions.IgnoreCase)) return "|SCHEDULE:WEST";
        if (Regex.IsMatch(value, @"\bPACIFIC\b", RegexOptions.IgnoreCase)) return "|SCHEDULE:PACIFIC";
        if (Regex.IsMatch(value, @"\bMOUNTAIN\b", RegexOptions.IgnoreCase)) return "|SCHEDULE:MOUNTAIN";
        if (Regex.IsMatch(value, @"\bCENTRAL\b", RegexOptions.IgnoreCase)) return "|SCHEDULE:CENTRAL";
        return string.Empty;
    }

    private static string FormatRelativeTime(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed < TimeSpan.FromMinutes(1)) return "moments ago";
        if (elapsed < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)elapsed.TotalMinutes):N0} min ago";
        if (elapsed < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)elapsed.TotalHours):N0} hr ago";
        return $"{Math.Max(1, (int)elapsed.TotalDays):N0} day{((int)elapsed.TotalDays == 1 ? string.Empty : "s")} ago";
    }

    [GeneratedRegex(@"(?:\d{2,5})\s*[×xX]\s*(\d{3,4})|\b(\d{3,4})p\b", RegexOptions.IgnoreCase)]
    private static partial Regex ResolutionRegex();

    private sealed class RouteBuilder(string key, int order)
    {
        public string Key { get; } = key;
        public int Order { get; } = order;
        public bool IsActive { get; set; } = true;
        public List<ChannelItem> Feeds { get; } = [];
        public HashSet<string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
