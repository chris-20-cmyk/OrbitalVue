using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public sealed class EpgCacheStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StreamVue.EpgCache.v1");
    private readonly string _cachePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EpgCacheStore(string? cachePath = null)
    {
        _cachePath = cachePath ?? StreamVueDataPaths.Resolve("epg-cache.v1.bin");
    }

    public async Task SaveAsync(string source, EpgSchedule schedule, CancellationToken cancellationToken = default)
    {
        var envelope = new CacheEnvelope
        {
            SourceKey = BuildSourceKey(source),
            DisplayName = schedule.DisplayName,
            LoadedAt = schedule.LoadedAt,
            Aliases = new Dictionary<string, string>(schedule.Aliases, StringComparer.Ordinal),
            ChannelCatalog = new Dictionary<string, string>(schedule.ChannelCatalog, StringComparer.Ordinal),
            Programmes = schedule.ProgrammesByChannel.Values.SelectMany(programmes => programmes).Select(programme => new CachedProgramme
            {
                ChannelId = programme.ChannelId,
                Title = programme.Title,
                Description = programme.Description,
                Category = programme.Category,
                Start = programme.Start,
                Stop = programme.Stop,
                EpisodeId = programme.EpisodeId,
                SeasonNumber = programme.SeasonNumber,
                EpisodeNumber = programme.EpisodeNumber,
                IsNewEpisode = programme.IsNewEpisode
            }).ToList()
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var compressed = new MemoryStream();
            await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                await JsonSerializer.SerializeAsync(gzip, envelope, cancellationToken: cancellationToken);
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

    public async Task<EpgSchedule?> TryLoadAsync(string source, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_cachePath)) return null;
            var protectedBytes = await File.ReadAllBytesAsync(_cachePath, cancellationToken);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            await using var compressed = new MemoryStream(clearBytes, writable: false);
            await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            var envelope = await JsonSerializer.DeserializeAsync<CacheEnvelope>(gzip, cancellationToken: cancellationToken);
            if (envelope is null || envelope.SourceKey != BuildSourceKey(source) || envelope.Programmes.Count == 0) return null;

            var programmes = envelope.Programmes
                .Select(programme => new EpgProgram(
                    programme.ChannelId,
                    programme.Title,
                    programme.Description,
                    programme.Category,
                    programme.Start,
                    programme.Stop,
                    programme.EpisodeId,
                    programme.SeasonNumber,
                    programme.EpisodeNumber,
                    programme.IsNewEpisode))
                .GroupBy(programme => EpgSchedule.NormalizeKey(programme.ChannelId), StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<EpgProgram>)group.OrderBy(programme => programme.Start).ToList(),
                    StringComparer.Ordinal);
            return new EpgSchedule(programmes, envelope.Aliases, envelope.DisplayName, envelope.LoadedAt, envelope.ChannelCatalog);
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

    private static string BuildSourceKey(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Trim().ToUpperInvariant())));

    private sealed class CacheEnvelope
    {
        public string SourceKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public DateTimeOffset LoadedAt { get; set; }
        public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> ChannelCatalog { get; set; } = new(StringComparer.Ordinal);
        public List<CachedProgramme> Programmes { get; set; } = [];
    }

    private sealed class CachedProgramme
    {
        public string ChannelId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Category { get; set; }
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset Stop { get; set; }
        public string? EpisodeId { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public bool? IsNewEpisode { get; set; }
    }
}
