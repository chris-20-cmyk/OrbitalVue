namespace StreamVue.Player.Playback;

public enum PlaybackState
{
    Idle,
    Opening,
    Buffering,
    Reconnecting,
    Playing,
    Paused,
    Stopped,
    Error
}

public sealed record PlaybackStatus(
    PlaybackState State,
    string Message,
    float BufferPercent = 0,
    string? TechnicalDetail = null)
{
    public const float BufferCompleteThreshold = 99.5f;

    public bool IsBufferComplete =>
        State == PlaybackState.Buffering && BufferPercent >= BufferCompleteThreshold;

    public bool ShouldShowBufferOverlay =>
        State is PlaybackState.Opening or PlaybackState.Reconnecting ||
        State == PlaybackState.Buffering && !IsBufferComplete;
}

public sealed record PlaybackSnapshot(
    bool IsPlaying,
    bool IsMuted,
    int Volume,
    long Time,
    long Length,
    int BufferEvents,
    int ReconnectAttempts,
    int ActiveCacheMilliseconds,
    string DecoderMode,
    int DecoderFallbacks,
    int StallRecoveries,
    string VideoCodec,
    string Resolution,
    double FramesPerSecond,
    double InputBitrateMbps,
    int DroppedFrames,
    int DisplayedFrames,
    int DecodedFrames,
    string AudioFormat,
    IReadOnlyList<PlaybackTrack> AudioTracks,
    IReadOnlyList<PlaybackTrack> SubtitleTracks,
    int AudioDelayMilliseconds,
    string DeinterlaceMode,
    string TuneStrategy = "Smart tune",
    int StartupMilliseconds = 0,
    string RecoveryReason = "No interventions");

public sealed record PlaybackTrack(int Id, string Name, bool IsSelected);

public sealed record LiveTimeshiftSnapshot(
    bool Enabled,
    bool IsLiveChannel,
    bool CanPause,
    bool CanRewind,
    bool IsPaused,
    TimeSpan BehindLive,
    TimeSpan Window,
    TimeSpan Remaining)
{
    public bool IsActive => Enabled && IsLiveChannel && (IsPaused || BehindLive > TimeSpan.FromSeconds(1));
}
