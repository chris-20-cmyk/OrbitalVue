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

    private readonly string _legacyCachePath;
    private readonly string? _cacheDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PlaylistCacheStore(string? cachePath = null, string? cacheDirectory = null)
    {
        _legacyCachePath = cachePath ?? StreamVueDataPaths.Resolve("playlist-cache.v1.bin");
        _cacheDirectory = cacheDirectory ?? (cachePath is null ? StreamVueDataPaths.Resolve("playlist-caches.v2") : null);
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
                CatchupMode = channel.CatchupMode,
                CatchupSource = channel.CatchupSource,
                CatchupDays = channel.CatchupDays,
                CatchupCorrectionMinutes = channel.CatchupCorrectionMinutes,
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

            var clearBytes = compressed.ToArray();
            try
            {
                var protectedBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
                var cachePath = ResolveCachePath(sourceType, sourceValue);
                var directory = Path.GetDirectoryName(cachePath)!;
                Directory.CreateDirectory(directory);
                var temporaryPath = cachePath + ".tmp";
                await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
                File.Move(temporaryPath, cachePath, overwrite: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearBytes);
            }
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
            var sourceKey = BuildSourceKey(sourceType, sourceValue);
            var primaryPath = ResolveCachePath(sourceType, sourceValue);
            var cached = await TryReadAsync(primaryPath, sourceKey, cancellationToken);
            if (cached is not null || string.Equals(primaryPath, _legacyCachePath, StringComparison.OrdinalIgnoreCase))
                return cached;
            return await TryReadAsync(_legacyCachePath, sourceKey, cancellationToken);
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

    public async Task DeleteAsync(string sourceType, string sourceValue, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var cachePath = ResolveCachePath(sourceType, sourceValue);
            if (File.Exists(cachePath)) File.Delete(cachePath);
            if (!string.Equals(cachePath, _legacyCachePath, StringComparison.OrdinalIgnoreCase))
            {
                var sourceKey = BuildSourceKey(sourceType, sourceValue);
                if (await TryReadAsync(_legacyCachePath, sourceKey, cancellationToken) is not null)
                    File.Delete(_legacyCachePath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CachedPlaylist?> TryReadAsync(string cachePath, string sourceKey, CancellationToken cancellationToken)
    {
        if (!File.Exists(cachePath)) return null;
        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(cachePath, cancellationToken);
            var compressedBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                await using var compressed = new MemoryStream(compressedBytes, writable: false);
                await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
                var envelope = await JsonSerializer.DeserializeAsync<PlaylistCacheEnvelope>(gzip, JsonOptions, cancellationToken);
                if (envelope is null || envelope.Channels.Count == 0 ||
                    !string.Equals(envelope.SourceKey, sourceKey, StringComparison.Ordinal))
                    return null;

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
                    CatchupMode = channel.CatchupMode,
                    CatchupSource = channel.CatchupSource,
                    CatchupDays = channel.CatchupDays,
                    CatchupCorrectionMinutes = channel.CatchupCorrectionMinutes,
                    Kind = channel.Kind
                }).ToList();
                return new CachedPlaylist(
                    new PlaylistResult(channels, envelope.DisplayName, "encrypted local cache", envelope.LoadedAt, envelope.GuideSource),
                    envelope.CachedAt);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(compressedBytes);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private string ResolveCachePath(string sourceType, string sourceValue) => _cacheDirectory is null
        ? _legacyCachePath
        : Path.Combine(_cacheDirectory, $"{BuildSourceKey(sourceType, sourceValue)}.bin");

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
        public string? CatchupMode { get; set; }
        public string? CatchupSource { get; set; }
        public int CatchupDays { get; set; }
        public int CatchupCorrectionMinutes { get; set; }
        public ChannelKind Kind { get; set; }
    }
}
