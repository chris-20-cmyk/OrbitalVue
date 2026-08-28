using StreamVue.Player.Services;

namespace StreamVue.Player.Models;

public sealed record MediaCenterCredentialBinding
{
    public string ContractVersion { get; init; } = MediaCenterSecurity.ContractVersion;
    public string Provider { get; init; } = string.Empty;
    public string ServerId { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string CredentialId { get; init; } = string.Empty;
    public string? UserId { get; init; }
    public bool AllowInsecureHttp { get; init; }
}

public sealed record MediaCenterCredential
{
    public int SchemaVersion { get; init; } = 1;
    public required MediaCenterCredentialBinding Binding { get; init; }
    public required string AccessToken { get; init; }
    public required string DisplayName { get; init; }
}

public sealed record MediaCenterLocator(string Provider, string ServerId, string ItemId);

public sealed record ResolvedMediaPlayback(
    string Url,
    string Method,
    long ResumePositionMilliseconds,
    string? Referrer = null);

public sealed record MediaCenterServerIdentity(string ServerId, string DisplayName, string? Version = null);
