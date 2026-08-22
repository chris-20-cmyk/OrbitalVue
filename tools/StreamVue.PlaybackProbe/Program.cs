using StreamVue.Player.Models;
using StreamVue.Player.Playback;
using StreamVue.Player.Services;
using LibVLCSharp.Shared;

if (args.Length == 1 && args[0] == "--reconnect-self-test")
{
    Core.Initialize();
    var recoveryPreferences = new PlaybackPreferences
    {
        AutoReconnect = true,
        MaxReconnectAttempts = 2,
        HardwareDecoding = false
    };
    var unavailableChannel = new ChannelItem
    {
        Number = 1,
        Name = "Unavailable recovery probe",
        Group = "Self-test",
        Url = "http://127.0.0.1:1/unavailable.ts",
        Kind = ChannelKind.Live
    };
    using var recoveryPlayer = new NativePlaybackEngine(recoveryPreferences);
    var reconnectStates = 0;
    recoveryPlayer.StatusChanged += (_, status) =>
    {
        if (status.State == PlaybackState.Reconnecting) Interlocked.Increment(ref reconnectStates);
    };

    recoveryPlayer.Play(unavailableChannel);
    for (var tick = 0; tick < 120 && Volatile.Read(ref reconnectStates) < 2; tick++)
        await Task.Delay(100);

    Console.WriteLine($"Automatic reconnect states: {reconnectStates}");
    Console.WriteLine($"Reconnect limit: {recoveryPreferences.MaxReconnectAttempts}");
    return reconnectStates == recoveryPreferences.MaxReconnectAttempts ? 0 : 1;
}

if (args.Length < 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: StreamVue.PlaybackProbe <playlist.m3u> [seconds] | --reconnect-self-test");
    return 2;
}

var duration = args.Length > 1 && int.TryParse(args[1], out var parsedSeconds)
    ? Math.Clamp(parsedSeconds, 10, 120)
    : 30;

Core.Initialize();
var playlist = await new M3uPlaylistParser().ParseFileAsync(args[0]);
var channel = playlist.Channels.FirstOrDefault(item => item.Kind == ChannelKind.Live);
if (channel is null)
{
    Console.Error.WriteLine("No live channel was found.");
    return 3;
}

var preferences = new PlaybackPreferences
{
    BufferPreset = BufferPreset.Smart,
    HardwareDecoding = true,
    AutoReconnect = false
};

using var player = new NativePlaybackEngine(preferences);
player.MediaPlayer.Mute = true;
var playingReached = false;
var errorReached = false;
long firstPlayingTime = -1;
long lastPlayingTime = -1;
var firstDisplayedFrames = -1;
var lastDisplayedFrames = -1;

player.StatusChanged += (_, status) =>
{
    if (status.State == PlaybackState.Playing)
    {
        if (!playingReached) firstPlayingTime = player.MediaPlayer.Time;
        playingReached = true;
    }
    else if (status.State == PlaybackState.Error)
    {
        errorReached = true;
    }
};

Console.WriteLine($"Testing live entry #{channel.Number:N0} for {duration} seconds with native hardware decode and Smart per-channel caching.");
if (!player.Play(channel))
{
    Console.Error.WriteLine("LibVLC declined the playback request.");
    return 4;
}

for (var second = 0; second < duration; second++)
{
    await Task.Delay(1_000);
    if (playingReached)
    {
        lastPlayingTime = player.MediaPlayer.Time;
        var sample = player.GetSnapshot();
        if (firstDisplayedFrames < 0 && sample.DisplayedFrames > 0) firstDisplayedFrames = sample.DisplayedFrames;
        lastDisplayedFrames = sample.DisplayedFrames;
    }
    if (errorReached) break;
}

var playbackSnapshot = player.GetSnapshot();
player.Stop();
var advancedMilliseconds = firstPlayingTime >= 0 && lastPlayingTime >= firstPlayingTime
    ? lastPlayingTime - firstPlayingTime
    : 0;

Console.WriteLine($"Playing reached: {playingReached}");
Console.WriteLine($"Playback clock advanced: {advancedMilliseconds / 1000d:0.0}s");
Console.WriteLine($"Buffer transitions: {playbackSnapshot.BufferEvents}");
Console.WriteLine($"Playback error: {errorReached}");
Console.WriteLine($"Active cache: {playbackSnapshot.ActiveCacheMilliseconds} ms");
Console.WriteLine($"Decoder: {playbackSnapshot.DecoderMode}");
Console.WriteLine($"Video: {playbackSnapshot.VideoCodec} {playbackSnapshot.Resolution} {playbackSnapshot.FramesPerSecond:0.##} fps");
Console.WriteLine($"Audio: {playbackSnapshot.AudioFormat}");
Console.WriteLine($"Dropped frames: {playbackSnapshot.DroppedFrames}");
Console.WriteLine($"Displayed frames advanced: {Math.Max(0, lastDisplayedFrames - Math.Max(0, firstDisplayedFrames))}");
Console.WriteLine($"Watchdog recoveries: {playbackSnapshot.StallRecoveries}");

var displayedFramesAdvanced = firstDisplayedFrames >= 0 && lastDisplayedFrames - firstDisplayedFrames >= Math.Max(30, duration - 10);
var playbackClockAdvanced = advancedMilliseconds >= Math.Max(5_000, (duration - 10) * 1_000L);
return playingReached && !errorReached && (displayedFramesAdvanced || playbackClockAdvanced) ? 0 : 1;
