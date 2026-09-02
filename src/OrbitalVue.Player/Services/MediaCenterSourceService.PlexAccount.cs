using System.IO;
using System.Net.Http;
using System.Text.Json;
using OrbitalVue.Player.Models;

namespace OrbitalVue.Player.Services;

public sealed partial class MediaCenterSourceService
{
    private const string PlexClientsBaseUrl = "https://clients.plex.tv/api/v2";
    private const string PlexAccountBaseUrl = "https://plex.tv/api/v2";
    private const int PlexAccountMaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan PlexPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PlexDiscoveryLifetime = TimeSpan.FromMinutes(10);
    private static readonly HashSet<string> PlexAccountHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "clients.plex.tv", "plex.tv"
    };

    private readonly object _plexDiscoveryGate = new();
    private readonly Dictionary<string, PlexAccountDiscoverySecret> _plexDiscoverySessions = new(StringComparer.Ordinal);

    public async Task<PlexPinChallenge> StartPlexAccountSignInAsync(
        CancellationToken cancellationToken = default)
    {
        RequirePlexAccountAccess();
        using var signer = _plexIdentityStore.OpenSigner(_deviceId);
        var body = JsonSerializer.Serialize(new
        {
            jwk = signer.PublicJwk,
            strong = true
        });
        using var document = await SendPlexAccountJsonAsync(
            HttpMethod.Post,
            $"{PlexClientsBaseUrl}/pins",
            PlexAccountHeaders(),
            body,
            cancellationToken);
        RequirePlexAccountAccess();
        var root = document.RootElement;
        var id = ReadInt(root, "id");
        if (id is null or <= 0)
            throw new InvalidDataException("Plex returned an incomplete sign-in request.");
        var code = MediaCenterSecurity.RequireIdentifier(
            ReadString(root, "code") ?? string.Empty,
            "Plex sign-in code");
        var now = DateTimeOffset.UtcNow;
        var expiresAt = ReadPlexExpiry(root, now) ?? now.AddMinutes(5);
        if (expiresAt <= now)
            throw new InvalidDataException("Plex returned an expired sign-in request.");
        return new PlexPinChallenge(
            id.Value,
            code,
            BuildPlexAuthorizationUrl(signer.ClientIdentifier, code),
            expiresAt);
    }

    public async Task<PlexServerDiscovery> WaitForPlexAccountServersAsync(
        PlexPinChallenge challenge,
        IProgress<PlaylistProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequirePlexAccountAccess();
        ValidatePlexChallenge(challenge);
        using var signer = _plexIdentityStore.OpenSigner(_deviceId);
        while (DateTimeOffset.UtcNow < challenge.ExpiresAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequirePlexAccountAccess();
            progress?.Report(new PlaylistProgress(0, "Waiting for Plex approval…"));
            var accountToken = await TryClaimPlexPinAsync(challenge, signer, cancellationToken);
            if (accountToken is null)
            {
                await Task.Delay(PlexPollInterval, cancellationToken);
                continue;
            }

            await VerifyPlexAccountTokenAsync(accountToken.Value, cancellationToken);
            RequirePlexAccountAccess();
            var servers = await DiscoverPlexAccountServersAsync(accountToken.Value, cancellationToken);
            RequirePlexAccountAccess();
            if (servers.Count == 0)
                throw new InvalidDataException("Plex approved OrbitalVue but returned no usable media servers.");

            var now = DateTimeOffset.UtcNow;
            var expiresAt = now.Add(PlexDiscoveryLifetime);
            if (accountToken.ExpiresAt is { } tokenExpiry && tokenExpiry < expiresAt)
                expiresAt = tokenExpiry;
            if (expiresAt <= now)
                throw new InvalidDataException("The Plex account approval expired before server discovery completed.");
            var sessionId = Guid.NewGuid().ToString("N");
            lock (_plexDiscoveryGate)
            {
                PrunePlexDiscoverySessions(now);
                while (_plexDiscoverySessions.Count >= 8)
                {
                    var oldest = _plexDiscoverySessions.MinBy(pair => pair.Value.ExpiresAt).Key;
                    _plexDiscoverySessions.Remove(oldest);
                }
                _plexDiscoverySessions[sessionId] = new PlexAccountDiscoverySecret(servers, expiresAt);
            }
            progress?.Report(new PlaylistProgress(servers.Count, $"Found {servers.Count:N0} Plex server(s)"));
            return new PlexServerDiscovery(
                sessionId,
                servers.Select(secret => secret.Server).ToList(),
                expiresAt);
        }
        throw new TimeoutException("The Plex sign-in request expired. Start a new sign-in.");
    }

    public async Task<PlaylistResult> ConnectDiscoveredPlexServerAsync(
        string sessionId,
        string serverId,
        string connectionUrl,
        bool allowInsecureHttp,
        IProgress<PlaylistProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequirePlexAccountAccess();
        var safeSessionId = MediaCenterSecurity.RequireIdentifier(sessionId, "Plex discovery session");
        var safeServerId = MediaCenterSecurity.RequireIdentifier(serverId, "Plex server identifier");
        var normalizedUrl = MediaCenterSecurity.NormalizeBaseUrl(connectionUrl);
        PlexAccountServerSecret selectedServer;
        PlexServerConnectionChoice selectedConnection;
        lock (_plexDiscoveryGate)
        {
            PrunePlexDiscoverySessions(DateTimeOffset.UtcNow);
            if (!_plexDiscoverySessions.TryGetValue(safeSessionId, out var session))
                throw new InvalidOperationException("The Plex server selection expired. Sign in again.");
            selectedServer = session.Servers.SingleOrDefault(value =>
                string.Equals(value.Server.ServerId, safeServerId, StringComparison.Ordinal))
                ?? throw new InvalidDataException("The selected Plex server is not part of this account approval.");
            selectedConnection = selectedServer.Server.Connections.SingleOrDefault(value =>
                string.Equals(value.Url, normalizedUrl, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("The selected Plex address is not part of this account approval.");
        }

        MediaCenterSecurity.RequireAllowedTransport(selectedConnection.Url, allowInsecureHttp);
        lock (_plexDiscoveryGate) _plexDiscoverySessions.Remove(safeSessionId);

        cancellationToken.ThrowIfCancellationRequested();
        RequirePlexAccountAccess();
        progress?.Report(new PlaylistProgress(0, "Verifying the selected Plex server…"));
        var identity = await ProbePlexIdentityAsync(selectedConnection.Url, cancellationToken);
        RequirePlexAccountAccess();
        if (!string.Equals(identity.ServerId, selectedServer.Server.ServerId, StringComparison.Ordinal))
            throw new InvalidDataException("The selected Plex server identity changed before connection.");

        var credential = CreateCredential(
            "plex",
            identity.ServerId,
            selectedConnection.Url,
            selectedServer.AccessToken,
            NormalizeDisplayName(selectedServer.Server.Name, identity.DisplayName, "Plex"),
            allowInsecureHttp);
        var previous = await _credentialStore.TryLoadForSourceAsync(
            "plex",
            selectedConnection.Url,
            cancellationToken);
        var saved = false;
        try
        {
            var playlist = await LoadPlexCatalogAsync(credential, progress, cancellationToken);
            RequirePlexAccountAccess();
            await _credentialStore.SaveAsync(credential, cancellationToken);
            saved = true;
            cancellationToken.ThrowIfCancellationRequested();
            RequirePlexAccountAccess();
            return playlist;
        }
        catch
        {
            if (saved)
            {
                try
                {
                    if (previous is null)
                        await _credentialStore.DeleteForSourceAsync("plex", selectedConnection.Url, CancellationToken.None);
                    else
                        await _credentialStore.SaveAsync(previous, CancellationToken.None);
                }
                catch
                {
                    // Preserve the original failure; the credential store remains DPAPI protected.
                }
            }
            throw;
        }
    }

    public void CancelPlexAccountDiscovery(string? sessionId = null)
    {
        lock (_plexDiscoveryGate)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                _plexDiscoverySessions.Clear();
                return;
            }
            _plexDiscoverySessions.Remove(sessionId.Trim());
        }
    }

    private async Task<PlexAccountToken?> TryClaimPlexPinAsync(
        PlexPinChallenge challenge,
        WindowsPlexDeviceSigner signer,
        CancellationToken cancellationToken)
    {
        var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (issuedAt <= 0) throw new InvalidOperationException("The Windows clock is invalid.");
        var proof = signer.SignJwt(new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["aud"] = "plex.tv",
            ["exp"] = issuedAt + 300,
            ["iat"] = issuedAt,
            ["iss"] = signer.ClientIdentifier
        });
        var url = AddQuery(
            $"{PlexClientsBaseUrl}/pins/{challenge.Id}",
            ("deviceJWT", proof));
        using var document = await SendPlexAccountJsonAsync(
            HttpMethod.Get,
            url,
            PlexAccountHeaders(),
            null,
            cancellationToken);
        var root = document.RootElement;
        var rawToken = ReadString(root, "authToken") ?? ReadString(root, "auth_token");
        if (string.IsNullOrWhiteSpace(rawToken)) return null;
        var token = ValidatePlexToken(rawToken);
        return new PlexAccountToken(token, ReadPlexExpiry(root, DateTimeOffset.UtcNow));
    }

    private async Task VerifyPlexAccountTokenAsync(
        string accountToken,
        CancellationToken cancellationToken)
    {
        using var document = await SendPlexAccountJsonAsync(
            HttpMethod.Get,
            $"{PlexAccountBaseUrl}/user",
            PlexAccountHeaders(accountToken),
            null,
            cancellationToken);
        if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            throw new InvalidDataException("Plex returned an invalid account response.");
    }

    private async Task<IReadOnlyList<PlexAccountServerSecret>> DiscoverPlexAccountServersAsync(
        string accountToken,
        CancellationToken cancellationToken)
    {
        var url = AddQuery(
            $"{PlexClientsBaseUrl}/resources",
            ("includeHttps", "1"),
            ("includeRelay", "1"),
            ("includeIPv6", "1"));
        using var document = await SendPlexAccountJsonAsync(
            HttpMethod.Get,
            url,
            PlexAccountHeaders(accountToken),
            null,
            cancellationToken);
        var values = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToArray()
            : ReadArray(document.RootElement, "resources").ToArray();
        var servers = new List<PlexAccountServerSecret>();
        foreach (var value in values)
        {
            var provides = (ReadString(value, "provides") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!provides.Contains("server", StringComparer.OrdinalIgnoreCase)) continue;
            string serverId;
            try
            {
                serverId = MediaCenterSecurity.RequireIdentifier(
                    ReadString(value, "clientIdentifier") ?? string.Empty,
                    "Plex server identifier");
            }
            catch
            {
                continue;
            }
            var rawServerToken = ReadString(value, "accessToken");
            if (string.IsNullOrWhiteSpace(rawServerToken)) continue;
            string serverToken;
            try
            {
                serverToken = ValidatePlexToken(rawServerToken);
            }
            catch
            {
                continue;
            }
            if (serverId.Contains(accountToken, StringComparison.Ordinal) ||
                serverId.Contains(serverToken, StringComparison.Ordinal)) continue;
            var name = SafePlexMetadata(
                ReadString(value, "name"),
                accountToken,
                serverToken) ?? "Plex server";
            var connections = ReadArray(value, "connections")
                .Select(connection => ParsePlexConnection(connection, accountToken, serverToken))
                .Where(connection => connection is not null)
                .Select(connection => connection!)
                .DistinctBy(connection => connection.Url, StringComparer.OrdinalIgnoreCase)
                .OrderBy(PlexConnectionPriority)
                .ToList();
            if (connections.Count == 0) continue;
            servers.Add(new PlexAccountServerSecret(
                new PlexDiscoveredServer(
                    serverId,
                    name,
                    ReadBool(value, "owned"),
                    connections),
                serverToken));
        }
        return servers
            .OrderByDescending(value => value.Server.IsOwned)
            .ThenBy(value => value.Server.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static PlexServerConnectionChoice? ParsePlexConnection(
        JsonElement value,
        string accountToken,
        string serverToken)
    {
        var candidate = ReadString(value, "uri");
        if (string.IsNullOrWhiteSpace(candidate))
        {
            var protocol = ReadString(value, "protocol")?.ToLowerInvariant();
            var address = ReadString(value, "address");
            var port = ReadInt(value, "port");
            if (protocol is not ("http" or "https") || string.IsNullOrWhiteSpace(address) || port is not (>= 1 and <= 65_535))
                return null;
            var host = address.Contains(':') && !address.StartsWith('[') ? $"[{address}]" : address;
            candidate = $"{protocol}://{host}:{port}";
        }
        string normalized;
        try
        {
            normalized = MediaCenterSecurity.NormalizeBaseUrl(candidate);
        }
        catch
        {
            return null;
        }
        if (normalized.Contains(accountToken, StringComparison.Ordinal) ||
            normalized.Contains(serverToken, StringComparison.Ordinal)) return null;
        var uri = new Uri(normalized);
        return new PlexServerConnectionChoice(
            normalized,
            ReadBool(value, "local"),
            ReadBool(value, "relay"),
            uri.Scheme == Uri.UriSchemeHttps,
            ReadBool(value, "IPv6") || uri.Host.Contains(':'));
    }

    private async Task<JsonDocument> SendPlexAccountJsonAsync(
        HttpMethod method,
        string url,
        IReadOnlyDictionary<string, string> headers,
        string? body,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(url, UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps ||
            !PlexAccountHosts.Contains(uri.IdnHost) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("The Plex account request address is unsafe.");
        return await SendJsonAsync(
            method,
            uri.ToString(),
            headers,
            body,
            cancellationToken,
            PlexAccountMaximumResponseBytes);
    }

    private Dictionary<string, string> PlexAccountHeaders(string? token = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "application/json",
            ["X-Plex-Client-Identifier"] = _deviceId,
            ["X-Plex-Product"] = "OrbitalVue",
            ["X-Plex-Version"] = ClientVersion
        };
        if (!string.IsNullOrWhiteSpace(token)) headers["X-Plex-Token"] = ValidatePlexToken(token);
        return headers;
    }

    private static void ValidatePlexChallenge(PlexPinChallenge challenge)
    {
        if (challenge.Id <= 0 || challenge.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The Plex sign-in request expired. Start a new sign-in.");
        MediaCenterSecurity.RequireIdentifier(challenge.Code, "Plex sign-in code");
        if (!Uri.TryCreate(challenge.AuthorizationUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.IdnHost, "app.plex.tv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Plex sign-in approval address is invalid.");
    }

    private static string BuildPlexAuthorizationUrl(string clientIdentifier, string code) =>
        $"https://app.plex.tv/auth#?clientID={Uri.EscapeDataString(clientIdentifier)}" +
        $"&code={Uri.EscapeDataString(code)}" +
        $"&context%5Bdevice%5D%5Bproduct%5D={Uri.EscapeDataString("OrbitalVue")}";

    private static DateTimeOffset? ReadPlexExpiry(JsonElement root, DateTimeOffset relativeTo)
    {
        var explicitValue = ReadString(root, "expiresAt") ?? ReadString(root, "expires_at");
        if (DateTimeOffset.TryParse(explicitValue, out var explicitExpiry)) return explicitExpiry;
        var seconds = ReadLong(root, "expiresIn") ?? ReadLong(root, "expires_in");
        return seconds is > 0 and <= 86_400 ? relativeTo.AddSeconds(seconds.Value) : null;
    }

    private static string ValidatePlexToken(string value)
    {
        var token = value.Trim();
        if (token.Length is 0 or > 16_384 || token.IndexOfAny(['\r', '\n']) >= 0)
            throw new InvalidDataException("Plex returned an invalid access token.");
        return token;
    }

    private static string? SafePlexMetadata(string? value, params string[] secrets)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            secrets.Any(secret => secret.Length == 0 || value.Contains(secret, StringComparison.Ordinal))) return null;
        var sanitized = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return sanitized.Length == 0 ? null : sanitized[..Math.Min(256, sanitized.Length)];
    }

    private static int PlexConnectionPriority(PlexServerConnectionChoice value) =>
        (value.IsSecure ? 0 : 1_000) +
        (value.IsLocal ? 0 : 100) +
        (value.IsRelay ? 50 : 0) +
        (value.IsIpv6 ? 1 : 0);

    private void PrunePlexDiscoverySessions(DateTimeOffset now)
    {
        foreach (var sessionId in _plexDiscoverySessions
                     .Where(pair => pair.Value.ExpiresAt <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
            _plexDiscoverySessions.Remove(sessionId);
    }

    private void RequirePlexAccountAccess()
    {
        var access = _premiumAccessProvider();
        if (!access.CanUseMediaCenters) CancelPlexAccountDiscovery();
        PremiumAccessPolicy.RequireMediaCenters(access);
    }

    private sealed record PlexAccountToken(string Value, DateTimeOffset? ExpiresAt);
    private sealed record PlexAccountServerSecret(PlexDiscoveredServer Server, string AccessToken);
    private sealed record PlexAccountDiscoverySecret(
        IReadOnlyList<PlexAccountServerSecret> Servers,
        DateTimeOffset ExpiresAt);
}
