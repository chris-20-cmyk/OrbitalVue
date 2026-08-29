using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public sealed partial class MediaCenterSourceService
{
    private const int PageSize = 200;
    private const int MaximumItems = 20_000;
    private const int MaximumResponseBytes = 32 * 1024 * 1024;
    private const string ClientVersion = "5.1.0";

    private readonly MediaCenterCredentialStore _credentialStore;
    private readonly HttpClient _http;
    private readonly string _deviceId;
    private readonly Func<PremiumAccessSnapshot> _premiumAccessProvider;

    public MediaCenterSourceService(
        MediaCenterCredentialStore? credentialStore = null,
        HttpClient? httpClient = null,
        string? deviceId = null,
        PremiumAccessSnapshot? premiumAccess = null,
        Func<PremiumAccessSnapshot>? premiumAccessProvider = null)
    {
        _credentialStore = credentialStore ?? new MediaCenterCredentialStore();
        _http = httpClient ?? CreateHttpClient();
        _deviceId = string.IsNullOrWhiteSpace(deviceId)
            ? ResolveDeviceId()
            : MediaCenterSecurity.RequireIdentifier(deviceId, "media-center device identifier");
        _premiumAccessProvider = premiumAccessProvider ?? (() => premiumAccess ?? PremiumAccessPolicy.Current);
    }

    public async Task<PlaylistResult> ConnectPlexAsync(
        string serverAddress,
        string accessToken,
        string? displayName,
        bool allowInsecureHttp,
        IProgress<PlaylistProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequirePremiumAccess();
        var baseUrl = MediaCenterSecurity.NormalizeBaseUrl(serverAddress);
        MediaCenterSecurity.RequireAllowedTransport(baseUrl, allowInsecureHttp);
        accessToken = accessToken.Trim();
        if (accessToken.Length == 0) throw new ArgumentException("Enter the Plex server token.", nameof(accessToken));
        if (accessToken.Length > 16_384) throw new ArgumentException("The Plex server token is too long.", nameof(accessToken));
        progress?.Report(new PlaylistProgress(0, "Verifying the Plex server identity…"));
        var identity = await ProbePlexIdentityAsync(baseUrl, cancellationToken);
        var name = NormalizeDisplayName(displayName, identity.DisplayName, "Plex");
        var credential = CreateCredential("plex", identity.ServerId, baseUrl, accessToken, name, allowInsecureHttp);
        var playlist = await LoadPlexCatalogAsync(credential, progress, cancellationToken);
        await _credentialStore.SaveAsync(credential, cancellationToken);
        return playlist;
    }

    public async Task<PlaylistResult> ConnectEmbyAsync(
        string serverAddress,
        string username,
        string password,
        string? displayName,
        bool allowInsecureHttp,
        IProgress<PlaylistProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequirePremiumAccess();
        var baseUrl = MediaCenterSecurity.NormalizeBaseUrl(serverAddress);
        MediaCenterSecurity.RequireAllowedTransport(baseUrl, allowInsecureHttp);
        username = username.Trim();
        if (username.Length == 0) throw new ArgumentException("Enter the Emby username.", nameof(username));
        if (username.Length > 256) throw new ArgumentException("The Emby username is too long.", nameof(username));
        if (password.Length == 0) throw new ArgumentException("Enter the Emby password.", nameof(password));
        if (password.Length > 4_096) throw new ArgumentException("The Emby password is too long.", nameof(password));
        progress?.Report(new PlaylistProgress(0, "Verifying the Emby server identity…"));
        var publicIdentity = await ProbeEmbyIdentityAsync(baseUrl, cancellationToken);
        progress?.Report(new PlaylistProgress(0, "Authenticating with Emby…"));
        using var authentication = await SendJsonAsync(
            HttpMethod.Post,
            MediaCenterSecurity.ResolveServerPath(MediaCenterSecurity.EmbyApiBaseUrl(baseUrl), "/Users/AuthenticateByName"),
            EmbyHeaders(null, null),
            JsonSerializer.Serialize(new { Username = username, Pw = password }),
            cancellationToken);
        var root = authentication.RootElement;
        var token = ReadString(root, "AccessToken")?.Trim();
        var serverId = ReadString(root, "ServerId");
        var user = ReadObject(root, "User");
        var userId = user is null ? null : ReadString(user.Value, "Id");
        var userName = user is null ? username : ReadString(user.Value, "Name") ?? username;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(serverId) || string.IsNullOrWhiteSpace(userId))
            throw new InvalidDataException("Emby authenticated but returned an incomplete session.");
        serverId = MediaCenterSecurity.RequireIdentifier(serverId, "Emby server identifier");
        userId = MediaCenterSecurity.RequireIdentifier(userId, "Emby user identifier");
        if (!string.Equals(serverId, publicIdentity.ServerId, StringComparison.Ordinal))
            throw new InvalidDataException("The Emby server identity changed during authentication.");
        var name = NormalizeDisplayName(displayName, publicIdentity.DisplayName, userName);
        var credential = CreateCredential("emby", serverId, baseUrl, token, name, allowInsecureHttp, userId);
        var playlist = await LoadEmbyCatalogAsync(credential, progress, cancellationToken);
        await _credentialStore.SaveAsync(credential, cancellationToken);
        return playlist;
    }

    public async Task<PlaylistResult> LoadSavedAsync(
        string provider,
        string serverAddress,
        IProgress<PlaylistProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequirePremiumAccess();
        provider = MediaCenterSecurity.NormalizeProvider(provider);
        var baseUrl = MediaCenterSecurity.NormalizeBaseUrl(serverAddress);
        var credential = await _credentialStore.TryLoadForSourceAsync(provider, baseUrl, cancellationToken)
            ?? throw new InvalidOperationException($"Reconnect this {ProviderLabel(provider)} server to unlock its protected credential.");
        MediaCenterSecurity.AssertCredentialBinding(credential, provider, baseUrl);
        return provider == "plex"
            ? await LoadPlexCatalogAsync(credential, progress, cancellationToken)
            : await LoadEmbyCatalogAsync(credential, progress, cancellationToken);
    }

    public async Task<bool> HasCredentialAsync(
        string provider,
        string serverAddress,
        CancellationToken cancellationToken = default)
    {
        RequirePremiumAccess();
        return await _credentialStore.TryLoadForSourceAsync(provider, serverAddress, cancellationToken) is not null;
    }

    public Task DeleteCredentialAsync(
        string provider,
        string serverAddress,
        CancellationToken cancellationToken = default) =>
        _credentialStore.DeleteForSourceAsync(provider, serverAddress, cancellationToken);

    public async Task<ResolvedMediaPlayback> ResolvePlaybackAsync(
        ChannelItem channel,
        CancellationToken cancellationToken = default)
    {
        RequirePremiumAccess();
        var locator = MediaCenterSecurity.ParsePlaybackLocator(channel.Url);
        var credential = await _credentialStore.TryLoadByServerAsync(locator.Provider, locator.ServerId, cancellationToken)
            ?? throw new InvalidOperationException($"Reconnect this {ProviderLabel(locator.Provider)} server to unlock playback.");
        MediaCenterSecurity.AssertCredentialBinding(
            credential,
            locator.Provider,
            credential.Binding.BaseUrl,
            locator.ServerId);
        return locator.Provider == "plex"
            ? await ResolvePlexPlaybackAsync(locator, credential, channel.ResumePositionMilliseconds, cancellationToken)
            : await ResolveEmbyPlaybackAsync(locator, credential, channel.ResumePositionMilliseconds, cancellationToken);
    }

    private async Task<PlaylistResult> LoadPlexCatalogAsync(
        MediaCenterCredential credential,
        IProgress<PlaylistProgress>? progress,
        CancellationToken cancellationToken)
    {
        credential = MediaCenterSecurity.ValidateCredential(credential);
        var identity = await ProbePlexIdentityAsync(credential.Binding.BaseUrl, cancellationToken);
        if (!string.Equals(identity.ServerId, credential.Binding.ServerId, StringComparison.Ordinal))
            throw new InvalidDataException("The Plex server identity does not match this protected credential.");

        progress?.Report(new PlaylistProgress(0, "Loading Plex libraries…"));
        using var libraryDocument = await SendJsonAsync(
            HttpMethod.Get,
            MediaCenterSecurity.ResolveServerPath(credential.Binding.BaseUrl, "/library/sections"),
            PlexHeaders(credential.AccessToken),
            null,
            cancellationToken);
        var container = ReadObject(libraryDocument.RootElement, "MediaContainer") ?? libraryDocument.RootElement;
        var libraries = ReadArray(container, "Directory")
            .Select(element => new MediaLibrary(
                MediaCenterSecurity.RequireIdentifier(ReadString(element, "key") ?? string.Empty, "Plex library identifier"),
                ReadString(element, "title") ?? "Plex library",
                ReadString(element, "type")?.ToLowerInvariant() ?? "other"))
            .ToList();
        var items = new List<CatalogMediaItem>();
        foreach (var library in libraries)
        {
            for (var start = 0; items.Count < MaximumItems; start += PageSize)
            {
                var path = $"/library/sections/{Uri.EscapeDataString(library.Id)}/all";
                var requestUrl = MediaCenterSecurity.ResolveServerPath(credential.Binding.BaseUrl, path);
                if (library.Kind == "show") requestUrl = AddQuery(requestUrl, ("type", "4"));
                var headers = PlexHeaders(credential.AccessToken);
                headers["X-Plex-Container-Start"] = start.ToString();
                headers["X-Plex-Container-Size"] = PageSize.ToString();
                using var pageDocument = await SendJsonAsync(HttpMethod.Get, requestUrl, headers, null, cancellationToken);
                var pageContainer = ReadObject(pageDocument.RootElement, "MediaContainer") ?? pageDocument.RootElement;
                var metadata = ReadArray(pageContainer, "Metadata").ToList();
                foreach (var element in metadata)
                {
                    var item = ParsePlexItem(element, library, credential.Binding.ServerId);
                    if (item is not null) items.Add(item);
                    if (items.Count >= MaximumItems) break;
                }
                progress?.Report(new PlaylistProgress(items.Count, $"Indexed {items.Count:N0} Plex items"));
                var total = ReadInt(pageContainer, "totalSize") ?? ReadInt(pageContainer, "size") ?? metadata.Count;
                if (metadata.Count == 0 || start + PageSize >= total) break;
            }
            if (items.Count >= MaximumItems) break;
        }
        return CreatePlaylist(credential, libraries, items);
    }

    private async Task<PlaylistResult> LoadEmbyCatalogAsync(
        MediaCenterCredential credential,
        IProgress<PlaylistProgress>? progress,
        CancellationToken cancellationToken)
    {
        credential = MediaCenterSecurity.ValidateCredential(credential);
        var identity = await ProbeEmbyIdentityAsync(credential.Binding.BaseUrl, cancellationToken);
        if (!string.Equals(identity.ServerId, credential.Binding.ServerId, StringComparison.Ordinal))
            throw new InvalidDataException("The Emby server identity does not match this protected credential.");
        var userId = credential.Binding.UserId
            ?? throw new InvalidDataException("The protected Emby credential has no user identifier.");
        var apiBase = MediaCenterSecurity.EmbyApiBaseUrl(credential.Binding.BaseUrl);
        progress?.Report(new PlaylistProgress(0, "Loading Emby libraries…"));
        using var libraryDocument = await SendJsonAsync(
            HttpMethod.Get,
            MediaCenterSecurity.ResolveServerPath(apiBase, $"/Users/{Uri.EscapeDataString(userId)}/Views"),
            EmbyHeaders(credential.AccessToken, userId),
            null,
            cancellationToken);
        var libraries = ReadArray(libraryDocument.RootElement, "Items")
            .Select(element => new MediaLibrary(
                MediaCenterSecurity.RequireIdentifier(ReadString(element, "Id") ?? string.Empty, "Emby library identifier"),
                ReadString(element, "Name") ?? "Emby library",
                ReadString(element, "CollectionType")?.ToLowerInvariant() ?? "other"))
            .ToList();
        var items = new List<CatalogMediaItem>();
        foreach (var library in libraries)
        {
            for (var start = 0; items.Count < MaximumItems; start += PageSize)
            {
                var requestUrl = MediaCenterSecurity.ResolveServerPath(apiBase, $"/Users/{Uri.EscapeDataString(userId)}/Items");
                requestUrl = AddQuery(requestUrl,
                    ("ParentId", library.Id),
                    ("Recursive", "true"),
                    ("IncludeItemTypes", "Movie,Episode,Video,MusicVideo,Recording,LiveTvChannel,Audio"),
                    ("Fields", "MediaSources,MediaStreams,Path,PrimaryImageAspectRatio,SortName,Overview"),
                    ("EnableImages", "true"),
                    ("EnableUserData", "true"),
                    ("StartIndex", start.ToString()),
                    ("Limit", PageSize.ToString()));
                using var pageDocument = await SendJsonAsync(
                    HttpMethod.Get,
                    requestUrl,
                    EmbyHeaders(credential.AccessToken, userId),
                    null,
                    cancellationToken);
                var pageItems = ReadArray(pageDocument.RootElement, "Items").ToList();
                foreach (var element in pageItems)
                {
                    var item = ParseEmbyItem(element, library, credential.Binding.ServerId);
                    if (item is not null) items.Add(item);
                    if (items.Count >= MaximumItems) break;
                }
                progress?.Report(new PlaylistProgress(items.Count, $"Indexed {items.Count:N0} Emby items"));
                var total = ReadInt(pageDocument.RootElement, "TotalRecordCount") ?? pageItems.Count;
                if (pageItems.Count == 0 || start + PageSize >= total) break;
            }
            if (items.Count >= MaximumItems) break;
        }
        return CreatePlaylist(credential, libraries, items);
    }

    private async Task<ResolvedMediaPlayback> ResolvePlexPlaybackAsync(
        MediaCenterLocator locator,
        MediaCenterCredential credential,
        long resumePositionMilliseconds,
        CancellationToken cancellationToken)
    {
        var identity = await ProbePlexIdentityAsync(credential.Binding.BaseUrl, cancellationToken);
        if (!string.Equals(identity.ServerId, locator.ServerId, StringComparison.Ordinal))
            throw new InvalidDataException("The Plex server identity does not match this protected item.");
        var url = MediaCenterSecurity.ResolveServerPath(
            credential.Binding.BaseUrl,
            $"/library/metadata/{Uri.EscapeDataString(locator.ItemId)}");
        url = AddQuery(url, ("includeMedia", "1"));
        using var document = await SendJsonAsync(HttpMethod.Get, url, PlexHeaders(credential.AccessToken), null, cancellationToken);
        var container = ReadObject(document.RootElement, "MediaContainer") ?? document.RootElement;
        var metadata = ReadArray(container, "Metadata").FirstOrDefault();
        if (metadata.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("Plex returned no playable metadata for this item.");
        var partPath = ReadArray(metadata, "Media")
            .SelectMany(media => ReadArray(media, "Part"))
            .Select(part => ReadString(part, "key"))
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        if (string.IsNullOrWhiteSpace(partPath))
            throw new InvalidDataException("Plex returned no direct-play media part for this item.");
        var playbackUrl = MediaCenterSecurity.ResolveServerPath(credential.Binding.BaseUrl, partPath);
        playbackUrl = MediaCenterSecurity.AddCredentialQuery(playbackUrl, "X-Plex-Token", credential.AccessToken);
        return new ResolvedMediaPlayback(playbackUrl, "direct-play", Math.Max(0, resumePositionMilliseconds));
    }

    private async Task<ResolvedMediaPlayback> ResolveEmbyPlaybackAsync(
        MediaCenterLocator locator,
        MediaCenterCredential credential,
        long resumePositionMilliseconds,
        CancellationToken cancellationToken)
    {
        var identity = await ProbeEmbyIdentityAsync(credential.Binding.BaseUrl, cancellationToken);
        if (!string.Equals(identity.ServerId, locator.ServerId, StringComparison.Ordinal))
            throw new InvalidDataException("The Emby server identity does not match this protected item.");
        var userId = credential.Binding.UserId
            ?? throw new InvalidDataException("The protected Emby credential has no user identifier.");
        var apiBase = MediaCenterSecurity.EmbyApiBaseUrl(credential.Binding.BaseUrl);
        var requestUrl = MediaCenterSecurity.ResolveServerPath(apiBase, $"/Items/{Uri.EscapeDataString(locator.ItemId)}/PlaybackInfo");
        requestUrl = AddQuery(requestUrl,
            ("UserId", userId),
            ("StartTimeTicks", (Math.Max(0, resumePositionMilliseconds) * 10_000).ToString()));
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            requestUrl,
            EmbyHeaders(credential.AccessToken, userId),
            null,
            cancellationToken);
        var source = ReadArray(document.RootElement, "MediaSources").FirstOrDefault();
        if (source.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("Emby returned no playable media source.");
        var sourceId = MediaCenterSecurity.RequireIdentifier(ReadString(source, "Id") ?? "default", "Emby media source identifier");
        var directPath = ReadString(source, "DirectStreamUrl");
        var transcodePath = ReadString(source, "TranscodingUrl");
        var supportsDirectPlay = ReadBool(source, "SupportsDirectPlay");
        var supportsDirectStream = ReadBool(source, "SupportsDirectStream");
        var supportsTranscode = ReadBool(source, "SupportsTranscoding");
        string method;
        string playbackUrl;
        if (supportsDirectPlay || supportsDirectStream)
        {
            method = supportsDirectPlay ? "direct-play" : "direct-stream";
            if (!string.IsNullOrWhiteSpace(directPath))
            {
                playbackUrl = MediaCenterSecurity.ResolveServerPath(apiBase, directPath);
            }
            else
            {
                var container = SafeContainer(ReadString(source, "Container"));
                playbackUrl = MediaCenterSecurity.ResolveServerPath(apiBase, $"/Videos/{Uri.EscapeDataString(locator.ItemId)}/stream.{container}");
                playbackUrl = AddQuery(playbackUrl,
                    ("MediaSourceId", sourceId),
                    ("PlaySessionId", ReadString(document.RootElement, "PlaySessionId") ?? Guid.NewGuid().ToString("N")),
                    ("Static", "true"));
            }
        }
        else if (supportsTranscode && !string.IsNullOrWhiteSpace(transcodePath))
        {
            method = "transcode";
            playbackUrl = MediaCenterSecurity.ResolveServerPath(apiBase, transcodePath);
        }
        else
        {
            throw new InvalidDataException("Emby did not provide a supported direct-play or transcode path.");
        }
        var referrer = ReadObject(source, "RequiredHttpHeaders") is { } requiredHeaders
            ? SanitizeHeaderValue(ReadString(requiredHeaders, "Referer") ?? ReadString(requiredHeaders, "Referrer"))
            : null;
        playbackUrl = MediaCenterSecurity.AddCredentialQuery(playbackUrl, "api_key", credential.AccessToken);
        return new ResolvedMediaPlayback(playbackUrl, method, Math.Max(0, resumePositionMilliseconds), referrer);
    }

    private async Task<MediaCenterServerIdentity> ProbePlexIdentityAsync(string baseUrl, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            MediaCenterSecurity.ResolveServerPath(baseUrl, "/identity"),
            PlexHeaders(null),
            null,
            cancellationToken);
        var container = ReadObject(document.RootElement, "MediaContainer") ?? document.RootElement;
        var serverId = MediaCenterSecurity.RequireIdentifier(
            ReadString(container, "machineIdentifier") ?? string.Empty,
            "Plex server identifier");
        return new MediaCenterServerIdentity(
            serverId,
            ReadString(container, "friendlyName") ?? "Plex",
            ReadString(container, "version"));
    }

    private async Task<MediaCenterServerIdentity> ProbeEmbyIdentityAsync(string baseUrl, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            MediaCenterSecurity.ResolveServerPath(MediaCenterSecurity.EmbyApiBaseUrl(baseUrl), "/System/Info/Public"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Accept"] = "application/json" },
            null,
            cancellationToken);
        var serverId = MediaCenterSecurity.RequireIdentifier(
            ReadString(document.RootElement, "Id") ?? ReadString(document.RootElement, "ServerId") ?? string.Empty,
            "Emby server identifier");
        return new MediaCenterServerIdentity(
            serverId,
            ReadString(document.RootElement, "ServerName") ?? "Emby",
            ReadString(document.RootElement, "Version"));
    }

    private PlaylistResult CreatePlaylist(
        MediaCenterCredential credential,
        IReadOnlyCollection<MediaLibrary> libraries,
        IReadOnlyCollection<CatalogMediaItem> items)
    {
        var libraryNames = libraries.ToDictionary(library => library.Id, library => library.Title, StringComparer.Ordinal);
        var channels = new List<ChannelItem>(items.Count);
        foreach (var item in items)
        {
            var kind = ToChannelKind(item.Kind);
            if (kind is null) continue;
            var group = !string.IsNullOrWhiteSpace(item.SeriesTitle)
                ? item.SeriesTitle
                : libraryNames.GetValueOrDefault(item.LibraryId) ?? item.LibraryTitle;
            channels.Add(new ChannelItem
            {
                Number = channels.Count + 1,
                Name = DisplayTitle(item),
                Url = MediaCenterSecurity.BuildPlaybackLocator(
                    credential.Binding.Provider,
                    credential.Binding.ServerId,
                    item.Id),
                Group = string.IsNullOrWhiteSpace(group) ? ProviderLabel(credential.Binding.Provider) : group,
                Kind = kind.Value,
                DurationMilliseconds = Math.Max(0, item.DurationMilliseconds),
                ResumePositionMilliseconds = Math.Max(0, item.ResumePositionMilliseconds),
                IsPlayed = item.Played
            });
        }
        return new PlaylistResult(
            channels,
            $"{credential.DisplayName} • {ProviderLabel(credential.Binding.Provider)}",
            MediaCenterSecurity.SafeDisplayLocation(credential.Binding.BaseUrl),
            DateTimeOffset.Now);
    }

    private static CatalogMediaItem? ParsePlexItem(JsonElement element, MediaLibrary library, string serverId)
    {
        var id = ReadString(element, "ratingKey");
        var title = ReadString(element, "title");
        var kind = ReadString(element, "type")?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || kind is not ("movie" or "episode" or "clip" or "track")) return null;
        id = MediaCenterSecurity.RequireIdentifier(id, "Plex item identifier");
        return new CatalogMediaItem(
            id,
            serverId,
            library.Id,
            library.Title,
            kind,
            title,
            ReadString(element, "grandparentTitle"),
            ReadInt(element, "parentIndex"),
            ReadInt(element, "index"),
            ReadLong(element, "duration") ?? 0,
            ReadLong(element, "viewOffset") ?? 0,
            (ReadInt(element, "viewCount") ?? 0) > 0);
    }

    private static CatalogMediaItem? ParseEmbyItem(JsonElement element, MediaLibrary library, string serverId)
    {
        var id = ReadString(element, "Id");
        var title = ReadString(element, "Name");
        var kind = ReadString(element, "Type")?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || kind is not ("movie" or "episode" or "video" or "musicvideo" or "recording" or "livetvchannel" or "audio")) return null;
        id = MediaCenterSecurity.RequireIdentifier(id, "Emby item identifier");
        var userData = ReadObject(element, "UserData");
        var durationTicks = ReadLong(element, "RunTimeTicks") ?? 0;
        var resumeTicks = userData is null ? 0 : ReadLong(userData.Value, "PlaybackPositionTicks") ?? 0;
        return new CatalogMediaItem(
            id,
            serverId,
            library.Id,
            library.Title,
            kind,
            title,
            ReadString(element, "SeriesName"),
            ReadInt(element, "ParentIndexNumber"),
            ReadInt(element, "IndexNumber"),
            Math.Max(0, durationTicks / 10_000),
            Math.Max(0, resumeTicks / 10_000),
            userData is not null && ReadBool(userData.Value, "Played"));
    }

    private static ChannelKind? ToChannelKind(string kind) => kind switch
    {
        "movie" or "clip" or "video" or "musicvideo" => ChannelKind.Movie,
        "episode" => ChannelKind.Series,
        "recording" => ChannelKind.Recording,
        "livetvchannel" => ChannelKind.Live,
        _ => null
    };

    private static string DisplayTitle(CatalogMediaItem item)
    {
        if (item.Kind != "episode") return item.Title;
        var season = item.SeasonNumber is null ? string.Empty : $"S{item.SeasonNumber:00}";
        var episode = item.EpisodeNumber is null ? string.Empty : $"E{item.EpisodeNumber:00}";
        var prefix = $"{season}{episode}";
        return prefix.Length == 0 ? item.Title : $"{prefix} • {item.Title}";
    }

    private static MediaCenterCredential CreateCredential(
        string provider,
        string serverId,
        string baseUrl,
        string accessToken,
        string displayName,
        bool allowInsecureHttp,
        string? userId = null)
    {
        var credentialId = MediaCenterSecurity.CreateCredentialId(provider, serverId, baseUrl, userId);
        return MediaCenterSecurity.ValidateCredential(new MediaCenterCredential
        {
            Binding = new MediaCenterCredentialBinding
            {
                Provider = provider,
                ServerId = serverId,
                BaseUrl = baseUrl,
                CredentialId = credentialId,
                UserId = userId,
                AllowInsecureHttp = allowInsecureHttp
            },
            AccessToken = accessToken,
            DisplayName = displayName
        });
    }

    private Dictionary<string, string> PlexHeaders(string? token)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "application/json",
            ["X-Plex-Client-Identifier"] = _deviceId,
            ["X-Plex-Product"] = "StreamVue",
            ["X-Plex-Version"] = ClientVersion,
            ["X-Plex-Provides"] = "controller"
        };
        if (!string.IsNullOrWhiteSpace(token)) headers["X-Plex-Token"] = token;
        return headers;
    }

    private Dictionary<string, string> EmbyHeaders(string? token, string? userId)
    {
        var authorization = new List<KeyValuePair<string, string>>
        {
            new("Client", "StreamVue"),
            new("Device", "Windows PC"),
            new("DeviceId", _deviceId),
            new("Version", ClientVersion)
        };
        if (!string.IsNullOrWhiteSpace(userId)) authorization.Add(new("UserId", userId));
        if (!string.IsNullOrWhiteSpace(token)) authorization.Add(new("Token", token));
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "application/json",
            ["X-Emby-Authorization"] = $"Emby {string.Join(", ", authorization.Select(pair => $"{pair.Key}=\"{SanitizeHeaderValue(pair.Value)}\""))}"
        };
        if (!string.IsNullOrWhiteSpace(token)) headers["X-Emby-Token"] = token;
        return headers;
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string url,
        IReadOnlyDictionary<string, string> headers,
        string? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        foreach (var (name, value) in headers)
        {
            if (!HeaderNamePattern().IsMatch(name) || ContainsAny(value, '\r', '\n'))
                throw new InvalidDataException("A media-center request header is invalid.");
            request.Headers.TryAddWithoutValidation(name, value);
        }
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new InvalidDataException("The media server returned an oversized response.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaximumResponseBytes)
                throw new InvalidDataException("The media server returned an oversized response.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        output.Position = 0;
        try
        {
            return await JsonDocument.ParseAsync(output, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The media server returned invalid JSON.", exception);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StreamVue", ClientVersion));
        return client;
    }

    private static string ResolveDeviceId()
    {
        var path = StreamVueDataPaths.Resolve("media-center-device-id.v1.txt");
        try
        {
            if (File.Exists(path))
            {
                var saved = File.ReadAllText(path).Trim();
                if (saved.Length > 0) return MediaCenterSecurity.RequireIdentifier(saved, "media-center device identifier");
            }
            var generated = $"streamvue-win-{Guid.NewGuid():N}";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, generated);
            return generated;
        }
        catch
        {
            var fallback = $"{Environment.MachineName}|{Environment.UserName}";
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(fallback))).ToLowerInvariant();
            return $"streamvue-win-{hash[..40]}";
        }
    }

    private static string NormalizeDisplayName(string? requested, params string?[] fallbacks)
    {
        var value = new[] { requested }.Concat(fallbacks).FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim() ?? "Media library";
        if (value.Length > 256) value = value[..256];
        return value;
    }

    private static string ProviderLabel(string provider) => provider == "plex" ? "Plex" : "Emby";

    private static string SafeContainer(string? value)
    {
        var candidate = value?.Split(',')[0].Trim().ToLowerInvariant();
        return candidate is not null && ContainerPattern().IsMatch(candidate) ? candidate : "mkv";
    }

    private static string? SanitizeHeaderValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sanitized = value.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\"", string.Empty).Trim();
        return sanitized.Length == 0 ? null : sanitized[..Math.Min(512, sanitized.Length)];
    }

    private void RequirePremiumAccess() =>
        PremiumAccessPolicy.RequireMediaCenters(_premiumAccessProvider());

    private static string AddQuery(string url, params (string Name, string Value)[] values)
    {
        var builder = new UriBuilder(url);
        var pairs = builder.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Select(parts => new KeyValuePair<string, string>(
                Uri.UnescapeDataString(parts[0]),
                parts.Length == 1 ? string.Empty : Uri.UnescapeDataString(parts[1])))
            .ToList();
        foreach (var (name, value) in values)
        {
            pairs.RemoveAll(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            pairs.Add(new KeyValuePair<string, string>(name, value));
        }
        builder.Query = string.Join("&", pairs.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return builder.Uri.ToString();
    }

    private static JsonElement? ReadObject(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Object) return value;
        if (value.ValueKind == JsonValueKind.Array)
        {
            var first = value.EnumerateArray().FirstOrDefault();
            return first.ValueKind == JsonValueKind.Object ? first : null;
        }
        return null;
    }

    private static IEnumerable<JsonElement> ReadArray(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value)) return [];
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        var value = ReadLong(element, name);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
    }

    private static long? ReadLong(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number) ? number : null;
    }

    private static bool ReadBool(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value)) return false;
        return value.ValueKind == JsonValueKind.True ||
               value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result) && result ||
               value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number != 0;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out value)) return true;
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderNamePattern();

    [GeneratedRegex("^[a-z0-9]{1,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContainerPattern();

    private static bool ContainsAny(string value, params char[] characters) => value.IndexOfAny(characters) >= 0;

    private sealed record MediaLibrary(string Id, string Title, string Kind);

    private sealed record CatalogMediaItem(
        string Id,
        string ServerId,
        string LibraryId,
        string LibraryTitle,
        string Kind,
        string Title,
        string? SeriesTitle,
        int? SeasonNumber,
        int? EpisodeNumber,
        long DurationMilliseconds,
        long ResumePositionMilliseconds,
        bool Played);
}
