using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public sealed record CachedPlaylist(PlaylistResult Playlist, DateTimeOffset CachedAt);

public sealed class PlaylistCacheStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StreamVue.PlaylistCache.v1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _cachePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PlaylistCacheStore(string? cachePath = null)
    {
        _cachePath = cachePath ?? StreamVueDataPaths.Resolve("playlist-cache.v1.bin");
    }

    public async Task SaveAsync(string sourceType, string sourceValue, PlaylistResult playlist, CancellationToken cancellationToken = default)
    {
        var envelope = new PlaylistCacheEnvelope
        {
            SourceKey = BuildSourceKey(sourceType, sourceValue),
            DisplayName = playlist.DisplayName,
            GuideSource = playlist.GuideSource,
            LoadedAt = playlist.LoadedAt,
            CachedAt = DateTimeOffset.UtcNow,
            Channels = playlist.Channels.Select(channel => new CachedChannel
            {
                Number = channel.Number,
                Name = channel.Name,
                Url = channel.Url,
                Group = channel.Group,
                LogoUrl = channel.LogoUrl,
                TvgId = channel.TvgId,
                TvgName = channel.TvgName,
                UserAgent = channel.UserAgent,
                Referrer = channel.Referrer,
                Kind = channel.Kind
            }).ToList()
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var compressed = new MemoryStream();
            await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                await JsonSerializer.SerializeAsync(gzip, envelope, JsonOptions, cancellationToken);
            }

            var protectedBytes = ProtectedData.Protect(compressed.ToArray(), Entropy, DataProtectionScope.CurrentUser);
            var directory = Path.GetDirectoryName(_cachePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _cachePath + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
            File.Move(temporaryPath, _cachePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CachedPlaylist?> TryLoadAsync(string sourceType, string sourceValue, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_cachePath)) return null;
            var protectedBytes = await File.ReadAllBytesAsync(_cachePath, cancellationToken);
            var compressedBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            await using var compressed = new MemoryStream(compressedBytes, writable: false);
            await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            var envelope = await JsonSerializer.DeserializeAsync<PlaylistCacheEnvelope>(gzip, JsonOptions, cancellationToken);
            if (envelope is null ||
                envelope.Channels.Count == 0 ||
                !string.Equals(envelope.SourceKey, BuildSourceKey(sourceType, sourceValue), StringComparison.Ordinal))
            {
                return null;
            }

            var channels = envelope.Channels.Select(channel => new ChannelItem
            {
                Number = channel.Number,
                Name = channel.Name,
                Url = channel.Url,
                Group = channel.Group,
                LogoUrl = channel.LogoUrl,
                TvgId = channel.TvgId,
                TvgName = channel.TvgName,
                UserAgent = channel.UserAgent,
                Referrer = channel.Referrer,
                Kind = channel.Kind
            }).ToList();

            return new CachedPlaylist(
                new PlaylistResult(channels, envelope.DisplayName, "encrypted local cache", envelope.LoadedAt, envelope.GuideSource),
                envelope.CachedAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string BuildSourceKey(string sourceType, string sourceValue)
    {
        var normalizedType = sourceType.Trim().ToLowerInvariant();
        var normalizedSource = sourceValue.Trim();
        if (normalizedType == "file")
        {
            try
            {
                normalizedSource = Path.GetFullPath(normalizedSource);
            }
            catch
            {
                // The original value still produces a stable cache key for an invalid or removed path.
            }
        }

        normalizedSource = normalizedSource.TrimEnd('/', '\\').ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{normalizedType}|{normalizedSource}")));
    }

    private sealed class PlaylistCacheEnvelope
    {
        public string SourceKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? GuideSource { get; set; }
        public DateTimeOffset LoadedAt { get; set; }
        public DateTimeOffset CachedAt { get; set; }
        public List<CachedChannel> Channels { get; set; } = [];
    }

    private sealed class CachedChannel
    {
        public int Number { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Group { get; set; } = "Uncategorized";
        public string? LogoUrl { get; set; }
        public string? TvgId { get; set; }
        public string? TvgName { get; set; }
        public string? UserAgent { get; set; }
        public string? Referrer { get; set; }
        public ChannelKind Kind { get; set; }
    }
}
