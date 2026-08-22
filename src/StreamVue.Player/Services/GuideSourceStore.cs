using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StreamVue.Player.Services;

public sealed class GuideSourceStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StreamVue.GuideSource.v1");
    private readonly string _path;

    public GuideSourceStore(string? path = null)
    {
        _path = path ?? StreamVueDataPaths.Resolve("guide-source.v1.bin");
    }

    public async Task SaveAsync(string playlistType, string playlistSource, string guideSource, CancellationToken cancellationToken = default)
    {
        var record = new StoredGuideSource(playlistType, NormalizePlaylistSource(playlistType, playlistSource), guideSource);
        var clearBytes = JsonSerializer.SerializeToUtf8Bytes(record);
        try
        {
            var protectedBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _path + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    public async Task<string?> TryLoadAsync(string playlistType, string playlistSource, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var stored = JsonSerializer.Deserialize<StoredGuideSource>(clearBytes);
                return stored is not null &&
                       stored.PlaylistType.Equals(playlistType, StringComparison.OrdinalIgnoreCase) &&
                       stored.PlaylistSource.Equals(NormalizePlaylistSource(playlistType, playlistSource), StringComparison.OrdinalIgnoreCase)
                    ? stored.GuideSource
                    : null;
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
            return null;
        }
    }

    private static string NormalizePlaylistSource(string playlistType, string source)
    {
        if (!playlistType.Equals("file", StringComparison.OrdinalIgnoreCase)) return source.Trim();
        try { return Path.GetFullPath(source); }
        catch { return source.Trim(); }
    }

    private sealed record StoredGuideSource(string PlaylistType, string PlaylistSource, string GuideSource);
}
