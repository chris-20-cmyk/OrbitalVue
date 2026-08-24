using System.IO;
using System.Text.Json;
using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public sealed class AppSettings
{
    public string? LastSourceType { get; set; }
    public string? LastSource { get; set; }
    public List<PlaylistSourceDefinition> PlaylistSources { get; set; } = [];
    public string? LastChannelKey { get; set; }
    public DateTimeOffset? LastPlaylistRefreshUtc { get; set; }
    public bool ResumeLastChannelOnStartup { get; set; }
    public bool MiniPlayerAlwaysOnTop { get; set; } = true;
    public PlaybackPreferences Playback { get; set; } = new();
    public List<string> FavoriteChannelKeys { get; set; } = [];
    public List<string> RecentChannelKeys { get; set; } = [];
    public Dictionary<string, ChannelPlaybackProfile> ChannelProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PlaylistHealthPreferences PlaylistHealth { get; set; } = new();
    public List<ProgramReminder> ProgramReminders { get; set; } = [];
    public string? RecordingsFolder { get; set; }
    public SmartDvrPreferences SmartDvr { get; set; } = new();
    public List<SeriesRecordingRule> SeriesRecordingRules { get; set; } = [];
    public List<ScheduledRecording> ScheduledRecordings { get; set; } = [];
    public Dictionary<string, DvrPlaybackProgress> RecordingPlaybackProgress { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public MultiviewPreferences Multiview { get; set; } = new();
}

public sealed class ChannelPlaybackProfile
{
    public BufferPreset? BufferPreset { get; set; }
    public bool? HardwareDecoding { get; set; }
    public string? AspectRatio { get; set; }
    public string? DeinterlaceMode { get; set; }
    public int? AudioDelayMilliseconds { get; set; }
    public int? AudioTrackId { get; set; }
    public int? SubtitleTrackId { get; set; }
    public int LearnedInstability { get; set; }
    public int SuccessfulStarts { get; set; }
    public int FailedStarts { get; set; }
    public int LastStartupMilliseconds { get; set; }
    public DateTimeOffset? LastSuccessfulUtc { get; set; }
    public string? LastRecoveryReason { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public bool HasOverrides => BufferPreset is not null || HardwareDecoding is not null ||
                                !string.IsNullOrWhiteSpace(AspectRatio) || !string.IsNullOrWhiteSpace(DeinterlaceMode) ||
                                AudioDelayMilliseconds is not null || AudioTrackId is not null || SubtitleTrackId is not null ||
                                LearnedInstability > 0;
}

public sealed class PlaylistHealthPreferences
{
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? LastSuccessUtc { get; set; }
    public string? LastError { get; set; }
    public int ChannelCount { get; set; }
    public int AddedChannels { get; set; }
    public int RemovedChannels { get; set; }
    public bool UsedCachedFallback { get; set; }
}

public sealed class ProgramReminder
{
    public required string ChannelKey { get; set; }
    public required string ChannelName { get; set; }
    public required string ProgramTitle { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset StopUtc { get; set; }
    public bool Notified { get; set; }
}

public sealed class MultiviewPreferences
{
    public string Layout { get; set; } = "Quad";
    public int ActiveSlot { get; set; }
    public int AudioSlot { get; set; }
    public List<string?> ChannelKeys { get; set; } = [null, null, null, null];
    public List<MultiviewLayoutPreset> SavedLayouts { get; set; } = [];
}

public sealed class MultiviewLayoutPreset
{
    public required string Name { get; set; }
    public string Layout { get; set; } = "Quad";
    public int ActiveSlot { get; set; }
    public int AudioSlot { get; set; }
    public List<string?> ChannelKeys { get; set; } = [null, null, null, null];
}

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public AppSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? StreamVueDataPaths.Resolve("settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new AppSettings();
            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await _saveGate.WaitAsync();
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            var tempPath = _settingsPath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
            }

            File.Move(tempPath, _settingsPath, overwrite: true);
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
