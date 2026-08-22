namespace StreamVue.Player.Models;

public sealed record PlaylistResult(
    IReadOnlyList<ChannelItem> Channels,
    string DisplayName,
    string Source,
    DateTimeOffset LoadedAt,
    string? GuideSource = null);

public sealed record PlaylistProgress(int ChannelsParsed, string Message);
