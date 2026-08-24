using System.Text.Json.Serialization;

namespace StreamVue.Player.Models;

public sealed class PlaylistSourceDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string SourceType { get; set; } = "file";
    public string SourceValue { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool RefreshOnStartup { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? LastSuccessUtc { get; set; }
    public string? LastError { get; set; }
    public int ChannelCount { get; set; }
    public bool UsedCachedFallback { get; set; }

    [JsonIgnore]
    public string TypeLabel => SourceType switch
    {
        "url" => "M3U URL",
        "xtream" => "XTREAM",
        _ => "M3U FILE"
    };

    [JsonIgnore]
    public string StatusText => !IsEnabled
        ? "DISABLED"
        : UsedCachedFallback
            ? "OFFLINE COPY"
            : LastSuccessUtc is not null
                ? "CONNECTED"
                : "READY";
}

public sealed record PlaylistSourceSnapshot(
    PlaylistSourceDefinition Source,
    PlaylistResult Playlist,
    bool UsedCachedFallback = false);

public sealed record PlaylistMergeSummary(
    PlaylistResult Playlist,
    int SourceCount,
    int InputChannelCount,
    int DuplicateChannelCount);
