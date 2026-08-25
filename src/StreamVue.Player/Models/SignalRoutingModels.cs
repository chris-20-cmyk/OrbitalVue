using System.Text.Json.Serialization;

namespace StreamVue.Player.Models;

public enum SignalFeedPreference
{
    Auto,
    Preferred,
    Blocked
}

public sealed class SignalRoutingPreferences
{
    public bool Enabled { get; set; } = true;
    public bool AutomaticFailover { get; set; } = true;
    public int MaximumAutomaticSwitches { get; set; } = 3;
    public Dictionary<string, SignalFeedHealth> FeedHealth { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SignalFeedHealth
{
    public string? LogicalChannelKey { get; set; }
    public string? ChannelName { get; set; }
    public string? SourceName { get; set; }
    public SignalFeedPreference Preference { get; set; }
    public int SuccessfulStarts { get; set; }
    public int FailedStarts { get; set; }
    public long BufferEvents { get; set; }
    public long Reconnects { get; set; }
    public long StallRecoveries { get; set; }
    public long DroppedFrames { get; set; }
    public int AutomaticFailovers { get; set; }
    public int LastStartupMilliseconds { get; set; }
    public int LastResolutionHeight { get; set; }
    public double LastInputBitrateMbps { get; set; }
    public double LastFramesPerSecond { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? LastSuccessUtc { get; set; }
    public DateTimeOffset? LastFailureUtc { get; set; }
    public string? LastFailureReason { get; set; }

    [JsonIgnore]
    public int CompletedStarts => SuccessfulStarts + FailedStarts;
}

public sealed record SignalRoute(
    string Key,
    ChannelItem Representative,
    IReadOnlyList<ChannelItem> Feeds)
{
    public int FeedCount => Feeds.Count;
    public bool HasAlternates => FeedCount > 1;
}

public sealed record SignalFeedScore(
    ChannelItem Feed,
    double Score,
    string Grade,
    string Explanation,
    bool IsEligible);

public sealed record SignalRouteChoice(string RouteKey, string ChannelName, string Group, int FeedCount)
{
    public string DisplayText => $"{ChannelName}  •  {FeedCount:N0} feed{(FeedCount == 1 ? string.Empty : "s")}";
    public override string ToString() => DisplayText;
}

public sealed record SignalFeedRow(
    ChannelItem Feed,
    string RouteKey,
    string SourceLabel,
    string FeedName,
    string ScoreText,
    string Grade,
    string ScoreExplanation,
    string QualityText,
    string ReliabilityText,
    string HistoryText,
    string LastSeenText,
    string StatusText,
    string UseActionLabel,
    string PreferenceActionLabel,
    string BlockActionLabel,
    bool IsActive,
    bool IsPreferred,
    bool IsBlocked,
    bool CanUse);
