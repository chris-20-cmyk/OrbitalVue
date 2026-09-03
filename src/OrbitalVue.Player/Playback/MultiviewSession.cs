using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using OrbitalVue.Player.Models;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace OrbitalVue.Player.Playback;

public enum MultiviewLayout
{
    Duo,
    Quad,
    Focus
}

public sealed class MultiviewSession : IDisposable
{
    public const int MaximumTiles = 4;

    private readonly PlaybackPreferences _sourcePreferences;
    private bool _disposed;

    public MultiviewSession(PlaybackPreferences sourcePreferences)
    {
        _sourcePreferences = sourcePreferences;
        Tiles = Enumerable.Range(0, MaximumTiles)
            .Select(index => new MultiviewTile(index, CreateTilePreferences))
            .ToArray();
        SelectSlot(0);
        SetAudioSlot(0);
    }

    public IReadOnlyList<MultiviewTile> Tiles { get; }

    public int ActiveSlot { get; private set; }

    public int AudioSlot { get; private set; }

    public void SelectSlot(int index)
    {
        ThrowIfDisposed();
        ActiveSlot = Math.Clamp(index, 0, MaximumTiles - 1);
        foreach (var tile in Tiles) tile.IsSelected = tile.Index == ActiveSlot;
    }

    public void SetAudioSlot(int index)
    {
        ThrowIfDisposed();
        AudioSlot = Math.Clamp(index, 0, MaximumTiles - 1);
        foreach (var tile in Tiles) tile.SetAudible(tile.Index == AudioSlot);
    }

    public void AssignChannel(int index, ChannelItem channel)
    {
        ThrowIfDisposed();
        var slot = Math.Clamp(index, 0, MaximumTiles - 1);
        Tiles[slot].Play(channel);
        SelectSlot(slot);
        if (Tiles.Count(tile => tile.HasChannel) == 1) SetAudioSlot(slot);
    }

    public void RestoreChannel(int index, ChannelItem channel)
    {
        ThrowIfDisposed();
        Tiles[Math.Clamp(index, 0, MaximumTiles - 1)].Prime(channel);
    }

    public void ClearSlot(int index)
    {
        ThrowIfDisposed();
        var slot = Math.Clamp(index, 0, MaximumTiles - 1);
        Tiles[slot].Clear();
        if (slot != AudioSlot) return;
        var replacement = Tiles.FirstOrDefault(tile => tile.HasChannel)?.Index ?? 0;
        SetAudioSlot(replacement);
    }

    public void SwapSlots(int firstIndex, int secondIndex)
    {
        ThrowIfDisposed();
        var first = Math.Clamp(firstIndex, 0, MaximumTiles - 1);
        var second = Math.Clamp(secondIndex, 0, MaximumTiles - 1);
        if (first == second) return;

        var firstChannel = Tiles[first].Channel;
        var secondChannel = Tiles[second].Channel;
        var originalAudioSlot = AudioSlot;
        Tiles[first].Clear();
        Tiles[second].Clear();
        if (secondChannel is not null) Tiles[first].Play(secondChannel);
        if (firstChannel is not null) Tiles[second].Play(firstChannel);

        if (originalAudioSlot == first) SetAudioSlot(second);
        else if (originalAudioSlot == second) SetAudioSlot(first);
        SelectSlot(second);
    }

    public IReadOnlyList<MultiviewTile> VisibleTiles(MultiviewLayout layout) => layout switch
    {
        MultiviewLayout.Focus => [Tiles[ActiveSlot]],
        MultiviewLayout.Duo => Tiles.Take(2).ToArray(),
        _ => Tiles
    };

    public void ApplyLayoutResourceBudget(MultiviewLayout layout)
    {
        ThrowIfDisposed();
        for (var index = 0; index < MaximumTiles; index++)
        {
            var shouldRun = layout switch
            {
                MultiviewLayout.Duo => index < 2,
                MultiviewLayout.Focus => index == ActiveSlot,
                _ => true
            };
            if (shouldRun) Tiles[index].Resume();
            else Tiles[index].Suspend();
        }
    }

    public void ToggleActivePause() => Tiles[ActiveSlot].TogglePause();

    public void RetryActive() => Tiles[ActiveSlot].Retry();

    public void StopAll()
    {
        if (_disposed) return;
        foreach (var tile in Tiles) tile.Suspend();
    }

    public void ResumeVisible(MultiviewLayout layout)
    {
        ThrowIfDisposed();
        ApplyLayoutResourceBudget(layout);
    }

    public void PrepareAssignedSurfaces()
    {
        ThrowIfDisposed();
        foreach (var tile in Tiles.Where(tile => tile.HasChannel)) tile.PrepareSurface();
    }

    public void SetFullscreenPresentation(bool fullscreen)
    {
        ThrowIfDisposed();
        foreach (var tile in Tiles) tile.IsFullscreen = fullscreen;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var tile in Tiles) tile.Dispose();
    }

    private PlaybackPreferences CreateTilePreferences() => new()
    {
        BufferPreset = _sourcePreferences.BufferPreset,
        HardwareDecoding = _sourcePreferences.HardwareDecoding,
        HdmiPassthrough = false,
        AdaptiveRefreshRate = false,
        AutoReconnect = _sourcePreferences.AutoReconnect,
        StallWatchdog = _sourcePreferences.StallWatchdog,
        MaxReconnectAttempts = Math.Min(2, _sourcePreferences.MaxReconnectAttempts),
        AspectRatio = _sourcePreferences.AspectRatio,
        DeinterlaceMode = _sourcePreferences.DeinterlaceMode,
        AudioDelayMilliseconds = _sourcePreferences.AudioDelayMilliseconds
    };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

public sealed class MultiviewTile : INotifyPropertyChanged, IDisposable
{
    private static readonly Brush IdleBrush = new SolidColorBrush(Color.FromRgb(89, 101, 121));
    private static readonly Brush LiveBrush = new SolidColorBrush(Color.FromRgb(53, 231, 211));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(244, 189, 107));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(255, 101, 119));

    private readonly Func<PlaybackPreferences> _preferencesFactory;
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private NativePlaybackEngine? _engine;
    private ChannelItem? _channel;
    private string _status = "Select a channel";
    private string _detail = "Click this tile, then choose from the channel list";
    private Brush _statusBrush = IdleBrush;
    private bool _isSelected;
    private bool _isAudible;
    private bool _isBuffering;
    private bool _isSuspended;
    private bool _isFullscreen;
    private double _bufferPercent;
    private bool _disposed;
    private bool _hasReachedPlaying;
    private int _errorCount;

    internal MultiviewTile(int index, Func<PlaybackPreferences> preferencesFactory)
    {
        Index = index;
        _preferencesFactory = preferencesFactory;
    }

    public int Index { get; }
    public int Number => Index + 1;
    public ChannelItem? Channel => _channel;
    public VlcMediaPlayer? MediaPlayer => _engine?.MediaPlayer;
    public bool HasChannel => _channel is not null;
    public string ChannelName => _channel?.Name ?? $"View {Number}";
    public string GroupName => _channel?.Group ?? "Unassigned";
    public string Initials => _channel?.Initials ?? Number.ToString();
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string Detail { get => _detail; private set => SetField(ref _detail, value); }
    public Brush StatusBrush { get => _statusBrush; private set => SetField(ref _statusBrush, value); }
    public double BufferPercent { get => _bufferPercent; private set => SetField(ref _bufferPercent, value); }
    public bool IsBuffering { get => _isBuffering; private set => SetField(ref _isBuffering, value); }
    public bool IsSuspended { get => _isSuspended; private set => SetField(ref _isSuspended, value); }
    public bool IsFullscreen { get => _isFullscreen; internal set => SetField(ref _isFullscreen, value); }
    public bool HasReachedPlaying { get => _hasReachedPlaying; private set => SetField(ref _hasReachedPlaying, value); }
    public int ErrorCount { get => _errorCount; private set => SetField(ref _errorCount, value); }

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetField(ref _isSelected, value);
    }

    public bool IsAudible
    {
        get => _isAudible;
        private set => SetField(ref _isAudible, value);
    }

    internal void Play(ChannelItem channel)
    {
        ThrowIfDisposed();
        EnsureEngine();
        _channel = channel;
        IsSuspended = false;
        OnPropertyChanged(nameof(Channel));
        OnPropertyChanged(nameof(HasChannel));
        OnPropertyChanged(nameof(ChannelName));
        OnPropertyChanged(nameof(GroupName));
        OnPropertyChanged(nameof(Initials));
        Status = "Tuning";
        Detail = "Opening native stream";
        StatusBrush = WarningBrush;
        _engine!.Play(channel);
        _engine.MediaPlayer.Mute = !IsAudible;
    }

    public PlaybackSnapshot? GetSnapshot() => _engine?.GetSnapshot();

    internal void Prime(ChannelItem channel)
    {
        ThrowIfDisposed();
        _channel = channel;
        IsSuspended = true;
        Status = "Standby";
        Detail = "Ready when this view becomes visible";
        StatusBrush = IdleBrush;
        OnPropertyChanged(nameof(Channel));
        OnPropertyChanged(nameof(HasChannel));
        OnPropertyChanged(nameof(ChannelName));
        OnPropertyChanged(nameof(GroupName));
        OnPropertyChanged(nameof(Initials));
    }

    internal void PrepareSurface()
    {
        ThrowIfDisposed();
        EnsureEngine();
    }

    internal void SetAudible(bool audible)
    {
        IsAudible = audible;
        if (_engine is not null) _engine.MediaPlayer.Mute = !audible;
    }

    internal void Suspend()
    {
        if (_disposed || _engine is null || _channel is null || IsSuspended) return;
        _engine.Stop();
        IsSuspended = true;
        IsBuffering = false;
        Status = "Standby";
        Detail = "Stream paused to protect PC and provider limits";
        StatusBrush = IdleBrush;
    }

    internal void Resume()
    {
        if (_disposed || _channel is null || !IsSuspended) return;
        EnsureEngine();
        IsSuspended = false;
        _engine!.Play(_channel);
        _engine.MediaPlayer.Mute = !IsAudible;
    }

    internal void TogglePause()
    {
        if (_disposed || _channel is null) return;
        if (IsSuspended) Resume();
        else _engine?.TogglePause();
    }

    internal void Retry()
    {
        if (_disposed || _channel is null) return;
        if (IsSuspended) Resume();
        else _engine?.Retry();
    }

    internal void Clear()
    {
        if (_disposed) return;
        DisposeEngine();
        _channel = null;
        IsSuspended = false;
        IsBuffering = false;
        BufferPercent = 0;
        Status = "Select a channel";
        Detail = "Click this tile, then choose from the channel list";
        StatusBrush = IdleBrush;
        OnPropertyChanged(nameof(Channel));
        OnPropertyChanged(nameof(MediaPlayer));
        OnPropertyChanged(nameof(HasChannel));
        OnPropertyChanged(nameof(ChannelName));
        OnPropertyChanged(nameof(GroupName));
        OnPropertyChanged(nameof(Initials));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeEngine();
    }

    private void EnsureEngine()
    {
        if (_engine is not null) return;
        _engine = new NativePlaybackEngine(_preferencesFactory());
        _engine.StatusChanged += Engine_StatusChanged;
        _engine.MediaPlayer.Mute = !IsAudible;
        OnPropertyChanged(nameof(MediaPlayer));
    }

    private void Engine_StatusChanged(object? sender, PlaybackStatus status)
    {
        void Apply()
        {
            if (_disposed) return;
            Status = status.State switch
            {
                PlaybackState.Playing => "Live",
                PlaybackState.Buffering when status.IsBufferComplete => "Live",
                PlaybackState.Reconnecting => "Recovering",
                PlaybackState.Error => "Signal lost",
                _ => status.Message
            };
            Detail = status.TechnicalDetail ?? _channel?.Group ?? "Native stream";
            if (status.State == PlaybackState.Playing) HasReachedPlaying = true;
            if (status.State == PlaybackState.Error) ErrorCount++;
            IsBuffering = status.ShouldShowBufferOverlay;
            BufferPercent = status.BufferPercent;
            StatusBrush = status.State switch
            {
                PlaybackState.Playing => LiveBrush,
                PlaybackState.Buffering when status.IsBufferComplete => LiveBrush,
                PlaybackState.Opening or PlaybackState.Buffering or PlaybackState.Reconnecting => WarningBrush,
                PlaybackState.Error => ErrorBrush,
                _ => IdleBrush
            };
        }

        if (_synchronizationContext is null) Apply();
        else _synchronizationContext.Post(_ => Apply(), null);
    }

    private void DisposeEngine()
    {
        if (_engine is null) return;
        _engine.StatusChanged -= Engine_StatusChanged;
        _engine.Dispose();
        _engine = null;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public event PropertyChangedEventHandler? PropertyChanged;
}
