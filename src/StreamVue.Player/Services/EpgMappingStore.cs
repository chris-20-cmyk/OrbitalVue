using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StreamVue.Player.Services;

public sealed class EpgMappingStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StreamVue.EpgMappings.v1");
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EpgMappingStore(string? path = null)
    {
        _path = path ?? StreamVueDataPaths.Resolve("epg-mappings.v1.bin");
    }

    public async Task SaveAsync(
        string playlistType,
        string playlistSource,
        IReadOnlyDictionary<string, string> mappings,
        CancellationToken cancellationToken = default)
    {
        var record = new StoredMappings(
            playlistType,
            NormalizePlaylistSource(playlistType, playlistSource),
            new Dictionary<string, string>(mappings, StringComparer.Ordinal));
        var clearBytes = JsonSerializer.SerializeToUtf8Bytes(record);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var protectedBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> TryLoadAsync(
        string playlistType,
        string playlistSource,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return new Dictionary<string, string>(StringComparer.Ordinal);
            var protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var stored = JsonSerializer.Deserialize<StoredMappings>(clearBytes);
                if (stored is null ||
                    !stored.PlaylistType.Equals(playlistType, StringComparison.OrdinalIgnoreCase) ||
                    !stored.PlaylistSource.Equals(NormalizePlaylistSource(playlistType, playlistSource), StringComparison.OrdinalIgnoreCase))
                {
                    return new Dictionary<string, string>(StringComparer.Ordinal);
                }

                return new Dictionary<string, string>(stored.Mappings, StringComparer.Ordinal);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearBytes);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string NormalizePlaylistSource(string playlistType, string source)
    {
        if (!playlistType.Equals("file", StringComparison.OrdinalIgnoreCase)) return source.Trim();
        try { return Path.GetFullPath(source); }
        catch { return source.Trim(); }
    }

    private sealed record StoredMappings(
        string PlaylistType,
        string PlaylistSource,
        Dictionary<string, string> Mappings);
}
