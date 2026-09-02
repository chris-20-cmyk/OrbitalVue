using System.IO;
using System.Text.Json;

namespace OrbitalVue.Player.Services;

public sealed record SessionRecoverySnapshot(
    bool IsActive,
    Guid SessionId,
    DateTimeOffset UpdatedUtc,
    string? ChannelKey,
    string Workspace,
    string ChannelSearch,
    string GuideSearch,
    string GuideFilter,
    DateTimeOffset GuideWindowStart,
    bool GuideTimelineMode,
    bool WasFullscreen);

public sealed class SessionRecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Guid _sessionId;

    public SessionRecoveryService(string? path = null)
    {
        _path = path ?? OrbitalVueDataPaths.Resolve("session-journal.json");
    }

    public async Task<SessionRecoverySnapshot?> BeginAsync(SessionRecoverySnapshot current)
    {
        await _gate.WaitAsync();
        try
        {
            var previous = await ReadUnsafeAsync();
            _sessionId = Guid.NewGuid();
            await WriteUnsafeAsync(current with
            {
                IsActive = true,
                SessionId = _sessionId,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
            return previous?.IsActive == true ? previous : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task HeartbeatAsync(SessionRecoverySnapshot current)
    {
        if (_sessionId == Guid.Empty) return;
        await _gate.WaitAsync();
        try
        {
            await WriteUnsafeAsync(current with
            {
                IsActive = true,
                SessionId = _sessionId,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteAsync(SessionRecoverySnapshot current)
    {
        if (_sessionId == Guid.Empty) return;
        await _gate.WaitAsync();
        try
        {
            await WriteUnsafeAsync(current with
            {
                IsActive = false,
                SessionId = _sessionId,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
            _sessionId = Guid.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SessionRecoverySnapshot?> ReadUnsafeAsync()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<SessionRecoverySnapshot>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteUnsafeAsync(SessionRecoverySnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions);
        File.Move(temporary, _path, overwrite: true);
    }
}
