using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OrbitalVue.Player.Models;

namespace OrbitalVue.Player.Services;

public static partial class MediaCenterSecurity
{
    public const string ContractVersion = "1.0";

    private static readonly HashSet<string> SupportedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "plex", "emby"
    };

    private static readonly HashSet<string> SensitiveQueryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "api_key", "api-key", "apikey", "access_token", "auth", "password", "pw", "token",
        "username", "x-emby-authorization", "x-emby-token", "x-plex-token"
    };

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    public static string NormalizeProvider(string provider)
    {
        var normalized = provider.Trim().ToLowerInvariant();
        if (!SupportedProviders.Contains(normalized))
            throw new ArgumentException("The media-center provider is not supported.", nameof(provider));
        return normalized;
    }

    public static string NormalizeBaseUrl(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length == 0) throw new ArgumentException("Enter a media-center server address.", nameof(input));
        if (!trimmed.Contains("://", StringComparison.Ordinal)) trimmed = $"https://{trimmed}";
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("Media-center servers must use HTTP or HTTPS.", nameof(input));
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Do not put credentials in the media-center server address.", nameof(input));
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("The media-center server address cannot contain a query or fragment.", nameof(input));

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/'),
            Query = string.Empty,
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty
        };
        return builder.Uri.ToString().TrimEnd('/');
    }

    public static void RequireAllowedTransport(string baseUrl, bool allowInsecureHttp)
    {
        var uri = new Uri(NormalizeBaseUrl(baseUrl));
        if (uri.Scheme == Uri.UriSchemeHttp && !allowInsecureHttp)
            throw new ArgumentException("This media server uses unencrypted HTTP. Confirm the trusted local connection before saving credentials.");
    }

    public static string SafeDisplayLocation(string baseUrl)
    {
        var uri = new Uri(NormalizeBaseUrl(baseUrl));
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        var path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath.TrimEnd('/');
        return $"{uri.IdnHost}{port}{path}";
    }

    public static string RequireIdentifier(string value, string label)
    {
        var trimmed = value.Trim();
        if (!IdentifierPattern().IsMatch(trimmed))
            throw new InvalidDataException($"The {label} is not a safe identifier.");
        return trimmed;
    }

    public static string CreateCredentialId(string provider, string serverId, string baseUrl, string? userId = null)
    {
        var identity = $"{NormalizeProvider(provider)}|{RequireIdentifier(serverId, "server identifier")}|{NormalizeBaseUrl(baseUrl)}|{userId ?? "server"}";
        return $"mc-{NormalizeProvider(provider)}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..48].ToLowerInvariant()}";
    }

    public static MediaCenterCredential ValidateCredential(MediaCenterCredential credential)
    {
        if (credential.SchemaVersion != 1 || credential.Binding.ContractVersion != ContractVersion)
            throw new InvalidDataException("The protected media-center credential version is not supported.");
        var provider = NormalizeProvider(credential.Binding.Provider);
        var baseUrl = NormalizeBaseUrl(credential.Binding.BaseUrl);
        RequireAllowedTransport(baseUrl, credential.Binding.AllowInsecureHttp);
        var serverId = RequireIdentifier(credential.Binding.ServerId, "media-center server identifier");
        var credentialId = RequireIdentifier(credential.Binding.CredentialId, "secure credential reference");
        var userId = credential.Binding.UserId is null
            ? null
            : RequireIdentifier(credential.Binding.UserId, "media-center user identifier");
        var expectedCredentialId = CreateCredentialId(provider, serverId, baseUrl, userId);
        if (!string.Equals(credentialId, expectedCredentialId, StringComparison.Ordinal))
            throw new InvalidDataException("The protected media-center credential reference does not match its server binding.");
        if (string.IsNullOrWhiteSpace(credential.AccessToken) || credential.AccessToken.Length > 16_384 || credential.AccessToken.ContainsAny('\r', '\n'))
            throw new InvalidDataException("The protected media-center token is invalid.");
        var displayName = credential.DisplayName.Trim();
        if (displayName.Length is 0 or > 256)
            throw new InvalidDataException("The protected media-center name is invalid.");
        return credential with
        {
            Binding = credential.Binding with
            {
                Provider = provider,
                ServerId = serverId,
                BaseUrl = baseUrl,
                CredentialId = credentialId,
                UserId = userId
            },
            DisplayName = displayName
        };
    }

    public static void AssertCredentialBinding(
        MediaCenterCredential credential,
        string provider,
        string baseUrl,
        string? serverId = null)
    {
        var validated = ValidateCredential(credential);
        if (!string.Equals(validated.Binding.Provider, NormalizeProvider(provider), StringComparison.Ordinal) ||
            !string.Equals(validated.Binding.BaseUrl, NormalizeBaseUrl(baseUrl), StringComparison.OrdinalIgnoreCase) ||
            serverId is not null && !string.Equals(validated.Binding.ServerId, RequireIdentifier(serverId, "media-center server identifier"), StringComparison.Ordinal))
            throw new InvalidDataException("The protected media-center credential does not belong to this server connection.");
    }

    public static string BuildPlaybackLocator(string provider, string serverId, string itemId)
    {
        var safeProvider = NormalizeProvider(provider);
        var safeServerId = RequireIdentifier(serverId, "media-center server identifier");
        var safeItemId = RequireIdentifier(itemId, "media-center item identifier");
        return $"orbitalvue-media://{safeProvider}/{Uri.EscapeDataString(safeServerId)}/{Uri.EscapeDataString(safeItemId)}";
    }

    public static MediaCenterLocator ParsePlaybackLocator(string value)
    {
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "orbitalvue-media", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("This is not an OrbitalVue media-center playback address.");
        var provider = NormalizeProvider(uri.Host);
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (parts.Length != 2) throw new InvalidDataException("The media-center playback address is incomplete.");
        var locator = new MediaCenterLocator(
            provider,
            RequireIdentifier(parts[0], "media-center server identifier"),
            RequireIdentifier(parts[1], "media-center item identifier"));
        if (!string.Equals(value, BuildPlaybackLocator(locator.Provider, locator.ServerId, locator.ItemId), StringComparison.Ordinal))
            throw new InvalidDataException("The media-center playback address is not canonical.");
        return locator;
    }

    public static bool IsPlaybackLocator(string? value) =>
        value?.StartsWith("orbitalvue-media://", StringComparison.Ordinal) == true;

    public static string BuildArtworkLocator(string provider, string serverId, string itemId, string? versionTag = null)
    {
        var safeProvider = NormalizeProvider(provider);
        var safeServerId = RequireIdentifier(serverId, "media-center server identifier");
        var safeItemId = RequireIdentifier(itemId, "media-center item identifier");
        var safeVersionTag = string.IsNullOrWhiteSpace(versionTag)
            ? null
            : RequireIdentifier(versionTag, "media-center artwork version");
        var suffix = safeVersionTag is null ? string.Empty : $"/{Uri.EscapeDataString(safeVersionTag)}";
        return $"orbitalvue-artwork://{safeProvider}/{Uri.EscapeDataString(safeServerId)}/{Uri.EscapeDataString(safeItemId)}{suffix}";
    }

    public static MediaCenterArtworkLocator ParseArtworkLocator(string value)
    {
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "orbitalvue-artwork", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("This is not an OrbitalVue media-center artwork address.");
        var provider = NormalizeProvider(uri.Host);
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (parts.Length is not (2 or 3)) throw new InvalidDataException("The media-center artwork address is incomplete.");
        var locator = new MediaCenterArtworkLocator(
            provider,
            RequireIdentifier(parts[0], "media-center server identifier"),
            RequireIdentifier(parts[1], "media-center item identifier"),
            parts.Length == 3 ? RequireIdentifier(parts[2], "media-center artwork version") : null);
        if (!string.Equals(value, BuildArtworkLocator(locator.Provider, locator.ServerId, locator.ItemId, locator.VersionTag), StringComparison.Ordinal))
            throw new InvalidDataException("The media-center artwork address is not canonical.");
        return locator;
    }

    public static bool IsArtworkLocator(string? value) =>
        value?.StartsWith("orbitalvue-artwork://", StringComparison.Ordinal) == true;

    public static string ResolveServerPath(string baseUrl, string path)
    {
        var normalizedBase = NormalizeBaseUrl(baseUrl);
        if (path.Contains('\\')) throw new InvalidDataException("The media server returned an invalid resource path.");
        var root = new Uri($"{normalizedBase}/", UriKind.Absolute);
        var resolved = new Uri(root, path.TrimStart('/'));
        if (!SameOrigin(root, resolved))
            throw new InvalidDataException("The media server returned a cross-origin playback address.");
        var query = ParseQuery(resolved.Query)
            .Where(pair => !SensitiveQueryNames.Contains(pair.Key))
            .ToList();
        var builder = new UriBuilder(resolved)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Fragment = string.Empty,
            Query = BuildQuery(query)
        };
        var result = builder.Uri.ToString();
        if (result.Length > 8_192) throw new InvalidDataException("The media server returned an excessively long resource path.");
        return result;
    }

    public static string AddCredentialQuery(string url, string name, string token)
    {
        if (!SensitiveQueryNames.Contains(name)) throw new InvalidOperationException("Only provider credential query parameters can be materialized.");
        var uri = new Uri(url, UriKind.Absolute);
        var query = ParseQuery(uri.Query).Where(pair => !pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        query.Add(new KeyValuePair<string, string>(name, token));
        return new UriBuilder(uri) { Query = BuildQuery(query) }.Uri.ToString();
    }

    public static string EmbyApiBaseUrl(string baseUrl)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        return normalized.EndsWith("/emby", StringComparison.OrdinalIgnoreCase) ? normalized : $"{normalized}/emby";
    }

    public static bool SameSource(string provider, string baseUrl, MediaCenterCredential credential) =>
        string.Equals(NormalizeProvider(provider), credential.Binding.Provider, StringComparison.Ordinal) &&
        string.Equals(NormalizeBaseUrl(baseUrl), credential.Binding.BaseUrl, StringComparison.OrdinalIgnoreCase);

    private static bool SameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    private static List<KeyValuePair<string, string>> ParseQuery(string query)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var rawKey = separator < 0 ? part : part[..separator];
            var rawValue = separator < 0 ? string.Empty : part[(separator + 1)..];
            result.Add(new KeyValuePair<string, string>(
                Uri.UnescapeDataString(rawKey.Replace('+', ' ')),
                Uri.UnescapeDataString(rawValue.Replace('+', ' '))));
        }
        return result;
    }

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> values) => string.Join("&", values.Select(pair =>
        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static bool ContainsAny(this string value, params char[] characters) => value.IndexOfAny(characters) >= 0;
}
