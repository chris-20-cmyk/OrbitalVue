using System.Collections.Concurrent;
using System.Collections.Generic;
using LibVLCSharp.Shared;
using StreamVue.Player.Models;
using StreamVue.Player.Services;

namespace StreamVue.Player.Playback;

public sealed class NativePlaybackEngine : IDisposable
{
    private readonly PlaybackPreferences _preferences;
    private readonly LibVLC _libVlc;
    private readonly object _reconnectGate = new();
    private readonly object _watchdogGate = new();
    private readonly ConcurrentDictionary<string, int> _channelInstability = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _softwareDecoderChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Timer _watchdogTimer;
    private Media? _currentMedia;
    private ChannelItem? _currentChannel;
    private ChannelPlaybackProfile? _currentProfile;
    private PlaybackTunePlan? _currentPlan;
    private CancellationTokenSource? _reconnectCancellation;
    private CancellationTokenSource? _stabilityCancellation;
    private int _bufferEvents;
    private int _reconnectAttempts;
    private int _reconnectCount;
    private int _decoderFallbacks;
    private int _stallRecoveries;
    private int _networkFailureCount;
    private int _activeCacheMilliseconds;
    private int _lastStartupMilliseconds;
    private int _lastDisplayedPictures;
    private int _lastPlayedAudioBuffers;
    private long _lastPlaybackTime = -1;
    private DateTimeOffset _playbackStartedUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastProgressUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _ignoreTerminationUntilUtc = DateTimeOffset.MinValue;
    private bool _hasReachedPlaying;
    private bool _bufferingActive;
    private bool _reconnectScheduled;
    private bool _softwareFallbackActive;
    private bool _decoderFallbackAttempted;
    private string _lastRecoveryReason = "No interventions";
    private bool _disposed;

    public NativePlaybackEngine(PlaybackPreferences preferences)
    {
        _preferences = preferences;
        var options = new List<string>
        {
            "--intf=dummy",
            "--no-video-title-show",
            "--no-snapshot-preview",
            preferences.HardwareDecoding ? "--avcodec-hw=any" : "--avcodec-hw=none"
        };

        if (preferences.HdmiPassthrough) options.Add("--spdif");
        _libVlc = new LibVLC(options.ToArray());
        _activeCacheMilliseconds = preferences.CacheMilliseconds;
        MediaPlayer = new MediaPlayer(_libVlc)
        {
            Volume = 82,
            EnableHardwareDecoding = preferences.HardwareDecoding
        };

        MediaPlayer.Opening += (_, _) => Publish(
            PlaybackState.Opening,
            $"Opening stream… • {_currentPlan?.Strategy ?? "Smart tune"}",
            technicalDetail: _currentPlan?.Explanation);
        MediaPlayer.Buffering += (_, args) => HandleBuffering(args.Cache);
        MediaPlayer.Playing += (_, _) => HandlePlaying();
        MediaPlayer.Paused += (_, _) => Publish(PlaybackState.Paused, "Paused");
        MediaPlayer.Stopped += (_, _) => Publish(PlaybackState.Stopped, "Stopped");
        MediaPlayer.EndReached += (_, _) => HandleUnexpectedTermination("The live stream ended unexpectedly.");
        MediaPlayer.EncounteredError += (_, _) => HandleUnexpectedTermination(
            "The provider closed the stream or returned media LibVLC could not decode.");

        _watchdogTimer = new System.Threading.Timer(WatchdogTick, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public MediaPlayer MediaPlayer { get; }

    public event EventHandler<PlaybackStatus>? StatusChanged;

    public bool Play(ChannelItem channel)
        => Play(channel, null);

    public bool Play(ChannelItem channel, ChannelPlaybackProfile? profile)
    {
        ThrowIfDisposed();
        CancelRecovery();
        _currentChannel = channel;
        _currentProfile = profile;
        _reconnectAttempts = 0;
        _reconnectCount = 0;
        _bufferEvents = 0;
        _decoderFallbacks = 0;
        _stallRecoveries = 0;
        _networkFailureCount = 0;
        _lastStartupMilliseconds = 0;
        _lastRecoveryReason = "No interventions";
        _softwareFallbackActive = EffectiveHardwareDecoding &&
                                  (profile?.HardwareDecoding == false ||
                                   _preferences.PlaybackIntelligence && _softwareDecoderChannels.ContainsKey(channel.StableKey));
        _decoderFallbackAttempted = _softwareFallbackActive;
        return StartPlayback(channel);
    }

    public bool Retry()
    {
        ThrowIfDisposed();
        if (_currentChannel is null) return false;
        CancelRecovery();
        _reconnectAttempts = 0;
        _networkFailureCount = 0;
        _reconnectCount++;
        _lastRecoveryReason = "Manual reconnect";
        return StartPlayback(_currentChannel);
    }

    public void ResetChannelLearning(ChannelItem channel)
    {
        ThrowIfDisposed();
        _softwareDecoderChannels.TryRemove(channel.StableKey, out _);
        _channelInstability.TryRemove(channel.StableKey, out _);
        if (ReferenceEquals(channel, _currentChannel))
        {
            _softwareFallbackActive = false;
            _decoderFallbackAttempted = false;
        }
    }

    private bool StartPlayback(ChannelItem channel)
    {
        ReleaseMedia();
        ResetProgressMonitor();

        _currentPlan = PlaybackIntelligencePolicy.CreatePlan(
            _preferences,
            channel.Url,
            _currentProfile,
            _channelInstability.GetValueOrDefault(channel.StableKey),
            _softwareFallbackActive);
        _activeCacheMilliseconds = _currentPlan.CacheMilliseconds;

        _currentMedia = new Media(_libVlc, new Uri(channel.Url));
        _currentMedia.AddOption($":network-caching={_activeCacheMilliseconds}");
        _currentMedia.AddOption($":live-caching={_activeCacheMilliseconds}");
        _currentMedia.AddOption($":file-caching={Math.Max(1_000, _activeCacheMilliseconds / 2)}");
        _currentMedia.AddOption(":http-reconnect");
        _currentMedia.AddOption(_currentPlan.UseHardwareDecoding ? ":avcodec-hw=any" : ":avcodec-hw=none");
        if (!string.IsNullOrWhiteSpace(channel.UserAgent))
            _currentMedia.AddOption($":http-user-agent={channel.UserAgent}");
        if (!string.IsNullOrWhiteSpace(channel.Referrer))
            _currentMedia.AddOption($":http-referrer={channel.Referrer}");

        MediaPlayer.EnableHardwareDecoding = _currentPlan.UseHardwareDecoding;
        MediaPlayer.Media = _currentMedia;
        ApplyAspectRatioToPlayer(EffectiveAspectRatio);
        ApplyAudioDelayToPlayer(EffectiveAudioDelayMilliseconds);
        ApplyDeinterlaceToPlayer(EffectiveDeinterlaceMode);
        Publish(PlaybackState.Opening, $"Tuning {channel.Name}… • {_currentPlan.Strategy}", technicalDetail: _currentPlan.Explanation);
        return MediaPlayer.Play();
    }

    public void TogglePause()
    {
        ThrowIfDisposed();
        MediaPlayer.Pause();
    }

    public void Stop()
    {
        if (_disposed) return;
        CancelRecovery();
        _currentChannel = null;
        _currentProfile = null;
        _currentPlan = null;
        ReleaseMedia();
        ResetProgressMonitor();
    }

    private void ReleaseMedia()
    {
        if (MediaPlayer.Media is not null)
            _ignoreTerminationUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(250);
        if (MediaPlayer.IsPlaying || MediaPlayer.Media is not null) MediaPlayer.Stop();
        MediaPlayer.Media = null;
        _currentMedia?.Dispose();
        _currentMedia = null;
    }

    public void SetVolume(int volume)
    {
        ThrowIfDisposed();
        MediaPlayer.Volume = Math.Clamp(volume, 0, 100);
    }

    public void ToggleMute()
    {
        ThrowIfDisposed();
        MediaPlayer.Mute = !MediaPlayer.Mute;
    }

    public void ApplyAspectRatio(string value)
    {
        ThrowIfDisposed();
        _preferences.AspectRatio = value;
        ApplyAspectRatioToPlayer(value);
    }

    public void ApplyChannelAspectRatio(string value)
    {
        ThrowIfDisposed();
        if (_currentProfile is not null) _currentProfile.AspectRatio = value;
        ApplyAspectRatioToPlayer(value);
    }

    private void ApplyAspectRatioToPlayer(string value)
    {
        MediaPlayer.AspectRatio = PlaybackAspectRatios.ToLibVlcValue(value);
        MediaPlayer.Scale = PlaybackAspectRatios.IsFill(value) ? 1.0f : 0.0f;
    }

    public void ApplyAudioDelay(int milliseconds)
    {
        ThrowIfDisposed();
        _preferences.AudioDelayMilliseconds = Math.Clamp(milliseconds, -500, 500);
        ApplyAudioDelayToPlayer(_preferences.AudioDelayMilliseconds);
    }

    private void ApplyAudioDelayToPlayer(int milliseconds) =>
        MediaPlayer.SetAudioDelay(Math.Clamp(milliseconds, -500, 500) * 1_000L);

    public void ApplyDeinterlace(string value)
    {
        ThrowIfDisposed();
        _preferences.DeinterlaceMode = value;
        ApplyDeinterlaceToPlayer(value);
    }

    private void ApplyDeinterlaceToPlayer(string value)
    {
        MediaPlayer.SetDeinterlace(value switch
        {
            "Off" => null,
            "Bob" => "bob",
            "Yadif 2×" => "yadif2x",
            "Blend" => "blend",
            _ => "auto"
        });
    }

    public bool SelectAudioTrack(int id)
    {
        ThrowIfDisposed();
        if (_currentProfile is not null) _currentProfile.AudioTrackId = id;
        return MediaPlayer.SetAudioTrack(id);
    }

    public bool SelectSubtitleTrack(int id)
    {
        ThrowIfDisposed();
        if (_currentProfile is not null) _currentProfile.SubtitleTrackId = id;
        return MediaPlayer.SetSpu(id);
    }

    public PlaybackSnapshot GetSnapshot()
    {
        var videoCodec = "—";
        var resolution = "—";
        var framesPerSecond = 0d;
        var audioFormat = "—";
        var inputBitrate = 0d;
        var droppedFrames = 0;
        var displayedFrames = 0;
        var decodedFrames = 0;

        try
        {
            if (_currentMedia is not null)
            {
                var statistics = _currentMedia.Statistics;
                inputBitrate = statistics.InputBitrate * 8d;
                droppedFrames = statistics.LostPictures;
                displayedFrames = statistics.DisplayedPictures;
                decodedFrames = statistics.DecodedVideo;

                foreach (var track in _currentMedia.Tracks ?? [])
                {
                    if (track.TrackType == TrackType.Video)
                    {
                        var video = track.Data.Video;
                        videoCodec = FourCc(track.Codec);
                        resolution = video.Width > 0 && video.Height > 0 ? $"{video.Width}×{video.Height}" : "—";
                        if (video.FrameRateDen > 0)
                            framesPerSecond = video.FrameRateNum / (double)video.FrameRateDen;
                    }
                    else if (track.TrackType == TrackType.Audio)
                    {
                        var audio = track.Data.Audio;
                        var channels = audio.Channels > 0 ? $" • {audio.Channels}ch" : string.Empty;
                        audioFormat = $"{FourCc(track.Codec)}{channels}";
                    }
                }
            }
        }
        catch
        {
            // LibVLC can replace track/statistics storage during a retune. The next telemetry tick will retry.
        }

        return new PlaybackSnapshot(
            MediaPlayer.IsPlaying,
            MediaPlayer.Mute,
            MediaPlayer.Volume,
            MediaPlayer.Time,
            MediaPlayer.Length,
            _bufferEvents,
            _reconnectCount,
            _activeCacheMilliseconds,
            _currentPlan?.UseHardwareDecoding == true
                ? "Hardware auto"
                : _softwareFallbackActive ? "Software fallback" : "Software",
            _decoderFallbacks,
            _stallRecoveries,
            videoCodec,
            resolution,
            framesPerSecond,
            inputBitrate,
            droppedFrames,
            displayedFrames,
            decodedFrames,
            audioFormat,
            ReadTrackDescriptions(MediaPlayer.AudioTrackDescription, MediaPlayer.AudioTrack, false),
            ReadTrackDescriptions(MediaPlayer.SpuDescription, MediaPlayer.Spu, true),
            EffectiveAudioDelayMilliseconds,
            EffectiveDeinterlaceMode,
            _currentPlan?.Strategy ?? "Smart tune",
            _lastStartupMilliseconds,
            _lastRecoveryReason);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watchdogTimer.Change(Timeout.Infinite, Timeout.Infinite);
        CancelRecovery();
        _currentChannel = null;
        _currentProfile = null;
        _currentPlan = null;
        ReleaseMedia();
        ResetProgressMonitor();
        _watchdogTimer.Dispose();
        MediaPlayer.Dispose();
        _libVlc.Dispose();
        GC.SuppressFinalize(this);
    }

    private void HandleBuffering(float cache)
    {
        if (_disposed) return;
        if (cache < 100 && !_bufferingActive)
        {
            _bufferingActive = true;
            _bufferEvents++;
            if (_hasReachedPlaying && _currentChannel is not null)
                RaiseInstability(_currentChannel);
        }
        else if (cache >= 100)
        {
            _bufferingActive = false;
            MarkProgress();
        }

        Publish(PlaybackState.Buffering, $"Buffering {cache:0}%", cache);
    }

    private void HandlePlaying()
    {
        if (_disposed) return;
        _bufferingActive = false;
        _hasReachedPlaying = true;
        _lastStartupMilliseconds = (int)Math.Clamp(
            (DateTimeOffset.UtcNow - _playbackStartedUtc).TotalMilliseconds,
            0,
            int.MaxValue);
        _networkFailureCount = 0;
        MarkProgress();
        ApplyAudioDelayToPlayer(EffectiveAudioDelayMilliseconds);
        ApplyDeinterlaceToPlayer(EffectiveDeinterlaceMode);
        if (_currentProfile?.AudioTrackId is int audioTrackId) MediaPlayer.SetAudioTrack(audioTrackId);
        if (_currentProfile?.SubtitleTrackId is int subtitleTrackId) MediaPlayer.SetSpu(subtitleTrackId);
        Publish(
            PlaybackState.Playing,
            _softwareFallbackActive ? "Live • software fallback" : $"Live • {_currentPlan?.Strategy ?? "Smart tune"}",
            technicalDetail: _currentPlan?.Explanation);

        _stabilityCancellation?.Cancel();
        _stabilityCancellation?.Dispose();
        _stabilityCancellation = new CancellationTokenSource();
        _ = ResetRecoveryBudgetAfterStablePlaybackAsync(_currentChannel, _stabilityCancellation.Token);
    }

    private void HandleUnexpectedTermination(string technicalDetail)
    {
        if (_disposed || DateTimeOffset.UtcNow < _ignoreTerminationUntilUtc) return;

        if (_currentChannel is not null) RaiseInstability(_currentChannel);
        _networkFailureCount++;

        if (_preferences.PlaybackIntelligence && _networkFailureCount >= 2 && TryEnableSoftwareFallback())
        {
            ScheduleReconnect("Repeated startup failures detected. Retuning this channel with the software decoder.", fast: true);
            return;
        }

        Publish(PlaybackState.Error, "Playback error", technicalDetail: technicalDetail);

        if (!_preferences.AutoReconnect || _currentChannel?.Kind != ChannelKind.Live)
            return;

        ScheduleReconnect(technicalDetail);
    }

    private bool TryEnableSoftwareFallback()
    {
        if (!EffectiveHardwareDecoding || _softwareFallbackActive || _decoderFallbackAttempted) return false;
        _decoderFallbackAttempted = true;
        _softwareFallbackActive = true;
        _decoderFallbacks++;
        if (_currentChannel is not null) _softwareDecoderChannels[_currentChannel.StableKey] = 1;
        return true;
    }

    private void ScheduleReconnect(string technicalDetail, bool fast = false)
    {
        ChannelItem? channel;
        CancellationToken token;
        int attempt;

        lock (_reconnectGate)
        {
            if (_disposed || _reconnectScheduled || _currentChannel is null) return;
            var maximumAttempts = Math.Clamp(_preferences.MaxReconnectAttempts, 1, 10);
            if (_reconnectAttempts >= maximumAttempts)
            {
                _reconnectScheduled = true;
                _lastRecoveryReason = technicalDetail;
                Publish(
                    PlaybackState.Error,
                    "The signal could not be restored",
                    technicalDetail: $"{technicalDetail} Automatic recovery reached its {maximumAttempts}-attempt limit.");
                return;
            }

            _reconnectAttempts++;
            _reconnectCount++;
            attempt = _reconnectAttempts;
            channel = _currentChannel;
            _reconnectScheduled = true;
            _reconnectCancellation?.Cancel();
            _reconnectCancellation?.Dispose();
            _reconnectCancellation = new CancellationTokenSource();
            token = _reconnectCancellation.Token;
        }

        var delay = fast ? TimeSpan.FromMilliseconds(650) : TimeSpan.FromSeconds(Math.Min(2 + attempt, 6));
        _lastRecoveryReason = technicalDetail;
        var delayText = delay.TotalSeconds < 1 ? "now" : $"in {delay.TotalSeconds:0}s";
        Publish(
            PlaybackState.Reconnecting,
            $"Recovering {delayText} • attempt {attempt}/{Math.Clamp(_preferences.MaxReconnectAttempts, 1, 10)}",
            technicalDetail: technicalDetail);
        _ = ReconnectAfterDelayAsync(channel, delay, token);
    }

    private async Task ReconnectAfterDelayAsync(ChannelItem channel, TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
            lock (_reconnectGate) _reconnectScheduled = false;
            if (token.IsCancellationRequested || _disposed || !ReferenceEquals(channel, _currentChannel)) return;
            StartPlayback(channel);
        }
        catch (OperationCanceledException)
        {
            // A manual stop, tune, retry, or shutdown superseded this recovery attempt.
        }
        finally
        {
            lock (_reconnectGate) _reconnectScheduled = false;
        }
    }

    private async Task ResetRecoveryBudgetAfterStablePlaybackAsync(ChannelItem? channel, CancellationToken token)
    {
        if (channel is null) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), token);
            if (!_disposed && ReferenceEquals(channel, _currentChannel) && MediaPlayer.IsPlaying)
            {
                _reconnectAttempts = 0;
                _channelInstability.AddOrUpdate(channel.StableKey, 0, (_, value) => Math.Max(0, value - 1));
            }
        }
        catch (OperationCanceledException)
        {
            // Playback changed before reaching the stable-session threshold.
        }
    }

    private void WatchdogTick(object? state)
    {
        if (_disposed || !_preferences.StallWatchdog || _currentChannel?.Kind != ChannelKind.Live) return;

        lock (_watchdogGate)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var time = MediaPlayer.Time;
                var statistics = _currentMedia?.Statistics;
                var displayedPictures = statistics?.DisplayedPictures ?? 0;
                var playedAudioBuffers = statistics?.PlayedAudioBuffers ?? 0;
                var hasVideo = _currentMedia?.Tracks?.Any(track => track.TrackType == TrackType.Video) == true;

                var openingTimedOut = PlaybackHealthPolicy.HasOpeningTimedOut(
                    now,
                    _playbackStartedUtc,
                    _hasReachedPlaying,
                    MediaPlayer.IsPlaying,
                    _bufferingActive,
                    PlaybackIntelligencePolicy.SelectStartupTimeout(_preferences, _activeCacheMilliseconds));
                if (openingTimedOut)
                {
                    _lastProgressUtc = now;
                    _stallRecoveries++;
                    _networkFailureCount++;
                    var openingChannel = _currentChannel;
                    if (openingChannel is not null) RaiseInstability(openingChannel);
                    var decoderFallback = _networkFailureCount >= 2 && TryEnableSoftwareFallback();
                    ScheduleReconnect(
                        decoderFallback
                            ? "The channel did not start after two attempts. Retuning with the software decoder and a larger buffer."
                            : "The provider did not deliver playable media before the startup deadline. Retuning with a larger smart buffer.",
                        fast: true);
                    return;
                }

                var videoProgress = displayedPictures > _lastDisplayedPictures || displayedPictures < _lastDisplayedPictures;
                var fallbackProgress = time > _lastPlaybackTime + 250 || playedAudioBuffers > _lastPlayedAudioBuffers;
                if (hasVideo ? videoProgress : fallbackProgress)
                {
                    _lastPlaybackTime = time;
                    _lastDisplayedPictures = displayedPictures;
                    _lastPlayedAudioBuffers = playedAudioBuffers;
                    _lastProgressUtc = now;
                    return;
                }

                var videoStartupFailed = PlaybackHealthPolicy.HasVideoStartupFailed(
                    now,
                    _playbackStartedUtc,
                    hasVideo,
                    displayedPictures,
                    MediaPlayer.IsPlaying,
                    _bufferingActive);
                if (!videoStartupFailed && !PlaybackHealthPolicy.IsStalled(
                        now,
                        _lastProgressUtc,
                        _playbackStartedUtc,
                        MediaPlayer.IsPlaying,
                        _bufferingActive)) return;

                _lastProgressUtc = now;
                _stallRecoveries++;
                var channel = _currentChannel;
                if (channel is not null) RaiseInstability(channel);

                var fallback = (videoStartupFailed || _reconnectAttempts >= 1) && TryEnableSoftwareFallback();
                ScheduleReconnect(
                    fallback
                        ? videoStartupFailed
                            ? "The hardware decoder produced audio but no video frames. Retuning this channel in software."
                            : "The stream clock froze. Retuning with the software decoder."
                        : "The stream clock froze even though the player reported Live. Retuning with a larger smart buffer.",
                    fast: true);
            }
            catch
            {
                // A retune can invalidate native statistics between reads; wait for the next watchdog sample.
            }
        }
    }

    private void ResetProgressMonitor()
    {
        lock (_watchdogGate)
        {
            _hasReachedPlaying = false;
            _bufferingActive = false;
            _lastStartupMilliseconds = 0;
            _lastPlaybackTime = -1;
            _lastDisplayedPictures = 0;
            _lastPlayedAudioBuffers = 0;
            _playbackStartedUtc = DateTimeOffset.UtcNow;
            _lastProgressUtc = _playbackStartedUtc;
        }
    }

    private void MarkProgress()
    {
        _lastProgressUtc = DateTimeOffset.UtcNow;
        _lastPlaybackTime = MediaPlayer.Time;
    }

    private void RaiseInstability(ChannelItem channel) =>
        _channelInstability.AddOrUpdate(channel.StableKey, 1, (_, value) => Math.Min(4, value + 1));

    private BufferPreset EffectiveBufferPreset => _currentProfile?.BufferPreset ?? _preferences.BufferPreset;
    private bool EffectiveHardwareDecoding => _currentProfile?.HardwareDecoding ?? _preferences.HardwareDecoding;
    private string EffectiveAspectRatio => _currentProfile?.AspectRatio ?? _preferences.AspectRatio;
    private string EffectiveDeinterlaceMode => _currentProfile?.DeinterlaceMode ?? _preferences.DeinterlaceMode;
    private int EffectiveAudioDelayMilliseconds => _currentProfile?.AudioDelayMilliseconds ?? _preferences.AudioDelayMilliseconds;

    private static IReadOnlyList<PlaybackTrack> ReadTrackDescriptions(
        LibVLCSharp.Shared.Structures.TrackDescription[]? descriptions,
        int selectedId,
        bool includeOff)
    {
        var tracks = new List<PlaybackTrack>();
        if (includeOff) tracks.Add(new PlaybackTrack(-1, "Off", selectedId == -1));
        if (descriptions is null) return tracks;

        foreach (var description in descriptions)
        {
            if (includeOff && description.Id == -1) continue;
            tracks.Add(new PlaybackTrack(
                description.Id,
                string.IsNullOrWhiteSpace(description.Name) ? $"Track {description.Id}" : description.Name,
                description.Id == selectedId));
        }

        return tracks;
    }

    private static string FourCc(uint value)
    {
        if (value == 0) return "—";
        Span<char> characters = stackalloc char[4];
        for (var index = 0; index < 4; index++)
        {
            var character = (char)((value >> (index * 8)) & 0xFF);
            characters[index] = char.IsControl(character) || character == '\0' ? ' ' : character;
        }

        var result = new string(characters).Trim();
        return string.IsNullOrWhiteSpace(result) ? "—" : result.ToUpperInvariant();
    }

    private void CancelRecovery()
    {
        lock (_reconnectGate)
        {
            _reconnectCancellation?.Cancel();
            _reconnectCancellation?.Dispose();
            _reconnectCancellation = null;
            _reconnectScheduled = false;
        }

        _stabilityCancellation?.Cancel();
        _stabilityCancellation?.Dispose();
        _stabilityCancellation = null;
    }

    private void Publish(PlaybackState state, string message, float bufferPercent = 0, string? technicalDetail = null) =>
        StatusChanged?.Invoke(this, new PlaybackStatus(state, message, bufferPercent, technicalDetail));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
