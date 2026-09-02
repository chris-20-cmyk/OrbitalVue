using System.IO;
using System.Net.Http;
using OrbitalVue.Player.Models;

namespace OrbitalVue.Player.Services;

public sealed partial class MediaCenterSourceService
{
    private const int MaximumArtworkResponseBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan ArtworkIdentityLifetime = TimeSpan.FromMinutes(5);

    private readonly object _artworkStateLock = new();
    private readonly SemaphoreSlim _artworkNetworkGate = new(4, 4);
    private readonly SemaphoreSlim _artworkIdentityGate = new(1, 1);
    private readonly Dictionary<string, ArtworkIdentityCacheEntry> _artworkIdentityCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _artworkServerLifetimes = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _artworkRequestLifetime = new();

    public async Task<byte[]> LoadArtworkAsync(
        string artworkLocator,
        int maximumWidth = 512,
        CancellationToken cancellationToken = default)
    {
        RequirePremiumAccess();
        var locator = MediaCenterSecurity.ParseArtworkLocator(artworkLocator);
        maximumWidth = Math.Clamp(maximumWidth, 64, 1_600);
        using var requestCancellation = CreateArtworkRequestCancellation(
            locator.Provider,
            locator.ServerId,
            cancellationToken);
        var credential = await _credentialStore.TryLoadByServerAsync(locator.Provider, locator.ServerId, requestCancellation.Token)
            ?? throw new InvalidOperationException($"Reconnect this {ProviderLabel(locator.Provider)} server to load protected artwork.");
        MediaCenterSecurity.AssertCredentialBinding(
            credential,
            locator.Provider,
            credential.Binding.BaseUrl,
            locator.ServerId);

        await _artworkNetworkGate.WaitAsync(requestCancellation.Token);
        try
        {
            await EnsureArtworkServerIdentityAsync(locator, credential, requestCancellation.Token);
            RequirePremiumAccess();
            var (url, headers) = CreateArtworkRequest(locator, credential, maximumWidth);
            return await SendArtworkAsync(url, headers, requestCancellation.Token);
        }
        finally
        {
            _artworkNetworkGate.Release();
        }
    }

    public void CancelAllArtworkRequests()
    {
        CancellationTokenSource globalLifetime;
        List<CancellationTokenSource> sourceLifetimes;
        lock (_artworkStateLock)
        {
            globalLifetime = _artworkRequestLifetime;
            _artworkRequestLifetime = new CancellationTokenSource();
            sourceLifetimes = _artworkServerLifetimes.Values.ToList();
            _artworkServerLifetimes.Clear();
            _artworkIdentityCache.Clear();
        }
        CancelAndDispose(globalLifetime);
        foreach (var lifetime in sourceLifetimes) CancelAndDispose(lifetime);
    }

    private void CancelArtworkRequestsForSource(string provider, string baseUrl, string? serverId)
    {
        var key = ArtworkSourceKey(provider, baseUrl);
        CancellationTokenSource? sourceLifetime;
        lock (_artworkStateLock)
        {
            _artworkIdentityCache.Remove(key);
            sourceLifetime = string.IsNullOrWhiteSpace(serverId)
                ? null
                : _artworkServerLifetimes.Remove(ArtworkServerKey(provider, serverId), out var removed)
                    ? removed
                    : null;
        }
        if (sourceLifetime is not null) CancelAndDispose(sourceLifetime);
    }

    private CancellationTokenSource CreateArtworkRequestCancellation(
        string provider,
        string serverId,
        CancellationToken cancellationToken)
    {
        var key = ArtworkServerKey(provider, serverId);
        lock (_artworkStateLock)
        {
            if (!_artworkServerLifetimes.TryGetValue(key, out var sourceLifetime))
            {
                sourceLifetime = new CancellationTokenSource();
                _artworkServerLifetimes[key] = sourceLifetime;
            }
            return CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _artworkRequestLifetime.Token,
                sourceLifetime.Token);
        }
    }

    private async Task EnsureArtworkServerIdentityAsync(
        MediaCenterArtworkLocator locator,
        MediaCenterCredential credential,
        CancellationToken cancellationToken)
    {
        if (IsArtworkIdentityCurrent(locator, credential.Binding.BaseUrl)) return;
        await _artworkIdentityGate.WaitAsync(cancellationToken);
        try
        {
            if (IsArtworkIdentityCurrent(locator, credential.Binding.BaseUrl)) return;
            var identity = locator.Provider == "plex"
                ? await ProbePlexIdentityAsync(credential.Binding.BaseUrl, cancellationToken)
                : await ProbeEmbyIdentityAsync(credential.Binding.BaseUrl, cancellationToken);
            if (!string.Equals(identity.ServerId, locator.ServerId, StringComparison.Ordinal))
                throw new InvalidDataException("The media-center server identity does not match this protected artwork.");
            RememberArtworkIdentity(locator.Provider, credential.Binding.BaseUrl, identity.ServerId);
        }
        finally
        {
            _artworkIdentityGate.Release();
        }
    }

    private bool IsArtworkIdentityCurrent(MediaCenterArtworkLocator locator, string baseUrl)
    {
        lock (_artworkStateLock)
        {
            if (!_artworkIdentityCache.TryGetValue(ArtworkSourceKey(locator.Provider, baseUrl), out var cached) ||
                cached.ExpiresUtc <= DateTimeOffset.UtcNow)
                return false;
            if (!string.Equals(cached.ServerId, locator.ServerId, StringComparison.Ordinal))
                throw new InvalidDataException("The cached media-center identity does not match this protected artwork.");
            return true;
        }
    }

    private void RememberArtworkIdentity(string provider, string baseUrl, string serverId)
    {
        var safeServerId = MediaCenterSecurity.RequireIdentifier(serverId, "media-center server identifier");
        lock (_artworkStateLock)
        {
            _artworkIdentityCache[ArtworkSourceKey(provider, baseUrl)] = new ArtworkIdentityCacheEntry(
                safeServerId,
                DateTimeOffset.UtcNow.Add(ArtworkIdentityLifetime));
        }
    }

    private (string Url, Dictionary<string, string> Headers) CreateArtworkRequest(
        MediaCenterArtworkLocator locator,
        MediaCenterCredential credential,
        int maximumWidth)
    {
        if (locator.Provider == "plex")
        {
            var sourcePath = $"/library/metadata/{Uri.EscapeDataString(locator.ItemId)}/thumb";
            if (!string.IsNullOrWhiteSpace(locator.VersionTag))
                sourcePath += $"/{Uri.EscapeDataString(locator.VersionTag)}";
            var url = MediaCenterSecurity.ResolveServerPath(
                credential.Binding.BaseUrl,
                "/photo/:/transcode");
            url = AddQuery(url,
                ("url", sourcePath),
                ("format", "jpeg"),
                ("width", maximumWidth.ToString()),
                ("height", Math.Min(2_400, maximumWidth * 3 / 2).ToString()),
                ("quality", "85"),
                ("upscale", "0"));
            var headers = PlexHeaders(credential.AccessToken);
            headers["Accept"] = "image/*";
            return (url, headers);
        }

        var embyUrl = MediaCenterSecurity.ResolveServerPath(
            MediaCenterSecurity.EmbyApiBaseUrl(credential.Binding.BaseUrl),
            $"/Items/{Uri.EscapeDataString(locator.ItemId)}/Images/Primary");
        embyUrl = AddQuery(embyUrl, ("maxWidth", maximumWidth.ToString()), ("quality", "90"));
        var embyHeaders = EmbyHeaders(credential.AccessToken, credential.Binding.UserId);
        embyHeaders["Accept"] = "image/*";
        return (embyUrl, embyHeaders);
    }

    private async Task<byte[]> SendArtworkAsync(
        string url,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (name, value) in headers)
        {
            if (!HeaderNamePattern().IsMatch(name) || ContainsAny(value, '\r', '\n'))
                throw new InvalidDataException("A media-center artwork header is invalid.");
            request.Headers.TryAddWithoutValidation(name, value);
        }
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType is null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The media server returned a non-image artwork response.");
        if (response.Content.Headers.ContentLength > MaximumArtworkResponseBytes)
            throw new InvalidDataException("The media server returned oversized artwork.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaximumArtworkResponseBytes)
                throw new InvalidDataException("The media server returned oversized artwork.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    private static string ArtworkSourceKey(string provider, string baseUrl) =>
        $"{MediaCenterSecurity.NormalizeProvider(provider)}|{MediaCenterSecurity.NormalizeBaseUrl(baseUrl)}";

    private static string ArtworkServerKey(string provider, string serverId) =>
        $"{MediaCenterSecurity.NormalizeProvider(provider)}|{MediaCenterSecurity.RequireIdentifier(serverId, "media-center server identifier")}";

    private static void CancelAndDispose(CancellationTokenSource cancellation)
    {
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
        cancellation.Dispose();
    }

    private sealed record ArtworkIdentityCacheEntry(string ServerId, DateTimeOffset ExpiresUtc);
}
