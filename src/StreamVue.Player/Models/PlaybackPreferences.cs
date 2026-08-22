namespace StreamVue.Player.Models;

public enum BufferPreset
{
    Smart,
    Responsive,
    Balanced,
    Stable
}

public sealed class PlaybackPreferences
{
    public BufferPreset BufferPreset { get; set; } = BufferPreset.Smart;
    public bool PlaybackIntelligence { get; set; } = true;
    public bool FastChannelChanges { get; set; } = true;
    public bool HardwareDecoding { get; set; } = true;
    public bool HdmiPassthrough { get; set; }
    public bool AdaptiveRefreshRate { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public bool StallWatchdog { get; set; } = true;
    public int MaxReconnectAttempts { get; set; } = 3;
    public int StartupTimeoutSeconds { get; set; } = 9;
    public string AspectRatio { get; set; } = "Auto";
    public string DeinterlaceMode { get; set; } = "Auto";
    public int AudioDelayMilliseconds { get; set; }

    public int CacheMilliseconds => BufferPreset switch
    {
        BufferPreset.Smart => 2_800,
        BufferPreset.Responsive => 1_200,
        BufferPreset.Stable => 8_000,
        _ => 4_000
    };
}
