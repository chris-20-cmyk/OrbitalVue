using OrbitalVue.Player.Models;

namespace OrbitalVue.Player.Services;

public enum ChannelHealthFilter
{
    All,
    NeedsAttention,
    MissingGuide,
    MissingLogo,
    Unreliable,
    Duplicates,
    Replay
}

public sealed record ChannelHealthRow(
    ChannelItem Channel,
    string RouteKey,
    string Status,
    string Detail,
    string FeedLabel,
    bool NeedsAttention,
    bool MissingGuide,
    bool MissingLogo,
    bool Unreliable,
    bool HasDuplicates,
    bool HasReplay)
{
    public string ChannelName => Channel.Name;
    public string Group => Channel.Group;
    public string Initials => Channel.Initials;
    public string SearchText => $"{Channel.Name}\n{Channel.Group}\n{Status}\n{Detail}".ToUpperInvariant();
}

public sealed record ChannelHealthSummary(
    IReadOnlyList<ChannelHealthRow> Rows,
    int NeedsAttention,
    int MissingGuide,
    int MissingLogo,
    int Unreliable,
    int Duplicates,
    int ReplayReady);

public static class ChannelHealthPolicy
{
    public static ChannelHealthSummary Analyze(
        IEnumerable<SignalRoute> routes,
        SignalRoutingPreferences preferences,
        Func<ChannelItem, bool> hasGuide)
    {
        var rows = new List<ChannelHealthRow>();
        foreach (var route in routes.Where(route => route.Representative.Kind == ChannelKind.Live))
        {
            var channel = route.Representative;
            var missingGuide = !hasGuide(channel);
            var missingLogo = route.Feeds.All(feed => string.IsNullOrWhiteSpace(feed.LogoUrl));
            var scores = route.Feeds.Select(feed => SmartSignalRoutingPolicy.Score(feed, preferences)).ToList();
            var observed = route.Feeds
                .Select(feed => preferences.FeedHealth.GetValueOrDefault(feed.StableKey))
                .Where(health => health is { CompletedStarts: > 0 })
                .ToList();
            var unreliable = observed.Count > 0 &&
                             (scores.Where(score => score.IsEligible).All(score => score.Score < 52) ||
                              observed.Sum(health => health!.FailedStarts) > observed.Sum(health => health!.SuccessfulStarts));
            var dead = observed.Count > 0 && observed.All(health => health!.SuccessfulStarts == 0 && health.FailedStarts >= 2);
            var duplicate = route.HasAlternates;
            var replay = route.Feeds.Any(feed => feed.HasCatchup);
            var issues = new List<string>();
            if (dead) issues.Add("all observed feeds failed");
            else if (unreliable) issues.Add("weak playback history");
            if (missingGuide) issues.Add("guide listing missing");
            if (missingLogo) issues.Add("logo missing");
            if (duplicate) issues.Add($"{route.FeedCount:N0} feeds routed together");
            if (replay) issues.Add("replay ready");
            var needsAttention = dead || unreliable || missingGuide || missingLogo;
            rows.Add(new ChannelHealthRow(
                channel,
                route.Key,
                dead ? "OFFLINE HISTORY" : unreliable ? "UNRELIABLE" : needsAttention ? "NEEDS DETAILS" : "HEALTHY",
                issues.Count == 0 ? "Guide, artwork, and observed playback look ready." : string.Join(" • ", issues),
                route.HasAlternates ? $"{route.FeedCount:N0} FEEDS" : "1 FEED",
                needsAttention,
                missingGuide,
                missingLogo,
                unreliable || dead,
                duplicate,
                replay));
        }

        return new ChannelHealthSummary(
            rows,
            rows.Count(row => row.NeedsAttention),
            rows.Count(row => row.MissingGuide),
            rows.Count(row => row.MissingLogo),
            rows.Count(row => row.Unreliable),
            rows.Count(row => row.HasDuplicates),
            rows.Count(row => row.HasReplay));
    }

    public static bool Matches(ChannelHealthRow row, ChannelHealthFilter filter) => filter switch
    {
        ChannelHealthFilter.NeedsAttention => row.NeedsAttention,
        ChannelHealthFilter.MissingGuide => row.MissingGuide,
        ChannelHealthFilter.MissingLogo => row.MissingLogo,
        ChannelHealthFilter.Unreliable => row.Unreliable,
        ChannelHealthFilter.Duplicates => row.HasDuplicates,
        ChannelHealthFilter.Replay => row.HasReplay,
        _ => true
    };
}
