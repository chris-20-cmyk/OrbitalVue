using StreamVue.Player.Services;

namespace StreamVue.Player.Models;

public sealed record PlexPinChallenge(
    int Id,
    string Code,
    string AuthorizationUrl,
    DateTimeOffset ExpiresAt);

public sealed record PlexServerConnectionChoice(
    string Url,
    bool IsLocal,
    bool IsRelay,
    bool IsSecure,
    bool IsIpv6)
{
    public string DisplayLabel => string.Join(" · ", new[]
    {
        IsSecure ? "Secure" : "HTTP",
        IsLocal ? "Local" : null,
        IsRelay ? "Relay" : null,
        IsIpv6 ? "IPv6" : null
    }.OfType<string>());

    public string DisplayLocation => MediaCenterSecurity.SafeDisplayLocation(Url);
}

public sealed record PlexDiscoveredServer(
    string ServerId,
    string Name,
    bool IsOwned,
    IReadOnlyList<PlexServerConnectionChoice> Connections)
{
    public string DisplayLabel => IsOwned ? Name : $"{Name} · Shared";
    public PlexServerConnectionChoice? PreferredConnection => Connections.FirstOrDefault();
}

/// <summary>
/// A token-free, short-lived bridge between account approval and server activation.
/// </summary>
public sealed record PlexServerDiscovery(
    string SessionId,
    IReadOnlyList<PlexDiscoveredServer> Servers,
    DateTimeOffset ExpiresAt);
