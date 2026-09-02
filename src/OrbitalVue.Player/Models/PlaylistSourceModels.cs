using System.IO;
using System.Text.Json.Serialization;

namespace OrbitalVue.Player.Models;

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
        "plex" => "PLEX",
        "emby" => "EMBY",
        _ => "M3U FILE"
    };

    [JsonIgnore]
    public string StatusText => !IsEnabled
        ? "DISABLED"
        : UsedCachedFallback
            ? "OFFLINE COPY"
            : !string.IsNullOrWhiteSpace(LastError)
                ? "NEEDS ATTENTION"
            : LastSuccessUtc is not null
                ? "CONNECTED"
                : "READY";

    [JsonIgnore]
    public string DisplayLocation
    {
        get
        {
            if (SourceType == "file")
            {
                try { return Path.GetFileName(SourceValue); }
                catch { return SourceValue; }
            }

            var candidate = SourceValue;
            if (!candidate.Contains("://", StringComparison.Ordinal)) candidate = $"http://{candidate}";
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return SourceType == "xtream" ? "Saved account" : "Saved URL";
            var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
            return $"{uri.Host}{port}{uri.AbsolutePath.TrimEnd('/')}";
        }
    }

    [JsonIgnore]
    public string ChannelCountText => ChannelCount == 1 ? "1 channel" : $"{ChannelCount:N0} channels";

    [JsonIgnore]
    public string LastRefreshText => LastSuccessUtc is null
        ? "Not loaded yet"
        : $"Updated {LastSuccessUtc.Value.ToLocalTime():MMM d, h:mm tt}";
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
