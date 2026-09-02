using StreamVue.Player.Models;
using StreamVue.Player.Services;

namespace StreamVue.Player.Playback;

public sealed record PlaybackTunePlan(
    int CacheMilliseconds,
    bool UseHardwareDecoding,
    string Strategy,
    string Explanation);

public static class PlaybackIntelligencePolicy
{
    public static PlaybackTunePlan CreatePlan(
        PlaybackPreferences preferences,
        string url,
        ChannelPlaybackProfile? profile,
        int sessionInstability,
        bool softwareFallbackActive)
    {
        var preset = profile?.BufferPreset ?? preferences.BufferPreset;
        var learnedInstability = preferences.PlaybackIntelligence
            ? Math.Max(Math.Clamp(sessionInstability, 0, 4), Math.Clamp(profile?.LearnedInstability ?? 0, 0, 4))
            : 0;
        var successfulStarts = preferences.PlaybackIntelligence ? Math.Max(0, profile?.SuccessfulStarts ?? 0) : 0;
        var fastTune = preferences.PlaybackIntelligence && preferences.FastChannelChanges &&
                       learnedInstability == 0 && preset == BufferPreset.Smart;
        var cacheMilliseconds = PlaybackHealthPolicy.SelectCacheMilliseconds(
            preset,
            url,
            learnedInstability,
            fastTune,
            successfulStarts);

        var hardwareRequested = profile?.HardwareDecoding ?? preferences.HardwareDecoding;
        var useHardware = hardwareRequested && !softwareFallbackActive;
        var strategy = !useHardware
            ? "Software safe mode"
            : preset == BufferPreset.Stable || learnedInstability >= 3
                ? "Stable recovery"
                : fastTune
                    ? successfulStarts >= 3 ? "Learned fast tune" : "Fast tune"
                    : "Smart tune";

        var explanation = strategy switch
        {
            "Software safe mode" => "This channel is using software decoding because its saved profile or recovery history requires it.",
            "Stable recovery" => "A larger channel-specific buffer is active because this stream has shown instability.",
            "Learned fast tune" => "This channel has started reliably, so OrbitalVue is using its fastest safe startup path.",
            "Fast tune" => "OrbitalVue is using a low-latency startup buffer and will expand it automatically if needed.",
            _ => "OrbitalVue is balancing startup speed and network resilience for this channel."
        };

        return new PlaybackTunePlan(cacheMilliseconds, useHardware, strategy, explanation);
    }

    public static TimeSpan SelectStartupTimeout(PlaybackPreferences preferences, int cacheMilliseconds)
    {
        var configured = Math.Clamp(preferences.StartupTimeoutSeconds, 6, 20);
        var cacheAwareSeconds = Math.Ceiling(cacheMilliseconds / 1000d) + 5;
        return TimeSpan.FromSeconds(Math.Clamp(Math.Max(configured, cacheAwareSeconds), 6, 20));
    }
}
