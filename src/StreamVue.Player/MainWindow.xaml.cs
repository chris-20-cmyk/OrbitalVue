using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StreamVue.Player.Models;
using StreamVue.Player.Playback;
using StreamVue.Player.Services;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Cursors = System.Windows.Input.Cursors;
using Application = System.Windows.Application;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using OpenFolderDialog = Microsoft.Win32.OpenFolderDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using RadioButton = System.Windows.Controls.RadioButton;

namespace StreamVue.Player;

public partial class MainWindow : Window
{
    private static readonly string[] RecommendedUsGuideSources =
    [
        "https://epgshare01.online/epgshare01/epg_ripper_US2.xml.gz",
        "https://epgshare01.online/epgshare01/epg_ripper_US_LOCALS1.xml.gz"
    ];
    private static readonly Brush IdleBrush = new SolidColorBrush(Color.FromRgb(82, 96, 117));
    private static readonly Brush LiveBrush = new SolidColorBrush(Color.FromRgb(53, 231, 211));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(244, 189, 107));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(255, 101, 119));

    private readonly PlaylistSourceService _playlistSource = new();
    private readonly XtreamSourceService _xtreamSource = new();
    private readonly MediaCenterSourceService _mediaCenterSource;
    private readonly MicrosoftStorePremiumService _premiumStore = new();
    private PremiumAccessSnapshot _premiumAccess = PremiumAccessPolicy.Current;
    private readonly AppSettingsStore _settingsStore = new();
    private readonly PlaylistCacheStore _playlistCache = new();
    private readonly PlaylistSourceRefreshService _playlistSourceRefresh;
    private readonly XtreamCredentialStore _xtreamCredentialStore = new();
    private readonly AppUpdateService _appUpdateService = new();
    private readonly SessionRecoveryService _sessionRecoveryService = new();
    private readonly EpgSourceService _epgSourceService = new();
    private readonly EpgCacheStore _epgCache = new();
    private readonly GuideSourceStore _guideSourceStore = new();
    private readonly EpgMappingStore _epgMappingStore = new();
    private readonly StreamVueMaintenanceService _maintenanceService = new();
    private readonly WindowsCastService _castService = new();
    private readonly DvrRecordingService _dvrRecording = new();
    private readonly WindowsWakeTimer _dvrWakeTimer = new();
    private readonly WindowsRecordingPowerGuard _recordingPowerGuard = new();
    private readonly DispatcherTimer _telemetryTimer;
    private readonly DispatcherTimer _fullscreenChromeTimer;
    private readonly DispatcherTimer _sleepTimer;
    private readonly FullscreenWindowController _fullscreenWindow = new();

    private AppSettings _settings = new();
    private HashSet<string> _favoriteKeys = new(StringComparer.OrdinalIgnoreCase);
    private NativePlaybackEngine? _playback;
    private MultiviewSession? _multiviewSession;
    private DisplayRefreshRateController? _displayRefreshRate;
    private IReadOnlyList<ChannelItem> _channels = [];
    private IReadOnlyList<ChannelItem> _allChannels = [];
    private IReadOnlyList<SignalRoute> _signalRoutes = [];
    private IReadOnlyDictionary<string, SignalRoute> _signalRoutesByKey = new Dictionary<string, SignalRoute>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, SignalRoute> _signalRoutesByFeedKey = new Dictionary<string, SignalRoute>(StringComparer.OrdinalIgnoreCase);
    private ICollectionView? _channelView;
    private ICollectionView? _channelHealthView;
    private IReadOnlyList<ChannelHealthRow> _channelHealthRows = [];
    private ChannelHealthFilter _channelHealthFilter = ChannelHealthFilter.All;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _updateCancellation;
    private CancellationTokenSource? _guideCancellation;
    private CancellationTokenSource? _mediaPlaybackCancellation;
    private CancellationTokenSource? _plexAccountCancellation;
    private PlexPinChallenge? _plexAccountChallenge;
    private PlexServerDiscovery? _plexAccountDiscovery;
    private ChannelItem? _currentChannel;
    private ChannelItem? _activeSignalFeed;
    private ChannelItem? _previousChannel;
    private ProgramReminder? _activeReminder;
    private EpgSchedule? _guideSchedule;
    private ICollectionView? _guideView;
    private IReadOnlyList<GuideChannelRow> _guideRows = [];
    private ICollectionView? _guideTimelineView;
    private IReadOnlyList<GuideTimelineRow> _guideTimelineRows = [];
    private ICollectionView? _mappingCandidateView;
    private IReadOnlyList<EpgChannelOption> _mappingCandidates = [];
    private IReadOnlyDictionary<string, string> _guideMappings = new Dictionary<string, string>(StringComparer.Ordinal);
    private IReadOnlyList<string> _activeGuideSources = [];
    private string? _activePlaylistSourceType;
    private string? _activePlaylistSourceValue;
    private DateTimeOffset _lastGuidePresentationUpdate;
    private DateTimeOffset _guideWindowStart = AlignTimelineStart(DateTimeOffset.UtcNow);
    private string _guideFilter = "All";
    private MediaLibraryBrowseMode _libraryBrowseMode = MediaLibraryBrowseMode.All;
    private MediaLibraryBrowseSummary _libraryBrowseSummary = new(false, 0, 0, 0, 0);
    private string _categoryFilter = string.Empty;
    private bool _favoritesOnly;
    private bool _windowReady;
    private bool _isLoading;
    private bool _isFullscreen;
    private bool _updateBusy;
    private bool _updatingTrackControls;
    private bool _applyingChannelProfile;
    private bool _playerChromeSuppressed;
    private bool _showPlayerTopStatus;
    private bool _showBufferOverlay;
    private bool _showRecoveryOverlay;
    private bool _guideLoading;
    private bool _guideTimelineMode = true;
    private bool _multiviewMode;
    private bool _isMiniPlayer;
    private bool _resumeLastChannelPending;
    private SessionRecoverySnapshot? _pendingSessionRecovery;
    private DateTimeOffset _lastSessionHeartbeatUtc = DateTimeOffset.MinValue;
    private bool _synchronizingGuideScroll;
    private string _trackControlSignature = string.Empty;
    private string _learnedProfileSignature = string.Empty;
    private string? _selectedSignalRouteKey;
    private IReadOnlyList<SignalRouteCandidate> _signalRouteCandidates = [];
    private readonly HashSet<string> _attemptedSignalFeedKeys = new(StringComparer.OrdinalIgnoreCase);
    private int _automaticSignalSwitches;
    private bool _signalSessionSuccessRecorded;
    private bool _signalSessionFailureRecorded;
    private int _lastSignalBufferEvents;
    private int _lastSignalReconnects;
    private int _lastSignalStallRecoveries;
    private int _lastSignalDroppedFrames;
    private DateTimeOffset _lastSignalHistorySaveUtc = DateTimeOffset.MinValue;
    private string _handledDvrTerminalSignature = string.Empty;
    private string _dvrScheduleSignature = string.Empty;
    private string _dvrLibrarySignature = string.Empty;
    private string _dvrWakeSignature = string.Empty;
    private string? _currentRecordingPath;
    private bool _recordingResumeApplied = true;
    private long _pendingMediaResumeMilliseconds;
    private bool _mediaResumeApplied = true;
    private string? _mediaPlaybackReportingSessionId;
    private PlaybackState _mediaPlaybackState = PlaybackState.Idle;
    private MediaCenterPlaybackState? _lastReportedMediaPlaybackState;
    private DateTimeOffset _lastMediaPlaybackReportUtc = DateTimeOffset.MinValue;
    private bool _seekingRecording;
    private bool _allowFinalClose;
    private bool _hiddenToTray;
    private bool _trayNoticeShown;
    private readonly bool _backgroundLaunch;
    private readonly bool _automationRun;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Windows.Forms.ToolStripMenuItem? _trayStatusItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayStopRecordingItem;
    private DateTimeOffset _lastRecordingProgressSaveUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDvrLibraryRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDvrStorageGuardUtc = DateTimeOffset.MinValue;
    private DateOnly? _selectedDvrCalendarDate;
    private System.Windows.Point _dragStartPoint;
    private MultiviewLayout _multiviewLayout = MultiviewLayout.Quad;
    private DateTimeOffset? _sleepDeadline;
    private WindowState _windowStateBeforeMini;
    private Rect _windowBoundsBeforeMini;
    private bool _topmostBeforeMini;
    private double _minimumWidthBeforeMini;
    private double _minimumHeightBeforeMini;
    private GridLength _navWidthBeforeMini;
    private GridLength _catalogWidthBeforeMini;
    private GridLength _inspectorWidthBeforeMini;
    private GridLength _footerHeightBeforeMini;
    private GridLength _playerHeaderHeightBeforeMini;
    private GridLength _playerControlsHeightBeforeMini;
    private Thickness _playerMarginBeforeMini;
    private CornerRadius _playerCornerRadiusBeforeMini;
    private Thickness _playerBorderBeforeMini;

    private const int GuideWindowMinutes = 360;
    private const double GuidePixelsPerMinute = 4;

    public MainWindow()
    {
        _mediaCenterSource = new MediaCenterSourceService(
            premiumAccessProvider: () => _premiumAccess);
        _premiumStore.StateChanged += PremiumStore_StateChanged;
        InitializeComponent();
        ApplyPremiumAccessPresentation();
        SourceTabs.SelectionChanged += SourceTabs_SelectionChanged;
        _playlistSourceRefresh = new PlaylistSourceRefreshService(
            LoadPlaylistSourceDefinitionAsync,
            (sourceType, sourceValue, token) => _playlistCache.TryLoadAsync(sourceType, sourceValue, token),
            (sourceType, sourceValue, playlist, token) => _playlistCache.SaveAsync(sourceType, sourceValue, playlist, token));
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        _backgroundLaunch = arguments.Contains("--background-dvr", StringComparer.OrdinalIgnoreCase);
        _automationRun = arguments.Any(argument =>
            argument.StartsWith("--capture-", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("--smoke-", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("--startup-refresh-probe", StringComparison.OrdinalIgnoreCase));
        if (_backgroundLaunch)
        {
            ShowActivated = false;
            WindowState = WindowState.Minimized;
        }
        _dvrWakeTimer.Triggered += DvrWakeTimer_Triggered;
        Application.Current.SessionEnding += Application_SessionEnding;
        PopulateAspectRatioBoxes();
        _telemetryTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, UpdateTelemetry, Dispatcher);
        _fullscreenChromeTimer = new DispatcherTimer(TimeSpan.FromSeconds(2.4), DispatcherPriority.Background, HideFullscreenChrome, Dispatcher);
        _sleepTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, SleepTimer_Tick, Dispatcher);
    }

    private void PopulateAspectRatioBoxes()
    {
        foreach (var label in PlaybackAspectRatios.SupportedLabels)
        {
            AspectBox.Items.Add(new ComboBoxItem { Content = label });
            DefaultAspectBox.Items.Add(new ComboBoxItem { Content = label });
        }

        AspectBox.SelectedIndex = 0;
        DefaultAspectBox.SelectedIndex = 0;
    }

    private void ApplyPremiumAccessPresentation()
    {
        var purchase = _premiumStore.State;
        PlexAccessBadge.Text = _premiumAccess.BadgeText;
        EmbyAccessBadge.Text = _premiumAccess.BadgeText;
        var purchaseControlsVisible = !_premiumAccess.CanUseMediaCenters &&
            (purchase.IsBusy || purchase.CanPurchase || purchase.CanRestore);
        PlexPurchasePanel.Visibility = purchaseControlsVisible ? Visibility.Visible : Visibility.Collapsed;
        EmbyPurchasePanel.Visibility = purchaseControlsVisible ? Visibility.Visible : Visibility.Collapsed;
        PlexPurchaseProgress.Visibility = purchase.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        EmbyPurchaseProgress.Visibility = purchase.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        var productLabel = string.IsNullOrWhiteSpace(purchase.ProductTitle)
            ? "Lifetime Plex + Emby access"
            : purchase.ProductTitle;
        PlexPurchaseProductText.Text = productLabel;
        EmbyPurchaseProductText.Text = productLabel;
        var purchaseLabel = string.IsNullOrWhiteSpace(purchase.FormattedPrice)
            ? "Buy once"
            : $"Buy once — {purchase.FormattedPrice}";
        PlexPurchaseButton.Content = purchaseLabel;
        EmbyPurchaseButton.Content = purchaseLabel;
        PlexPurchaseButton.IsEnabled = purchase.CanPurchase && !purchase.IsBusy;
        EmbyPurchaseButton.IsEnabled = purchase.CanPurchase && !purchase.IsBusy;
        PlexRestoreButton.IsEnabled = purchase.CanRestore && !purchase.IsBusy;
        EmbyRestoreButton.IsEnabled = purchase.CanRestore && !purchase.IsBusy;
        if (_premiumAccess.CanUseMediaCenters)
        {
            PlexAccessDetailText.Text = $"Index movies and episodes without storing playable token URLs in the library. {_premiumAccess.Explanation}";
            EmbyAccessDetailText.Text = $"Connect directly with a protected Windows-user-bound session. {_premiumAccess.Explanation}";
            PlexConnectButton.Content = "Connect Plex";
            EmbyConnectButton.Content = "Connect Emby";
            PlexConnectButton.Opacity = 1;
            EmbyConnectButton.Opacity = 1;
            PlexAccountPanel.IsEnabled = true;
            foreach (var control in MediaCenterCredentialControls()) control.IsEnabled = true;
            PlexConnectButton.IsEnabled = true;
            EmbyConnectButton.IsEnabled = true;
            return;
        }

        var lockedMessage = purchase.Message ?? _premiumAccess.Explanation;
        PlexAccessDetailText.Text = lockedMessage;
        EmbyAccessDetailText.Text = lockedMessage;
        PlexConnectButton.Content = "Premium unavailable";
        EmbyConnectButton.Content = "Premium unavailable";
        PlexConnectButton.IsEnabled = false;
        EmbyConnectButton.IsEnabled = false;
        PlexConnectButton.Opacity = 0.48;
        EmbyConnectButton.Opacity = 0.48;
        CancelPlexAccountSignInCore(lockedMessage);
        PlexAccountPanel.IsEnabled = false;
        foreach (var control in MediaCenterCredentialControls()) control.IsEnabled = false;
    }

    private System.Windows.Controls.Control[] MediaCenterCredentialControls() =>
    [
        PlexServerBox, PlexNameBox, PlexTokenBox, PlexAllowHttpBox,
        EmbyServerBox, EmbyNameBox, EmbyUsernameBox, EmbyPasswordBox, EmbyAllowHttpBox
    ];

    private void PremiumStore_StateChanged(object? sender, PremiumPurchaseState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => PremiumStore_StateChanged(sender, state));
            return;
        }
        var previous = _premiumAccess;
        _premiumAccess = state.Access;
        ApplyPremiumAccessPresentation();
        RefreshPlaylistSourceList();
        if (!previous.CanUseMediaCenters && state.Access.CanUseMediaCenters)
        {
            ImportStatusText.Text = "Lifetime premium access verified";
            ImportDetailText.Text = state.Message ?? state.Access.Explanation;
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = "Microsoft Store premium access verified";
            return;
        }
        if (!previous.CanUseMediaCenters || state.Access.CanUseMediaCenters) return;
        CancelPlexAccountSignInCore("Premium access is no longer verified. Plex account sign-in was canceled.");
        ResetArtworkLoading();
        if (_currentChannel?.IsProtectedMedia == true)
        {
            ObserveMediaPlaybackReport(EndCurrentMediaPlaybackReporting(_playback?.GetSnapshot()));
            _mediaCenterSource.CancelAllPlaybackReportingSessions();
            CancelMediaPlaybackResolution(clearResume: true);
            _playback?.Stop();
            _displayRefreshRate?.Restore();
        }
        ImportStatusText.Text = "Premium access is no longer verified";
        ImportDetailText.Text = state.Message ?? state.Access.Explanation;
        FooterStatusDot.Fill = WarningBrush;
        FooterStatusText.Text = "Protected media-center playback stopped";
    }

    private void SourceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, SourceTabs) || _premiumAccess.CanUseMediaCenters) return;
        if (ReferenceEquals(SourceTabs.SelectedItem, PlexSourceTab) ||
            ReferenceEquals(SourceTabs.SelectedItem, EmbySourceTab))
        {
            ImportStatusText.Text = "Premium store verification required";
            ImportDetailText.Text = "No media-server credential or request is allowed in this store build. Playlist sources remain available.";
            return;
        }

        if (ImportStatusText.Text == "Premium store verification required")
        {
            ImportStatusText.Text = "Ready to connect";
            ImportDetailText.Text = "Choose a source that you are authorized to access.";
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _settingsStore.LoadAsync();
        var playlistSourcesMigrated = PlaylistSourcePolicy.NormalizeSettings(_settings);
        _settings.Playback ??= new PlaybackPreferences();
        _settings.FavoriteChannelKeys ??= [];
        _settings.RecentChannelKeys ??= [];
        _settings.ChannelProfiles = _settings.ChannelProfiles is null
            ? new Dictionary<string, ChannelPlaybackProfile>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ChannelPlaybackProfile>(_settings.ChannelProfiles, StringComparer.OrdinalIgnoreCase);
        _settings.SignalRouting ??= new SignalRoutingPreferences();
        _settings.Updates ??= new AppUpdatePreferences();
        _settings.SignalRouting.MaximumAutomaticSwitches = Math.Clamp(_settings.SignalRouting.MaximumAutomaticSwitches, 1, 8);
        _settings.SignalRouting.FeedHealth = _settings.SignalRouting.FeedHealth is null
            ? new Dictionary<string, SignalFeedHealth>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SignalFeedHealth>(_settings.SignalRouting.FeedHealth, StringComparer.OrdinalIgnoreCase);
        SmartSignalRoutingPolicy.NormalizeManualRoutes(_settings.SignalRouting);
        _settings.PlaylistHealth ??= new PlaylistHealthPreferences();
        _settings.ProgramReminders ??= [];
        _settings.SmartDvr ??= new SmartDvrPreferences();
        _settings.SeriesRecordingRules ??= [];
        _settings.ScheduledRecordings ??= [];
        _settings.RecordingPlaybackProgress = _settings.RecordingPlaybackProgress is null
            ? new Dictionary<string, DvrPlaybackProgress>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, DvrPlaybackProgress>(_settings.RecordingPlaybackProgress, StringComparer.OrdinalIgnoreCase);
        foreach (var staleKey in _settings.RecordingPlaybackProgress
                     .Where(pair => pair.Value is null || pair.Value.UpdatedUtc < DateTimeOffset.UtcNow.AddDays(-180))
                     .Select(pair => pair.Key)
                     .ToList())
            _settings.RecordingPlaybackProgress.Remove(staleKey);
        _settings.RecordingsFolder = ResolveRecordingsFolder(_settings.RecordingsFolder);
        _settings.Multiview ??= new MultiviewPreferences();
        _settings.Multiview.ChannelKeys ??= [null, null, null, null];
        _settings.Multiview.SavedLayouts ??= [];
        _favoriteKeys = new HashSet<string>(_settings.FavoriteChannelKeys ?? [], StringComparer.OrdinalIgnoreCase);
        await _premiumStore.StartAsync(new WindowInteropHelper(this).EnsureHandle());
        ApplySettingsToControls();
        RefreshSavedMultiviewLayouts();
        UpdatePlaylistHealthUi();
        RefreshPlaylistSourceList();
        RecordingFolderBox.Text = _settings.RecordingsFolder;
        ApplySmartDvrSettingsToControls();
        NormalizeScheduledRecordings();
        UpdateDvrUi(_dvrRecording.Snapshot);
        CreatePlaybackEngine();
        InitializeTrayRecorder();
        _displayRefreshRate = new DisplayRefreshRateController(new WindowInteropHelper(this).Handle);
        _telemetryTimer.Start();
        _windowReady = true;
        if (!_automationRun)
        {
            _pendingSessionRecovery = await _sessionRecoveryService.BeginAsync(CreateSessionSnapshot());
            if (!_settings.RestoreUnexpectedSession) _pendingSessionRecovery = null;
        }
        if (playlistSourcesMigrated) await _settingsStore.SaveAsync(_settings);
        if (_backgroundLaunch) _ = Dispatcher.BeginInvoke(() => HideToBackground(showNotice: false), DispatcherPriority.Background);

        var commandLineArguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        await HandleUpdateRecoveryLaunchAsync(commandLineArguments);
        var fullscreenWindowSmokeArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--smoke-fullscreen-window");
        if (fullscreenWindowSmokeArgumentIndex >= 0 && fullscreenWindowSmokeArgumentIndex + 1 < commandLineArguments.Length)
        {
            await RunFullscreenWindowSmokeAsync(
                commandLineArguments[fullscreenWindowSmokeArgumentIndex + 1],
                commandLineArguments.Contains("--maximized", StringComparer.OrdinalIgnoreCase));
            return;
        }

        var smokeArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--smoke-live");
        if (smokeArgumentIndex >= 0 && smokeArgumentIndex + 4 < commandLineArguments.Length)
        {
            var smokeDuration = int.TryParse(commandLineArguments[smokeArgumentIndex + 2], out var parsedDuration)
                ? Math.Clamp(parsedDuration, 10, 120)
                : 30;
            await RunVisualPlaybackSmokeAsync(
                commandLineArguments[smokeArgumentIndex + 1],
                smokeDuration,
                commandLineArguments[smokeArgumentIndex + 3],
                commandLineArguments[smokeArgumentIndex + 4],
                commandLineArguments.Contains("--fullscreen", StringComparer.OrdinalIgnoreCase));
            return;
        }

        var currentCacheSmokeArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--smoke-live-current-cache");
        if (currentCacheSmokeArgumentIndex >= 0 && currentCacheSmokeArgumentIndex + 3 < commandLineArguments.Length)
        {
            if (string.IsNullOrWhiteSpace(_settings.LastSourceType) || string.IsNullOrWhiteSpace(_settings.LastSource))
                throw new InvalidOperationException("No saved playlist connection is available for the playback smoke test.");
            var cached = await _playlistCache.TryLoadAsync(_settings.LastSourceType, _settings.LastSource);
            if (cached is null) throw new InvalidOperationException("The encrypted playlist cache was not available for the playback smoke test.");
            var smokeDuration = int.TryParse(commandLineArguments[currentCacheSmokeArgumentIndex + 1], out var parsedDuration)
                ? Math.Clamp(parsedDuration, 10, 120)
                : 30;
            await RunVisualPlaybackSmokeAsync(
                cached.Playlist,
                smokeDuration,
                commandLineArguments[currentCacheSmokeArgumentIndex + 2],
                commandLineArguments[currentCacheSmokeArgumentIndex + 3],
                commandLineArguments.Contains("--fullscreen", StringComparer.OrdinalIgnoreCase));
            return;
        }

        var multiviewSmokeArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--smoke-multiview-current-cache");
        if (multiviewSmokeArgumentIndex >= 0 && multiviewSmokeArgumentIndex + 3 < commandLineArguments.Length)
        {
            if (string.IsNullOrWhiteSpace(_settings.LastSourceType) || string.IsNullOrWhiteSpace(_settings.LastSource))
                throw new InvalidOperationException("No saved playlist connection is available for the multiview smoke test.");
            var cached = await _playlistCache.TryLoadAsync(_settings.LastSourceType, _settings.LastSource);
            if (cached is null) throw new InvalidOperationException("The encrypted playlist cache was not available for the multiview smoke test.");
            var smokeDuration = int.TryParse(commandLineArguments[multiviewSmokeArgumentIndex + 1], out var parsedDuration)
                ? Math.Clamp(parsedDuration, 15, 120)
                : 30;
            await RunMultiviewSmokeAsync(
                cached.Playlist,
                smokeDuration,
                commandLineArguments[multiviewSmokeArgumentIndex + 2],
                commandLineArguments[multiviewSmokeArgumentIndex + 3],
                commandLineArguments.Contains("--fullscreen", StringComparer.OrdinalIgnoreCase));
            return;
        }

        var guideSmokeArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--smoke-guide");
        if (guideSmokeArgumentIndex >= 0 && guideSmokeArgumentIndex + 3 < commandLineArguments.Length)
        {
            await RunGuideSmokeAsync(
                commandLineArguments[guideSmokeArgumentIndex + 1],
                commandLineArguments[guideSmokeArgumentIndex + 2],
                commandLineArguments[guideSmokeArgumentIndex + 3]);
            return;
        }

        var guideCacheSmokeArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--smoke-guide-cache");
        if (guideCacheSmokeArgumentIndex >= 0 && guideCacheSmokeArgumentIndex + 4 < commandLineArguments.Length)
        {
            await RunGuideCacheSmokeAsync(
                commandLineArguments[guideCacheSmokeArgumentIndex + 1],
                commandLineArguments[guideCacheSmokeArgumentIndex + 2],
                commandLineArguments[guideCacheSmokeArgumentIndex + 3],
                commandLineArguments[guideCacheSmokeArgumentIndex + 4]);
            return;
        }

        var currentGuideCacheSmokeArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--smoke-guide-current-cache");
        if (currentGuideCacheSmokeArgumentIndex >= 0 && currentGuideCacheSmokeArgumentIndex + 2 < commandLineArguments.Length)
        {
            if (string.IsNullOrWhiteSpace(_settings.LastSourceType) || string.IsNullOrWhiteSpace(_settings.LastSource))
                throw new InvalidOperationException("No saved playlist connection is available for the guide smoke test.");
            await RunGuideCacheSmokeAsync(
                _settings.LastSourceType,
                _settings.LastSource,
                commandLineArguments[currentGuideCacheSmokeArgumentIndex + 1],
                commandLineArguments[currentGuideCacheSmokeArgumentIndex + 2]);
            return;
        }

        var playlistCaptureArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--capture-playlist-ui");
        if (playlistCaptureArgumentIndex >= 0 && playlistCaptureArgumentIndex + 2 < commandLineArguments.Length)
        {
            var playlistPath = commandLineArguments[playlistCaptureArgumentIndex + 1];
            var playlist = await _playlistSource.LoadFileAsync(playlistPath);
            ApplyPlaylist(playlist);
            HideModal(ImportOverlay);
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = $"Grouped library preview • {playlist.Channels.Count:N0} entries";
            SourceRefreshText.Text = "Fresh local playlist preview";
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(500);
            CaptureWindow(commandLineArguments[playlistCaptureArgumentIndex + 2]);
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        var guideCaptureArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--capture-guide-ui");
        if (guideCaptureArgumentIndex >= 0 && guideCaptureArgumentIndex + 2 < commandLineArguments.Length)
        {
            var playlist = await _playlistSource.LoadFileAsync(commandLineArguments[guideCaptureArgumentIndex + 1]);
            ApplyPlaylist(playlist);
            ApplyGuideSchedule(CreatePreviewGuideSchedule(playlist.Channels));
            GuideNavigation.IsChecked = true;
            SetGuideReadyStatus(_guideSchedule!, "2 combined XMLTV sources");
            GuideStatusText.Text = "Updated moments ago from 2 combined XMLTV sources • encrypted cache ready";
            FooterStatusText.Text = "TV guide preview • verified US + local listings";
            if (commandLineArguments.Contains("--maximized", StringComparer.OrdinalIgnoreCase))
                WindowState = WindowState.Maximized;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(500);
            CaptureWindow(commandLineArguments[guideCaptureArgumentIndex + 2]);
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        var guidePreviewCaptureArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--capture-guide-preview");
        if (guidePreviewCaptureArgumentIndex >= 0 && guidePreviewCaptureArgumentIndex + 1 < commandLineArguments.Length)
        {
            ApplyPreviewPlaylist();
            ApplyGuideSchedule(CreatePreviewGuideSchedule(_channels));
            GuideNavigation.IsChecked = true;
            SetGuideReadyStatus(_guideSchedule!, "encrypted preview cache");
            GuideStatusText.Text = "Updated moments ago • six-hour timeline ready • encrypted cache ready";
            FooterStatusText.Text = "Guide Pro preview • timeline and manual mapping ready";
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(500);
            CaptureWindow(commandLineArguments[guidePreviewCaptureArgumentIndex + 1]);
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        var multiviewPreviewCaptureArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--capture-multiview-preview");
        if (multiviewPreviewCaptureArgumentIndex >= 0 && multiviewPreviewCaptureArgumentIndex + 1 < commandLineArguments.Length)
        {
            ApplyPreviewPlaylist();
            _multiviewMode = true;
            _multiviewLayout = MultiviewLayout.Quad;
            GuideWorkspace.Visibility = Visibility.Collapsed;
            PlayerPanel.Visibility = Visibility.Collapsed;
            InspectorPanel.Visibility = Visibility.Collapsed;
            CatalogPanel.Visibility = Visibility.Visible;
            MultiviewWorkspace.Visibility = Visibility.Visible;
            CatalogHeading.Text = "Multiview channels";
            EnsureMultiviewSession();
            for (var index = 0; index < Math.Min(4, _channels.Count); index++)
                _multiviewSession!.RestoreChannel(index, ResolveBestSignalFeed(_channels[index]));
            _multiviewSession!.SelectSlot(1);
            _multiviewSession.SetAudioSlot(1);
            _multiviewSession.PrepareAssignedSurfaces();
            _playerChromeSuppressed = true;
            UpdateMultiviewLayout(managePlayback: false);
            if (commandLineArguments.Contains("--fullscreen", StringComparer.OrdinalIgnoreCase))
                EnterFullscreen();
            else if (commandLineArguments.Contains("--maximized", StringComparer.OrdinalIgnoreCase))
                WindowState = WindowState.Maximized;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(500);
            if (commandLineArguments.Contains("--hold-screen", StringComparer.OrdinalIgnoreCase))
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                Close();
                Application.Current.Shutdown(0);
                return;
            }
            CaptureWindow(commandLineArguments[multiviewPreviewCaptureArgumentIndex + 1]);
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        var mappingPreviewCaptureArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--capture-mapping-preview");
        if (mappingPreviewCaptureArgumentIndex >= 0 && mappingPreviewCaptureArgumentIndex + 1 < commandLineArguments.Length)
        {
            ApplyPreviewPlaylist();
            ApplyGuideSchedule(CreatePreviewGuideSchedule(_channels));
            GuideNavigation.IsChecked = true;
            await OpenMappingEditorAsync(_channels.FirstOrDefault(channel => channel.Kind == ChannelKind.Live && GetGuideProgrammes(channel).Count == 0));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(500);
            CaptureWindow(commandLineArguments[mappingPreviewCaptureArgumentIndex + 1]);
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        var captureArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--capture-ui");
        if (captureArgumentIndex >= 0 && captureArgumentIndex + 1 < commandLineArguments.Length)
        {
            ApplyPreviewPlaylist();
            if (commandLineArguments.Contains("--timeshift-preview", StringComparer.OrdinalIgnoreCase) && _channels.Count > 0)
            {
                _currentChannel = _channels[0];
                NowPlayingHeading.Text = _currentChannel.Name;
                NowPlayingSubheading.Text = _currentChannel.Group;
                UpdateTimeshiftUi(new LiveTimeshiftSnapshot(
                    Enabled: true,
                    IsLiveChannel: true,
                    CanPause: true,
                    CanRewind: true,
                    IsPaused: true,
                    BehindLive: TimeSpan.FromMinutes(2.5),
                    Window: TimeSpan.FromMinutes(60),
                    Remaining: TimeSpan.FromMinutes(57.5)));
            }
            if (commandLineArguments.Contains("--fullscreen-hud", StringComparer.OrdinalIgnoreCase) && _channels.Count > 0)
            {
                _currentChannel = _channels[0];
                NowPlayingHeading.Text = _currentChannel.Name;
                NowPlayingSubheading.Text = _currentChannel.Group;
                _showPlayerTopStatus = true;
                UpdateFullscreenHud(new PlaybackSnapshot(
                    true, false, 82, 0, 0, 0, 0, 2_800, "Hardware auto", 0, 0,
                    "H264", "1920×1080", 59.94, 7.8, 0, 100, 100, "EAC3 • 6ch", [], [], 0, "Auto"));
                RefreshPlayerSurfaceVisibility();
            }
            if (commandLineArguments.Contains("--mini-player", StringComparer.OrdinalIgnoreCase))
                EnterMiniPlayer();
            else if (commandLineArguments.Contains("--fullscreen", StringComparer.OrdinalIgnoreCase))
                EnterFullscreen();
            if (commandLineArguments.Contains("--quick-tune", StringComparer.OrdinalIgnoreCase))
                OpenQuickTune("news");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(350);
            if (commandLineArguments.Contains("--timeshift-preview", StringComparer.OrdinalIgnoreCase))
            {
                UpdateTimeshiftUi(new LiveTimeshiftSnapshot(
                    Enabled: true,
                    IsLiveChannel: true,
                    CanPause: true,
                    CanRewind: true,
                    IsPaused: true,
                    BehindLive: TimeSpan.FromMinutes(2.5),
                    Window: TimeSpan.FromMinutes(60),
                    Remaining: TimeSpan.FromMinutes(57.5)));
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            }
            if (commandLineArguments.Contains("--screen-capture", StringComparer.OrdinalIgnoreCase))
                CaptureScreenWindow(commandLineArguments[captureArgumentIndex + 1]);
            else
                CaptureWindow(commandLineArguments[captureArgumentIndex + 1]);
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        var settingsCaptureArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--capture-settings-ui");
        if (settingsCaptureArgumentIndex >= 0 && settingsCaptureArgumentIndex + 1 < commandLineArguments.Length)
        {
            await PrepareModalCaptureAsync(commandLineArguments);
            if (commandLineArguments.Contains("--maximized", StringComparer.OrdinalIgnoreCase))
                WindowState = WindowState.Maximized;
            OpenSettingsModal();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (commandLineArguments.Contains("--convenience", StringComparer.OrdinalIgnoreCase))
            {
                SettingsScrollViewer.ScrollToEnd();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            }
            await Task.Delay(500);
            CaptureWindow(commandLineArguments[settingsCaptureArgumentIndex + 1]);
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        var updateCaptureArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--capture-update-ui");
        if (updateCaptureArgumentIndex >= 0 && updateCaptureArgumentIndex + 1 < commandLineArguments.Length)
        {
            await PrepareModalCaptureAsync(commandLineArguments);
            if (commandLineArguments.Contains("--maximized", StringComparer.OrdinalIgnoreCase))
                WindowState = WindowState.Maximized;
            OpenUpdateModal();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(500);
            CaptureWindow(commandLineArguments[updateCaptureArgumentIndex + 1]);
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        var castCaptureArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--capture-cast-ui");
        if (castCaptureArgumentIndex >= 0 && castCaptureArgumentIndex + 1 < commandLineArguments.Length)
        {
            await PrepareModalCaptureAsync(commandLineArguments);
            if (commandLineArguments.Contains("--maximized", StringComparer.OrdinalIgnoreCase))
                WindowState = WindowState.Maximized;
            OpenCastPanel();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(500);
            CaptureWindow(commandLineArguments[castCaptureArgumentIndex + 1]);
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        var signalRoutingCaptureArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--capture-signal-routing-ui");
        if (signalRoutingCaptureArgumentIndex >= 0 && signalRoutingCaptureArgumentIndex + 1 < commandLineArguments.Length)
        {
            PrepareSignalRoutingPreview();
            if (commandLineArguments.Contains("--maximized", StringComparer.OrdinalIgnoreCase))
                WindowState = WindowState.Maximized;
            OpenSignalRoutingPanel();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(500);
            CaptureWindow(commandLineArguments[signalRoutingCaptureArgumentIndex + 1]);
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        var channelHealthCaptureArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--capture-channel-health-ui");
        if (channelHealthCaptureArgumentIndex >= 0 && channelHealthCaptureArgumentIndex + 1 < commandLineArguments.Length)
        {
            PrepareSignalRoutingPreview();
            ApplyGuideSchedule(CreatePreviewGuideSchedule(_channels.Take(1).ToList()));
            if (commandLineArguments.Contains("--maximized", StringComparer.OrdinalIgnoreCase))
                WindowState = WindowState.Maximized;
            OpenChannelHealth_Click(this, new RoutedEventArgs());
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(500);
            CaptureWindow(commandLineArguments[channelHealthCaptureArgumentIndex + 1]);
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        var dvrCaptureArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--capture-dvr-ui");
        if (dvrCaptureArgumentIndex >= 0 && dvrCaptureArgumentIndex + 1 < commandLineArguments.Length)
        {
            await PrepareModalCaptureAsync(commandLineArguments);
            if (_channels.Count > 0)
            {
                var previewStart = DateTimeOffset.UtcNow.AddMinutes(38);
                var previewSeriesRuleId = Guid.NewGuid();
                _settings.SmartDvr = new SmartDvrPreferences
                {
                    StartPaddingMinutes = 2,
                    EndPaddingMinutes = 5,
                    StorageReserveGigabytes = 10,
                    DefaultPriority = DvrSchedulePriority.Normal,
                    BackgroundRecordingEnabled = true,
                    WakeForRecordings = true,
                    LiveTimeshiftEnabled = true,
                    LiveTimeshiftMinutes = 60,
                    MaximumRecoveryAttempts = 3
                };
                _settings.SeriesRecordingRules =
                [
                    new SeriesRecordingRule
                    {
                        Id = previewSeriesRuleId,
                        ChannelKey = _channels[0].StableKey,
                        ChannelName = _channels[0].Name,
                        ProgramTitle = "Prime Time Live",
                        Priority = DvrSchedulePriority.High,
                        StartPaddingMinutes = 2,
                        EndPaddingMinutes = 5,
                        EpisodeSelection = DvrEpisodeSelection.NewEpisodesOnly,
                        KeepLatestCount = 3,
                        AnyChannel = true
                    }
                ];
                _settings.ScheduledRecordings =
                [
                    new ScheduledRecording
                    {
                        ChannelKey = _channels[0].StableKey,
                        ChannelName = _channels[0].Name,
                        ProgramTitle = "Prime Time Live",
                        ProgrammeStartUtc = previewStart,
                        ProgrammeStopUtc = previewStart.AddHours(2),
                        StartUtc = previewStart.AddMinutes(-2),
                        StopUtc = previewStart.AddHours(2).AddMinutes(5),
                        StartPaddingMinutes = 2,
                        EndPaddingMinutes = 5,
                        Priority = DvrSchedulePriority.High,
                        SeriesRuleId = previewSeriesRuleId,
                        EpisodeLabel = "S02E05",
                        IsNewEpisode = true
                    },
                    new ScheduledRecording
                    {
                        ChannelKey = _channels[Math.Min(1, _channels.Count - 1)].StableKey,
                        ChannelName = _channels[Math.Min(1, _channels.Count - 1)].Name,
                        ProgramTitle = "Championship Pregame",
                        StartUtc = previewStart.AddMinutes(30),
                        StopUtc = previewStart.AddHours(1.5),
                        Priority = DvrSchedulePriority.Normal
                    }
                ];
                ApplySmartDvrSettingsToControls();
                _dvrWakeSignature = string.Empty;
                RefreshDvrWakeTimer();
            }
            if (commandLineArguments.Contains("--maximized", StringComparer.OrdinalIgnoreCase))
                WindowState = WindowState.Maximized;
            OpenDvrPanel();
            var previewRecording = new DvrLibraryItem(
                Path.Combine(Path.GetTempPath(), "Prime Time Live.ts"),
                DateTimeOffset.UtcNow.AddDays(-1),
                4_831_838_208);
            DvrLibraryList.ItemsSource = new[]
            {
                new DvrLibraryRow(
                    previewRecording,
                    DvrRecordingService.CreateLibraryKey(previewRecording.FilePath),
                    new DvrPlaybackProgress
                    {
                        PositionMilliseconds = 2_145_000,
                        DurationMilliseconds = 6_720_000,
                        UpdatedUtc = DateTimeOffset.UtcNow
                    },
                    false,
                    true)
            };
            DvrLibraryEmptyText.Visibility = Visibility.Collapsed;
            DvrStorageText.Text = "186.4 GB free of 930.9 GB  •  12 recordings use 41.7 GB";
            DvrStorageProgress.Value = 80;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (commandLineArguments.Contains("--lower-dvr", StringComparer.OrdinalIgnoreCase))
            {
                DvrScrollViewer.ScrollToVerticalOffset(430);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            }
            await Task.Delay(500);
            CaptureWindow(commandLineArguments[dvrCaptureArgumentIndex + 1]);
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        var importCaptureArgumentIndex = Array.FindIndex(commandLineArguments, argument => argument == "--capture-import-ui");
        if (importCaptureArgumentIndex >= 0 && importCaptureArgumentIndex + 1 < commandLineArguments.Length)
        {
            await PrepareModalCaptureAsync(commandLineArguments);
            if (commandLineArguments.Contains("--guide-tab", StringComparer.OrdinalIgnoreCase))
                SourceTabs.SelectedItem = GuideSourceTab;
            if (commandLineArguments.Contains("--plex-tab", StringComparer.OrdinalIgnoreCase))
                SourceTabs.SelectedItem = PlexSourceTab;
            if (commandLineArguments.Contains("--emby-tab", StringComparer.OrdinalIgnoreCase))
                SourceTabs.SelectedItem = EmbySourceTab;
            if (commandLineArguments.Contains("--maximized", StringComparer.OrdinalIgnoreCase))
                WindowState = WindowState.Maximized;
            ShowModal(ImportOverlay);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(500);
            CaptureWindow(commandLineArguments[importCaptureArgumentIndex + 1]);
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        _ = CheckForUpdatesOnStartupAsync();

        var startupRefreshProbeIndex = Array.FindIndex(commandLineArguments, argument => argument == "--startup-refresh-probe");
        var startupRefreshProbePath = startupRefreshProbeIndex >= 0 && startupRefreshProbeIndex + 1 < commandLineArguments.Length
            ? commandLineArguments[startupRefreshProbeIndex + 1]
            : null;
        var commandLineFile = commandLineArguments.FirstOrDefault(argument =>
            File.Exists(argument) && IsPlaylistFileExtension(Path.GetExtension(argument)));
        if (!string.IsNullOrWhiteSpace(commandLineFile)) _pendingSessionRecovery = null;
        _resumeLastChannelPending = (_settings.ResumeLastChannelOnStartup || _pendingSessionRecovery is not null) && string.IsNullOrWhiteSpace(commandLineFile);

        if (!string.IsNullOrWhiteSpace(commandLineFile))
        {
            FilePathBox.Text = commandLineFile;
            var loaded = await LoadPlaylistAsync(
                (progress, token) => _playlistSource.LoadFileAsync(commandLineFile, progress, token),
                "file",
                commandLineFile);
            if (startupRefreshProbePath is not null) await CompleteStartupRefreshProbeAsync(startupRefreshProbePath, loaded);
            return;
        }

        await PopulateLastSourceFieldsAsync();
        if (_settings.PlaylistSources.Any(source => source.IsEnabled))
        {
            var loaded = await LoadUnifiedPlaylistSourcesAsync(startup: true);
            if (startupRefreshProbePath is not null) await CompleteStartupRefreshProbeAsync(startupRefreshProbePath, loaded);
            return;
        }

        ShowModal(ImportOverlay);
        if (startupRefreshProbePath is not null) await CompleteStartupRefreshProbeAsync(startupRefreshProbePath, false);
    }

    private async Task PopulateLastSourceFieldsAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.LastSourceType) || string.IsNullOrWhiteSpace(_settings.LastSource)) return;
        switch (_settings.LastSourceType)
        {
            case "file":
                FilePathBox.Text = _settings.LastSource;
                break;
            case "url":
                PlaylistUrlBox.Text = _settings.LastSource;
                break;
            case "xtream":
                var credentials = await _xtreamCredentialStore.TryLoadAsync(_settings.LastSource);
                if (credentials is null) break;
                XtreamServerBox.Text = credentials.Server;
                XtreamUsernameBox.Text = credentials.Username;
                XtreamPasswordBox.Password = credentials.Password;
                break;
            case "plex":
                PlexServerBox.Text = _settings.LastSource;
                break;
            case "emby":
                EmbyServerBox.Text = _settings.LastSource;
                break;
        }
    }

    private async Task<PlaylistResult> LoadPlaylistSourceDefinitionAsync(
        PlaylistSourceDefinition source,
        IProgress<PlaylistProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (source.SourceType is "plex" or "emby") PremiumAccessPolicy.RequireMediaCenters(_premiumAccess);
        return source.SourceType switch
        {
            "file" => await _playlistSource.LoadFileAsync(source.SourceValue, progress, cancellationToken),
            "url" => await _playlistSource.LoadUrlAsync(source.SourceValue, progress, cancellationToken),
            "xtream" => await LoadSavedXtreamSourceAsync(source.SourceValue, progress, cancellationToken),
            "plex" or "emby" => await _mediaCenterSource.LoadSavedAsync(
                source.SourceType,
                source.SourceValue,
                progress,
                cancellationToken),
            _ => throw new InvalidDataException("This saved playlist type is not supported.")
        };
    }

    private async Task<PlaylistResult> LoadSavedXtreamSourceAsync(
        string sourceValue,
        IProgress<PlaylistProgress>? progress,
        CancellationToken cancellationToken)
    {
        var credentials = await _xtreamCredentialStore.TryLoadAsync(sourceValue, cancellationToken);
        if (credentials is null)
            throw new InvalidOperationException("Sign in to this Xtream account once to enable secure automatic refresh.");
        return await _xtreamSource.LoadAsync(
            credentials.Server,
            credentials.Username,
            credentials.Password,
            progress,
            cancellationToken);
    }

    private async Task<bool> LoadUnifiedPlaylistSourcesAsync(bool startup, bool closeImportOnSuccess = false)
    {
        if (_isLoading) return false;
        var configuredSources = (_settings.PlaylistSources ?? [])
            .Where(source => source.IsEnabled)
            .OrderBy(source => source.SortOrder)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var enabledSources = configuredSources
            .Where(source => _premiumAccess.CanUseMediaCenters || source.SourceType is not ("plex" or "emby"))
            .ToList();
        if (enabledSources.Count == 0 && configuredSources.Count > 0)
        {
            EnsureMediaCenterAccess(configuredSources[0].TypeLabel);
            return false;
        }
        if (enabledSources.Count == 0) return false;

        _isLoading = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var token = _loadCancellation.Token;
        var previousKeys = _channels.Select(channel => channel.StableKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ImportProgress.Visibility = Visibility.Visible;
        ImportProgress.IsIndeterminate = true;
        ImportStatusText.Text = startup ? "Refreshing your sources…" : "Refreshing enabled sources…";
        ImportDetailText.Text = $"Preparing one library from {enabledSources.Count:N0} saved source{(enabledSources.Count == 1 ? string.Empty : "s")}";
        FooterStatusDot.Fill = WarningBrush;

        var progress = new Progress<PlaylistSourceRefreshProgress>(value =>
        {
            var stage = $"Source {value.SourceNumber:N0}/{value.SourceCount:N0} • {value.Message}";
            ImportStatusText.Text = value.Message;
            ImportDetailText.Text = stage;
            FooterStatusText.Text = stage;
            if (PlaylistHealthOverlay.Visibility == Visibility.Visible)
            {
                PlaylistHealthStateText.Text = value.Message;
                PlaylistHealthDetailText.Text = stage;
            }
        });

        try
        {
            var summary = await _playlistSourceRefresh.RefreshAsync(
                enabledSources,
                source => !startup || source.RefreshOnStartup,
                progress,
                token);
            var now = DateTimeOffset.UtcNow;
            _settings.PlaylistHealth ??= new PlaylistHealthPreferences();
            _settings.PlaylistHealth.LastAttemptUtc = now;

            if (!summary.HasPlaylist || summary.Merge is null)
            {
                _settings.PlaylistHealth.LastError = summary.FailedSourceCount == 1
                    ? "The saved source could not be loaded and no encrypted copy was available."
                    : $"{summary.FailedSourceCount:N0} saved sources could not be loaded and had no encrypted copies.";
                _settings.PlaylistHealth.UsedCachedFallback = false;
                await _settingsStore.SaveAsync(_settings);
                RefreshPlaylistSourceList();
                ImportStatusText.Text = "Saved sources need attention";
                ImportDetailText.Text = _settings.PlaylistHealth.LastError;
                FooterStatusDot.Fill = ErrorBrush;
                FooterStatusText.Text = "No saved playlist source could be opened";
                if (startup && !_backgroundLaunch)
                {
                    SourceTabs.SelectedItem = SavedSourcesTab;
                    ShowModal(ImportOverlay);
                }
                return false;
            }

            var playlist = summary.Merge.Playlist;
            var availableOutcomes = summary.Outcomes
                .Where(outcome => outcome.Playlist is not null)
                .OrderBy(outcome => outcome.Source.SortOrder)
                .ToList();
            if (availableOutcomes.Count == 1)
            {
                _activePlaylistSourceType = availableOutcomes[0].Source.SourceType;
                _activePlaylistSourceValue = availableOutcomes[0].Source.SourceValue;
            }
            else
            {
                _activePlaylistSourceType = "multi";
                _activePlaylistSourceValue = string.Join('|', availableOutcomes.Select(outcome => outcome.Source.Id.ToString("N")));
            }

            ApplyPlaylist(playlist);
            var currentKeys = _channels.Select(channel => channel.StableKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _settings.LastPlaylistRefreshUtc = summary.LiveSourceCount > 0
                ? now
                : enabledSources.Where(source => source.LastSuccessUtc is not null)
                    .Select(source => source.LastSuccessUtc!.Value)
                    .DefaultIfEmpty(now)
                    .Max();
            _settings.PlaylistHealth.LastSuccessUtc = _settings.LastPlaylistRefreshUtc;
            var attentionCount = summary.FailedSourceCount + summary.FallbackSourceCount;
            _settings.PlaylistHealth.LastError = attentionCount == 0
                ? null
                : $"{attentionCount:N0} of {enabledSources.Count:N0} sources need attention.";
            _settings.PlaylistHealth.ChannelCount = playlist.Channels.Count;
            _settings.PlaylistHealth.AddedChannels = previousKeys.Count == 0 ? 0 : currentKeys.Count(key => !previousKeys.Contains(key));
            _settings.PlaylistHealth.RemovedChannels = previousKeys.Count == 0 ? 0 : previousKeys.Count(key => !currentKeys.Contains(key));
            _settings.PlaylistHealth.UsedCachedFallback = summary.CachedSourceCount > 0;
            await _settingsStore.SaveAsync(_settings);
            RefreshPlaylistSourceList();
            UpdatePlaylistHealthUi();
            if (closeImportOnSuccess) HideModal(ImportOverlay);

            var refreshedAt = FormatPlaylistTime(_settings.LastPlaylistRefreshUtc ?? now);
            SourceRefreshText.Text = summary.LiveSourceCount == 0
                ? $"Offline copies • {availableOutcomes.Count:N0} sources"
                : $"Updated {refreshedAt} • {summary.LiveSourceCount:N0}/{enabledSources.Count:N0} sources live";
            FooterStatusDot.Fill = attentionCount == 0 ? LiveBrush : WarningBrush;
            FooterStatusText.Text = availableOutcomes.Count == 1
                ? $"Playlist ready • {playlist.Channels.Count:N0} channels"
                : $"Unified library ready • {playlist.Channels.Count:N0} channels from {availableOutcomes.Count:N0} sources";
            ImportStatusText.Text = attentionCount == 0 ? "Every enabled source is ready" : "Library ready with source alerts";
            ImportDetailText.Text = $"{playlist.Channels.Count:N0} channels • {summary.Merge.DuplicateChannelCount:N0} exact duplicates removed";
            _ = ConfigureGuideForPlaylistAsync(playlist, _activePlaylistSourceType, _activePlaylistSourceValue);
            return true;
        }
        catch (OperationCanceledException)
        {
            ImportStatusText.Text = "Source refresh cancelled";
            ImportDetailText.Text = "The existing library was left unchanged.";
            return false;
        }
        catch (Exception exception)
        {
            ImportStatusText.Text = "Could not refresh saved sources";
            ImportDetailText.Text = SafeErrorMessage(exception);
            FooterStatusDot.Fill = ErrorBrush;
            FooterStatusText.Text = "Saved source refresh failed";
            return false;
        }
        finally
        {
            ImportProgress.Visibility = Visibility.Collapsed;
            _isLoading = false;
            RefreshPlaylistSourceList();
        }
    }

    private void RefreshPlaylistSourceList()
    {
        if (PlaylistSourceList is null) return;
        var sources = (_settings.PlaylistSources ?? [])
            .OrderBy(source => source.SortOrder)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        PlaylistSourceList.ItemsSource = null;
        PlaylistSourceList.ItemsSource = sources;
        SourceManagerEmptyText.Visibility = sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var enabled = sources.Count(source => source.IsEnabled);
        SourceManagerSummaryText.Text = sources.Count == 0
            ? "Add an M3U, Xtream, Plex, or Emby source to begin."
            : $"{sources.Count:N0} saved • {enabled:N0} enabled • startup refresh is set per source";
        SourceRefreshEnabledButton.IsEnabled = !_isLoading && enabled > 0;
    }

    private async Task CompleteStartupRefreshProbeAsync(string reportPath, bool loaded)
    {
        var report = new
        {
            Loaded = loaded,
            Channels = _channels.Count,
            Source = SourceNameText.Text,
            RefreshStatus = SourceRefreshText.Text,
            FooterStatus = FooterStatusText.Text,
            UsedCachedPlaylist = SourceRefreshText.Text.StartsWith("Offline copy", StringComparison.Ordinal)
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Close();
        Application.Current.Shutdown(loaded ? 0 : 1);
    }

    private void CreatePlaybackEngine()
    {
        _playback?.Dispose();
        _playback = new NativePlaybackEngine(
            _settings.Playback,
            _settings.SmartDvr.LiveTimeshiftEnabled,
            _settings.SmartDvr.LiveTimeshiftMinutes);
        _playback.StatusChanged += Playback_StatusChanged;
        VideoSurface.MediaPlayer = _playback.MediaPlayer;
        VolumeSlider.Value = _playback.MediaPlayer.Volume;
        DecodeValue.Text = _settings.Playback.HardwareDecoding ? "Hardware auto" : "Software";
        CacheValue.Text = $"{_settings.Playback.CacheMilliseconds / 1000d:0.0} seconds";
    }

    private async Task<bool> LoadPlaylistAsync(
        Func<IProgress<PlaylistProgress>, CancellationToken, Task<PlaylistResult>> loader,
        string sourceType,
        string sourceValue,
        bool allowCachedFallback = false)
    {
        if (_isLoading) return false;
        _isLoading = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();

        ImportProgress.Visibility = Visibility.Visible;
        ImportProgress.IsIndeterminate = true;
        ImportStatusText.Text = "Connecting…";
        ImportDetailText.Text = "Preparing the native channel index";
        FooterStatusDot.Fill = WarningBrush;
        _settings.PlaylistHealth ??= new PlaylistHealthPreferences();
        _settings.PlaylistHealth.LastAttemptUtc = DateTimeOffset.UtcNow;
        _settings.PlaylistHealth.UsedCachedFallback = false;

        CachedPlaylist? previousPlaylist = null;
        try
        {
            previousPlaylist = await _playlistCache.TryLoadAsync(sourceType, sourceValue, _loadCancellation.Token);
        }
        catch
        {
            // Playlist comparison is helpful telemetry, never a loading requirement.
        }

        var progress = new Progress<PlaylistProgress>(value =>
        {
            ImportStatusText.Text = value.Message;
            ImportDetailText.Text = value.ChannelsParsed == 0
                ? "Waiting for the provider"
                : $"{value.ChannelsParsed:N0} entries processed without loading video data";
            FooterStatusText.Text = value.Message;
        });

        try
        {
            var result = await loader(progress, _loadCancellation.Token);
            _activePlaylistSourceType = sourceType;
            _activePlaylistSourceValue = sourceValue;
            ApplyPlaylist(result);
            _settings.LastSourceType = sourceType;
            _settings.LastSource = sourceValue;
            _settings.LastPlaylistRefreshUtc = DateTimeOffset.UtcNow;
            var previousKeys = previousPlaylist?.Playlist.Channels
                .Select(SmartSignalRoutingPolicy.CreateLogicalChannelKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var currentKeys = _channels
                .Select(channel => channel.SignalRouteKey ?? SmartSignalRoutingPolicy.CreateLogicalChannelKey(channel))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _settings.PlaylistHealth.LastSuccessUtc = _settings.LastPlaylistRefreshUtc;
            _settings.PlaylistHealth.LastError = null;
            _settings.PlaylistHealth.ChannelCount = result.Channels.Count;
            _settings.PlaylistHealth.AddedChannels = previousKeys is null ? 0 : currentKeys.Count(key => !previousKeys.Contains(key));
            _settings.PlaylistHealth.RemovedChannels = previousKeys is null ? 0 : previousKeys.Count(key => !currentKeys.Contains(key));
            _settings.PlaylistHealth.UsedCachedFallback = false;
            var playlistSource = PlaylistSourcePolicy.GetOrAdd(_settings, sourceType, sourceValue, result.DisplayName);
            playlistSource.LastAttemptUtc = _settings.PlaylistHealth.LastAttemptUtc;
            playlistSource.LastSuccessUtc = _settings.PlaylistHealth.LastSuccessUtc;
            playlistSource.LastError = null;
            playlistSource.ChannelCount = result.Channels.Count;
            playlistSource.UsedCachedFallback = false;
            try
            {
                await _playlistCache.SaveAsync(sourceType, sourceValue, result, _loadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A cache failure must never prevent a freshly loaded playlist from being used.
            }
            await _settingsStore.SaveAsync(_settings);
            HideModal(ImportOverlay);
            FooterStatusDot.Fill = LiveBrush;
            var refreshedAt = FormatPlaylistTime(_settings.LastPlaylistRefreshUtc.Value);
            SourceRefreshText.Text = $"Updated {refreshedAt} • auto-refresh on launch";
            FooterStatusText.Text = $"Playlist refreshed • {result.Channels.Count:N0} channels • {refreshedAt}";
            _ = ConfigureGuideForPlaylistAsync(result, sourceType, sourceValue);
            return true;
        }
        catch (OperationCanceledException)
        {
            ImportStatusText.Text = "Import cancelled";
            ImportDetailText.Text = "No changes were made.";
            return false;
        }
        catch (Exception exception)
        {
            if (allowCachedFallback)
            {
                var cached = await _playlistCache.TryLoadAsync(sourceType, sourceValue);
                if (cached is not null)
                {
                    _activePlaylistSourceType = sourceType;
                    _activePlaylistSourceValue = sourceValue;
                    ApplyPlaylist(cached.Playlist);
                    HideModal(ImportOverlay);
                    FooterStatusDot.Fill = WarningBrush;
                    var refreshedAt = FormatPlaylistTime(cached.Playlist.LoadedAt);
                    SourceRefreshText.Text = $"Offline copy • refreshed {refreshedAt}";
                    FooterStatusText.Text = $"Provider unavailable • using {cached.Playlist.Channels.Count:N0} cached channels";
                    ImportStatusText.Text = "Using saved playlist";
                    ImportDetailText.Text = $"The provider could not be reached. Showing the encrypted copy refreshed {refreshedAt}.";
                    _settings.PlaylistHealth.LastError = SafeErrorMessage(exception);
                    _settings.PlaylistHealth.ChannelCount = cached.Playlist.Channels.Count;
                    _settings.PlaylistHealth.UsedCachedFallback = true;
                    var playlistSource = PlaylistSourcePolicy.GetOrAdd(_settings, sourceType, sourceValue, cached.Playlist.DisplayName);
                    playlistSource.LastAttemptUtc = _settings.PlaylistHealth.LastAttemptUtc;
                    playlistSource.LastSuccessUtc ??= cached.Playlist.LoadedAt;
                    playlistSource.LastError = _settings.PlaylistHealth.LastError;
                    playlistSource.ChannelCount = cached.Playlist.Channels.Count;
                    playlistSource.UsedCachedFallback = true;
                    await _settingsStore.SaveAsync(_settings);
                    _ = ConfigureGuideForPlaylistAsync(cached.Playlist, sourceType, sourceValue);
                    return true;
                }
            }

            ImportStatusText.Text = "Could not connect";
            ImportDetailText.Text = SafeErrorMessage(exception);
            FooterStatusDot.Fill = ErrorBrush;
            FooterStatusText.Text = "Playlist connection failed";
            SourceRefreshText.Text = "Automatic refresh needs attention";
            _settings.PlaylistHealth.LastError = SafeErrorMessage(exception);
            _settings.PlaylistHealth.UsedCachedFallback = false;
            var failedSource = PlaylistSourcePolicy.Find(_settings, sourceType, sourceValue);
            if (failedSource is not null)
            {
                failedSource.LastAttemptUtc = _settings.PlaylistHealth.LastAttemptUtc;
                failedSource.LastError = _settings.PlaylistHealth.LastError;
                failedSource.UsedCachedFallback = false;
            }
            await _settingsStore.SaveAsync(_settings);
            return false;
        }
        finally
        {
            ImportProgress.Visibility = Visibility.Collapsed;
            _isLoading = false;
            UpdatePlaylistHealthUi();
            RefreshPlaylistSourceList();
        }
    }

    private static string FormatPlaylistTime(DateTimeOffset timestamp)
    {
        var local = timestamp.ToLocalTime();
        return local.Date == DateTime.Today
            ? $"today at {local:h:mm tt}"
            : local.ToString("MMM d 'at' h:mm tt");
    }

    private void UpdatePlaylistHealthUi()
    {
        if (PlaylistHealthStateText is null) return;
        var health = _settings.PlaylistHealth ?? new PlaylistHealthPreferences();
        PlaylistHealthCountText.Text = $"{health.ChannelCount:N0} channels";
        PlaylistHealthChangesText.Text = health.AddedChannels == 0 && health.RemovedChannels == 0
            ? health.LastSuccessUtc is null ? "No comparison yet" : "No channel changes detected"
            : $"+{health.AddedChannels:N0} added  •  −{health.RemovedChannels:N0} removed";
        PlaylistHealthTimeText.Text = health.LastSuccessUtc is null
            ? "Not yet"
            : FormatPlaylistTime(health.LastSuccessUtc.Value);
        PlaylistHealthCacheText.Text = health.LastSuccessUtc is null
            ? "Encrypted fallback ready after first load"
            : health.UsedCachedFallback ? "Current library includes a protected offline copy" : "Encrypted fallback verified and ready";
        PlaylistHealthErrorText.Visibility = string.IsNullOrWhiteSpace(health.LastError) ? Visibility.Collapsed : Visibility.Visible;
        PlaylistHealthErrorText.Text = string.IsNullOrWhiteSpace(health.LastError) ? string.Empty : $"Latest provider response: {health.LastError}";

        if (health.LastSuccessUtc is null)
        {
            PlaylistHealthGlyph.Text = "○";
            PlaylistHealthStateText.Text = "Waiting for a playlist";
            PlaylistHealthDetailText.Text = "Connect an M3U, Xtream, Plex, or Emby source to enable launch verification.";
        }
        else if (health.UsedCachedFallback)
        {
            PlaylistHealthGlyph.Text = "↻";
            PlaylistHealthStateText.Text = "Protected by the last working copy";
            PlaylistHealthDetailText.Text = "One or more sources are using protected offline data, so the available library remains online.";
        }
        else
        {
            PlaylistHealthGlyph.Text = "✓";
            PlaylistHealthStateText.Text = "Playlist verified";
            PlaylistHealthDetailText.Text = "The provider responded successfully and the encrypted fallback was refreshed.";
        }
    }

    private void OpenPlaylistHealth_Click(object sender, RoutedEventArgs e)
    {
        UpdatePlaylistHealthUi();
        ShowModal(PlaylistHealthOverlay);
    }

    private void ClosePlaylistHealth_Click(object sender, RoutedEventArgs e) => HideModal(PlaylistHealthOverlay);

    private void OpenChannelHealth_Click(object sender, RoutedEventArgs e)
    {
        if (_signalRoutes.Count == 0)
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "Connect a playlist before opening channel health";
            return;
        }

        var summary = ChannelHealthPolicy.Analyze(
            _signalRoutes,
            _settings.SignalRouting,
            channel => GetGuideProgrammes(channel).Count > 0);
        _channelHealthRows = summary.Rows;
        _channelHealthView = CollectionViewSource.GetDefaultView(_channelHealthRows);
        _channelHealthView.Filter = FilterChannelHealth;
        ChannelHealthList.ItemsSource = _channelHealthView;
        HealthAttentionCount.Text = summary.NeedsAttention.ToString("N0");
        HealthGuideCount.Text = summary.MissingGuide.ToString("N0");
        HealthDuplicateCount.Text = summary.Duplicates.ToString("N0");
        HealthReplayCount.Text = summary.ReplayReady.ToString("N0");
        HealthChannelSearchBox.Clear();
        HealthChannelFilterBox.SelectedIndex = 0;
        _channelHealthFilter = ChannelHealthFilter.All;
        RefreshChannelHealthView();
        ShowModal(ChannelHealthOverlay);
    }

    private void CloseChannelHealth_Click(object sender, RoutedEventArgs e) => HideModal(ChannelHealthOverlay);

    private void HealthChannelSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshChannelHealthView();

    private void HealthChannelFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HealthChannelFilterBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
            Enum.TryParse<ChannelHealthFilter>(tag, true, out var filter))
            _channelHealthFilter = filter;
        RefreshChannelHealthView();
    }

    private bool FilterChannelHealth(object item)
    {
        if (item is not ChannelHealthRow row || !ChannelHealthPolicy.Matches(row, _channelHealthFilter)) return false;
        var query = HealthChannelSearchBox.Text.Trim();
        return query.Length == 0 || row.SearchText.Contains(query.ToUpperInvariant(), StringComparison.Ordinal);
    }

    private void RefreshChannelHealthView()
    {
        if (ChannelHealthList is null) return;
        _channelHealthView?.Refresh();
        var visible = ChannelHealthList.Items.Count;
        HealthVisibleCount.Text = $"{visible:N0} OF {_channelHealthRows.Count:N0} LIVE CHANNELS";
    }

    private void TuneChannelHealth_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChannelHealthRow row) return;
        HideModal(ChannelHealthOverlay);
        WatchFromGuide(row.Channel);
    }

    private async void ReviewChannelHealth_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChannelHealthRow row) return;
        if (row.MissingGuide)
        {
            HideModal(ChannelHealthOverlay);
            await OpenMappingEditorAsync(row.Channel);
            return;
        }
        if (row.HasDuplicates || row.Unreliable)
        {
            OpenSignalRoutingPanel(row.RouteKey);
            return;
        }
        HideModal(ChannelHealthOverlay);
        FooterStatusDot.Fill = row.MissingLogo ? WarningBrush : LiveBrush;
        FooterStatusText.Text = row.MissingLogo
            ? $"{row.ChannelName} has no logo in the connected playlist"
            : $"{row.ChannelName} has no route issue that needs action";
    }

    private async void RefreshPlaylistNow_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if ((_settings.PlaylistSources ?? []).Any(source => source.IsEnabled))
        {
            PlaylistHealthGlyph.Text = "↻";
            PlaylistHealthStateText.Text = "Refreshing every enabled source…";
            PlaylistHealthDetailText.Text = "Each provider is checked independently, with its encrypted offline copy ready if needed.";
            var unifiedLoaded = await LoadUnifiedPlaylistSourcesAsync(startup: false);
            UpdatePlaylistHealthUi();
            if (!unifiedLoaded) PlaylistHealthStateText.Text = "Playlist refresh needs attention";
            return;
        }

        if (string.IsNullOrWhiteSpace(_activePlaylistSourceType) || string.IsNullOrWhiteSpace(_activePlaylistSourceValue))
        {
            HideModal(PlaylistHealthOverlay);
            RefreshPlaylistSourceList();
            ShowModal(ImportOverlay);
            return;
        }

        PlaylistHealthGlyph.Text = "↻";
        PlaylistHealthStateText.Text = "Refreshing playlist…";
        PlaylistHealthDetailText.Text = "Checking the provider and comparing it with the last working copy.";
        var loaded = _activePlaylistSourceType switch
        {
            "file" => await LoadPlaylistAsync(
                (progress, token) => _playlistSource.LoadFileAsync(_activePlaylistSourceValue, progress, token),
                "file", _activePlaylistSourceValue, allowCachedFallback: true),
            "url" => await LoadPlaylistAsync(
                (progress, token) => _playlistSource.LoadUrlAsync(_activePlaylistSourceValue, progress, token),
                "url", _activePlaylistSourceValue, allowCachedFallback: true),
            "xtream" => await RefreshSavedXtreamPlaylistAsync(_activePlaylistSourceValue),
            "plex" or "emby" => await RefreshSavedMediaCenterAsync(
                _activePlaylistSourceType,
                _activePlaylistSourceValue),
            _ => false
        };
        UpdatePlaylistHealthUi();
        if (!loaded) PlaylistHealthStateText.Text = "Playlist refresh needs attention";
    }

    private async Task<bool> RefreshSavedXtreamPlaylistAsync(string sourceValue)
    {
        var credentials = await _xtreamCredentialStore.TryLoadAsync(sourceValue);
        if (credentials is null) return false;
        return await LoadPlaylistAsync(
            (progress, token) => _xtreamSource.LoadAsync(credentials.Server, credentials.Username, credentials.Password, progress, token),
            "xtream", sourceValue, allowCachedFallback: true);
    }

    private Task<bool> RefreshSavedMediaCenterAsync(string provider, string sourceValue)
    {
        if (!EnsureMediaCenterAccess(provider)) return Task.FromResult(false);
        return LoadPlaylistAsync(
            (progress, token) => _mediaCenterSource.LoadSavedAsync(provider, sourceValue, progress, token),
            provider,
            sourceValue,
            allowCachedFallback: true);
    }

    private static bool IsPlaylistFileExtension(string extension) =>
        extension.Equals(".m3u", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);

    private void ApplyPlaylist(PlaylistResult result)
    {
        ResetArtworkLoading();
        var rebuildMultiview = _multiviewSession is not null;
        _multiviewSession?.Dispose();
        _multiviewSession = null;
        _allChannels = result.Channels;
        _signalRouteCandidates = [];
        _signalRoutes = SmartSignalRoutingPolicy.BuildRoutes(_allChannels, _settings.SignalRouting);
        _signalRoutesByKey = _signalRoutes.ToDictionary(route => route.Key, StringComparer.OrdinalIgnoreCase);
        _signalRoutesByFeedKey = _signalRoutes
            .SelectMany(route => route.Feeds.Select(feed => new KeyValuePair<string, SignalRoute>(feed.StableKey, route)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var route in _signalRoutes.Where(route => route.Feeds.All(feed =>
                     !SmartSignalRoutingPolicy.Score(feed, _settings.SignalRouting).IsEligible)))
            SmartSignalRoutingPolicy.GetOrCreateHealth(_settings.SignalRouting, route.Representative, route.Key).Preference = SignalFeedPreference.Auto;
        _favoriteKeys = _favoriteKeys
            .Select(key => _signalRoutesByFeedKey.TryGetValue(key, out var route) ? route.Representative.StableKey : key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _settings.FavoriteChannelKeys = _favoriteKeys.Order(StringComparer.OrdinalIgnoreCase).ToList();
        _settings.RecentChannelKeys = (_settings.RecentChannelKeys ?? [])
            .Select(key => _signalRoutesByFeedKey.TryGetValue(key, out var route) ? route.Representative.StableKey : key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
        _channels = _signalRoutes.Select(route => route.Representative).ToList();
        foreach (var route in _signalRoutes)
            route.Representative.IsFavorite = route.Feeds.Any(channel => _favoriteKeys.Contains(channel.StableKey));
        _channelView = CollectionViewSource.GetDefaultView(_channels);
        _channelView.Filter = FilterChannel;
        _categoryFilter = string.Empty;
        _libraryBrowseSummary = MediaLibraryBrowsePolicy.Summarize(_channels);
        ApplyChannelGrouping();
        ChannelList.ItemsSource = _channelView;
        SourceNameText.Text = result.DisplayName;

        CategoryBox.Items.Clear();
        CategoryBox.Items.Add(new ComboBoxItem
        {
            Content = _libraryBrowseSummary.IsMediaCenterLibrary ? "All libraries" : "All groups",
            Tag = string.Empty
        });
        foreach (var group in _channels
                     .GroupBy(channel => channel.Group, StringComparer.OrdinalIgnoreCase)
                     .Select(group => new { group.Key, Count = group.Count() }))
        {
            CategoryBox.Items.Add(new ComboBoxItem { Content = $"{group.Key} ({group.Count:N0})", Tag = group.Key });
        }

        CategoryBox.SelectedIndex = 0;
        _libraryBrowseMode = MediaLibraryBrowseMode.All;
        AllFilter.IsChecked = true;
        ContinueWatchingFilter.Visibility = _libraryBrowseSummary.IsMediaCenterLibrary
            ? Visibility.Visible
            : Visibility.Collapsed;
        ContinueWatchingFilter.Content = $"Continue ({_libraryBrowseSummary.ContinueWatchingCount:N0})";
        RecentlyAddedFilter.Visibility = _libraryBrowseSummary.IsMediaCenterLibrary
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecentlyAddedFilter.Content = $"Recent ({_libraryBrowseSummary.RecentlyAddedCount:N0})";
        CatalogHeading.Text = _libraryBrowseSummary.IsMediaCenterLibrary ? "Library" : "Channels";
        SearchHint.Text = _libraryBrowseSummary.IsMediaCenterLibrary
            ? "Search titles, series, or libraries"
            : "Search channels or groups";
        RefreshChannelCount();
        var alternateFeedCount = Math.Max(0, _allChannels.Count - _channels.Count);
        NowPlayingSubheading.Text = alternateFeedCount == 0
            ? $"{_channels.Count:N0} channels ready • select one to tune"
            : $"{_channels.Count:N0} channels • {alternateFeedCount:N0} backup feeds ready";
        SignalRoutesButton.Visibility = _signalRoutes.Any(route => route.HasAlternates)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (rebuildMultiview && _multiviewMode)
        {
            EnsureMultiviewSession();
            UpdateMultiviewLayout();
        }
        TryResumeLastChannelAfterPlaylistLoad();
    }

    private SignalRoute? GetSignalRoute(ChannelItem? channel)
    {
        if (channel is null) return null;
        if (!string.IsNullOrWhiteSpace(channel.SignalRouteKey) &&
            _signalRoutesByKey.TryGetValue(channel.SignalRouteKey, out var route))
            return route;
        return _signalRoutesByFeedKey.TryGetValue(channel.StableKey, out route) ? route : null;
    }

    private ChannelItem ResolveBestSignalFeed(ChannelItem channel, IReadOnlySet<string>? excludedFeedKeys = null)
    {
        var route = GetSignalRoute(channel);
        if (route is null || channel.Kind != ChannelKind.Live || !_settings.SignalRouting.Enabled)
            return route?.Representative ?? channel;
        return SmartSignalRoutingPolicy.SelectBestFeed(route, _settings.SignalRouting, excludedFeedKeys)
               ?? route.Representative;
    }

    private ChannelItem? FindLogicalChannel(string? storedKey)
    {
        if (string.IsNullOrWhiteSpace(storedKey)) return null;
        var channel = _channels.FirstOrDefault(candidate =>
            candidate.StableKey.Equals(storedKey, StringComparison.OrdinalIgnoreCase));
        if (channel is not null) return channel;
        return _signalRoutesByFeedKey.TryGetValue(storedKey, out var route) ? route.Representative : null;
    }

    private string? GetLogicalChannelKey(ChannelItem? channel) =>
        channel is null ? null : GetSignalRoute(channel)?.Representative.StableKey ?? channel.StableKey;

    private void TryResumeLastChannelAfterPlaylistLoad()
    {
        if (!_resumeLastChannelPending) return;
        _resumeLastChannelPending = false;
        var recovery = _pendingSessionRecovery;
        var storedKey = recovery?.ChannelKey ?? _settings.LastChannelKey;

        var channel = string.IsNullOrWhiteSpace(storedKey) ? null : _channels.FirstOrDefault(item =>
            string.Equals(item.StableKey, storedKey, StringComparison.OrdinalIgnoreCase));
        if (channel is null && !string.IsNullOrWhiteSpace(storedKey) && _signalRoutesByFeedKey.TryGetValue(storedKey, out var legacyRoute))
            channel = legacyRoute.Representative;
        if (channel is not null)
        {
            ChannelList.SelectedItem = channel;
            ChannelList.ScrollIntoView(channel);
            if (!ReferenceEquals(_currentChannel, channel)) TuneChannel(channel);
        }

        if (recovery is not null)
        {
            RestoreInterruptedWorkspace(recovery);
            _pendingSessionRecovery = null;
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = channel is null
                ? "Recovered the workspace from the interrupted session"
                : $"Recovered interrupted session • {channel.Name}";
        }
        else if (channel is not null)
        {
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = $"Reopened your last channel • {channel.Name}";
        }
    }

    private void RestoreInterruptedWorkspace(SessionRecoverySnapshot recovery)
    {
        SearchBox.Text = recovery.ChannelSearch;
        GuideSearchBox.Text = recovery.GuideSearch;
        SelectComboByTag(GuideFilterBox, recovery.GuideFilter);
        _guideWindowStart = recovery.GuideWindowStart < DateTimeOffset.UtcNow.AddDays(-14) ||
                            recovery.GuideWindowStart > DateTimeOffset.UtcNow.AddDays(14)
            ? AlignTimelineStart(DateTimeOffset.UtcNow)
            : recovery.GuideWindowStart;
        SetGuideViewMode(recovery.GuideTimelineMode);
        switch (recovery.Workspace)
        {
            case "Guide": GuideNavigation.IsChecked = true; break;
            case "Multiview": MultiviewNavigation.IsChecked = true; break;
            case "Favorites": FavoritesNavigation.IsChecked = true; break;
            default: WatchNavigation.IsChecked = true; break;
        }
        if (recovery.WasFullscreen && !_backgroundLaunch) EnterFullscreen();
    }

    private SessionRecoverySnapshot CreateSessionSnapshot()
    {
        var workspace = _multiviewMode ? "Multiview"
            : GuideWorkspace.Visibility == Visibility.Visible ? "Guide"
            : _favoritesOnly ? "Favorites"
            : "Watch";
        var guideFilter = (GuideFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        return new SessionRecoverySnapshot(
            true,
            Guid.Empty,
            DateTimeOffset.UtcNow,
            GetLogicalChannelKey(_currentChannel) ?? _settings.LastChannelKey,
            workspace,
            SearchBox.Text,
            GuideSearchBox.Text,
            guideFilter,
            _guideWindowStart,
            _guideTimelineMode,
            _isFullscreen);
    }

    private async Task ConfigureGuideForPlaylistAsync(PlaylistResult playlist, string sourceType, string sourceValue)
    {
        try
        {
            _guideMappings = await _epgMappingStore.TryLoadAsync(sourceType, sourceValue);
            var stored = await _guideSourceStore.TryLoadAsync(sourceType, sourceValue);
            var sources = ParseGuideSources(stored);
            if (sources.Count == 0 && !string.IsNullOrWhiteSpace(playlist.GuideSource))
                sources = ParseGuideSources(playlist.GuideSource);
            if (sources.Count == 0 && IsPredominantlyUsPlaylist(playlist.Channels))
                sources = RecommendedUsGuideSources;

            _activeGuideSources = sources;
            if (sources.Count == 0)
            {
                GuideStatusText.Text = "No XMLTV source was advertised. Choose a guide source to add listings.";
                GuideCoverageText.Text = "Guide source needed";
                GuideEmptyState.Visibility = Visibility.Visible;
                return;
            }

            if (sources.SequenceEqual(RecommendedUsGuideSources, StringComparer.OrdinalIgnoreCase))
                GuideSourceBox.Text = string.Join(Environment.NewLine, RecommendedUsGuideSources);
            await RefreshGuideAsync(forceNetworkRefresh: false);
        }
        catch (Exception exception)
        {
            GuideStatusText.Text = $"Guide setup needs attention • {SafeGuideErrorMessage(exception)}";
        }
    }

    private async Task RefreshGuideAsync(bool forceNetworkRefresh)
    {
        if (_activeGuideSources.Count == 0 || _channels.Count == 0)
        {
            GuideStatusText.Text = "Choose an XMLTV guide source first.";
            return;
        }

        _guideCancellation?.Cancel();
        _guideCancellation?.Dispose();
        _guideCancellation = new CancellationTokenSource();
        var token = _guideCancellation.Token;
        var sourceKey = string.Join('\n', _activeGuideSources);
        _guideLoading = true;
        GuideStatusText.Text = "Preparing live programme listings…";
        FooterStatusText.Text = "Refreshing TV guide…";

        EpgSchedule? cached = null;
        try
        {
            cached = await _epgCache.TryLoadAsync(sourceKey, token);
            if (!forceNetworkRefresh && cached is not null && cached.LoadedAt > DateTimeOffset.UtcNow.AddHours(-6))
            {
                ApplyGuideSchedule(cached);
                SetGuideReadyStatus(cached, "encrypted cache");
                return;
            }

            var progress = new Progress<PlaylistProgress>(value =>
            {
                GuideStatusText.Text = value.Message;
                FooterStatusText.Text = value.Message;
            });
            var schedules = new List<EpgSchedule>();
            Exception? lastFailure = null;
            foreach (var source in _activeGuideSources)
            {
                try
                {
                    schedules.Add(await _epgSourceService.LoadAsync(
                        source,
                        _allChannels.Count > 0 ? _allChannels : _channels,
                        _guideMappings.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        progress,
                        token));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lastFailure = exception;
                }
            }

            if (schedules.Count == 0)
                throw lastFailure ?? new InvalidDataException("No programme listings were returned by the selected guide sources.");
            var schedule = schedules.Count == 1
                ? schedules[0]
                : EpgSchedule.Merge(schedules, $"{schedules.Count} combined XMLTV sources");
            await _epgCache.SaveAsync(sourceKey, schedule, token);
            ApplyGuideSchedule(schedule);
            SetGuideReadyStatus(schedule, "live refresh");
        }
        catch (OperationCanceledException)
        {
            // A newer guide refresh superseded this one.
        }
        catch (Exception exception)
        {
            if (cached is not null)
            {
                ApplyGuideSchedule(cached);
                SetGuideReadyStatus(cached, "offline encrypted copy");
                FooterStatusDot.Fill = WarningBrush;
            }
            else
            {
                GuideStatusText.Text = $"Guide unavailable • {SafeGuideErrorMessage(exception)}";
                GuideCoverageText.Text = "No listings loaded";
                GuideEmptyState.Visibility = Visibility.Visible;
                FooterStatusText.Text = "TV guide refresh needs attention";
            }
        }
        finally
        {
            _guideLoading = false;
        }
    }

    private void ApplyGuideSchedule(EpgSchedule schedule)
    {
        _guideSchedule = schedule;
        var now = DateTimeOffset.UtcNow;
        foreach (var channel in _channels)
            channel.ApplyGuide(channel.Kind == ChannelKind.Live ? GetGuideNowNext(channel, now) : null);

        _guideRows = _channels
            .Where(channel => channel.Kind == ChannelKind.Live)
            .Select(channel => BuildGuideRow(channel, schedule, now))
            .ToList();
        _guideView = CollectionViewSource.GetDefaultView(_guideRows);
        _guideView.Filter = FilterGuideRow;
        GuideList.ItemsSource = _guideView;
        RebuildGuideTimeline();
        RefreshGuideViews();
        var generatedSchedules = MaterializeSeriesRecordingRules();
        if (generatedSchedules > 0)
            _ = _settingsStore.SaveAsync(_settings);
        _lastGuidePresentationUpdate = now;
        UpdateCurrentGuide(_currentChannel);
    }

    private IReadOnlyList<EpgProgram> GetGuideProgrammes(ChannelItem channel)
    {
        if (_guideSchedule is null) return [];
        var feeds = GetSignalRoute(channel)?.Feeds ?? [channel];
        return SmartSignalRoutingPolicy.MergeProgrammes(
            feeds.Select(feed => _guideSchedule.GetProgrammes(feed, _guideMappings)));
    }

    private EpgNowNext GetGuideNowNext(ChannelItem channel, DateTimeOffset now) =>
        SmartSignalRoutingPolicy.GetNowNext(GetGuideProgrammes(channel), now);

    private GuideChannelRow BuildGuideRow(ChannelItem channel, EpgSchedule schedule, DateTimeOffset now)
    {
        var programmes = GetGuideProgrammes(channel);
        var nowNext = SmartSignalRoutingPolicy.GetNowNext(programmes, now);
        var current = nowNext.Current;
        var next = nowNext.Next;
        if (current is null && next is null)
        {
            var eventFeed = IsTemporaryEventFeed(channel);
            return new GuideChannelRow(
                channel,
                eventFeed ? "Event listing unavailable" : "No guide match",
                eventFeed ? "Temporary provider feed" : "Add another XMLTV source",
                0,
                eventFeed ? "Game details are carried in the channel name" : "Listings can still be assigned later",
                string.Empty,
                false);
        }

        var duration = current is null ? 0 : (current.Stop - current.Start).TotalSeconds;
        var progress = duration <= 0 ? 0 : Math.Clamp((now - current!.Start).TotalSeconds / duration * 100, 0, 100);
        return new GuideChannelRow(
            channel,
            current?.Title ?? "No programme airing",
            current?.LocalTimeRange ?? $"Next at {next!.Start.ToLocalTime():h:mm tt}",
            progress,
            next?.Title ?? "No later listing",
            next?.LocalTimeRange ?? string.Empty,
            programmes.Count > 0);
    }

    private bool FilterGuideRow(object item)
    {
        if (item is not GuideChannelRow row) return false;
        if (!MatchesGuideFilter(row.Channel, row.HasSchedule)) return false;
        var search = GuideSearchBox.Text.Trim();
        if (search.Length == 0) return true;
        return row.ChannelName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.Group.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.CurrentTitle.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.NextTitle.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildGuideTimeline()
    {
        if (_guideSchedule is null)
        {
            _guideTimelineRows = [];
            GuideTimelineChannels.ItemsSource = null;
            GuideTimelineRows.ItemsSource = null;
            return;
        }

        var windowEnd = _guideWindowStart.AddMinutes(GuideWindowMinutes);
        var now = DateTimeOffset.UtcNow;
        var timelineWidth = GuideWindowMinutes * GuidePixelsPerMinute;
        var nowMarkerLeft = (now - _guideWindowStart).TotalMinutes * GuidePixelsPerMinute;
        var showNowMarker = nowMarkerLeft >= 0 && nowMarkerLeft <= timelineWidth;

        var markers = new List<GuideTimeMarker>();
        for (var minute = 0; minute < GuideWindowMinutes; minute += 30)
        {
            var markerTime = _guideWindowStart.AddMinutes(minute).ToLocalTime();
            markers.Add(new GuideTimeMarker(
                minute * GuidePixelsPerMinute,
                30 * GuidePixelsPerMinute,
                markerTime.ToString("h:mm tt"),
                markerTime.ToString("ddd MMM d"),
                markerTime.Minute == 0));
        }

        GuideTimeHeader.Width = timelineWidth;
        GuideTimeHeader.ItemsSource = markers;
        var localStart = _guideWindowStart.ToLocalTime();
        var localEnd = windowEnd.ToLocalTime();
        GuideWindowText.Text = localStart.Date == localEnd.Date
            ? $"{localStart:ddd, MMM d}  •  {localStart:h:mm tt} – {localEnd:h:mm tt}"
            : $"{localStart:ddd h:mm tt} – {localEnd:ddd h:mm tt}";

        _guideTimelineRows = _channels
            .Where(channel => channel.Kind == ChannelKind.Live)
            .Select(channel =>
            {
                var programmes = GetGuideProgrammes(channel);
                var blocks = programmes
                    .Where(programme => programme.Stop > _guideWindowStart && programme.Start < windowEnd)
                    .Select(programme =>
                    {
                        var clippedStart = programme.Start < _guideWindowStart ? _guideWindowStart : programme.Start;
                        var clippedStop = programme.Stop > windowEnd ? windowEnd : programme.Stop;
                        var left = (clippedStart - _guideWindowStart).TotalMinutes * GuidePixelsPerMinute;
                        var width = Math.Max(28, (clippedStop - clippedStart).TotalMinutes * GuidePixelsPerMinute - 3);
                        return new GuideProgrammeBlock(
                            channel,
                            programme,
                            left,
                            width,
                            programme.Start <= now && programme.Stop > now,
                            programme.Stop <= now,
                            false);
                    })
                    .ToList();

                var hasSchedule = programmes.Count > 0;
                if (blocks.Count == 0)
                {
                    var placeholderWidth = Math.Min(timelineWidth - 12, 420);
                    blocks.Add(new GuideProgrammeBlock(channel, null, 8, placeholderWidth, false, false, true));
                }

                var mappingStatus = hasSchedule
                    ? _guideMappings.ContainsKey(channel.GuideMappingKey) ? "MANUAL MATCH" : "GUIDE READY"
                    : IsTemporaryEventFeed(channel) ? "EVENT FEED" : "MAP LISTING";
                return new GuideTimelineRow(
                    channel,
                    blocks,
                    timelineWidth,
                    nowMarkerLeft,
                    showNowMarker,
                    hasSchedule,
                    mappingStatus);
            })
            .ToList();

        _guideTimelineView = CollectionViewSource.GetDefaultView(_guideTimelineRows);
        _guideTimelineView.Filter = FilterGuideTimelineRow;
        GuideTimelineChannels.ItemsSource = _guideTimelineView;
        GuideTimelineRows.ItemsSource = _guideTimelineView;
    }

    private bool FilterGuideTimelineRow(object item)
    {
        if (item is not GuideTimelineRow row) return false;
        if (!MatchesGuideFilter(row.Channel, row.HasSchedule)) return false;
        var search = GuideSearchBox.Text.Trim();
        if (search.Length == 0) return true;
        return row.ChannelName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.Group.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.Blocks.Any(block => block.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                       block.Category.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesGuideFilter(ChannelItem channel, bool hasSchedule)
    {
        if (_guideFilter == "All") return true;
        if (_guideFilter == "Favorites") return channel.IsFavorite;
        if (_guideFilter == "Unmatched") return !hasSchedule && !IsTemporaryEventFeed(channel);
        var text = $"{channel.Name} {channel.Group}";
        return _guideFilter switch
        {
            "Sports" => ContainsAny(text, "SPORT", "NFL", "NBA", "MLB", "NHL", "ESPN", "FANDUEL", "RACING", "GOLF", "TENNIS"),
            "Movies" => ContainsAny(text, "MOVIE", "CINEMA", "HBO", "SHOWTIME", "STARZ"),
            "News" => ContainsAny(text, "NEWS", "CNN", "MSNBC", "FOX NEWS", "NEWSMAX", "C-SPAN"),
            _ => true
        };
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private void RefreshGuideViews()
    {
        _guideView?.Refresh();
        _guideTimelineView?.Refresh();
        if (GuideTimelineChannels is null || GuideList is null || GuideEmptyState is null) return;
        var visibleCount = _guideTimelineMode ? GuideTimelineChannels.Items.Count : GuideList.Items.Count;
        GuideEmptyState.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetGuideReadyStatus(EpgSchedule schedule, string sourceLabel)
    {
        var liveChannels = _channels.Count(channel => channel.Kind == ChannelKind.Live);
        var matched = _channels.Count(channel => channel.Kind == ChannelKind.Live && GetGuideProgrammes(channel).Count > 0);
        var percentage = liveChannels == 0 ? 0 : matched * 100d / liveChannels;
        GuideStatusText.Text = $"Updated {schedule.LoadedAt.ToLocalTime():h:mm tt} from {sourceLabel} • {schedule.ProgramCount:N0} programmes";
        var manualCount = _guideMappings.Count(mapping => _channels.Any(channel => channel.GuideMappingKey == mapping.Key));
        GuideCoverageText.Text = manualCount > 0
            ? $"{matched:N0}/{liveChannels:N0} matched • {percentage:0}% • {manualCount:N0} manual"
            : $"{matched:N0}/{liveChannels:N0} matched • {percentage:0}%";
        FooterStatusDot.Fill = LiveBrush;
        FooterStatusText.Text = $"TV guide ready • {matched:N0} matched live channels";
    }

    private void UpdateCurrentGuide(ChannelItem? channel)
    {
        var guide = channel is null || channel.Kind != ChannelKind.Live ? null : GetGuideNowNext(channel, DateTimeOffset.UtcNow);
        if (guide?.Current is null)
        {
            NowPlayingProgramText.Visibility = Visibility.Collapsed;
            NowPlayingNextText.Visibility = Visibility.Collapsed;
            InspectorGuideCard.Visibility = Visibility.Collapsed;
            UpdateFullscreenHud();
            return;
        }

        NowPlayingProgramText.Text = $"NOW  {guide.Current.Title}  •  {guide.Current.LocalTimeRange}";
        NowPlayingProgramText.Visibility = Visibility.Visible;
        InspectorProgramText.Text = $"{guide.Current.Title}  •  {guide.Current.LocalTimeRange}";
        var nextText = guide.Next is null ? "No later listing" : $"NEXT  {guide.Next.Title}  •  {guide.Next.LocalTimeRange}";
        NowPlayingNextText.Text = nextText;
        NowPlayingNextText.Visibility = Visibility.Visible;
        InspectorNextText.Text = nextText;
        InspectorGuideCard.Visibility = Visibility.Visible;
        UpdateFullscreenHud();
    }

    private void UpdateFullscreenHud(PlaybackSnapshot? snapshot = null)
    {
        if (HudChannelName is null) return;
        HudClock.Text = DateTime.Now.ToString("h:mm tt");
        if (_currentChannel is null)
        {
            HudChannelNumber.Text = string.Empty;
            HudChannelName.Text = "No channel selected";
            HudGroupName.Text = string.Empty;
            HudChannelInitials.Text = "TV";
            HudNowTitle.Text = "Choose a channel to begin";
            HudNextTitle.Text = "QUICK TUNE  Press Q to search";
            HudProgramProgress.Value = 0;
            HudProgramTime.Text = string.Empty;
            HudTechnical.Text = "LIBVLC NATIVE";
            return;
        }

        var channel = _currentChannel;
        HudChannelNumber.Text = channel.Kind == ChannelKind.Recording ? "DVR" : $"CH {channel.Number}";
        HudChannelName.Text = channel.Name;
        HudGroupName.Text = $"• {channel.Group}";
        HudChannelInitials.Text = channel.Initials;
        var guide = channel.Kind == ChannelKind.Live ? GetGuideNowNext(channel, DateTimeOffset.UtcNow) : null;
        if (guide?.Current is not null)
        {
            var duration = (guide.Current.Stop - guide.Current.Start).TotalSeconds;
            HudProgramProgress.Value = duration <= 0
                ? 0
                : Math.Clamp((DateTimeOffset.UtcNow - guide.Current.Start).TotalSeconds / duration * 100, 0, 100);
            HudNowTitle.Text = guide.Current.Title;
            HudProgramTime.Text = guide.Current.LocalTimeRange;
            HudNextTitle.Text = guide.Next is null
                ? "NEXT  No later listing"
                : $"NEXT  {guide.Next.Title}  •  {guide.Next.LocalTimeRange}";
        }
        else
        {
            HudProgramProgress.Value = 0;
            HudNowTitle.Text = channel.Kind == ChannelKind.Live ? "Live television" : channel.KindLabel;
            HudProgramTime.Text = string.Empty;
            HudNextTitle.Text = channel.Kind == ChannelKind.Recording
                ? "SAVED LOCALLY  •  ORIGINAL QUALITY"
                : "Guide information unavailable";
        }

        if (snapshot is not null)
        {
            var video = snapshot.Resolution == "—" ? snapshot.VideoCodec : $"{snapshot.VideoCodec} {snapshot.Resolution}";
            var fps = snapshot.FramesPerSecond > 0 ? $" • {snapshot.FramesPerSecond:0.##} fps" : string.Empty;
            HudTechnical.Text = $"{video}{fps} • {snapshot.DecoderMode} • {snapshot.AudioFormat}";
        }
    }

    private static IReadOnlyList<string> ParseGuideSources(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static bool IsPredominantlyUsPlaylist(IReadOnlyList<ChannelItem> channels)
    {
        var live = channels.Where(channel => channel.Kind == ChannelKind.Live).ToList();
        if (live.Count == 0) return false;
        var us = live.Count(channel =>
            channel.Name.StartsWith("US", StringComparison.OrdinalIgnoreCase) ||
            channel.Group.StartsWith("US", StringComparison.OrdinalIgnoreCase) ||
            channel.TvgId?.Contains(".us", StringComparison.OrdinalIgnoreCase) == true);
        return us >= Math.Max(3, live.Count / 2);
    }

    private static bool IsTemporaryEventFeed(ChannelItem channel) =>
        channel.Group is "NFL" or "MLB Baseball league" or "WNBA League Pass" ||
        channel.Name.Contains(" vs ", StringComparison.OrdinalIgnoreCase) ||
        channel.Name.Contains(" @ ", StringComparison.OrdinalIgnoreCase) ||
        channel.Name.Contains("MiLB", StringComparison.OrdinalIgnoreCase);

    private void ApplyPreviewPlaylist()
    {
        var previewChannels = new List<ChannelItem>
        {
            new() { Number = 1, Name = "World News HD", Group = "Newsroom", Url = "https://example.invalid/live/1.ts", Kind = ChannelKind.Live, IsFavorite = true },
            new() { Number = 2, Name = "Stadium Sports", Group = "Live Sports", Url = "https://example.invalid/live/2.ts", Kind = ChannelKind.Live },
            new() { Number = 3, Name = "Cinema One", Group = "Movies", Url = "https://example.invalid/movie/3.mp4", Kind = ChannelKind.Movie },
            new() { Number = 4, Name = "Nature 4K", Group = "Documentary", Url = "https://example.invalid/live/4.ts", Kind = ChannelKind.Live },
            new() { Number = 5, Name = "Night Sessions", Group = "Music", Url = "https://example.invalid/live/5.ts", Kind = ChannelKind.Live },
            new() { Number = 6, Name = "Archive Series", Group = "Series", Url = "https://example.invalid/series/6.mkv", Kind = ChannelKind.Series }
        };
        ApplyPlaylist(new PlaylistResult(previewChannels, "OrbitalVue editorial preview", "preview", DateTimeOffset.Now));
        HideModal(ImportOverlay);
        FooterStatusDot.Fill = LiveBrush;
        FooterStatusText.Text = "Native interface preview • Direct MPEG-TS ready";
        SourceRefreshText.Text = "Interface preview library";
    }

    private void PrepareSignalRoutingPreview()
    {
        var primarySource = Guid.NewGuid();
        var backupSource = Guid.NewGuid();
        var regionalSource = Guid.NewGuid();
        var previewChannels = new List<ChannelItem>
        {
            new() { Number = 1, Name = "World News HD", Group = "Newsroom", Url = "https://primary.invalid/live/world-news.ts", TvgId = "world.news", LogoUrl = "https://assets.invalid/world-news.png", Kind = ChannelKind.Live, SourceId = primarySource, SourceName = "Primary lineup", CatchupMode = "default", CatchupSource = "https://primary.invalid/replay?start={utc}&duration={duration}", CatchupDays = 7 },
            new() { Number = 2, Name = "US: World News FHD (D)", Group = "US News", Url = "https://backup.invalid/live/world-news.m3u8", TvgId = "world.news", Kind = ChannelKind.Live, SourceId = backupSource, SourceName = "Fiber backup" },
            new() { Number = 3, Name = "World News SD", Group = "Regional News", Url = "https://regional.invalid/live/world-news.ts", TvgId = "world.news", Kind = ChannelKind.Live, SourceId = regionalSource, SourceName = "Regional fallback" },
            new() { Number = 4, Name = "Stadium Sports HD", Group = "Live Sports", Url = "https://primary.invalid/live/stadium.ts", TvgId = "stadium.sports", Kind = ChannelKind.Live, SourceId = primarySource, SourceName = "Primary lineup" },
            new() { Number = 5, Name = "Stadium Sports FHD", Group = "Sports", Url = "https://backup.invalid/live/stadium.m3u8", TvgId = "stadium.sports", Kind = ChannelKind.Live, SourceId = backupSource, SourceName = "Fiber backup" },
            new() { Number = 6, Name = "Cinema One", Group = "Movies", Url = "https://primary.invalid/movie/cinema.mp4", Kind = ChannelKind.Movie, SourceId = primarySource, SourceName = "Primary lineup" }
        };
        ApplyPlaylist(new PlaylistResult(previewChannels, "Three connected sources", "signal-routing-preview", DateTimeOffset.Now));
        var route = _signalRoutes.First(candidate => candidate.Representative.Name.Contains("World News", StringComparison.OrdinalIgnoreCase));
        var primary = SmartSignalRoutingPolicy.GetOrCreateHealth(_settings.SignalRouting, route.Feeds[0], route.Key);
        primary.SuccessfulStarts = 42;
        primary.FailedStarts = 2;
        primary.BufferEvents = 3;
        primary.Reconnects = 1;
        primary.LastStartupMilliseconds = 940;
        primary.LastResolutionHeight = 1080;
        primary.LastFramesPerSecond = 59.94;
        primary.LastInputBitrateMbps = 8.4;
        primary.LastAttemptUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        primary.LastSuccessUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        primary.Preference = SignalFeedPreference.Preferred;
        var backup = SmartSignalRoutingPolicy.GetOrCreateHealth(_settings.SignalRouting, route.Feeds[1], route.Key);
        backup.SuccessfulStarts = 19;
        backup.FailedStarts = 1;
        backup.BufferEvents = 2;
        backup.LastStartupMilliseconds = 1_420;
        backup.LastResolutionHeight = 1080;
        backup.LastFramesPerSecond = 59.94;
        backup.LastInputBitrateMbps = 7.1;
        backup.LastAttemptUtc = DateTimeOffset.UtcNow.AddHours(-3);
        backup.LastSuccessUtc = DateTimeOffset.UtcNow.AddHours(-3);
        var regional = SmartSignalRoutingPolicy.GetOrCreateHealth(_settings.SignalRouting, route.Feeds[2], route.Key);
        regional.SuccessfulStarts = 3;
        regional.FailedStarts = 5;
        regional.BufferEvents = 14;
        regional.Reconnects = 7;
        regional.LastStartupMilliseconds = 6_800;
        regional.LastResolutionHeight = 576;
        regional.LastInputBitrateMbps = 2.1;
        regional.LastAttemptUtc = DateTimeOffset.UtcNow.AddDays(-1);
        regional.LastFailureUtc = DateTimeOffset.UtcNow.AddDays(-1);
        regional.Preference = SignalFeedPreference.Blocked;
        _settings.SignalRouting.Enabled = true;
        _settings.SignalRouting.AutomaticFailover = true;
        _currentChannel = route.Representative;
        _activeSignalFeed = route.Feeds[0];
        NowPlayingHeading.Text = route.Representative.Name;
        NowPlayingSubheading.Text = route.Representative.Group;
        InspectorChannelName.Text = route.Representative.Name;
        InspectorGroupName.Text = route.Representative.Group;
        InspectorInitials.Text = route.Representative.Initials;
        ShowChannelArtwork(route.Representative);
        ActiveFeedValue.Text = SmartSignalRoutingPolicy.SourceLabel(_activeSignalFeed);
        UpdateSignalRouteAvailability();
        FooterStatusDot.Fill = LiveBrush;
        FooterStatusText.Text = "Smart routing preview • automatic failover armed";
        SourceRefreshText.Text = "Three source routes indexed";
    }

    private static EpgSchedule CreatePreviewGuideSchedule(IReadOnlyList<ChannelItem> channels)
    {
        var now = DateTimeOffset.UtcNow;
        var programmes = new Dictionary<string, IReadOnlyList<EpgProgram>>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
        var programmeTitles = new[]
        {
            "Live studio coverage", "Morning Edition", "Championship Replay", "Home & Design",
            "Breaking News Live", "Classic Cinema", "Inside the Game", "Prime Time"
        };
        foreach (var channel in channels.Where(channel => channel.Kind == ChannelKind.Live && !IsTemporaryEventFeed(channel)))
        {
            var id = EpgSchedule.NormalizeKey(channel.TvgId ?? $"PREVIEW-{channel.Number}");
            catalog[id] = channel.Name;
            if (channel.Number % 5 == 0) continue;
            programmes[id] =
            [
                new EpgProgram(id, programmeTitles[channel.Number % programmeTitles.Length], null, null, now.AddMinutes(-18), now.AddMinutes(42)),
                new EpgProgram(id, programmeTitles[(channel.Number + 3) % programmeTitles.Length], null, null, now.AddMinutes(42), now.AddMinutes(102))
            ];
            foreach (var key in EpgSchedule.CandidateKeys(channel.TvgId, channel.TvgName, channel.Name)) aliases[key] = id;
        }
        catalog["NEWSMAX2.US2"] = "Newsmax 2";
        catalog["FS1.US2"] = "FOX Sports 1";
        catalog["SPECTRUMSPORTSNET.US2"] = "Spectrum SportsNet";
        catalog["REDBULLTV.US2"] = "Red Bull TV";
        return new EpgSchedule(programmes, aliases, "OrbitalVue guide preview", now, catalog);
    }

    private async Task RunGuideSmokeAsync(string playlistPath, string reportPath, string capturePath)
    {
        var started = DateTimeOffset.UtcNow;
        var playlist = await _playlistSource.LoadFileAsync(playlistPath);
        _activePlaylistSourceType = "file";
        _activePlaylistSourceValue = playlistPath;
        ApplyPlaylist(playlist);
        await ConfigureGuideForPlaylistAsync(playlist, "file", playlistPath);
        if (_guideSchedule is null) throw new InvalidOperationException("The guide smoke test did not load a schedule.");

        GuideNavigation.IsChecked = true;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(500);
        CaptureWindow(capturePath);

        var liveChannels = playlist.Channels.Where(channel => channel.Kind == ChannelKind.Live).ToList();
        var matched = liveChannels.Count(channel => _guideSchedule.GetProgrammes(channel, _guideMappings).Count > 0);
        var report = new
        {
            passed = matched > 0 && _guideSchedule.ProgramCount > 0,
            playlist = Path.GetFileName(playlistPath),
            liveChannels = liveChannels.Count,
            matchedChannels = matched,
            coveragePercent = liveChannels.Count == 0 ? 0 : Math.Round(matched * 100d / liveChannels.Count, 2),
            temporaryEventFeedsWithoutSchedule = liveChannels.Count(channel => IsTemporaryEventFeed(channel) && _guideSchedule.GetProgrammes(channel, _guideMappings).Count == 0),
            programmes = _guideSchedule.ProgramCount,
            encryptedCacheReady = true,
            sourceAddressesExcluded = true,
            elapsedSeconds = Math.Round((DateTimeOffset.UtcNow - started).TotalSeconds, 2)
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Close();
        Application.Current.Shutdown(report.passed ? 0 : 1);
    }

    private async Task RunGuideCacheSmokeAsync(string sourceType, string sourceValue, string reportPath, string capturePath)
    {
        var started = DateTimeOffset.UtcNow;
        var cached = await _playlistCache.TryLoadAsync(sourceType, sourceValue);
        if (cached is null) throw new InvalidOperationException("The encrypted playlist cache was not available for the guide smoke test.");

        _activePlaylistSourceType = sourceType;
        _activePlaylistSourceValue = sourceValue;
        ApplyPlaylist(cached.Playlist);
        await ConfigureGuideForPlaylistAsync(cached.Playlist, sourceType, sourceValue);
        if (_guideSchedule is null) throw new InvalidOperationException("The cached-playlist guide smoke test did not load a schedule.");
        if (_guideSchedule.ChannelCatalog.Count == 0)
            await RefreshGuideAsync(forceNetworkRefresh: true);
        if (_guideSchedule is null) throw new InvalidOperationException("The guide refresh did not retain a schedule.");

        GuideNavigation.IsChecked = true;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(600);
        CaptureWindow(capturePath);

        var liveChannels = cached.Playlist.Channels.Where(channel => channel.Kind == ChannelKind.Live).ToList();
        var matched = liveChannels.Count(channel => _guideSchedule.GetProgrammes(channel, _guideMappings).Count > 0);
        var report = new
        {
            passed = matched > 0 && _guideSchedule.ProgramCount > 0 && _guideSchedule.ChannelCatalog.Count > 0,
            playlistCache = "Windows-user encrypted",
            liveChannels = liveChannels.Count,
            matchedChannels = matched,
            coveragePercent = liveChannels.Count == 0 ? 0 : Math.Round(matched * 100d / liveChannels.Count, 2),
            guideCatalogChannels = _guideSchedule.ChannelCatalog.Count,
            programmes = _guideSchedule.ProgramCount,
            manualMappings = _guideMappings.Count,
            timelineRows = _guideTimelineRows.Count,
            encryptedCacheReady = true,
            sourceAddressesExcluded = true,
            elapsedSeconds = Math.Round((DateTimeOffset.UtcNow - started).TotalSeconds, 2)
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Close();
        Application.Current.Shutdown(report.passed ? 0 : 1);
    }

    private async Task PrepareModalCaptureAsync(string[] commandLineArguments)
    {
        ApplyPreviewPlaylist();
        if (!commandLineArguments.Contains("--active-player", StringComparer.OrdinalIgnoreCase)) return;

        ChannelList.SelectedIndex = 0;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(250);
    }

    private void CaptureWindow(string outputPath)
    {
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private void CaptureScreenWindow(string outputPath)
    {
        try
        {
            var bounds = FullscreenWindowController.GetWindowBounds(this);
            using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new System.Drawing.Size(bounds.Width, bounds.Height));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Win32Exception)
        {
            CaptureWindow(outputPath);
        }
    }

    private async Task RunFullscreenWindowSmokeAsync(string reportPath, bool startMaximized)
    {
        if (startMaximized) WindowState = WindowState.Maximized;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(250);

        var beforeState = WindowState;
        var beforeResizeMode = ResizeMode;
        var beforeTopmost = Topmost;
        var beforeBounds = FullscreenWindowController.GetWindowBounds(this);

        EnterFullscreen();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(250);
        var displayBounds = _fullscreenWindow.ActiveDisplay;
        var fullscreenBounds = FullscreenWindowController.GetWindowBounds(this);
        var displayMatched = displayBounds == fullscreenBounds;

        ExitFullscreen();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(250);
        var restoredBounds = FullscreenWindowController.GetWindowBounds(this);
        var boundsRestored = beforeState == WindowState.Maximized || BoundsNearlyEqual(beforeBounds, restoredBounds);
        var stateRestored = WindowState == beforeState && ResizeMode == beforeResizeMode && Topmost == beforeTopmost;
        var passed = displayMatched && boundsRestored && stateRestored;

        var report = new
        {
            passed,
            startedMaximized = startMaximized,
            displayMatched,
            boundsRestored,
            stateRestored,
            beforeState = beforeState.ToString(),
            restoredState = WindowState.ToString(),
            beforeBounds,
            displayBounds,
            fullscreenBounds,
            restoredBounds
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Close();
        Application.Current.Shutdown(passed ? 0 : 1);
    }

    private static bool BoundsNearlyEqual(FullscreenDisplayBounds first, FullscreenDisplayBounds second) =>
        Math.Abs(first.Left - second.Left) <= 1 && Math.Abs(first.Top - second.Top) <= 1 &&
        Math.Abs(first.Width - second.Width) <= 1 && Math.Abs(first.Height - second.Height) <= 1;

    private async Task RunMultiviewSmokeAsync(PlaylistResult playlist, int duration, string reportPath, string snapshotDirectory, bool fullscreen = false)
    {
        var channels = playlist.Channels.Where(channel => channel.Kind == ChannelKind.Live).Take(2).ToArray();
        if (channels.Length < 2) throw new InvalidOperationException("The playlist needs at least two live channels for the multiview smoke test.");

        ApplyPlaylist(playlist);
        _settings.Multiview = new MultiviewPreferences
        {
            Layout = MultiviewLayout.Duo.ToString(),
            ActiveSlot = 0,
            AudioSlot = 0,
            ChannelKeys = [null, null, null, null]
        };
        _multiviewLayout = MultiviewLayout.Duo;
        SetGuideMode(false);
        SetMultiviewMode(true);
        _multiviewSession!.AssignChannel(0, ResolveBestSignalFeed(channels[0]));
        _multiviewSession.AssignChannel(1, ResolveBestSignalFeed(channels[1]));
        _multiviewSession.SelectSlot(0);
        _multiviewSession.SetAudioSlot(0);
        UpdateMultiviewLayout();
        if (fullscreen) EnterFullscreen();
        else WindowState = WindowState.Maximized;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        var fullscreenDisplay = fullscreen ? _fullscreenWindow.ActiveDisplay : null;
        FullscreenDisplayBounds? fullscreenWindow = fullscreen ? FullscreenWindowController.GetWindowBounds(this) : null;
        var fullscreenBoundsMatch = !fullscreen || fullscreenDisplay == fullscreenWindow;

        foreach (var tile in _multiviewSession.Tiles.Take(2))
            if (tile.MediaPlayer is not null) tile.MediaPlayer.Volume = 0;

        var firstDisplayedFrames = new[] { -1, -1 };
        var firstTimes = new long[] { -1, -1 };
        for (var second = 0; second < duration; second++)
        {
            await Task.Delay(1_000);
            for (var index = 0; index < 2; index++)
            {
                var sample = _multiviewSession.Tiles[index].GetSnapshot();
                if (sample is null) continue;
                if (firstDisplayedFrames[index] < 0 && sample.DisplayedFrames > 0)
                    firstDisplayedFrames[index] = sample.DisplayedFrames;
                if (firstTimes[index] < 0 && sample.IsPlaying)
                    firstTimes[index] = sample.Time;
            }
        }

        var finalSnapshots = _multiviewSession.Tiles.Take(2).Select(tile => tile.GetSnapshot()).ToArray();
        Directory.CreateDirectory(snapshotDirectory);
        var snapshotResults = new List<bool>();
        for (var index = 0; index < 2; index++)
        {
            var snapshotPath = Path.Combine(snapshotDirectory, $"multiview-{index + 1}.png");
            snapshotResults.Add(_multiviewSession.Tiles[index].MediaPlayer?.TakeSnapshot(0, snapshotPath, 0, 0) == true);
        }
        await Task.Delay(400);

        _multiviewLayout = MultiviewLayout.Focus;
        _multiviewSession.SelectSlot(0);
        UpdateMultiviewLayout();
        await Task.Delay(500);
        var hiddenViewEnteredStandby = _multiviewSession.Tiles[1].IsSuspended;
        var singleAudioPolicy = _multiviewSession.Tiles.Count(tile => tile.IsAudible) == 1 &&
                                _multiviewSession.Tiles[0].IsAudible;

        var tileReports = Enumerable.Range(0, 2).Select(index =>
        {
            var tile = _multiviewSession.Tiles[index];
            var sample = finalSnapshots[index];
            var framesAdvanced = sample is null || firstDisplayedFrames[index] < 0
                ? 0
                : Math.Max(0, sample.DisplayedFrames - firstDisplayedFrames[index]);
            var clockAdvanced = sample is null || firstTimes[index] < 0
                ? 0
                : Math.Max(0, sample.Time - firstTimes[index]);
            return new
            {
                view = index + 1,
                channel = tile.ChannelName,
                group = tile.GroupName,
                reachedPlaying = tile.HasReachedPlaying,
                errors = tile.ErrorCount,
                isPlaying = sample?.IsPlaying ?? false,
                muted = sample?.IsMuted ?? true,
                decoder = sample?.DecoderMode ?? "Unavailable",
                video = sample is null ? "Unavailable" : $"{sample.VideoCodec} {sample.Resolution}",
                framesPerSecond = Math.Round(sample?.FramesPerSecond ?? 0, 2),
                displayedFramesAdvanced = framesAdvanced,
                playbackClockAdvancedMilliseconds = clockAdvanced,
                droppedFrames = sample?.DroppedFrames ?? 0,
                bufferEvents = sample?.BufferEvents ?? 0,
                reconnects = sample?.ReconnectAttempts ?? 0,
                decoderFallbacks = sample?.DecoderFallbacks ?? 0,
                watchdogRecoveries = sample?.StallRecoveries ?? 0,
                snapshotSaved = snapshotResults[index]
            };
        }).ToArray();

        var minimumProgress = Math.Max(30, duration - 10);
        var passed = tileReports.All(tile =>
                         tile.reachedPlaying && tile.isPlaying &&
                         (tile.errors == 0 || tile.decoderFallbacks > 0 || tile.reconnects > 0) &&
                         (tile.displayedFramesAdvanced >= minimumProgress || tile.playbackClockAdvancedMilliseconds >= 5_000)) &&
                     singleAudioPolicy && hiddenViewEnteredStandby && fullscreenBoundsMatch;
        var report = new
        {
            passed,
            layout = "2-up live verification, then focus standby verification",
            durationSeconds = duration,
            singleAudioPolicy,
            hiddenViewEnteredStandby,
            fullscreenRequested = fullscreen,
            fullscreenBoundsMatch,
            fullscreenDisplay,
            fullscreenWindow,
            providerAddressesExcluded = true,
            tiles = tileReports
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (_isFullscreen) ExitFullscreen();
        Close();
        Application.Current.Shutdown(passed ? 0 : 1);
    }

    private async Task RunVisualPlaybackSmokeAsync(string playlistPath, int duration, string reportPath, string snapshotPath, bool fullscreen = false)
    {
        var playlist = await _playlistSource.LoadFileAsync(playlistPath);
        await RunVisualPlaybackSmokeAsync(playlist, duration, reportPath, snapshotPath, fullscreen);
    }

    private async Task RunVisualPlaybackSmokeAsync(PlaylistResult playlist, int duration, string reportPath, string snapshotPath, bool fullscreen = false)
    {
        _settings.Playback.BufferPreset = BufferPreset.Smart;
        _settings.Playback.HardwareDecoding = true;
        _settings.Playback.StallWatchdog = true;
        _settings.Playback.AdaptiveRefreshRate = false;
        CreatePlaybackEngine();
        ApplyPlaylist(playlist);
        HideModal(ImportOverlay);
        var channel = playlist.Channels.First(item => item.Kind == ChannelKind.Live);

        var playingReached = false;
        var errorReached = false;
        var rebufferTransitions = 0;
        var lastState = PlaybackState.Idle;
        long firstPlayingTime = -1;
        long lastPlayingTime = -1;
        var firstDisplayedFrames = -1;
        var lastDisplayedFrames = -1;
        var snapshotSaved = false;

        void TrackStatus(object? _, PlaybackStatus status)
        {
            if (status.State == PlaybackState.Playing)
            {
                if (!playingReached) firstPlayingTime = _playback?.MediaPlayer.Time ?? -1;
                playingReached = true;
            }
            else if (status.State == PlaybackState.Buffering && lastState == PlaybackState.Playing)
            {
                rebufferTransitions++;
            }
            else if (status.State == PlaybackState.Error)
            {
                errorReached = true;
            }
            lastState = status.State;
        }

        _playback!.StatusChanged += TrackStatus;
        _playback.MediaPlayer.Mute = true;
        ChannelList.SelectedItem = channel;
        ChannelList.ScrollIntoView(channel);
        if (fullscreen) EnterFullscreen();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        var fullscreenDisplay = fullscreen ? _fullscreenWindow.ActiveDisplay : null;
        FullscreenDisplayBounds? fullscreenWindow = fullscreen ? FullscreenWindowController.GetWindowBounds(this) : null;
        var fullscreenBoundsMatch = !fullscreen || fullscreenDisplay == fullscreenWindow;

        for (var second = 0; second < duration; second++)
        {
            await Task.Delay(1_000);
            if (playingReached)
            {
                lastPlayingTime = _playback.MediaPlayer.Time;
                var sample = _playback.GetSnapshot();
                if (firstDisplayedFrames < 0 && sample.DisplayedFrames > 0) firstDisplayedFrames = sample.DisplayedFrames;
                lastDisplayedFrames = sample.DisplayedFrames;
            }
            if (!snapshotSaved && second >= Math.Max(5, duration - 5))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(snapshotPath))!);
                snapshotSaved = _playback.MediaPlayer.TakeSnapshot(0, snapshotPath, 0, 0);
            }
            if (errorReached) break;
        }

        var advancedMilliseconds = firstPlayingTime >= 0 && lastPlayingTime >= firstPlayingTime
            ? lastPlayingTime - firstPlayingTime
            : 0;
        var playbackSnapshot = _playback.GetSnapshot();
        var report = new
        {
            PlaylistEntries = playlist.Channels.Count,
            ChannelNumber = channel.Number,
            DurationSeconds = duration,
            PlayingReached = playingReached,
            PlaybackClockSeconds = Math.Round(advancedMilliseconds / 1000d, 1),
            DisplayedFramesAdvanced = Math.Max(0, lastDisplayedFrames - Math.Max(0, firstDisplayedFrames)),
            RebufferTransitions = rebufferTransitions,
            PlaybackError = errorReached,
            BufferOverlayVisibleAtEnd = BufferOverlay.Visibility == Visibility.Visible,
            FullscreenRequested = fullscreen,
            FullscreenBoundsMatch = fullscreenBoundsMatch,
            FullscreenDisplay = fullscreenDisplay,
            FullscreenWindow = fullscreenWindow,
            SnapshotSaved = snapshotSaved && File.Exists(snapshotPath),
            HardwareDecodingRequested = _settings.Playback.HardwareDecoding,
            ActiveCacheMilliseconds = playbackSnapshot.ActiveCacheMilliseconds,
            Decoder = playbackSnapshot.DecoderMode,
            DecoderFallbacks = playbackSnapshot.DecoderFallbacks,
            StallRecoveries = playbackSnapshot.StallRecoveries,
            Video = $"{playbackSnapshot.VideoCodec} {playbackSnapshot.Resolution}",
            FramesPerSecond = playbackSnapshot.FramesPerSecond,
            DroppedFrames = playbackSnapshot.DroppedFrames,
            Audio = playbackSnapshot.AudioFormat
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        _playback.StatusChanged -= TrackStatus;
        _playback.Stop();
        if (_isFullscreen) ExitFullscreen();
        Close();
        Application.Current.Shutdown(0);
    }

    private bool FilterChannel(object item)
    {
        if (item is not ChannelItem channel) return false;
        if (_favoritesOnly && !channel.IsFavorite) return false;
        if (!MediaLibraryBrowsePolicy.Matches(channel, _libraryBrowseMode)) return false;
        if (_categoryFilter.Length > 0 && !channel.Group.Equals(_categoryFilter, StringComparison.OrdinalIgnoreCase)) return false;

        var query = SearchBox.Text.Trim();
        if (query.Length == 0) return true;
        var normalized = query.ToUpperInvariant();
        return channel.SearchText.Contains(normalized, StringComparison.Ordinal) ||
               GetSignalRoute(channel)?.Feeds.Any(feed => feed.SearchText.Contains(normalized, StringComparison.Ordinal)) == true;
    }

    private void RefreshFilters()
    {
        _channelView?.Refresh();
        RefreshChannelCount();
    }

    private void RefreshChannelCount()
    {
        var visible = ChannelList.Items.Count;
        FavoritesEmptyState.Visibility = _favoritesOnly && visible == 0 ? Visibility.Visible : Visibility.Collapsed;
        var unit = _libraryBrowseSummary.IsMediaCenterLibrary ? "titles" : "channels";
        var channelText = visible == _channels.Count
            ? $"{_channels.Count:N0} {unit}"
            : $"{visible:N0} of {_channels.Count:N0} {unit}";
        var visibleGroups = _channelView?.Groups?.Count ?? 0;
        ChannelCountText.Text = _categoryFilter.Length == 0 && visibleGroups > 0
            ? $"{channelText} • {visibleGroups:N0} groups"
            : channelText;
    }

    private async void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChannelList.SelectedItem is not ChannelItem channel) return;
        if (_multiviewMode)
        {
            if (channel.IsProtectedMedia)
            {
                FooterStatusDot.Fill = WarningBrush;
                FooterStatusText.Text = "Plex and Emby libraries currently open in the main player • exit Multiview to play this item";
                return;
            }
            EnsureMultiviewSession();
            _multiviewSession!.AssignChannel(_multiviewSession.ActiveSlot, ResolveBestSignalFeed(channel));
            UpdateMultiviewPresentation();
            await PersistMultiviewAsync();
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = $"{channel.Name} assigned to View {_multiviewSession.ActiveSlot + 1}";
            return;
        }
        TuneChannel(channel);
    }

    private void TuneChannel(
        ChannelItem channel,
        string? recordingPath = null,
        ChannelItem? requestedSignalFeed = null,
        bool rememberChannel = true)
    {
        if (string.IsNullOrWhiteSpace(recordingPath) && channel.IsProtectedMedia)
        {
            _ = TuneProtectedMediaAsync(channel, rememberChannel);
            return;
        }

        CancelMediaPlaybackResolution(clearResume: true);
        TuneChannelCore(channel, recordingPath, requestedSignalFeed, rememberChannel);
    }

    private void TuneChannelCore(
        ChannelItem channel,
        string? recordingPath = null,
        ChannelItem? requestedSignalFeed = null,
        bool rememberChannel = true,
        ChannelItem? playbackOverride = null,
        ChannelPlaybackProfile? playbackProfile = null,
        string? mediaPlaybackReportingSessionId = null,
        bool force = false)
    {
        var route = string.IsNullOrWhiteSpace(recordingPath) && playbackOverride is null ? GetSignalRoute(channel) : null;
        var logicalChannel = route?.Representative ?? channel;
        if (!force && ReferenceEquals(logicalChannel, _currentChannel) &&
            string.Equals(recordingPath, _currentRecordingPath, StringComparison.OrdinalIgnoreCase) &&
            (requestedSignalFeed is null || string.Equals(requestedSignalFeed.StableKey, _activeSignalFeed?.StableKey, StringComparison.OrdinalIgnoreCase))) return;
        var previousSnapshot = _playback?.GetSnapshot();
        CaptureSignalTelemetry(previousSnapshot, persist: false);
        SaveCurrentRecordingProgress(force: true);
        ObserveMediaPlaybackReport(EndCurrentMediaPlaybackReporting(previousSnapshot));
        _displayRefreshRate?.Restore();
        if (_currentChannel is not null && string.IsNullOrWhiteSpace(_currentRecordingPath) && !ReferenceEquals(_currentChannel, logicalChannel))
            _previousChannel = _currentChannel;
        _currentRecordingPath = string.IsNullOrWhiteSpace(recordingPath) ? null : Path.GetFullPath(recordingPath);
        _recordingResumeApplied = _currentRecordingPath is null;
        _currentChannel = logicalChannel;
        _mediaPlaybackReportingSessionId = mediaPlaybackReportingSessionId;
        _mediaPlaybackState = PlaybackState.Idle;
        _lastReportedMediaPlaybackState = null;
        _lastMediaPlaybackReportUtc = DateTimeOffset.MinValue;
        if (_currentRecordingPath is null && rememberChannel) _settings.LastChannelKey = logicalChannel.StableKey;
        _learnedProfileSignature = string.Empty;
        NowPlayingHeading.Text = logicalChannel.Name;
        NowPlayingSubheading.Text = logicalChannel.Group;
        InspectorChannelName.Text = logicalChannel.Name;
        InspectorGroupName.Text = logicalChannel.Group;
        InspectorInitials.Text = logicalChannel.Initials;
        ShowChannelArtwork(logicalChannel);
        UpdateCurrentFavoriteButton(logicalChannel);
        UpdateCurrentGuide(logicalChannel);
        _showPlayerTopStatus = logicalChannel.Kind == ChannelKind.Live;
        RefreshPlayerSurfaceVisibility();
        PlaybackDetailText.Text = logicalChannel.Group;
        var profile = playbackProfile ?? GetOrCreateChannelProfile(logicalChannel);
        _applyingChannelProfile = true;
        SelectComboByContent(AspectBox, profile.AspectRatio ?? _settings.Playback.AspectRatio);
        _applyingChannelProfile = false;
        _attemptedSignalFeedKeys.Clear();
        _automaticSignalSwitches = 0;
        if (route is not null && logicalChannel.Kind == ChannelKind.Live)
        {
            var feed = requestedSignalFeed is not null && route.Feeds.Any(candidate => candidate.StableKey == requestedSignalFeed.StableKey)
                ? requestedSignalFeed
                : ResolveBestSignalFeed(logicalChannel);
            PlaySignalFeed(route, feed, profile);
        }
        else
        {
            _activeSignalFeed = logicalChannel;
            ResetSignalSessionCounters();
            _playback?.Play(playbackOverride ?? logicalChannel, profile);
        }
        if (_currentRecordingPath is null && rememberChannel) TouchRecentChannel(logicalChannel);
        UpdateSignalRouteAvailability();
        UpdateFullscreenHud();
    }

    private async Task TuneProtectedMediaAsync(
        ChannelItem logicalChannel,
        bool rememberChannel,
        ChannelPlaybackProfile? profile = null)
    {
        if (!EnsureMediaCenterAccess(logicalChannel.SourceName ?? "Media center")) return;
        CancelMediaPlaybackResolution(clearResume: false);
        var cancellation = new CancellationTokenSource();
        _mediaPlaybackCancellation = cancellation;
        FooterStatusDot.Fill = WarningBrush;
        FooterStatusText.Text = $"Preparing protected {logicalChannel.SourceName ?? "media-center"} playback…";
        PlaybackDetailText.Text = "Resolving a short-lived playback address";

        string? pendingReportingSessionId = null;
        var reportingSessionAdopted = false;
        try
        {
            var resolved = await _mediaCenterSource.ResolvePlaybackAsync(logicalChannel, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            pendingReportingSessionId = resolved.ReportingSessionId;
            var playbackChannel = CreateResolvedMediaChannel(logicalChannel, resolved);
            _pendingMediaResumeMilliseconds = Math.Max(0, resolved.ResumePositionMilliseconds);
            _mediaResumeApplied = _pendingMediaResumeMilliseconds < 30_000 || logicalChannel.Kind == ChannelKind.Live;
            TuneChannelCore(
                logicalChannel,
                rememberChannel: rememberChannel,
                playbackOverride: playbackChannel,
                playbackProfile: profile,
                mediaPlaybackReportingSessionId: pendingReportingSessionId,
                force: true);
            reportingSessionAdopted = string.Equals(
                _mediaPlaybackReportingSessionId,
                pendingReportingSessionId,
                StringComparison.Ordinal);
            PlaybackDetailText.Text = $"{logicalChannel.Group} • {resolved.Method.Replace('-', ' ')}";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (cancellation.IsCancellationRequested) return;
            FooterStatusDot.Fill = ErrorBrush;
            FooterStatusText.Text = "Protected playback could not be prepared";
            PlaybackDetailText.Text = SafeErrorMessage(exception);
        }
        finally
        {
            if (!reportingSessionAdopted)
                _mediaCenterSource.CancelPlaybackReportingSession(pendingReportingSessionId);
            if (ReferenceEquals(_mediaPlaybackCancellation, cancellation))
                _mediaPlaybackCancellation = null;
            cancellation.Dispose();
        }
    }

    private static ChannelItem CreateResolvedMediaChannel(ChannelItem logicalChannel, ResolvedMediaPlayback resolved) => new()
    {
        Number = logicalChannel.Number,
        Name = logicalChannel.Name,
        Url = resolved.Url,
        Group = logicalChannel.Group,
        LogoUrl = logicalChannel.LogoUrl,
        TvgId = logicalChannel.TvgId,
        TvgName = logicalChannel.TvgName,
        UserAgent = logicalChannel.UserAgent,
        Referrer = resolved.Referrer ?? logicalChannel.Referrer,
        Kind = logicalChannel.Kind == ChannelKind.Recording ? ChannelKind.Replay : logicalChannel.Kind,
        SourceId = logicalChannel.SourceId,
        SourceName = logicalChannel.SourceName,
        CatchupMode = logicalChannel.CatchupMode,
        CatchupSource = logicalChannel.CatchupSource,
        CatchupDays = logicalChannel.CatchupDays,
        CatchupCorrectionMinutes = logicalChannel.CatchupCorrectionMinutes,
        DurationMilliseconds = logicalChannel.DurationMilliseconds,
        ResumePositionMilliseconds = resolved.ResumePositionMilliseconds,
        IsPlayed = logicalChannel.IsPlayed
    };

    private void CancelMediaPlaybackResolution(bool clearResume)
    {
        _mediaPlaybackCancellation?.Cancel();
        _mediaPlaybackCancellation = null;
        if (!clearResume) return;
        _pendingMediaResumeMilliseconds = 0;
        _mediaResumeApplied = true;
    }

    private void PlaySignalFeed(SignalRoute route, ChannelItem feed, ChannelPlaybackProfile? profile = null)
    {
        _activeSignalFeed = feed;
        _attemptedSignalFeedKeys.Add(feed.StableKey);
        ResetSignalSessionCounters();
        var health = SmartSignalRoutingPolicy.GetOrCreateHealth(_settings.SignalRouting, feed, route.Key);
        health.LastAttemptUtc = DateTimeOffset.UtcNow;
        PlaybackDetailText.Text = route.HasAlternates
            ? $"{route.Representative.Group} • {SmartSignalRoutingPolicy.SourceLabel(feed)} • feed {_attemptedSignalFeedKeys.Count:N0}/{route.FeedCount:N0}"
            : route.Representative.Group;
        ActiveFeedValue.Text = SmartSignalRoutingPolicy.SourceLabel(feed);
        ActiveFeedValue.ToolTip = feed.Name;
        _playback?.Play(feed, profile ?? GetOrCreateChannelProfile(route.Representative));
        if (SignalRoutingOverlay.Visibility == Visibility.Visible) RefreshSignalRoutingPanel(route.Key);
    }

    private void ResetSignalSessionCounters()
    {
        _signalSessionSuccessRecorded = false;
        _signalSessionFailureRecorded = false;
        _lastSignalBufferEvents = 0;
        _lastSignalReconnects = 0;
        _lastSignalStallRecoveries = 0;
        _lastSignalDroppedFrames = 0;
    }

    private void RetuneCurrentPlayback(ChannelPlaybackProfile? profile = null)
    {
        if (_currentChannel is null) return;
        if (_currentChannel.IsProtectedMedia && _currentRecordingPath is null)
        {
            _ = TuneProtectedMediaAsync(_currentChannel, rememberChannel: false, profile: profile);
            return;
        }
        CaptureSignalTelemetry(_playback?.GetSnapshot(), persist: false);
        var route = GetSignalRoute(_currentChannel);
        if (route is not null && _currentChannel.Kind == ChannelKind.Live && _currentRecordingPath is null)
        {
            var currentEligible = _activeSignalFeed is not null && route.Feeds.Any(feed => feed.StableKey == _activeSignalFeed.StableKey) &&
                                  SmartSignalRoutingPolicy.Score(_activeSignalFeed, _settings.SignalRouting).IsEligible;
            var feed = currentEligible ? _activeSignalFeed! : ResolveBestSignalFeed(_currentChannel);
            _attemptedSignalFeedKeys.Clear();
            _automaticSignalSwitches = 0;
            PlaySignalFeed(route, feed, profile ?? GetOrCreateChannelProfile(_currentChannel));
            return;
        }

        _activeSignalFeed = _currentChannel;
        ResetSignalSessionCounters();
        _playback?.Play(_currentChannel, profile ?? GetOrCreateChannelProfile(_currentChannel));
    }

    private bool TryAutomaticSignalFailover(PlaybackStatus status)
    {
        if (!status.IsTerminalFailure || _currentRecordingPath is not null || _currentChannel?.Kind != ChannelKind.Live ||
            _currentChannel.IsProtectedMedia ||
            _activeSignalFeed is null || !_settings.SignalRouting.Enabled || !_settings.SignalRouting.AutomaticFailover)
            return false;
        var route = GetSignalRoute(_currentChannel);
        if (route is null || !route.HasAlternates) return false;

        RecordSignalFailure(status.TechnicalDetail ?? status.Message);
        var maximum = Math.Clamp(_settings.SignalRouting.MaximumAutomaticSwitches, 1, 8);
        if (_automaticSignalSwitches >= maximum) return false;
        var next = SmartSignalRoutingPolicy.SelectBestFeed(route, _settings.SignalRouting, _attemptedSignalFeedKeys);
        if (next is null) return false;

        var previous = _activeSignalFeed;
        var previousHealth = SmartSignalRoutingPolicy.GetOrCreateHealth(_settings.SignalRouting, previous, route.Key);
        previousHealth.AutomaticFailovers++;
        _automaticSignalSwitches++;
        _showRecoveryOverlay = true;
        RecoveryHeading.Text = "Switching to a backup feed";
        RecoveryDetail.Text = $"{SmartSignalRoutingPolicy.SourceLabel(previous)} stopped responding. " +
                              $"Trying {SmartSignalRoutingPolicy.SourceLabel(next)} automatically.";
        FooterStatusDot.Fill = WarningBrush;
        FooterStatusText.Text = $"Signal routing • backup {_automaticSignalSwitches:N0}/{maximum:N0}";
        PlaySignalFeed(route, next, GetOrCreateChannelProfile(_currentChannel));
        _ = _settingsStore.SaveAsync(_settings);
        RefreshPlayerSurfaceVisibility();
        return true;
    }

    private void RecordSignalFailure(string reason)
    {
        if (_signalSessionFailureRecorded || _activeSignalFeed is null || _currentChannel?.Kind != ChannelKind.Live) return;
        _signalSessionFailureRecorded = true;
        CaptureSignalTelemetry(_playback?.GetSnapshot(), persist: false);
        var route = GetSignalRoute(_currentChannel);
        if (route is null) return;
        var health = SmartSignalRoutingPolicy.GetOrCreateHealth(_settings.SignalRouting, _activeSignalFeed, route.Key);
        health.FailedStarts++;
        health.LastFailureUtc = DateTimeOffset.UtcNow;
        health.LastFailureReason = reason;
        _lastSignalHistorySaveUtc = DateTimeOffset.UtcNow;
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void RecordSignalSuccess(PlaybackSnapshot snapshot)
    {
        if (_signalSessionSuccessRecorded || _activeSignalFeed is null || _currentChannel?.Kind != ChannelKind.Live) return;
        var route = GetSignalRoute(_currentChannel);
        if (route is null) return;
        _signalSessionSuccessRecorded = true;
        var health = SmartSignalRoutingPolicy.GetOrCreateHealth(_settings.SignalRouting, _activeSignalFeed, route.Key);
        health.SuccessfulStarts++;
        health.LastSuccessUtc = DateTimeOffset.UtcNow;
        health.LastStartupMilliseconds = snapshot.StartupMilliseconds;
        ApplySignalQuality(health, snapshot);
        _lastSignalHistorySaveUtc = DateTimeOffset.UtcNow;
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void CaptureSignalTelemetry(PlaybackSnapshot? snapshot, bool persist)
    {
        if (snapshot is null || _activeSignalFeed is null || _currentChannel?.Kind != ChannelKind.Live) return;
        var route = GetSignalRoute(_currentChannel);
        if (route is null) return;
        var health = SmartSignalRoutingPolicy.GetOrCreateHealth(_settings.SignalRouting, _activeSignalFeed, route.Key);
        health.BufferEvents += CounterDelta(snapshot.BufferEvents, ref _lastSignalBufferEvents);
        health.Reconnects += CounterDelta(snapshot.ReconnectAttempts, ref _lastSignalReconnects);
        health.StallRecoveries += CounterDelta(snapshot.StallRecoveries, ref _lastSignalStallRecoveries);
        health.DroppedFrames += CounterDelta(snapshot.DroppedFrames, ref _lastSignalDroppedFrames);
        ApplySignalQuality(health, snapshot);
        if (snapshot.IsPlaying) RecordSignalSuccess(snapshot);
        var now = DateTimeOffset.UtcNow;
        if (persist || now - _lastSignalHistorySaveUtc >= TimeSpan.FromSeconds(20))
        {
            _lastSignalHistorySaveUtc = now;
            _ = _settingsStore.SaveAsync(_settings);
        }
    }

    private static int CounterDelta(int current, ref int previous)
    {
        current = Math.Max(0, current);
        var delta = current >= previous ? current - previous : current;
        previous = current;
        return delta;
    }

    private static void ApplySignalQuality(SignalFeedHealth health, PlaybackSnapshot snapshot)
    {
        var height = SmartSignalRoutingPolicy.ParseResolutionHeight(snapshot.Resolution);
        if (height > 0) health.LastResolutionHeight = height;
        if (snapshot.InputBitrateMbps > 0) health.LastInputBitrateMbps = snapshot.InputBitrateMbps;
        if (snapshot.FramesPerSecond > 0) health.LastFramesPerSecond = snapshot.FramesPerSecond;
        if (snapshot.StartupMilliseconds > 0) health.LastStartupMilliseconds = snapshot.StartupMilliseconds;
    }

    private ChannelPlaybackProfile GetOrCreateChannelProfile(ChannelItem channel)
    {
        _settings.ChannelProfiles ??= new Dictionary<string, ChannelPlaybackProfile>(StringComparer.OrdinalIgnoreCase);
        if (_settings.ChannelProfiles.TryGetValue(channel.StableKey, out var profile)) return profile;
        profile = new ChannelPlaybackProfile { UpdatedUtc = DateTimeOffset.UtcNow };
        _settings.ChannelProfiles[channel.StableKey] = profile;
        return profile;
    }

    private void TouchRecentChannel(ChannelItem channel)
    {
        _settings.RecentChannelKeys ??= [];
        _settings.RecentChannelKeys.RemoveAll(key => string.Equals(key, channel.StableKey, StringComparison.OrdinalIgnoreCase));
        _settings.RecentChannelKeys.Insert(0, channel.StableKey);
        if (_settings.RecentChannelKeys.Count > 24) _settings.RecentChannelKeys.RemoveRange(24, _settings.RecentChannelKeys.Count - 24);
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void Playback_StatusChanged(object? sender, PlaybackStatus status)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var previousMediaPlaybackState = _mediaPlaybackState;
            _mediaPlaybackState = status.State;
            if (status.IsTerminalFailure && TryAutomaticSignalFailover(status)) return;
            if (status.IsTerminalFailure) RecordSignalFailure(status.TechnicalDetail ?? status.Message);
            StreamStateValue.Text = status.State.ToString();
            PlaybackStatusText.Text = status.Message.ToUpperInvariant();
            PlaybackDetailText.Text = status.TechnicalDetail ?? _currentChannel?.Group ?? "Native playback engine";

            _showBufferOverlay = status.ShouldShowBufferOverlay;
            _showRecoveryOverlay = status.State is PlaybackState.Reconnecting or PlaybackState.Error;
            if (_showRecoveryOverlay)
            {
                RecoveryHeading.Text = status.State == PlaybackState.Error ? "The signal could not be restored" : "Restoring the signal";
                RecoveryDetail.Text = status.TechnicalDetail ?? status.Message;
            }
            RefreshPlayerSurfaceVisibility();
            if (status.ShouldShowBufferOverlay)
            {
                BufferText.Text = status.Message;
                BufferProgress.IsIndeterminate = status.BufferPercent <= 0;
                BufferProgress.Value = status.BufferPercent;
            }

            switch (status.State)
            {
                case PlaybackState.Opening:
                case PlaybackState.Reconnecting:
                    FooterStatusDot.Fill = WarningBrush;
                    LiveDot.Fill = WarningBrush;
                    PlayPauseGlyph.Text = "Ⅱ";
                    break;
                case PlaybackState.Buffering when status.IsBufferComplete:
                    StreamStateValue.Text = "Playing";
                    PlaybackStatusText.Text = "LIVE";
                    FooterStatusDot.Fill = LiveBrush;
                    LiveDot.Fill = LiveBrush;
                    PlayPauseGlyph.Text = "Ⅱ";
                    break;
                case PlaybackState.Buffering:
                    FooterStatusDot.Fill = WarningBrush;
                    LiveDot.Fill = WarningBrush;
                    PlayPauseGlyph.Text = "Ⅱ";
                    break;
                case PlaybackState.Playing:
                    if (_playback is not null) RecordSignalSuccess(_playback.GetSnapshot());
                    _showRecoveryOverlay = false;
                    RefreshPlayerSurfaceVisibility();
                    FooterStatusDot.Fill = LiveBrush;
                    FooterStatusText.Text = _currentRecordingPath is null
                        ? $"Native playback • {_currentChannel?.Name}"
                        : $"DVR playback • {_currentChannel?.Name}";
                    LiveDot.Fill = LiveBrush;
                    PlayPauseGlyph.Text = "Ⅱ";
                    QueueMediaPlaybackReport(
                        MediaCenterPlaybackState.Playing,
                        force: previousMediaPlaybackState != PlaybackState.Playing);
                    break;
                case PlaybackState.Paused:
                    FooterStatusDot.Fill = WarningBrush;
                    LiveDot.Fill = WarningBrush;
                    PlayPauseGlyph.Text = "▶";
                    QueueMediaPlaybackReport(MediaCenterPlaybackState.Paused, force: true);
                    break;
                case PlaybackState.Stopped when _currentRecordingPath is not null && status.Message == "Recording finished":
                    _settings.RecordingPlaybackProgress.Remove(DvrRecordingService.CreateLibraryKey(_currentRecordingPath));
                    _ = _settingsStore.SaveAsync(_settings);
                    FooterStatusDot.Fill = LiveBrush;
                    FooterStatusText.Text = $"Recording finished • {_currentChannel?.Name}";
                    LiveDot.Fill = IdleBrush;
                    PlayPauseGlyph.Text = "▶";
                    break;
                case PlaybackState.Error:
                    ObserveMediaPlaybackReport(EndCurrentMediaPlaybackReporting(_playback?.GetSnapshot()));
                    FooterStatusDot.Fill = ErrorBrush;
                    FooterStatusText.Text = _currentRecordingPath is null
                        ? "Playback error — try Stable buffer or another channel"
                        : "Recording playback error — the saved file may be unavailable or damaged";
                    LiveDot.Fill = ErrorBrush;
                    PlayPauseGlyph.Text = "▶";
                    break;
                default:
                    LiveDot.Fill = IdleBrush;
                    PlayPauseGlyph.Text = "▶";
                    break;
            }
        });
    }

    private void UpdateTelemetry(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (!_automationRun && now - _lastSessionHeartbeatUtc >= TimeSpan.FromSeconds(20))
        {
            _lastSessionHeartbeatUtc = now;
            _ = _sessionRecoveryService.HeartbeatAsync(CreateSessionSnapshot());
        }
        if (_guideSchedule is not null && DateTimeOffset.UtcNow - _lastGuidePresentationUpdate > TimeSpan.FromMinutes(1))
            ApplyGuideSchedule(_guideSchedule);
        if (_playback is null) return;
        var snapshot = _playback.GetSnapshot();
        VolumeGlyph.Text = snapshot.IsMuted || snapshot.Volume == 0 ? "×" : snapshot.Volume < 50 ? "◖" : "◗";
        BufferEventsValue.Text = snapshot.BufferEvents.ToString("N0");
        ReconnectsValue.Text = snapshot.ReconnectAttempts.ToString("N0");
        CacheValue.Text = $"{snapshot.ActiveCacheMilliseconds / 1000d:0.0} seconds";
        TuneStrategyValue.Text = snapshot.TuneStrategy;
        StartupValue.Text = snapshot.StartupMilliseconds > 0
            ? $"{snapshot.StartupMilliseconds / 1000d:0.0} seconds"
            : "—";
        DecodeValue.Text = snapshot.DecoderMode;
        DecoderBadgeText.Text = snapshot.DecoderMode switch
        {
            "Hardware auto" => "HW AUTO",
            "Software fallback" => "SW FALLBACK",
            _ => "SOFTWARE"
        };
        VideoFormatValue.Text = snapshot.VideoCodec == "—" && snapshot.Resolution == "—"
            ? "—"
            : $"{snapshot.VideoCodec} • {snapshot.Resolution}";
        FrameRateValue.Text = snapshot.FramesPerSecond > 0 ? $"{snapshot.FramesPerSecond:0.##} fps" : "—";
        BitrateValue.Text = snapshot.InputBitrateMbps > 0 ? $"{snapshot.InputBitrateMbps:0.00} Mbps" : "—";
        AudioFormatValue.Text = snapshot.AudioFormat;
        DroppedFramesValue.Text = snapshot.DroppedFrames.ToString("N0");
        RecoveryValue.Text = snapshot.DecoderFallbacks == 0 && snapshot.StallRecoveries == 0
            ? "No interventions"
            : $"{snapshot.DecoderFallbacks} decode • {snapshot.StallRecoveries} stall";
        UpdateFullscreenHud(snapshot);
        UpdateRecordingPlayback(snapshot);
        LearnCurrentChannelProfile(snapshot);
        CaptureSignalTelemetry(snapshot, persist: false);
        CheckProgramReminders();
        var dvrSnapshot = _dvrRecording.Poll(DateTimeOffset.UtcNow);
        ApplyDvrScheduleState(dvrSnapshot);
        CheckScheduledRecordings();
        dvrSnapshot = _dvrRecording.Poll(DateTimeOffset.UtcNow);
        ApplyDvrScheduleState(dvrSnapshot);
        UpdateDvrUi(dvrSnapshot);
        _recordingPowerGuard.SetActive(dvrSnapshot.IsActive);
        RefreshDvrWakeTimer();
        UpdateTrayRecorderStatus(dvrSnapshot);

        if (_playback.EnforceTimeshiftWindow(DateTimeOffset.UtcNow))
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "Timeshift window reached • returned to live";
        }
        UpdateTimeshiftUi(_playback.GetLiveTimeshiftSnapshot(DateTimeOffset.UtcNow));

        if (_settings.Playback.AdaptiveRefreshRate && snapshot.IsPlaying && snapshot.FramesPerSecond > 0)
            _displayRefreshRate?.TryMatch(snapshot.FramesPerSecond);
        DisplayRateValue.Text = _settings.Playback.AdaptiveRefreshRate
            ? _displayRefreshRate?.Status ?? "Display unavailable"
            : "Display default";

        if (SettingsOverlay.Visibility == Visibility.Visible)
            RefreshTrackControls(snapshot);
    }

    private void LearnCurrentChannelProfile(PlaybackSnapshot snapshot)
    {
        if (_currentChannel is null || _currentRecordingPath is not null || !snapshot.IsPlaying || !_settings.Playback.PlaybackIntelligence) return;
        var signature = $"{_currentChannel.StableKey}:{snapshot.DecoderFallbacks}:{snapshot.StallRecoveries}:{snapshot.ReconnectAttempts}:{snapshot.StartupMilliseconds}";
        if (signature == _learnedProfileSignature) return;
        _learnedProfileSignature = signature;

        var profile = GetOrCreateChannelProfile(_currentChannel);
        profile.SuccessfulStarts++;
        profile.LastStartupMilliseconds = snapshot.StartupMilliseconds;
        profile.LastSuccessfulUtc = DateTimeOffset.UtcNow;
        var requiredRecovery = snapshot.DecoderFallbacks > 0 || snapshot.StallRecoveries > 0 || snapshot.ReconnectAttempts > 0;
        if (requiredRecovery)
        {
            profile.FailedStarts++;
            profile.LastRecoveryReason = snapshot.RecoveryReason;
        }
        if (snapshot.DecoderFallbacks > 0) profile.HardwareDecoding = false;
        profile.LearnedInstability = requiredRecovery
            ? Math.Clamp(Math.Max(profile.LearnedInstability, snapshot.StallRecoveries + snapshot.ReconnectAttempts), 0, 4)
            : profile.SuccessfulStarts % 3 == 0
                ? Math.Max(0, profile.LearnedInstability - 1)
                : profile.LearnedInstability;
        profile.UpdatedUtc = DateTimeOffset.UtcNow;
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void UpdateRecordingPlayback(PlaybackSnapshot snapshot)
    {
        var isRecordingPlayback = _currentRecordingPath is not null && _currentChannel?.Kind == ChannelKind.Recording;
        var isProtectedLibraryPlayback = _currentRecordingPath is null &&
                                         _currentChannel?.IsProtectedMedia == true &&
                                         _currentChannel.Kind != ChannelKind.Live;
        RecordingSeekPanel.Visibility = isRecordingPlayback || isProtectedLibraryPlayback
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (isProtectedLibraryPlayback)
        {
            UpdateProtectedLibraryPlayback(snapshot);
            return;
        }
        if (!isRecordingPlayback) return;

        var length = Math.Max(0, snapshot.Length);
        var position = Math.Clamp(snapshot.Time, 0, Math.Max(0, length));
        if (!_recordingResumeApplied && length > 0)
        {
            _recordingResumeApplied = true;
            var key = DvrRecordingService.CreateLibraryKey(_currentRecordingPath!);
            if (_settings.RecordingPlaybackProgress.TryGetValue(key, out var progress) &&
                progress.PositionMilliseconds >= 30_000 &&
                progress.PositionMilliseconds < length - 30_000 &&
                _playback?.SeekTo(progress.PositionMilliseconds) == true)
            {
                position = progress.PositionMilliseconds;
                FooterStatusDot.Fill = LiveBrush;
                FooterStatusText.Text = $"Resumed recording at {FormatPlaybackTime(position)} • {_currentChannel?.Name}";
            }
        }

        if (!_seekingRecording)
        {
            PlaybackSeekSlider.Maximum = Math.Max(1, length);
            PlaybackSeekSlider.Value = position;
        }
        RecordingPositionText.Text = FormatPlaybackTime(position);
        RecordingDurationText.Text = FormatPlaybackTime(length);

        if (DateTimeOffset.UtcNow - _lastRecordingProgressSaveUtc >= TimeSpan.FromSeconds(10))
            SaveCurrentRecordingProgress(force: false, snapshot);
    }

    private void UpdateProtectedLibraryPlayback(PlaybackSnapshot snapshot)
    {
        var length = Math.Max(0, snapshot.Length);
        var position = Math.Clamp(snapshot.Time, 0, Math.Max(0, length));
        if (!_mediaResumeApplied && length > 0)
        {
            var resumePosition = Math.Clamp(_pendingMediaResumeMilliseconds, 0, length);
            if (resumePosition < 30_000 || resumePosition >= length - 30_000)
            {
                _mediaResumeApplied = true;
            }
            else if (_playback?.SeekTo(resumePosition) == true)
            {
                _mediaResumeApplied = true;
                position = resumePosition;
                FooterStatusDot.Fill = LiveBrush;
                FooterStatusText.Text = $"Resumed at {FormatPlaybackTime(position)} • {_currentChannel?.Name}";
                QueueMediaPlaybackReport(MediaCenterPlaybackState.Playing, snapshot with { Time = position }, force: true);
            }
        }

        if (!_seekingRecording)
        {
            PlaybackSeekSlider.Maximum = Math.Max(1, length);
            PlaybackSeekSlider.Value = position;
        }
        RecordingPositionText.Text = FormatPlaybackTime(position);
        RecordingDurationText.Text = FormatPlaybackTime(length);
        if (_mediaPlaybackState == PlaybackState.Playing)
            QueueMediaPlaybackReport(MediaCenterPlaybackState.Playing, snapshot);
    }

    private void QueueMediaPlaybackReport(
        MediaCenterPlaybackState state,
        PlaybackSnapshot? snapshot = null,
        bool force = false)
    {
        if (string.IsNullOrWhiteSpace(_mediaPlaybackReportingSessionId) ||
            _currentChannel?.IsProtectedMedia != true ||
            _currentChannel.Kind == ChannelKind.Live) return;
        var now = DateTimeOffset.UtcNow;
        if (!force &&
            _lastReportedMediaPlaybackState == state &&
            now - _lastMediaPlaybackReportUtc < TimeSpan.FromSeconds(10)) return;
        snapshot ??= _playback?.GetSnapshot();
        if (snapshot is null) return;
        _lastReportedMediaPlaybackState = state;
        _lastMediaPlaybackReportUtc = now;
        try
        {
            ObserveMediaPlaybackReport(_mediaCenterSource.ReportPlaybackAsync(
                _mediaPlaybackReportingSessionId,
                state,
                snapshot.Time,
                snapshot.Length,
                snapshot.IsMuted,
                snapshot.Volume));
        }
        catch
        {
            // Reporting is best-effort and must never interrupt local playback.
        }
    }

    private Task EndCurrentMediaPlaybackReporting(
        PlaybackSnapshot? snapshot = null,
        CancellationToken cancellationToken = default)
    {
        var reportingSessionId = _mediaPlaybackReportingSessionId;
        _mediaPlaybackReportingSessionId = null;
        _lastReportedMediaPlaybackState = null;
        _lastMediaPlaybackReportUtc = DateTimeOffset.MinValue;
        if (string.IsNullOrWhiteSpace(reportingSessionId)) return Task.CompletedTask;
        snapshot ??= _playback?.GetSnapshot();
        try
        {
            return _mediaCenterSource.StopPlaybackReportingAsync(
                reportingSessionId,
                snapshot?.Time ?? 0,
                snapshot?.Length ?? 0,
                snapshot?.IsMuted ?? false,
                snapshot?.Volume ?? 100,
                cancellationToken);
        }
        catch
        {
            _mediaCenterSource.CancelPlaybackReportingSession(reportingSessionId);
            return Task.CompletedTask;
        }
    }

    private static async void ObserveMediaPlaybackReport(Task report)
    {
        try
        {
            await report.ConfigureAwait(false);
        }
        catch
        {
            // Plex/Emby progress synchronization fails open so video keeps playing.
        }
    }

    private void SaveCurrentRecordingProgress(bool force, PlaybackSnapshot? snapshot = null)
    {
        if (_currentRecordingPath is null || _playback is null) return;
        snapshot ??= _playback.GetSnapshot();
        if (snapshot.Length <= 0 || snapshot.Time < 0) return;

        var key = DvrRecordingService.CreateLibraryKey(_currentRecordingPath);
        var position = Math.Clamp(snapshot.Time, 0, snapshot.Length);
        var changed = false;
        if (position >= snapshot.Length - 30_000 || force && position < 30_000)
        {
            changed = _settings.RecordingPlaybackProgress.Remove(key);
        }
        else if (position >= 30_000)
        {
            if (!_settings.RecordingPlaybackProgress.TryGetValue(key, out var existing) ||
                Math.Abs(existing.PositionMilliseconds - position) >= 1_000 ||
                existing.DurationMilliseconds != snapshot.Length)
            {
                _settings.RecordingPlaybackProgress[key] = new DvrPlaybackProgress
                {
                    PositionMilliseconds = position,
                    DurationMilliseconds = snapshot.Length,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
                changed = true;
            }
        }

        _lastRecordingProgressSaveUtc = DateTimeOffset.UtcNow;
        if (changed) _ = _settingsStore.SaveAsync(_settings);
    }

    private void PlaybackSeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _seekingRecording = HasSeekableLibraryPlayback;

    private void PlaybackSeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!HasSeekableLibraryPlayback) return;
        _seekingRecording = false;
        _playback?.SeekTo((long)PlaybackSeekSlider.Value);
        RecordingPositionText.Text = FormatPlaybackTime((long)PlaybackSeekSlider.Value);
        if (_currentRecordingPath is not null) _lastRecordingProgressSaveUtc = DateTimeOffset.MinValue;
        else QueueMediaPlaybackReport(CurrentMediaCenterPlaybackState(), _playback?.GetSnapshot(), force: true);
    }

    private void PlaybackSeekSlider_KeyUp(object sender, KeyEventArgs e)
    {
        if (!HasSeekableLibraryPlayback || e.Key is not (Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown)) return;
        _playback?.SeekTo((long)PlaybackSeekSlider.Value);
        if (_currentRecordingPath is not null) _lastRecordingProgressSaveUtc = DateTimeOffset.MinValue;
        else QueueMediaPlaybackReport(CurrentMediaCenterPlaybackState(), _playback?.GetSnapshot(), force: true);
    }

    private MediaCenterPlaybackState CurrentMediaCenterPlaybackState() =>
        _mediaPlaybackState == PlaybackState.Paused
            ? MediaCenterPlaybackState.Paused
            : MediaCenterPlaybackState.Playing;

    private bool HasSeekableLibraryPlayback =>
        _currentRecordingPath is not null ||
        _currentChannel?.IsProtectedMedia == true && _currentChannel.Kind != ChannelKind.Live;

    private static string FormatPlaybackTime(long milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }

    private void CheckProgramReminders()
    {
        if (_settings.ProgramReminders is null || _settings.ProgramReminders.Count == 0) return;
        var now = DateTimeOffset.UtcNow;
        var expired = _settings.ProgramReminders.RemoveAll(reminder => reminder.StopUtc <= now.AddMinutes(-5));
        var due = _settings.ProgramReminders
            .Where(reminder => !reminder.Notified && reminder.StartUtc <= now.AddMinutes(2) && reminder.StopUtc > now)
            .OrderBy(reminder => reminder.StartUtc)
            .FirstOrDefault();
        if (due is not null)
        {
            due.Notified = true;
            _activeReminder = due;
            ReminderTitle.Text = due.ProgramTitle;
            ReminderChannel.Text = $"{due.ChannelName} • {due.StartUtc.ToLocalTime():h:mm tt}";
            ReminderToast.Visibility = Visibility.Visible;
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = $"Reminder • {due.ProgramTitle} is starting on {due.ChannelName}";
        }
        if (expired > 0 || due is not null) _ = _settingsStore.SaveAsync(_settings);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null) SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_windowReady) RefreshFilters();
    }

    private void KindFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string mode } &&
            Enum.TryParse<MediaLibraryBrowseMode>(mode, ignoreCase: true, out var parsed))
            _libraryBrowseMode = parsed;
        if (_windowReady)
        {
            ApplyChannelGrouping();
            RefreshFilters();
        }
    }

    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _categoryFilter = (CategoryBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        if (_windowReady)
        {
            ApplyChannelGrouping();
            RefreshFilters();
        }
    }

    private void ApplyChannelGrouping()
    {
        if (_channelView is null) return;
        _channelView.GroupDescriptions.Clear();
        _channelView.SortDescriptions.Clear();
        if (_categoryFilter.Length == 0 && !MediaLibraryBrowsePolicy.UsesEditorialOrder(_libraryBrowseMode))
            _channelView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ChannelItem.Group))
            {
                StringComparison = StringComparison.OrdinalIgnoreCase
            });
        if (_libraryBrowseMode == MediaLibraryBrowseMode.ContinueWatching)
            _channelView.SortDescriptions.Add(new SortDescription(nameof(ChannelItem.LastPlayedAtUtc), ListSortDirection.Descending));
        else if (_libraryBrowseMode == MediaLibraryBrowseMode.RecentlyAdded)
            _channelView.SortDescriptions.Add(new SortDescription(nameof(ChannelItem.AddedAtUtc), ListSortDirection.Descending));
    }

    private void WatchNavigation_Checked(object sender, RoutedEventArgs e)
    {
        if (!_windowReady) return;
        SetMultiviewMode(false);
        SetGuideMode(false);
        _favoritesOnly = false;
        CatalogHeading.Text = _libraryBrowseSummary.IsMediaCenterLibrary ? "Library" : "Channels";
        RefreshFilters();
        if (_currentChannel is not null && _playback?.MediaPlayer.Media is null)
            RetuneCurrentPlayback();
    }

    private void FavoritesNavigation_Checked(object sender, RoutedEventArgs e)
    {
        if (!_windowReady) return;
        SetMultiviewMode(false);
        SetGuideMode(false);
        _favoritesOnly = true;
        CatalogHeading.Text = "Favorites";
        RefreshFilters();
        if (_currentChannel is not null && _playback?.MediaPlayer.Media is null)
            RetuneCurrentPlayback();
    }

    private void GuideNavigation_Checked(object sender, RoutedEventArgs e)
    {
        if (!_windowReady) return;
        SetMultiviewMode(false);
        SetGuideMode(true);
        if (_guideSchedule is not null && DateTimeOffset.UtcNow - _lastGuidePresentationUpdate > TimeSpan.FromMinutes(1))
            ApplyGuideSchedule(_guideSchedule);
    }

    private void SetGuideMode(bool showGuide)
    {
        GuideWorkspace.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        CatalogPanel.Visibility = showGuide ? Visibility.Collapsed : Visibility.Visible;
        PlayerPanel.Visibility = showGuide ? Visibility.Collapsed : Visibility.Visible;
        InspectorPanel.Visibility = showGuide ? Visibility.Collapsed : Visibility.Visible;
        _playerChromeSuppressed = showGuide || ImportOverlay.Visibility == Visibility.Visible || PlaylistHealthOverlay.Visibility == Visibility.Visible || ChannelHealthOverlay.Visibility == Visibility.Visible ||
                                  SettingsOverlay.Visibility == Visibility.Visible || UpdateOverlay.Visibility == Visibility.Visible ||
                                  CastOverlay.Visibility == Visibility.Visible || DvrOverlay.Visibility == Visibility.Visible ||
                                  SignalRoutingOverlay.Visibility == Visibility.Visible || MappingOverlay.Visibility == Visibility.Visible;
        RefreshPlayerSurfaceVisibility();
    }

    private void MultiviewNavigation_Checked(object sender, RoutedEventArgs e)
    {
        if (!_windowReady) return;
        SetGuideMode(false);
        SetMultiviewMode(true);
    }

    private void SetMultiviewMode(bool enabled)
    {
        if (!enabled)
        {
            if (_multiviewMode) _multiviewSession?.StopAll();
            _multiviewSession?.SetFullscreenPresentation(false);
            _multiviewMode = false;
            MultiviewWorkspace.Visibility = Visibility.Collapsed;
            _playerChromeSuppressed = GuideWorkspace.Visibility == Visibility.Visible || IsAnyModalVisible();
            ApplyFullscreenPresentation(_isFullscreen);
            RefreshPlayerSurfaceVisibility();
            return;
        }

        _multiviewMode = true;
        _displayRefreshRate?.Restore();
        CaptureSignalTelemetry(_playback?.GetSnapshot(), persist: true);
        _playback?.Stop();
        _showBufferOverlay = false;
        GuideWorkspace.Visibility = Visibility.Collapsed;
        PlayerPanel.Visibility = Visibility.Collapsed;
        InspectorPanel.Visibility = Visibility.Collapsed;
        CatalogPanel.Visibility = Visibility.Visible;
        MultiviewWorkspace.Visibility = Visibility.Visible;
        _favoritesOnly = false;
        CatalogHeading.Text = "Multiview channels";
        RefreshFilters();
        EnsureMultiviewSession();
        _multiviewSession?.SetFullscreenPresentation(_isFullscreen);
        UpdateMultiviewLayout();
        ApplyFullscreenPresentation(_isFullscreen);
        _playerChromeSuppressed = true;
        RefreshPlayerSurfaceVisibility();
        FooterStatusDot.Fill = LiveBrush;
        FooterStatusText.Text = "Multiview ready • select a view and choose a channel";
    }

    private void EnsureMultiviewSession()
    {
        if (_multiviewSession is not null) return;
        _settings.Multiview ??= new MultiviewPreferences();
        _multiviewLayout = Enum.TryParse<MultiviewLayout>(_settings.Multiview.Layout, true, out var layout)
            ? layout
            : MultiviewLayout.Quad;
        _multiviewSession = new MultiviewSession(_settings.Playback);
        RefreshSavedMultiviewLayouts();
        _multiviewSession.SetFullscreenPresentation(_isFullscreen && _multiviewMode);
        _multiviewSession.SelectSlot(Math.Clamp(_settings.Multiview.ActiveSlot, 0, MultiviewSession.MaximumTiles - 1));
        _multiviewSession.SetAudioSlot(Math.Clamp(_settings.Multiview.AudioSlot, 0, MultiviewSession.MaximumTiles - 1));

        var keys = _settings.Multiview.ChannelKeys ?? [];
        for (var index = 0; index < Math.Min(keys.Count, MultiviewSession.MaximumTiles); index++)
        {
            var key = keys[index];
            if (string.IsNullOrWhiteSpace(key)) continue;
            var channel = FindLogicalChannel(key);
            if (channel is not null) _multiviewSession.RestoreChannel(index, ResolveBestSignalFeed(channel));
        }

        if (_multiviewSession.Tiles.All(tile => !tile.HasChannel) && _currentChannel is not null)
            _multiviewSession.RestoreChannel(0, ResolveBestSignalFeed(_currentChannel));
    }

    private void UpdateMultiviewLayout(bool managePlayback = true)
    {
        if (_multiviewSession is null) return;
        if (_multiviewLayout == MultiviewLayout.Duo && _multiviewSession.ActiveSlot >= 2)
            _multiviewSession.SelectSlot(0);

        var panelKey = _multiviewLayout switch
        {
            MultiviewLayout.Duo => "MultiviewDuoPanel",
            MultiviewLayout.Focus => "MultiviewFocusPanel",
            _ => "MultiviewQuadPanel"
        };
        MultiviewTiles.ItemsSource = null;
        MultiviewTiles.ItemsPanel = (ItemsPanelTemplate)MultiviewWorkspace.FindResource(panelKey);
        MultiviewTiles.ItemsSource = _multiviewSession.VisibleTiles(_multiviewLayout);
        if (managePlayback) _multiviewSession.ApplyLayoutResourceBudget(_multiviewLayout);
        UpdateMultiviewPresentation();
        RefreshPlayerSurfaceVisibility();
    }

    private void UpdateMultiviewPresentation()
    {
        if (_multiviewSession is null) return;
        var active = _multiviewSession.Tiles[_multiviewSession.ActiveSlot];
        var audio = _multiviewSession.Tiles[_multiviewSession.AudioSlot];
        MultiviewActiveText.Text = $"VIEW {active.Number} SELECTED • AUDIO VIEW {audio.Number}";
        MultiviewStatusText.Text = active.HasChannel
            ? $"View {active.Number}: {active.ChannelName} • choose another channel to replace it."
            : $"View {active.Number} is ready • choose a channel from the library.";
        MultiviewDuoButton.Opacity = _multiviewLayout == MultiviewLayout.Duo ? 1 : 0.62;
        MultiviewQuadButton.Opacity = _multiviewLayout == MultiviewLayout.Quad ? 1 : 0.62;
        MultiviewFocusButton.Opacity = _multiviewLayout == MultiviewLayout.Focus ? 1 : 0.62;
    }

    private async void MultiviewDuo_Click(object sender, RoutedEventArgs e)
    {
        _multiviewLayout = MultiviewLayout.Duo;
        UpdateMultiviewLayout();
        await PersistMultiviewAsync();
    }

    private async void MultiviewQuad_Click(object sender, RoutedEventArgs e)
    {
        _multiviewLayout = MultiviewLayout.Quad;
        UpdateMultiviewLayout();
        await PersistMultiviewAsync();
    }

    private async void MultiviewFocus_Click(object sender, RoutedEventArgs e)
    {
        _multiviewLayout = MultiviewLayout.Focus;
        UpdateMultiviewLayout();
        await PersistMultiviewAsync();
    }

    private async void MultiviewTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MultiviewTile tile || _multiviewSession is null) return;
        _dragStartPoint = e.GetPosition(this);
        _multiviewSession.SelectSlot(tile.Index);
        if (_multiviewLayout == MultiviewLayout.Focus) UpdateMultiviewLayout();
        else UpdateMultiviewPresentation();
        if (e.ClickCount >= 2)
        {
            _multiviewLayout = MultiviewLayout.Focus;
            UpdateMultiviewLayout();
        }
        await PersistMultiviewAsync();
    }

    private async void MultiviewSelectTile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MultiviewTile tile || _multiviewSession is null) return;
        _multiviewSession.SelectSlot(tile.Index);
        UpdateMultiviewPresentation();
        SearchBox.Focus();
        SearchBox.SelectAll();
        await PersistMultiviewAsync();
        e.Handled = true;
    }

    private async void MultiviewAudio_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MultiviewTile tile || _multiviewSession is null) return;
        _multiviewSession.SelectSlot(tile.Index);
        _multiviewSession.SetAudioSlot(tile.Index);
        UpdateMultiviewPresentation();
        await PersistMultiviewAsync();
        e.Handled = true;
    }

    private async void MultiviewTileFocus_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MultiviewTile tile || _multiviewSession is null) return;
        _multiviewSession.SelectSlot(tile.Index);
        _multiviewLayout = MultiviewLayout.Focus;
        UpdateMultiviewLayout();
        await PersistMultiviewAsync();
        e.Handled = true;
    }

    private async void MultiviewClear_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MultiviewTile tile || _multiviewSession is null) return;
        _multiviewSession.ClearSlot(tile.Index);
        UpdateMultiviewPresentation();
        await PersistMultiviewAsync();
        e.Handled = true;
    }

    private async void MultiviewClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (_multiviewSession is null) return;
        foreach (var tile in _multiviewSession.Tiles) _multiviewSession.ClearSlot(tile.Index);
        _multiviewSession.SelectSlot(0);
        _multiviewSession.SetAudioSlot(0);
        UpdateMultiviewLayout();
        await PersistMultiviewAsync();
    }

    private async void MultiviewSwap_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MultiviewTile tile || _multiviewSession is null) return;
        var selected = _multiviewSession.ActiveSlot;
        if (selected == tile.Index)
        {
            FooterStatusText.Text = "Select one view, then use Swap on another view";
            e.Handled = true;
            return;
        }
        _multiviewSession.SwapSlots(selected, tile.Index);
        UpdateMultiviewLayout();
        await PersistMultiviewAsync();
        FooterStatusText.Text = $"Swapped View {selected + 1} and View {tile.Number}";
        e.Handled = true;
    }

    private async void MultiviewSaveLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_multiviewSession is null) return;
        var name = MultiviewLayoutNameBox.Text.Trim();
        if (name.Length == 0) name = $"Layout {_settings.Multiview.SavedLayouts.Count + 1}";
        if (name.Length > 36) name = name[..36];
        var preset = new MultiviewLayoutPreset
        {
            Name = name,
            Layout = _multiviewLayout.ToString(),
            ActiveSlot = _multiviewSession.ActiveSlot,
            AudioSlot = _multiviewSession.AudioSlot,
            ChannelKeys = _multiviewSession.Tiles.Select(tile => GetLogicalChannelKey(tile.Channel)).ToList()
        };
        var existing = _settings.Multiview.SavedLayouts.FindIndex(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _settings.Multiview.SavedLayouts[existing] = preset;
        else _settings.Multiview.SavedLayouts.Add(preset);
        await _settingsStore.SaveAsync(_settings);
        RefreshSavedMultiviewLayouts(preset.Name);
        FooterStatusText.Text = $"Saved Multiview layout • {preset.Name}";
    }

    private async void MultiviewLoadLayout_Click(object sender, RoutedEventArgs e)
    {
        if (MultiviewSavedLayoutsBox.SelectedItem is not MultiviewLayoutPreset preset) return;
        EnsureMultiviewSession();
        foreach (var tile in _multiviewSession!.Tiles) _multiviewSession.ClearSlot(tile.Index);
        for (var index = 0; index < Math.Min(preset.ChannelKeys.Count, MultiviewSession.MaximumTiles); index++)
        {
            var key = preset.ChannelKeys[index];
            if (string.IsNullOrWhiteSpace(key)) continue;
            var channel = FindLogicalChannel(key);
            if (channel is not null) _multiviewSession.RestoreChannel(index, ResolveBestSignalFeed(channel));
        }
        _multiviewLayout = Enum.TryParse<MultiviewLayout>(preset.Layout, true, out var layout) ? layout : MultiviewLayout.Quad;
        _multiviewSession.SelectSlot(Math.Clamp(preset.ActiveSlot, 0, MultiviewSession.MaximumTiles - 1));
        _multiviewSession.SetAudioSlot(Math.Clamp(preset.AudioSlot, 0, MultiviewSession.MaximumTiles - 1));
        UpdateMultiviewLayout();
        await PersistMultiviewAsync();
        FooterStatusText.Text = $"Loaded Multiview layout • {preset.Name}";
    }

    private void RefreshSavedMultiviewLayouts(string? selectedName = null)
    {
        if (MultiviewSavedLayoutsBox is null) return;
        _settings.Multiview ??= new MultiviewPreferences();
        _settings.Multiview.SavedLayouts ??= [];
        MultiviewSavedLayoutsBox.ItemsSource = null;
        MultiviewSavedLayoutsBox.ItemsSource = _settings.Multiview.SavedLayouts.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        MultiviewSavedLayoutsBox.SelectedItem = MultiviewSavedLayoutsBox.Items
            .OfType<MultiviewLayoutPreset>()
            .FirstOrDefault(item => item.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase)) ??
            MultiviewSavedLayoutsBox.Items.OfType<MultiviewLayoutPreset>().FirstOrDefault();
    }

    private void ChannelList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _dragStartPoint = e.GetPosition(this);

    private void ChannelList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_multiviewMode || e.LeftButton != MouseButtonState.Pressed || ChannelList.SelectedItem is not ChannelItem channel) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(ChannelList, channel, System.Windows.DragDropEffects.Copy);
    }

    private void MultiviewTile_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || (sender as FrameworkElement)?.DataContext is not MultiviewTile tile) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop((DependencyObject)sender, tile, System.Windows.DragDropEffects.Move);
    }

    private async void MultiviewTile_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MultiviewTile target || _multiviewSession is null) return;
        if (e.Data.GetDataPresent(typeof(ChannelItem)) && e.Data.GetData(typeof(ChannelItem)) is ChannelItem channel)
        {
            _multiviewSession.AssignChannel(target.Index, ResolveBestSignalFeed(channel));
        }
        else if (e.Data.GetDataPresent(typeof(MultiviewTile)) && e.Data.GetData(typeof(MultiviewTile)) is MultiviewTile source)
        {
            _multiviewSession.SwapSlots(source.Index, target.Index);
        }
        else return;
        UpdateMultiviewLayout();
        await PersistMultiviewAsync();
        e.Handled = true;
    }

    private async Task PersistMultiviewAsync()
    {
        if (_multiviewSession is null) return;
        _settings.Multiview ??= new MultiviewPreferences();
        _settings.Multiview.Layout = _multiviewLayout.ToString();
        _settings.Multiview.ActiveSlot = _multiviewSession.ActiveSlot;
        _settings.Multiview.AudioSlot = _multiviewSession.AudioSlot;
        _settings.Multiview.ChannelKeys = _multiviewSession.Tiles.Select(tile => GetLogicalChannelKey(tile.Channel)).ToList();
        await _settingsStore.SaveAsync(_settings);
    }

    private void GuideSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshGuideViews();
    }

    private void GuideFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GuideFilterBox.SelectedItem is ComboBoxItem item)
            _guideFilter = item.Tag?.ToString() ?? "All";
        RefreshGuideViews();
    }

    private void GuideTimelineView_Click(object sender, RoutedEventArgs e) => SetGuideViewMode(true);

    private void GuideListView_Click(object sender, RoutedEventArgs e) => SetGuideViewMode(false);

    private void SetGuideViewMode(bool timeline)
    {
        _guideTimelineMode = timeline;
        GuideTimelinePanel.Visibility = timeline ? Visibility.Visible : Visibility.Collapsed;
        GuideListPanel.Visibility = timeline ? Visibility.Collapsed : Visibility.Visible;
        GuideTimelineViewButton.Opacity = timeline ? 1 : 0.62;
        GuideListViewButton.Opacity = timeline ? 0.62 : 1;
        RefreshGuideViews();
    }

    private void GuidePreviousWindow_Click(object sender, RoutedEventArgs e)
    {
        _guideWindowStart = _guideWindowStart.AddMinutes(-90);
        RebuildGuideTimeline();
        RefreshGuideViews();
    }

    private void GuideJumpNow_Click(object sender, RoutedEventArgs e)
    {
        _guideWindowStart = AlignTimelineStart(DateTimeOffset.UtcNow);
        RebuildGuideTimeline();
        RefreshGuideViews();
        GuideTimelineScroll.ScrollToHorizontalOffset(0);
    }

    private void GuideNextWindow_Click(object sender, RoutedEventArgs e)
    {
        _guideWindowStart = _guideWindowStart.AddMinutes(90);
        RebuildGuideTimeline();
        RefreshGuideViews();
    }

    private static DateTimeOffset AlignTimelineStart(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var aligned = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute / 30 * 30, 0, TimeSpan.Zero);
        return aligned.AddMinutes(-30);
    }

    private void GuideTimelineScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_synchronizingGuideScroll) return;
        _synchronizingGuideScroll = true;
        GuideTimeHeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
        GuideChannelScroll.ScrollToVerticalOffset(e.VerticalOffset);
        _synchronizingGuideScroll = false;
    }

    private void GuideChannelScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_synchronizingGuideScroll) return;
        _synchronizingGuideScroll = true;
        GuideTimelineScroll.ScrollToVerticalOffset(e.VerticalOffset);
        _synchronizingGuideScroll = false;
    }

    private void GuideProgramme_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GuideProgrammeBlock block) return;
        if (block.IsPlaceholder)
        {
            _ = OpenMappingEditorAsync(block.Channel);
            return;
        }
        if (block.CanReplay)
        {
            PlayCatchupProgramme(block);
            return;
        }
        WatchFromGuide(block.Channel);
    }

    private void GuideContextWatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GuideProgrammeBlock block)
            WatchFromGuide(block.Channel);
    }

    private void GuideContextReplay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GuideProgrammeBlock { CanReplay: true } block)
            PlayCatchupProgramme(block);
    }

    private void PlayCatchupProgramme(GuideProgrammeBlock block)
    {
        if (block.Programme is null) return;
        try
        {
            if (_multiviewMode) SetMultiviewMode(false);
            var replay = CatchupReplayPolicy.CreateReplayChannel(block.Channel, block.Programme);
            WatchNavigation.IsChecked = true;
            ChannelList.SelectedItem = null;
            TuneChannel(replay, rememberChannel: false);
            _previousChannel = block.Channel;
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = $"Replay • {block.Programme.Title} • Backspace returns to live TV";
        }
        catch (InvalidOperationException exception)
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = exception.Message;
        }
    }

    private async void GuideContextReminder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GuideProgrammeBlock { Programme: not null } block)
            await ToggleProgramReminderAsync(block);
    }

    private async Task ToggleProgramReminderAsync(GuideProgrammeBlock block)
    {
        if (block.Programme is null) return;
        if (block.Programme.Start <= DateTimeOffset.UtcNow)
        {
            FooterStatusText.Text = "Reminders can be added to upcoming programs";
            return;
        }

        _settings.ProgramReminders ??= [];
        var existing = _settings.ProgramReminders.FirstOrDefault(reminder =>
            reminder.ChannelKey.Equals(block.Channel.StableKey, StringComparison.OrdinalIgnoreCase) &&
            reminder.StartUtc == block.Programme.Start &&
            reminder.ProgramTitle.Equals(block.Programme.Title, StringComparison.Ordinal));
        if (existing is null)
        {
            _settings.ProgramReminders.Add(new ProgramReminder
            {
                ChannelKey = block.Channel.StableKey,
                ChannelName = block.Channel.Name,
                ProgramTitle = block.Programme.Title,
                StartUtc = block.Programme.Start,
                StopUtc = block.Programme.Stop
            });
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = $"Reminder set • {block.Programme.Title} at {block.Programme.Start.ToLocalTime():h:mm tt}";
        }
        else
        {
            _settings.ProgramReminders.Remove(existing);
            FooterStatusText.Text = $"Reminder removed • {block.Programme.Title}";
        }
        await _settingsStore.SaveAsync(_settings);
    }

    private async void GuideContextRecording_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GuideProgrammeBlock { Programme: not null } block) return;
        await ToggleScheduledRecordingAsync(block);
    }

    private async void GuideContextSeriesRecording_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GuideProgrammeBlock { Programme: not null } block) return;
        await ToggleSeriesRecordingRuleAsync(block);
    }

    private void WatchReminder_Click(object sender, RoutedEventArgs e)
    {
        var channel = _activeReminder is null
            ? null
            : _channels.FirstOrDefault(candidate => candidate.StableKey.Equals(_activeReminder.ChannelKey, StringComparison.OrdinalIgnoreCase));
        ReminderToast.Visibility = Visibility.Collapsed;
        _activeReminder = null;
        if (channel is not null) WatchFromGuide(channel);
    }

    private void DismissReminder_Click(object sender, RoutedEventArgs e)
    {
        ReminderToast.Visibility = Visibility.Collapsed;
        _activeReminder = null;
    }

    private async void GuideMapChannel_Click(object sender, RoutedEventArgs e)
    {
        var channel = (sender as FrameworkElement)?.DataContext switch
        {
            GuideTimelineRow timeline => timeline.Channel,
            GuideChannelRow row => row.Channel,
            _ => null
        };
        await OpenMappingEditorAsync(channel);
    }

    private void GuideWatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GuideChannelRow row) WatchFromGuide(row.Channel);
    }

    private void GuideList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GuideList.SelectedItem is GuideChannelRow row) WatchFromGuide(row.Channel);
    }

    private void WatchFromGuide(ChannelItem channel)
    {
        _favoritesOnly = false;
        SearchBox.Clear();
        CategoryBox.SelectedIndex = 0;
        _libraryBrowseMode = MediaLibraryBrowseMode.All;
        AllFilter.IsChecked = true;
        RefreshFilters();
        ChannelList.SelectedItem = channel;
        ChannelList.ScrollIntoView(channel);
        WatchNavigation.IsChecked = true;
    }

    private async void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is ChannelItem channel)
            await ToggleFavoriteAsync(channel);
    }

    private async void ToggleCurrentFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_currentChannel is not null && _currentChannel.Kind != ChannelKind.Recording)
            await ToggleFavoriteAsync(_currentChannel);
    }

    private async Task ToggleFavoriteAsync(ChannelItem channel)
    {
        if (channel.Kind == ChannelKind.Recording) return;
        channel.IsFavorite = !channel.IsFavorite;
        var route = GetSignalRoute(channel);
        foreach (var feed in route?.Feeds ?? [channel]) _favoriteKeys.Remove(feed.StableKey);
        if (channel.IsFavorite) _favoriteKeys.Add(route?.Representative.StableKey ?? channel.StableKey);

        _settings.FavoriteChannelKeys = _favoriteKeys.Order(StringComparer.OrdinalIgnoreCase).ToList();
        await _settingsStore.SaveAsync(_settings);
        UpdateCurrentFavoriteButton(_currentChannel);
        if (_favoritesOnly) RefreshFilters();
        RefreshGuideViews();
        FooterStatusText.Text = channel.IsFavorite
            ? $"Added {channel.Name} to favorites"
            : $"Removed {channel.Name} from favorites";
    }

    private void UpdateCurrentFavoriteButton(ChannelItem? channel)
    {
        CurrentFavoriteButton.Visibility = channel is null || channel.Kind is ChannelKind.Recording or ChannelKind.Replay
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (channel is null || channel.Kind is ChannelKind.Recording or ChannelKind.Replay) return;
        CurrentFavoriteButton.Content = channel.IsFavorite ? "★ Favorited" : "☆ Add favorite";
    }

    private void UpdateSignalRouteAvailability()
    {
        if (SignalRoutesButton is null) return;
        var currentRoute = GetSignalRoute(_currentChannel);
        SignalRoutesButton.Visibility = currentRoute is not null || _signalRoutes.Any(route => route.HasAlternates)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SignalRoutesButton.Content = currentRoute?.HasAlternates == true
            ? $"◉ Choose from {currentRoute.FeedCount:N0} feeds"
            : "◉ Signal history";
    }

    private void OpenSignalRouting_Click(object sender, RoutedEventArgs e) => OpenSignalRoutingPanel();

    private void OpenSignalRoutingPanel(string? preferredRouteKey = null)
    {
        var currentRoute = GetSignalRoute(_currentChannel);
        var routes = _signalRoutes
            .Where(route => route.HasAlternates || ReferenceEquals(route, currentRoute) ||
                route.Key.Equals(preferredRouteKey, StringComparison.OrdinalIgnoreCase) ||
                route.Feeds.Any(feed => _settings.SignalRouting.SeparatedFeedKeys.Contains(feed.StableKey, StringComparer.OrdinalIgnoreCase)) ||
                route.Feeds.Any(feed =>
                _settings.SignalRouting.FeedHealth.TryGetValue(feed.StableKey, out var health) && health.CompletedStarts > 0))
            .OrderBy(route => route.Representative.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(route => route.Representative.Group, StringComparer.OrdinalIgnoreCase)
            .Select(route => new SignalRouteChoice(route.Key, route.Representative.Name, route.Representative.Group, route.FeedCount))
            .ToList();
        if (routes.Count == 0)
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "Signal routing will appear when a live channel is available";
            return;
        }

        SignalRouteChannelBox.ItemsSource = null;
        SignalRouteChannelBox.ItemsSource = routes;
        SignalRoutingAutoCheck.IsChecked = _settings.SignalRouting.AutomaticFailover;
        if (_signalRouteCandidates.Count == 0)
            _signalRouteCandidates = _allChannels
                .Where(channel => channel.Kind == ChannelKind.Live)
                .OrderBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(channel => SmartSignalRoutingPolicy.SourceLabel(channel), StringComparer.OrdinalIgnoreCase)
                .Select(channel => new SignalRouteCandidate(
                    channel,
                    $"{channel.Name}  •  {channel.Group}  •  {SmartSignalRoutingPolicy.SourceLabel(channel)}"))
                .ToList();
        SignalRouteCandidateBox.ItemsSource = _signalRouteCandidates;
        SignalRouteCandidateBox.SelectedItem = null;
        SignalRouteCandidateBox.Text = string.Empty;
        var selectedKey = preferredRouteKey ?? currentRoute?.Key ?? _selectedSignalRouteKey ?? routes[0].RouteKey;
        SignalRouteChannelBox.SelectedItem = routes.FirstOrDefault(route => route.RouteKey.Equals(selectedKey, StringComparison.OrdinalIgnoreCase)) ?? routes[0];
        ShowModal(SignalRoutingOverlay);
        RefreshSignalRoutingPanel(selectedKey);
    }

    private void SignalRouteChannelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SignalRouteChannelBox.SelectedItem is SignalRouteChoice choice)
            RefreshSignalRoutingPanel(choice.RouteKey);
    }

    private void RefreshSignalRoutingPanel(string? routeKey)
    {
        if (string.IsNullOrWhiteSpace(routeKey) || !_signalRoutesByKey.TryGetValue(routeKey, out var route)) return;
        _selectedSignalRouteKey = route.Key;
        var activeKey = GetSignalRoute(_currentChannel)?.Key.Equals(route.Key, StringComparison.OrdinalIgnoreCase) == true
            ? _activeSignalFeed?.StableKey
            : null;
        var rows = route.Feeds
            .Select(feed => SmartSignalRoutingPolicy.CreateRow(
                route,
                feed,
                _settings.SignalRouting,
                string.Equals(feed.StableKey, activeKey, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(row => row.IsActive)
            .ThenByDescending(row => row.IsPreferred)
            .ThenBy(row => row.IsBlocked)
            .ThenByDescending(row => SmartSignalRoutingPolicy.Score(row.Feed, _settings.SignalRouting).Score)
            .ThenBy(row => row.SourceLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();
        SignalRouteList.ItemsSource = null;
        SignalRouteList.ItemsSource = rows;
        var measured = route.Feeds.Count(feed =>
            _settings.SignalRouting.FeedHealth.TryGetValue(feed.StableKey, out var health) && health.CompletedStarts > 0);
        var eligible = rows.Count(row => !row.IsBlocked);
        SignalRouteHeadingText.Text = route.Representative.Name;
        SignalRouteSummaryText.Text = $"{route.FeedCount:N0} provider feed{(route.FeedCount == 1 ? string.Empty : "s")} • {eligible:N0} eligible • {measured:N0} measured • unified guide fallback active";
        SignalRouteStatusText.Text = _settings.SignalRouting.AutomaticFailover && route.HasAlternates
            ? "AUTO FAILOVER ARMED"
            : route.HasAlternates ? "MANUAL ROUTING" : "SINGLE FEED";
    }

    private async void SignalRoutingAutoCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.SignalRouting.AutomaticFailover = SignalRoutingAutoCheck.IsChecked == true;
        await _settingsStore.SaveAsync(_settings);
        RefreshSignalRoutingPanel(_selectedSignalRouteKey);
        FooterStatusText.Text = _settings.SignalRouting.AutomaticFailover
            ? "Automatic backup-feed switching is armed"
            : "Signal feeds will be changed manually";
    }

    private void UseSignalFeed_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SignalFeedRow row ||
            !_signalRoutesByKey.TryGetValue(row.RouteKey, out var route) || row.IsBlocked) return;
        HideModal(SignalRoutingOverlay);
        TuneChannel(route.Representative, requestedSignalFeed: row.Feed);
        ChannelList.SelectedItem = route.Representative;
        ChannelList.ScrollIntoView(route.Representative);
        FooterStatusDot.Fill = WarningBrush;
        FooterStatusText.Text = $"Manual feed selected • {row.SourceLabel}";
    }

    private async void PreferSignalFeed_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SignalFeedRow row ||
            !_signalRoutesByKey.TryGetValue(row.RouteKey, out var route)) return;
        var selected = SmartSignalRoutingPolicy.GetOrCreateHealth(_settings.SignalRouting, row.Feed, route.Key);
        var makePreferred = selected.Preference != SignalFeedPreference.Preferred;
        foreach (var feed in route.Feeds)
        {
            var health = SmartSignalRoutingPolicy.GetOrCreateHealth(_settings.SignalRouting, feed, route.Key);
            if (health.Preference == SignalFeedPreference.Blocked && feed.StableKey != row.Feed.StableKey) continue;
            health.Preference = makePreferred && feed.StableKey == row.Feed.StableKey
                ? SignalFeedPreference.Preferred
                : SignalFeedPreference.Auto;
        }
        await _settingsStore.SaveAsync(_settings);
        RefreshSignalRoutingPanel(route.Key);
        FooterStatusText.Text = makePreferred
            ? $"Preferred feed • {row.SourceLabel}"
            : $"Automatic feed ranking restored • {route.Representative.Name}";
    }

    private async void BlockSignalFeed_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SignalFeedRow row ||
            !_signalRoutesByKey.TryGetValue(row.RouteKey, out var route)) return;
        var health = SmartSignalRoutingPolicy.GetOrCreateHealth(_settings.SignalRouting, row.Feed, route.Key);
        if (health.Preference == SignalFeedPreference.Blocked)
        {
            health.Preference = SignalFeedPreference.Auto;
            await _settingsStore.SaveAsync(_settings);
            RefreshSignalRoutingPanel(route.Key);
            FooterStatusText.Text = $"Feed allowed again • {row.SourceLabel}";
            return;
        }

        var otherEligible = route.Feeds.Any(feed => feed.StableKey != row.Feed.StableKey &&
            SmartSignalRoutingPolicy.Score(feed, _settings.SignalRouting).IsEligible);
        if (!otherEligible)
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "At least one feed must remain available for this channel";
            return;
        }

        health.Preference = SignalFeedPreference.Blocked;
        await _settingsStore.SaveAsync(_settings);
        if (row.IsActive)
        {
            var next = SmartSignalRoutingPolicy.SelectBestFeed(route, _settings.SignalRouting);
            if (next is not null) TuneChannel(route.Representative, requestedSignalFeed: next);
        }
        RefreshSignalRoutingPanel(route.Key);
        FooterStatusText.Text = $"Feed excluded from automatic routing • {row.SourceLabel}";
    }

    private async void ResetSignalHistory_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedSignalRouteKey) || !_signalRoutesByKey.TryGetValue(_selectedSignalRouteKey, out var route)) return;
        foreach (var feed in route.Feeds)
        {
            var preference = _settings.SignalRouting.FeedHealth.TryGetValue(feed.StableKey, out var existing)
                ? existing.Preference
                : SignalFeedPreference.Auto;
            _settings.SignalRouting.FeedHealth[feed.StableKey] = new SignalFeedHealth
            {
                LogicalChannelKey = route.Key,
                ChannelName = feed.Name,
                SourceName = SmartSignalRoutingPolicy.SourceLabel(feed),
                Preference = preference
            };
        }
        await _settingsStore.SaveAsync(_settings);
        RefreshSignalRoutingPanel(route.Key);
        FooterStatusText.Text = $"Signal history reset • {route.Representative.Name}";
    }

    private async void LinkSignalFeed_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedSignalRouteKey) ||
            !_signalRoutesByKey.TryGetValue(_selectedSignalRouteKey, out var route)) return;
        var candidate = SignalRouteCandidateBox.SelectedItem as SignalRouteCandidate ??
                        _signalRouteCandidates.FirstOrDefault(item =>
                            item.DisplayText.Equals(SignalRouteCandidateBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "Choose a feed from the route-editor search results first";
            return;
        }
        if (route.Feeds.Any(feed => feed.StableKey.Equals(candidate.Feed.StableKey, StringComparison.OrdinalIgnoreCase)))
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "That feed is already part of the selected channel route";
            return;
        }

        SmartSignalRoutingPolicy.LinkFeedToRoute(_settings.SignalRouting, route, candidate.Feed);
        await _settingsStore.SaveAsync(_settings);
        RebuildManualSignalRoutes(candidate.Feed.StableKey);
        FooterStatusDot.Fill = LiveBrush;
        FooterStatusText.Text = $"Linked {candidate.Feed.Name} to {route.Representative.Name}";
    }

    private async void EditSignalRouteFeed_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SignalFeedRow row) return;
        var isSeparated = SmartSignalRoutingPolicy.ToggleFeedSeparation(_settings.SignalRouting, row.Feed);
        await _settingsStore.SaveAsync(_settings);
        RebuildManualSignalRoutes(row.Feed.StableKey);
        FooterStatusDot.Fill = isSeparated ? WarningBrush : LiveBrush;
        FooterStatusText.Text = isSeparated
            ? $"{row.Feed.Name} will remain its own channel"
            : $"Automatic channel matching restored for {row.Feed.Name}";
    }

    private void RebuildManualSignalRoutes(string focusFeedKey)
    {
        var channels = _allChannels;
        var displayName = SourceNameText.Text;
        var source = _activePlaylistSourceValue ?? "manual-signal-route";
        ApplyPlaylist(new PlaylistResult(channels, displayName, source, DateTimeOffset.Now));
        var focusRoute = _signalRoutesByFeedKey.GetValueOrDefault(focusFeedKey);
        OpenSignalRoutingPanel(focusRoute?.Key);
    }

    private void CloseSignalRouting_Click(object sender, RoutedEventArgs e) => HideModal(SignalRoutingOverlay);

    private void OpenImport_Click(object sender, RoutedEventArgs e)
    {
        RefreshPlaylistSourceList();
        SourceTabs.SelectedItem = (_settings.PlaylistSources ?? []).Count > 0 ? SavedSourcesTab : FileSourceTab;
        ShowModal(ImportOverlay);
        ImportStatusText.Text = "Ready to connect";
        ImportDetailText.Text = (_settings.PlaylistSources ?? []).Count > 0
            ? "Choose a saved source, refresh the unified library, or add another provider."
            : "Nothing is uploaded; OrbitalVue reads the source directly.";
    }

    private void AddPlaylistSource_Click(object sender, RoutedEventArgs e)
    {
        SourceTabs.SelectedItem = FileSourceTab;
        ImportStatusText.Text = "Add another source";
        ImportDetailText.Text = "Choose M3U, Xtream, Plex, or Emby above.";
    }

    private async void SourceEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: PlaylistSourceDefinition source } checkBox) return;
        source.IsEnabled = checkBox.IsChecked == true;
        PlaylistSourcePolicy.NormalizeSettings(_settings);
        await _settingsStore.SaveAsync(_settings);
        RefreshPlaylistSourceList();
        ImportStatusText.Text = source.IsEnabled ? $"{source.Name} enabled" : $"{source.Name} paused";
        ImportDetailText.Text = source.IsEnabled
            ? "It will be included the next time the unified library refreshes."
            : "Its saved details and encrypted offline copy remain on this PC.";
    }

    private async void SourceStartupRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: PlaylistSourceDefinition source } checkBox) return;
        source.RefreshOnStartup = checkBox.IsChecked == true;
        PlaylistSourcePolicy.NormalizeSettings(_settings);
        await _settingsStore.SaveAsync(_settings);
        RefreshPlaylistSourceList();
        ImportStatusText.Text = source.RefreshOnStartup
            ? $"{source.Name} will refresh at launch"
            : $"{source.Name} will open its offline copy at launch";
        ImportDetailText.Text = source.RefreshOnStartup
            ? "OrbitalVue will check this provider whenever the app opens."
            : "Use Refresh enabled whenever you want to contact this provider manually.";
    }

    private async void UsePlaylistSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlaylistSourceDefinition source }) return;
        await UsePlaylistSourceAsync(source);
    }

    private async Task<bool> UsePlaylistSourceAsync(PlaylistSourceDefinition source)
    {
        if (source.SourceType is "plex" or "emby" && !EnsureMediaCenterAccess(source.TypeLabel)) return false;
        switch (source.SourceType)
        {
            case "file":
                FilePathBox.Text = source.SourceValue;
                return await LoadPlaylistAsync(
                    (progress, token) => _playlistSource.LoadFileAsync(source.SourceValue, progress, token),
                    source.SourceType,
                    source.SourceValue,
                    allowCachedFallback: true);
            case "url":
                PlaylistUrlBox.Text = source.SourceValue;
                return await LoadPlaylistAsync(
                    (progress, token) => _playlistSource.LoadUrlAsync(source.SourceValue, progress, token),
                    source.SourceType,
                    source.SourceValue,
                    allowCachedFallback: true);
            case "xtream":
                var credentials = await _xtreamCredentialStore.TryLoadAsync(source.SourceValue);
                if (credentials is null)
                {
                    XtreamServerBox.Text = source.SourceValue;
                    XtreamUsernameBox.Clear();
                    XtreamPasswordBox.Clear();
                    SourceTabs.SelectedItem = XtreamSourceTab;
                    ImportStatusText.Text = "Sign in to this account again";
                    ImportDetailText.Text = "Enter the username and password once to restore encrypted automatic refresh.";
                    return false;
                }

                XtreamServerBox.Text = credentials.Server;
                XtreamUsernameBox.Text = credentials.Username;
                XtreamPasswordBox.Password = credentials.Password;
                return await LoadPlaylistAsync(
                    (progress, token) => _xtreamSource.LoadAsync(
                        credentials.Server,
                        credentials.Username,
                        credentials.Password,
                        progress,
                        token),
                    source.SourceType,
                    source.SourceValue,
                    allowCachedFallback: true);
            case "plex":
            case "emby":
            {
                var hasCredential = await _mediaCenterSource.HasCredentialAsync(source.SourceType, source.SourceValue);
                if (!hasCredential)
                {
                    if (source.SourceType == "plex")
                    {
                        PlexServerBox.Text = source.SourceValue;
                        PlexTokenBox.Clear();
                        SourceTabs.SelectedItem = PlexSourceTab;
                    }
                    else
                    {
                        EmbyServerBox.Text = source.SourceValue;
                        EmbyUsernameBox.Clear();
                        EmbyPasswordBox.Clear();
                        SourceTabs.SelectedItem = EmbySourceTab;
                    }
                    ImportStatusText.Text = $"Reconnect this {source.TypeLabel} library";
                    ImportDetailText.Text = "Enter the account details once to restore its Windows-protected playback credential.";
                    return false;
                }

                if (source.SourceType == "plex") PlexServerBox.Text = source.SourceValue;
                else EmbyServerBox.Text = source.SourceValue;
                return await LoadPlaylistAsync(
                    (progress, token) => _mediaCenterSource.LoadSavedAsync(
                        source.SourceType,
                        source.SourceValue,
                        progress,
                        token),
                    source.SourceType,
                    source.SourceValue,
                    allowCachedFallback: true);
            }
            default:
                ImportStatusText.Text = "Unsupported saved source";
                ImportDetailText.Text = "Remove this entry and add it again using a supported source type.";
                return false;
        }
    }

    private async void RefreshEnabledSources_Click(object sender, RoutedEventArgs e)
    {
        await LoadUnifiedPlaylistSourcesAsync(startup: false);
    }

    private async void MovePlaylistSourceUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PlaylistSourceDefinition source })
            await MovePlaylistSourceAsync(source, -1);
    }

    private async void MovePlaylistSourceDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PlaylistSourceDefinition source })
            await MovePlaylistSourceAsync(source, 1);
    }

    private async Task MovePlaylistSourceAsync(PlaylistSourceDefinition source, int offset)
    {
        var ordered = (_settings.PlaylistSources ?? [])
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var currentIndex = ordered.FindIndex(item => item.Id == source.Id);
        var targetIndex = currentIndex + offset;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= ordered.Count) return;

        (ordered[currentIndex], ordered[targetIndex]) = (ordered[targetIndex], ordered[currentIndex]);
        for (var index = 0; index < ordered.Count; index++) ordered[index].SortOrder = index;
        _settings.PlaylistSources = ordered;
        PlaylistSourcePolicy.NormalizeSettings(_settings);
        await _settingsStore.SaveAsync(_settings);
        RefreshPlaylistSourceList();
        ImportStatusText.Text = $"Moved {source.Name}";
        ImportDetailText.Text = "Source order sets priority when two providers contain the same channel.";
    }

    private async void RemovePlaylistSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlaylistSourceDefinition source }) return;
        var confirmation = MessageBox.Show(
            this,
            $"Remove {source.Name} from OrbitalVue?\n\nIts encrypted offline playlist will also be removed. Other sources and the currently playing channel are not affected.",
            "Remove playlist source",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;

        _settings.PlaylistSources.RemoveAll(item => item.Id == source.Id);
        if (!string.IsNullOrWhiteSpace(_settings.LastSourceType) &&
            !string.IsNullOrWhiteSpace(_settings.LastSource) &&
            PlaylistSourcePolicy.Matches(source, _settings.LastSourceType, _settings.LastSource))
        {
            var next = _settings.PlaylistSources
                .Where(item => item.IsEnabled)
                .OrderBy(item => item.SortOrder)
                .FirstOrDefault();
            _settings.LastSourceType = next?.SourceType;
            _settings.LastSource = next?.SourceValue;
        }
        PlaylistSourcePolicy.NormalizeSettings(_settings);
        await _settingsStore.SaveAsync(_settings);

        var cleanupWarning = false;
        try
        {
            await _playlistCache.DeleteAsync(source.SourceType, source.SourceValue);
        }
        catch
        {
            cleanupWarning = true;
        }

        if (source.SourceType == "xtream" &&
            !_settings.PlaylistSources.Any(item =>
                item.SourceType == "xtream" && PlaylistSourcePolicy.Matches(item, "xtream", source.SourceValue)))
        {
            try
            {
                await _xtreamCredentialStore.DeleteAsync(source.SourceValue);
            }
            catch
            {
                cleanupWarning = true;
            }
        }

        if (source.SourceType is "plex" or "emby" &&
            !_settings.PlaylistSources.Any(item =>
                item.SourceType == source.SourceType &&
                PlaylistSourcePolicy.Matches(item, source.SourceType, source.SourceValue)))
        {
            try
            {
                await _mediaCenterSource.DeleteCredentialAsync(source.SourceType, source.SourceValue);
            }
            catch
            {
                cleanupWarning = true;
            }
        }

        RefreshPlaylistSourceList();
        ImportStatusText.Text = $"Removed {source.Name}";
        ImportDetailText.Text = cleanupWarning
            ? "The source was removed, but Windows could not delete all of its protected local data yet."
            : "Its protected offline data was removed from this PC.";
    }

    private void OpenGuideSource_Click(object sender, RoutedEventArgs e)
    {
        SourceTabs.SelectedItem = GuideSourceTab;
        ShowModal(ImportOverlay);
        ImportStatusText.Text = "TV guide sources";
        ImportDetailText.Text = "XMLTV data is read directly and cached with Windows encryption.";
    }

    private void BrowseGuideFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an XMLTV guide",
            Filter = "XMLTV guide (*.xml;*.xml.gz)|*.xml;*.xml.gz|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) GuideSourceBox.Text = dialog.FileName;
    }

    private async void LoadGuide_Click(object sender, RoutedEventArgs e)
    {
        await SaveAndLoadGuideSourcesAsync(ParseGuideSources(GuideSourceBox.Text));
    }

    private async void UseRecommendedGuide_Click(object sender, RoutedEventArgs e)
    {
        GuideSourceBox.Text = string.Join(Environment.NewLine, RecommendedUsGuideSources);
        await SaveAndLoadGuideSourcesAsync(RecommendedUsGuideSources);
    }

    private async Task SaveAndLoadGuideSourcesAsync(IReadOnlyList<string> sources)
    {
        if (_channels.Count == 0 || string.IsNullOrWhiteSpace(_activePlaylistSourceType) || string.IsNullOrWhiteSpace(_activePlaylistSourceValue))
        {
            ImportStatusText.Text = "Connect a playlist first";
            ImportDetailText.Text = "Guide sources are saved against the active playlist.";
            return;
        }
        if (sources.Count == 0 || sources.Any(source => !IsSupportedGuideSource(source)))
        {
            ImportStatusText.Text = "Enter a valid guide source";
            ImportDetailText.Text = "Use an existing XML/XML.GZ file or a complete HTTP/HTTPS address, one per line.";
            return;
        }

        _activeGuideSources = sources;
        await _guideSourceStore.SaveAsync(
            _activePlaylistSourceType,
            _activePlaylistSourceValue,
            string.Join(Environment.NewLine, sources));
        HideModal(ImportOverlay);
        GuideNavigation.IsChecked = true;
        await RefreshGuideAsync(forceNetworkRefresh: true);
    }

    private static bool IsSupportedGuideSource(string source) =>
        File.Exists(source) || Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private async void RefreshGuide_Click(object sender, RoutedEventArgs e)
    {
        if (_guideLoading) return;
        await RefreshGuideAsync(forceNetworkRefresh: true);
    }

    private async void OpenGuideMapping_Click(object sender, RoutedEventArgs e) => await OpenMappingEditorAsync();

    private async Task OpenMappingEditorAsync(ChannelItem? preferredChannel = null)
    {
        if (_channels.Count == 0 || _guideSchedule is null)
        {
            GuideStatusText.Text = "Load guide listings before assigning channels.";
            return;
        }

        ShowModal(MappingOverlay);
        MappingStatusText.Text = "Preparing unmatched channels and guide candidates…";
        if (_guideSchedule.ChannelCatalog.Count == 0 && _activeGuideSources.Count > 0 && !_guideLoading)
        {
            MappingStatusText.Text = "Refreshing the guide channel catalog for first-time mapping…";
            await RefreshGuideAsync(forceNetworkRefresh: true);
        }

        BuildMappingEditor(preferredChannel);
    }

    private void BuildMappingEditor(ChannelItem? preferredChannel = null)
    {
        if (_guideSchedule is null) return;
        var stableChannels = _channels
            .Where(channel => channel.Kind == ChannelKind.Live && !IsTemporaryEventFeed(channel))
            .Select(channel => new GuideMappingChannelRow(
                channel,
                _guideMappings.TryGetValue(channel.GuideMappingKey, out var mapped)
                    ? $"Mapped to {mapped}"
                    : GetGuideProgrammes(channel).Count > 0 ? "Matched automatically" : "No guide match",
                _guideMappings.GetValueOrDefault(channel.GuideMappingKey)))
            .Where(row => row.IsMapped || GetGuideProgrammes(row.Channel).Count == 0)
            .OrderByDescending(row => row.IsMapped)
            .ThenBy(row => row.ChannelName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        MappingChannelList.ItemsSource = stableChannels;
        var eventCount = _channels.Count(channel => channel.Kind == ChannelKind.Live && IsTemporaryEventFeed(channel) && GetGuideProgrammes(channel).Count == 0);
        MappingStatusText.Text = $"{stableChannels.Count(row => !row.IsMapped):N0} stable channels need attention • {stableChannels.Count(row => row.IsMapped):N0} manual mappings • {eventCount:N0} temporary event feeds ignored";
        MappingCatalogText.Text = $"{_guideSchedule.ChannelCatalog.Count:N0} XMLTV channels available";

        var selected = preferredChannel is null
            ? stableChannels.FirstOrDefault()
            : stableChannels.FirstOrDefault(row => ReferenceEquals(row.Channel, preferredChannel)) ?? stableChannels.FirstOrDefault();
        MappingChannelList.SelectedItem = selected;
        if (selected is not null) MappingChannelList.ScrollIntoView(selected);
    }

    private void MappingChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MappingChannelList.SelectedItem is not GuideMappingChannelRow row || _guideSchedule is null) return;
        MappingSelectedChannelText.Text = row.ChannelName;
        MappingSelectedChannelDetail.Text = $"{row.Group} • {row.Status}";
        MappingSearchBox.Clear();
        _mappingCandidates = _guideSchedule.GuideChannels
            .OrderByDescending(candidate => MappingCandidateScore(row.Channel, candidate))
            .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _mappingCandidateView = CollectionViewSource.GetDefaultView(_mappingCandidates);
        _mappingCandidateView.Filter = FilterMappingCandidate;
        MappingCandidateList.ItemsSource = _mappingCandidateView;
        MappingCandidateList.SelectedItem = row.MappedChannelId is null
            ? null
            : _mappingCandidates.FirstOrDefault(candidate => candidate.ChannelId.Equals(row.MappedChannelId, StringComparison.OrdinalIgnoreCase));
        MappingClearButton.IsEnabled = row.IsMapped;
    }

    private static int MappingCandidateScore(ChannelItem channel, EpgChannelOption candidate)
    {
        var channelCanonical = EpgSchedule.CanonicalName(channel.Name);
        var candidateCanonical = EpgSchedule.CanonicalName(candidate.DisplayName);
        var channelSignature = EpgSchedule.SignatureName(channel.Name);
        var candidateSignature = EpgSchedule.SignatureName(candidate.DisplayName);
        var score = 0;
        if (channelCanonical.Length > 0 && candidateCanonical == channelCanonical) score += 2_000;
        if (channelSignature.Length > 0 && candidateSignature == channelSignature) score += 1_600;
        if (channelCanonical.Length > 0 && candidateCanonical.Length > 0 &&
            (candidateCanonical.Contains(channelCanonical, StringComparison.Ordinal) || channelCanonical.Contains(candidateCanonical, StringComparison.Ordinal))) score += 800;
        foreach (var token in channel.Name.Split([' ', ':', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries))
            if (token.Length >= 3 && candidate.SearchText.Contains(token, StringComparison.OrdinalIgnoreCase)) score += 80;
        return score;
    }

    private bool FilterMappingCandidate(object item)
    {
        if (item is not EpgChannelOption candidate) return false;
        var search = MappingSearchBox.Text.Trim();
        return search.Length == 0 || candidate.SearchText.Contains(search.ToUpperInvariant(), StringComparison.Ordinal);
    }

    private void MappingSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (MappingSearchHint is not null)
            MappingSearchHint.Visibility = MappingSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _mappingCandidateView?.Refresh();
    }

    private async void SaveMapping_Click(object sender, RoutedEventArgs e)
    {
        if (MappingChannelList.SelectedItem is not GuideMappingChannelRow row ||
            MappingCandidateList.SelectedItem is not EpgChannelOption candidate ||
            string.IsNullOrWhiteSpace(_activePlaylistSourceType) ||
            string.IsNullOrWhiteSpace(_activePlaylistSourceValue))
        {
            MappingStatusText.Text = "Select both a playlist channel and an XMLTV channel.";
            return;
        }

        var mappings = new Dictionary<string, string>(_guideMappings, StringComparer.Ordinal)
        {
            [row.Channel.GuideMappingKey] = candidate.ChannelId
        };
        _guideMappings = mappings;
        await _epgMappingStore.SaveAsync(_activePlaylistSourceType, _activePlaylistSourceValue, mappings);
        MappingStatusText.Text = $"Saved {row.ChannelName} → {candidate.DisplayName}. Refreshing programmes…";
        await RefreshGuideAsync(forceNetworkRefresh: true);
        HideModal(MappingOverlay);
    }

    private async void ClearMapping_Click(object sender, RoutedEventArgs e)
    {
        if (MappingChannelList.SelectedItem is not GuideMappingChannelRow row ||
            string.IsNullOrWhiteSpace(_activePlaylistSourceType) ||
            string.IsNullOrWhiteSpace(_activePlaylistSourceValue)) return;
        var mappings = new Dictionary<string, string>(_guideMappings, StringComparer.Ordinal);
        mappings.Remove(row.Channel.GuideMappingKey);
        _guideMappings = mappings;
        await _epgMappingStore.SaveAsync(_activePlaylistSourceType, _activePlaylistSourceValue, mappings);
        if (_guideSchedule is not null) ApplyGuideSchedule(_guideSchedule);
        BuildMappingEditor(row.Channel);
    }

    private void CloseMapping_Click(object sender, RoutedEventArgs e) => HideModal(MappingOverlay);

    private void CloseImport_Click(object sender, RoutedEventArgs e)
    {
        if (!_isLoading) HideModal(ImportOverlay);
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an IPTV playlist",
            Filter = "IPTV playlists (*.m3u;*.m3u8;*.txt)|*.m3u;*.m3u8;*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) FilePathBox.Text = dialog.FileName;
    }

    private async void LoadFile_Click(object sender, RoutedEventArgs e)
    {
        var path = FilePathBox.Text.Trim();
        await LoadPlaylistAsync(
            (progress, token) => _playlistSource.LoadFileAsync(path, progress, token),
            "file",
            path);
    }

    private async void LoadUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = PlaylistUrlBox.Text.Trim();
        await LoadPlaylistAsync(
            (progress, token) => _playlistSource.LoadUrlAsync(url, progress, token),
            "url",
            url);
    }

    private async void LoadXtream_Click(object sender, RoutedEventArgs e)
    {
        var server = XtreamServerBox.Text.Trim();
        var username = XtreamUsernameBox.Text.Trim();
        var password = XtreamPasswordBox.Password;
        var loaded = await LoadPlaylistAsync(
            (progress, token) => _xtreamSource.LoadAsync(server, username, password, progress, token),
            "xtream",
            server);
        if (!loaded) return;

        try
        {
            await _xtreamCredentialStore.SaveAsync(new XtreamCredentials(server, username, password));
        }
        catch
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "Playlist loaded • secure Xtream auto-refresh could not be enabled";
        }
    }

    private async void LoadPlex_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureMediaCenterAccess("Plex")) return;
        if (!TryPrepareMediaCenterConnection(
                "Plex",
                PlexServerBox.Text,
                PlexAllowHttpBox.IsChecked == true,
                out var server)) return;

        PlexServerBox.Text = server;
        var accessToken = PlexTokenBox.Password;
        var displayName = PlexNameBox.Text.Trim();
        var loaded = await LoadPlaylistAsync(
            (progress, token) => _mediaCenterSource.ConnectPlexAsync(
                server,
                accessToken,
                displayName,
                PlexAllowHttpBox.IsChecked == true,
                progress,
                token),
            "plex",
            server);
        if (loaded) PlexTokenBox.Clear();
    }

    private async void StartPlexAccountSignIn_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureMediaCenterAccess("Plex")) return;
        CancelPlexAccountSignInCore(
            "Preparing a signed Plex approval request. Your account token will remain outside this screen.");
        var cancellation = new CancellationTokenSource();
        _plexAccountCancellation = cancellation;
        PlexAccountSignInButton.IsEnabled = false;
        PlexAccountProgress.Visibility = Visibility.Visible;
        PlexAccountCancelButton.Visibility = Visibility.Visible;
        try
        {
            var challenge = await _mediaCenterSource.StartPlexAccountSignInAsync(cancellation.Token);
            if (!ReferenceEquals(_plexAccountCancellation, cancellation)) return;
            _plexAccountChallenge = challenge;
            PlexAccountStatusText.Text =
                $"Approval code {challenge.Code} is ready. Complete approval in Plex; OrbitalVue will detect it automatically.";
            PlexAccountOpenButton.Visibility = Visibility.Visible;
            OpenPlexAccountApproval(challenge);

            var progress = new Progress<PlaylistProgress>(value =>
            {
                PlexAccountStatusText.Text = value.Message;
                FooterStatusText.Text = value.Message;
            });
            var discovery = await _mediaCenterSource.WaitForPlexAccountServersAsync(
                challenge,
                progress,
                cancellation.Token);
            if (!ReferenceEquals(_plexAccountCancellation, cancellation))
            {
                _mediaCenterSource.CancelPlexAccountDiscovery(discovery.SessionId);
                return;
            }
            _plexAccountDiscovery = discovery;
            PlexAccountServerBox.ItemsSource = discovery.Servers;
            PlexAccountServerBox.SelectedIndex = 0;
            PlexAccountPicker.Visibility = Visibility.Visible;
            PlexAccountStatusText.Text =
                $"Plex approved OrbitalVue and returned {discovery.Servers.Count:N0} server(s). Choose the connection to keep on this PC.";
            PlexAccountSignInButton.Content = "Use another Plex account";
            PlexAccountSignInButton.IsEnabled = true;
            PlexAccountOpenButton.Visibility = Visibility.Collapsed;
            PlexAccountCancelButton.Visibility = Visibility.Collapsed;
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = "Plex account approved • choose a server";
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_plexAccountCancellation, cancellation))
                CancelPlexAccountSignInCore("Plex account sign-in was canceled.");
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_plexAccountCancellation, cancellation))
            {
                CancelPlexAccountSignInCore(SafeErrorMessage(exception));
                FooterStatusDot.Fill = ErrorBrush;
                FooterStatusText.Text = "Plex account sign-in needs attention";
            }
        }
        finally
        {
            if (ReferenceEquals(_plexAccountCancellation, cancellation))
            {
                _plexAccountCancellation = null;
                PlexAccountProgress.Visibility = Visibility.Collapsed;
                if (_plexAccountDiscovery is null)
                {
                    PlexAccountSignInButton.IsEnabled = _premiumAccess.CanUseMediaCenters;
                    PlexAccountCancelButton.Visibility = Visibility.Collapsed;
                }
            }
            cancellation.Dispose();
        }
    }

    private void OpenPlexAccountApproval_Click(object sender, RoutedEventArgs e)
    {
        if (_plexAccountChallenge is { } challenge) OpenPlexAccountApproval(challenge);
    }

    private void OpenPlexAccountApproval(PlexPinChallenge challenge)
    {
        try
        {
            Process.Start(new ProcessStartInfo(challenge.AuthorizationUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            PlexAccountStatusText.Text =
                $"Windows could not open the browser. Copy this approval address manually: {challenge.AuthorizationUrl}\n{SafeErrorMessage(exception)}";
        }
    }

    private void CancelPlexAccountSignIn_Click(object sender, RoutedEventArgs e) =>
        CancelPlexAccountSignInCore("Plex account sign-in was canceled.");

    private void PlexAccountServer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlexAccountServerBox.SelectedItem is not PlexDiscoveredServer server)
        {
            PlexAccountConnectionBox.ItemsSource = null;
            PlexAccountLocationText.Text = string.Empty;
            return;
        }
        PlexAccountConnectionBox.ItemsSource = server.Connections;
        PlexAccountConnectionBox.SelectedIndex = 0;
    }

    private void PlexAccountConnection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlexAccountConnectionBox.SelectedItem is not PlexServerConnectionChoice connection)
        {
            PlexAccountLocationText.Text = string.Empty;
            PlexAccountAllowHttpBox.Visibility = Visibility.Collapsed;
            return;
        }
        PlexAccountLocationText.Text = connection.DisplayLocation;
        PlexAccountAllowHttpBox.IsChecked = false;
        PlexAccountAllowHttpBox.Visibility = connection.IsSecure ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ConnectPlexAccount_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureMediaCenterAccess("Plex")) return;
        if (_plexAccountDiscovery is not { } discovery ||
            PlexAccountServerBox.SelectedItem is not PlexDiscoveredServer server ||
            PlexAccountConnectionBox.SelectedItem is not PlexServerConnectionChoice connection)
        {
            PlexAccountStatusText.Text = "Choose a discovered Plex server and connection.";
            return;
        }
        if (!connection.IsSecure && PlexAccountAllowHttpBox.IsChecked != true)
        {
            PlexAccountStatusText.Text =
                "This address uses HTTP. Confirm the trusted local-network warning before OrbitalVue can send the server token.";
            return;
        }

        PlexAccountConnectButton.IsEnabled = false;
        PlexAccountProgress.Visibility = Visibility.Visible;
        PlexAccountStatusText.Text = "Verifying the selected server before saving its protected credential…";
        var loaded = await LoadPlaylistAsync(
            (progress, token) => _mediaCenterSource.ConnectDiscoveredPlexServerAsync(
                discovery.SessionId,
                server.ServerId,
                connection.Url,
                PlexAccountAllowHttpBox.IsChecked == true,
                progress,
                token),
            "plex",
            connection.Url);
        PlexAccountConnectButton.IsEnabled = true;
        PlexAccountProgress.Visibility = Visibility.Collapsed;
        if (loaded)
        {
            PlexServerBox.Text = connection.Url;
            CancelPlexAccountSignInCore(
                $"{server.Name} is connected. Its server token is protected for this Windows user.");
            return;
        }
        CancelPlexAccountSignInCore("The server selection could not be activated. Sign in with Plex again to retry.");
    }

    private void CancelPlexAccountSignInCore(string status)
    {
        _plexAccountCancellation?.Cancel();
        _plexAccountCancellation?.Dispose();
        _plexAccountCancellation = null;
        if (_plexAccountDiscovery is { } discovery)
            _mediaCenterSource.CancelPlexAccountDiscovery(discovery.SessionId);
        _plexAccountChallenge = null;
        _plexAccountDiscovery = null;
        PlexAccountServerBox.ItemsSource = null;
        PlexAccountConnectionBox.ItemsSource = null;
        PlexAccountPicker.Visibility = Visibility.Collapsed;
        PlexAccountProgress.Visibility = Visibility.Collapsed;
        PlexAccountOpenButton.Visibility = Visibility.Collapsed;
        PlexAccountCancelButton.Visibility = Visibility.Collapsed;
        PlexAccountSignInButton.Content = "Sign in with Plex";
        PlexAccountSignInButton.IsEnabled = _premiumAccess.CanUseMediaCenters;
        PlexAccountStatusText.Text = status;
    }

    private async void LoadEmby_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureMediaCenterAccess("Emby")) return;
        if (!TryPrepareMediaCenterConnection(
                "Emby",
                EmbyServerBox.Text,
                EmbyAllowHttpBox.IsChecked == true,
                out var server)) return;

        EmbyServerBox.Text = server;
        var username = EmbyUsernameBox.Text.Trim();
        var password = EmbyPasswordBox.Password;
        var displayName = EmbyNameBox.Text.Trim();
        var loaded = await LoadPlaylistAsync(
            (progress, token) => _mediaCenterSource.ConnectEmbyAsync(
                server,
                username,
                password,
                displayName,
                EmbyAllowHttpBox.IsChecked == true,
                progress,
                token),
            "emby",
            server);
        if (loaded) EmbyPasswordBox.Clear();
    }

    private bool TryPrepareMediaCenterConnection(
        string provider,
        string serverAddress,
        bool allowInsecureHttp,
        out string normalizedServer)
    {
        try
        {
            normalizedServer = MediaCenterSecurity.NormalizeBaseUrl(serverAddress);
            MediaCenterSecurity.RequireAllowedTransport(normalizedServer, allowInsecureHttp);
            return true;
        }
        catch (Exception exception)
        {
            normalizedServer = string.Empty;
            ImportProgress.Visibility = Visibility.Collapsed;
            ImportStatusText.Text = $"Check the {provider} server address";
            ImportDetailText.Text = SafeErrorMessage(exception);
            FooterStatusDot.Fill = ErrorBrush;
            FooterStatusText.Text = $"{provider} connection needs attention";
            return false;
        }
    }

    private bool EnsureMediaCenterAccess(string provider)
    {
        if (_premiumAccess.CanUseMediaCenters) return true;
        ImportProgress.Visibility = Visibility.Collapsed;
        ImportStatusText.Text = $"{provider} premium access is unavailable";
        ImportDetailText.Text = _premiumStore.State.Message ?? _premiumAccess.Explanation;
        FooterStatusDot.Fill = WarningBrush;
        FooterStatusText.Text = "A verified one-time store purchase is required";
        if (!_backgroundLaunch)
        {
            SourceTabs.SelectedItem = provider.Contains("Emby", StringComparison.OrdinalIgnoreCase)
                ? EmbySourceTab
                : PlexSourceTab;
            ShowModal(ImportOverlay);
        }
        return false;
    }

    private async void PurchasePremium_Click(object sender, RoutedEventArgs e) =>
        await _premiumStore.PurchaseAsync();

    private async void RestorePremium_Click(object sender, RoutedEventArgs e) =>
        await _premiumStore.RestoreAsync();

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_currentChannel is null)
        {
            ShowModal(ImportOverlay);
            return;
        }

        if (_playback?.MediaPlayer.Media is null)
        {
            if (_currentRecordingPath is not null) _recordingResumeApplied = false;
            RetuneCurrentPlayback();
        }
        else _playback?.TogglePause();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        CaptureSignalTelemetry(_playback?.GetSnapshot(), persist: true);
        SaveCurrentRecordingProgress(force: true);
        ObserveMediaPlaybackReport(EndCurrentMediaPlaybackReporting(_playback?.GetSnapshot()));
        CancelMediaPlaybackResolution(clearResume: true);
        _playback?.Stop();
        if (_currentRecordingPath is not null) _recordingResumeApplied = false;
        _displayRefreshRate?.Restore();
    }

    private void RewindLive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: not null } button ||
            !int.TryParse(button.Tag.ToString(), out var seconds) ||
            _playback is null) return;
        if (_playback.TryRewindLive(TimeSpan.FromSeconds(seconds)))
        {
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = $"Timeshift • rewound {seconds} seconds";
        }
        else
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "This provider does not expose a seekable live window yet • Pause still keeps a local timeshift buffer";
        }
        UpdateTimeshiftUi(_playback.GetLiveTimeshiftSnapshot(DateTimeOffset.UtcNow));
    }

    private void GoLive_Click(object sender, RoutedEventArgs e)
    {
        if (_playback?.GoLive() != true) return;
        FooterStatusDot.Fill = LiveBrush;
        FooterStatusText.Text = "Returned to the live edge";
        UpdateTimeshiftUi(_playback.GetLiveTimeshiftSnapshot(DateTimeOffset.UtcNow));
    }

    private void UpdateTimeshiftUi(LiveTimeshiftSnapshot snapshot)
    {
        var available = snapshot.Enabled && snapshot.IsLiveChannel && _currentRecordingPath is null && !_multiviewMode;
        var visibility = available ? Visibility.Visible : Visibility.Collapsed;
        Rewind10Button.Visibility = visibility;
        Rewind60Button.Visibility = visibility;
        GoLiveButton.Visibility = visibility;
        TimeshiftPanel.Visibility = visibility;
        Rewind10Button.IsEnabled = snapshot.CanRewind;
        Rewind60Button.IsEnabled = snapshot.CanRewind;
        GoLiveButton.IsEnabled = snapshot.IsActive;
        if (!available) return;

        var behindSeconds = Math.Max(0, snapshot.BehindLive.TotalSeconds);
        var windowSeconds = Math.Max(1, snapshot.Window.TotalSeconds);
        TimeshiftWindowProgress.Value = Math.Clamp(behindSeconds / windowSeconds * 100d, 0d, 100d);
        TimeshiftStatusText.Text = snapshot.IsPaused
            ? $"PAUSED • {FormatTimeshiftDuration(snapshot.BehindLive)} BEHIND"
            : snapshot.IsActive
                ? $"TIMESHIFT • {FormatTimeshiftDuration(snapshot.BehindLive)} BEHIND"
                : "LIVE BUFFER READY";
        TimeshiftWindowText.Text = $"{snapshot.Window.TotalMinutes:0} MIN";
    }

    private static string FormatTimeshiftDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
        : $"{duration.Minutes}:{duration.Seconds:00}";

    private void RetryPlayback_Click(object sender, RoutedEventArgs e)
    {
        if (_currentChannel is null)
        {
            ShowModal(ImportOverlay);
            return;
        }

        _playback?.Retry();
    }

    private async void StabilizePlayback_Click(object sender, RoutedEventArgs e)
    {
        if (_currentChannel is null)
        {
            ShowModal(ImportOverlay);
            return;
        }
        if (_currentRecordingPath is not null)
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "Stable buffer is only needed for live channels";
            return;
        }

        var profile = GetOrCreateChannelProfile(_currentChannel);
        profile.BufferPreset = BufferPreset.Stable;
        profile.LearnedInstability = 4;
        profile.UpdatedUtc = DateTimeOffset.UtcNow;
        CacheValue.Text = "8.0 seconds";
        await _settingsStore.SaveAsync(_settings);
        FooterStatusDot.Fill = WarningBrush;
        FooterStatusText.Text = $"Stable buffer remembered for {_currentChannel.Name} • reconnecting";
        _playback?.Retry();
    }

    private void Mute_Click(object sender, RoutedEventArgs e) => _playback?.ToggleMute();

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_windowReady) _playback?.SetVolume((int)e.NewValue);
    }

    private async void AspectBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_windowReady || _applyingChannelProfile || AspectBox.SelectedItem is not ComboBoxItem item) return;
        var aspect = item.Content?.ToString() ?? "Auto";
        if (_currentChannel is null)
        {
            _settings.Playback.AspectRatio = aspect;
            _playback?.ApplyAspectRatio(aspect);
        }
        else
        {
            var profile = GetOrCreateChannelProfile(_currentChannel);
            profile.AspectRatio = aspect;
            profile.UpdatedUtc = DateTimeOffset.UtcNow;
            _playback?.ApplyChannelAspectRatio(aspect);
        }
        await _settingsStore.SaveAsync(_settings);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsModal();
    }

    private void OpenSettingsModal()
    {
        ApplySettingsToControls();
        UpdateCurrentChannelProfileCard();
        if (_playback is not null) RefreshTrackControls(_playback.GetSnapshot());
        RestartHint.Visibility = Visibility.Collapsed;
        ShowModal(SettingsOverlay);
    }

    private void UpdateCurrentChannelProfileCard()
    {
        if (_currentChannel is null)
        {
            CurrentChannelProfileCard.Visibility = Visibility.Collapsed;
            return;
        }
        CurrentChannelProfileCard.Visibility = Visibility.Visible;
        _settings.ChannelProfiles.TryGetValue(_currentChannel.StableKey, out var profile);
        profile ??= new ChannelPlaybackProfile();
        SelectComboByTag(CurrentChannelBufferBox, profile.BufferPreset?.ToString() ?? "Default");
        SelectComboByTag(CurrentChannelDecoderBox, profile.HardwareDecoding switch
        {
            true => "Hardware",
            false => "Software",
            _ => "Default"
        });

        var snapshot = _playback?.GetSnapshot();
        var startup = profile.LastStartupMilliseconds > 0
            ? $" • last start {profile.LastStartupMilliseconds / 1000d:0.0}s"
            : string.Empty;
        if (!profile.HasOverrides)
        {
            CurrentChannelProfileText.Text = $"{_currentChannel.Name} • {snapshot?.TuneStrategy ?? "PC defaults"}{startup}. OrbitalVue will learn and remember any recovery this channel needs.";
            return;
        }
        var parts = new List<string>();
        if (profile.AspectRatio is not null) parts.Add($"aspect {profile.AspectRatio}");
        if (profile.BufferPreset is not null) parts.Add($"{profile.BufferPreset} buffer");
        if (profile.HardwareDecoding == false) parts.Add("software decoding");
        if (profile.AudioTrackId is not null) parts.Add("audio track remembered");
        if (profile.SubtitleTrackId is not null) parts.Add("subtitle choice remembered");
        if (profile.LearnedInstability > 0) parts.Add($"recovery level {profile.LearnedInstability}");
        CurrentChannelProfileText.Text = $"{_currentChannel.Name} • {snapshot?.TuneStrategy ?? "saved profile"} • {string.Join(" • ", parts)}{startup}";
    }

    private async void ApplyChannelProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentChannel is null) return;
        var profile = GetOrCreateChannelProfile(_currentChannel);
        var bufferValue = (CurrentChannelBufferBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Default";
        profile.BufferPreset = Enum.TryParse<BufferPreset>(bufferValue, out var preset) ? preset : null;
        var decoderValue = (CurrentChannelDecoderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Default";
        profile.HardwareDecoding = decoderValue switch
        {
            "Hardware" => true,
            "Software" => false,
            _ => null
        };
        if (profile.BufferPreset == BufferPreset.Stable) profile.LearnedInstability = Math.Max(3, profile.LearnedInstability);
        profile.UpdatedUtc = DateTimeOffset.UtcNow;
        _playback?.ResetChannelLearning(_activeSignalFeed ?? _currentChannel);
        await _settingsStore.SaveAsync(_settings);
        _learnedProfileSignature = string.Empty;
        RetuneCurrentPlayback(profile);
        HideModal(SettingsOverlay);
        FooterStatusDot.Fill = WarningBrush;
        FooterStatusText.Text = $"Playback IQ profile applied to {_currentChannel.Name}";
    }

    private async void ResetChannelProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentChannel is null) return;
        _settings.ChannelProfiles.Remove(_currentChannel.StableKey);
        _playback?.ResetChannelLearning(_activeSignalFeed ?? _currentChannel);
        await _settingsStore.SaveAsync(_settings);
        _applyingChannelProfile = true;
        SelectComboByContent(AspectBox, _settings.Playback.AspectRatio);
        _applyingChannelProfile = false;
        RetuneCurrentPlayback(new ChannelPlaybackProfile());
        UpdateCurrentChannelProfileCard();
        FooterStatusText.Text = $"Reset remembered playback choices for {_currentChannel.Name}";
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e) => HideModal(SettingsOverlay);

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.Playback.PlaybackIntelligence = PlaybackIntelligenceCheck.IsChecked == true;
        _settings.Playback.FastChannelChanges = FastTuneCheck.IsChecked == true;
        _settings.Playback.HardwareDecoding = HardwareDecodeCheck.IsChecked == true;
        _settings.Playback.HdmiPassthrough = PassthroughCheck.IsChecked == true;
        _settings.Playback.AdaptiveRefreshRate = RefreshRateCheck.IsChecked == true;
        _settings.Playback.AutoReconnect = AutoReconnectCheck.IsChecked == true;
        _settings.Playback.StallWatchdog = StallWatchdogCheck.IsChecked == true;
        _settings.Playback.BufferPreset = ReadSelectedBufferPreset();
        _settings.Playback.AspectRatio = (DefaultAspectBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Auto";
        _settings.Playback.DeinterlaceMode = (DeinterlaceBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Auto";
        _settings.Playback.AudioDelayMilliseconds = (int)AudioDelaySlider.Value;
        _settings.ResumeLastChannelOnStartup = ResumeLastChannelCheck.IsChecked == true;
        _settings.RestoreUnexpectedSession = UnexpectedSessionRecoveryCheck.IsChecked == true;
        _settings.MiniPlayerAlwaysOnTop = MiniPlayerTopmostCheck.IsChecked == true;
        await _settingsStore.SaveAsync(_settings);
        CacheValue.Text = $"{_settings.Playback.CacheMilliseconds / 1000d:0.0} seconds";
        DecodeValue.Text = _settings.Playback.HardwareDecoding ? "Hardware auto" : "Software";
        _playback?.ApplyDeinterlace(_settings.Playback.DeinterlaceMode);
        _playback?.ApplyAudioDelay(_settings.Playback.AudioDelayMilliseconds);
        _playback?.ApplyAspectRatio(_settings.Playback.AspectRatio);
        SelectComboByContent(AspectBox, _settings.Playback.AspectRatio);
        if (!_settings.Playback.AdaptiveRefreshRate) _displayRefreshRate?.Restore();
        if (_isMiniPlayer) Topmost = _settings.MiniPlayerAlwaysOnTop;
        HideModal(SettingsOverlay);
        FooterStatusText.Text = "Settings saved • Smart playback controls are active";
    }

    private void ApplySettingsToControls()
    {
        PlaybackIntelligenceCheck.IsChecked = _settings.Playback.PlaybackIntelligence;
        FastTuneCheck.IsChecked = _settings.Playback.FastChannelChanges;
        HardwareDecodeCheck.IsChecked = _settings.Playback.HardwareDecoding;
        PassthroughCheck.IsChecked = _settings.Playback.HdmiPassthrough;
        RefreshRateCheck.IsChecked = _settings.Playback.AdaptiveRefreshRate;
        AutoReconnectCheck.IsChecked = _settings.Playback.AutoReconnect;
        StallWatchdogCheck.IsChecked = _settings.Playback.StallWatchdog;
        SelectComboByTag(BufferPresetBox, _settings.Playback.BufferPreset.ToString());
        SelectComboByContent(DefaultAspectBox, _settings.Playback.AspectRatio);
        SelectComboByContent(AspectBox, _settings.Playback.AspectRatio);
        SelectComboByContent(DeinterlaceBox, _settings.Playback.DeinterlaceMode);
        AudioDelaySlider.Value = _settings.Playback.AudioDelayMilliseconds;
        AudioDelayValue.Text = $"{_settings.Playback.AudioDelayMilliseconds:+0;-0;0} ms";
        ResumeLastChannelCheck.IsChecked = _settings.ResumeLastChannelOnStartup;
        UnexpectedSessionRecoveryCheck.IsChecked = _settings.RestoreUnexpectedSession;
        MiniPlayerTopmostCheck.IsChecked = _settings.MiniPlayerAlwaysOnTop;
        UpdateSleepTimerStatus();
    }

    private void StartSleepTimer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: not null } button ||
            !int.TryParse(button.Tag.ToString(), out var minutes)) return;

        _sleepDeadline = DateTimeOffset.Now.AddMinutes(minutes);
        _sleepTimer.Stop();
        _sleepTimer.Start();
        UpdateSleepTimerStatus();
        FooterStatusDot.Fill = WarningBrush;
        FooterStatusText.Text = $"Sleep timer set • playback stops in {minutes} minutes";
    }

    private void CancelSleepTimer_Click(object sender, RoutedEventArgs e)
    {
        var wasActive = _sleepDeadline is not null;
        _sleepDeadline = null;
        _sleepTimer.Stop();
        UpdateSleepTimerStatus();
        if (wasActive) FooterStatusText.Text = "Sleep timer canceled";
    }

    private void SleepTimer_Tick(object? sender, EventArgs e)
    {
        if (_sleepDeadline is null)
        {
            _sleepTimer.Stop();
            UpdateSleepTimerStatus();
            return;
        }

        if (DateTimeOffset.Now < _sleepDeadline)
        {
            UpdateSleepTimerStatus();
            return;
        }

        _sleepDeadline = null;
        _sleepTimer.Stop();
        if (_isFullscreen) ExitFullscreen();
        _multiviewSession?.StopAll();
        CaptureSignalTelemetry(_playback?.GetSnapshot(), persist: true);
        _playback?.Stop();
        UpdateSleepTimerStatus();
        FooterStatusDot.Fill = IdleBrush;
        FooterStatusText.Text = "Sleep timer finished • playback stopped";
    }

    private void UpdateSleepTimerStatus()
    {
        if (SleepTimerStatusText is null) return;
        if (_sleepDeadline is null)
        {
            SleepTimerStatusText.Text = "Off • playback will continue until you stop it";
            return;
        }

        var remaining = _sleepDeadline.Value - DateTimeOffset.Now;
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        SleepTimerStatusText.Text = $"Active • {minutes} min remaining • stops at {_sleepDeadline.Value:h:mm tt}";
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Back up OrbitalVue",
            FileName = $"OrbitalVue-backup-{DateTime.Now:yyyy-MM-dd}.orbitalvue-backup",
            DefaultExt = ".orbitalvue-backup",
            AddExtension = true,
            Filter = "OrbitalVue backup (*.orbitalvue-backup)|*.orbitalvue-backup|Legacy StreamVue backup (*.streamvue-backup)|*.streamvue-backup"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            await _settingsStore.SaveAsync(_settings);
            var fileCount = await _maintenanceService.CreateBackupAsync(dialog.FileName);
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = $"OrbitalVue backup created • {fileCount} protected data files";
        }
        catch (Exception exception)
        {
            FooterStatusDot.Fill = ErrorBrush;
            FooterStatusText.Text = $"Backup failed • {SafeMaintenanceErrorMessage(exception)}";
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Restore an OrbitalVue backup",
            CheckFileExists = true,
            Filter = "OrbitalVue backup (*.orbitalvue-backup)|*.orbitalvue-backup|Legacy StreamVue backup (*.streamvue-backup)|*.streamvue-backup"
        };
        if (dialog.ShowDialog(this) != true) return;

        var confirmation = MessageBox.Show(
            this,
            "Restoring this backup will replace the playlists, favorites, guide data, settings, and Playback IQ profiles currently saved on this Windows account. OrbitalVue will restart afterward.\n\nContinue?",
            "Restore OrbitalVue backup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            await _settingsStore.SaveAsync(_settings);
            _windowReady = false;
            _telemetryTimer.Stop();
            _multiviewSession?.StopAll();
            _playback?.Stop();
            var fileCount = await _maintenanceService.RestoreBackupAsync(dialog.FileName);
            MessageBox.Show(
                this,
                $"Restored {fileCount} OrbitalVue data files. A recovery copy of the replaced data was kept, and OrbitalVue will now restart.",
                "Backup restored",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RestartApplication();
        }
        catch (Exception exception)
        {
            _windowReady = true;
            _telemetryTimer.Start();
            FooterStatusDot.Fill = ErrorBrush;
            FooterStatusText.Text = $"Restore failed • {SafeMaintenanceErrorMessage(exception)}";
        }
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export OrbitalVue diagnostics",
            FileName = $"OrbitalVue-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            Filter = "ZIP archive (*.zip)|*.zip"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var context = new StreamVueDiagnosticContext(
                _channels.Count,
                _channels.Select(channel => channel.Group).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                _activePlaylistSourceType ?? _settings.LastSourceType,
                _settings.PlaylistHealth?.UsedCachedFallback == true,
                _activeGuideSources.Count,
                _currentChannel?.StableKey,
                _playback?.GetSnapshot());
            await _maintenanceService.ExportDiagnosticsAsync(dialog.FileName, _settings, context);
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = "Privacy-filtered diagnostics exported";
        }
        catch (Exception exception)
        {
            FooterStatusDot.Fill = ErrorBrush;
            FooterStatusText.Text = $"Diagnostics export failed • {SafeMaintenanceErrorMessage(exception)}";
        }
    }

    private void RestartApplication()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            Application.Current.Shutdown();
            return;
        }

        Process.Start(new ProcessStartInfo(processPath, "--wait-for-instance") { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    private static string SafeMaintenanceErrorMessage(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Windows denied access to that location.",
        FileNotFoundException => "The selected file could not be found.",
        InvalidDataException invalidData => invalidData.Message,
        IOException => "The file is being used or there is not enough available storage.",
        _ => "OrbitalVue could not complete that operation."
    };

    private BufferPreset ReadSelectedBufferPreset()
    {
        var value = (BufferPresetBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return Enum.TryParse<BufferPreset>(value, out var preset) ? preset : BufferPreset.Balanced;
    }

    private static void SelectComboByTag(ComboBox comboBox, string value)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase));
    }

    private static void SelectComboByContent(ComboBox comboBox, string value)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase));
    }

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (_windowReady) RestartHint.Visibility = Visibility.Visible;
    }

    private void BufferPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_windowReady && SettingsOverlay?.Visibility == Visibility.Visible) RestartHint.Visibility = Visibility.Visible;
    }

    private void DefaultAspectBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_windowReady && SettingsOverlay?.Visibility == Visibility.Visible) RestartHint.Visibility = Visibility.Visible;
    }

    private void AudioTrackBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingTrackControls || !_windowReady || AudioTrackBox.SelectedItem is not ComboBoxItem item) return;
        if (int.TryParse(item.Tag?.ToString(), out var trackId))
        {
            _playback?.SelectAudioTrack(trackId);
            if (_currentChannel is not null)
            {
                var profile = GetOrCreateChannelProfile(_currentChannel);
                profile.AudioTrackId = trackId;
                profile.UpdatedUtc = DateTimeOffset.UtcNow;
                _ = _settingsStore.SaveAsync(_settings);
            }
        }
    }

    private void SubtitleTrackBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingTrackControls || !_windowReady || SubtitleTrackBox.SelectedItem is not ComboBoxItem item) return;
        if (int.TryParse(item.Tag?.ToString(), out var trackId))
        {
            _playback?.SelectSubtitleTrack(trackId);
            if (_currentChannel is not null)
            {
                var profile = GetOrCreateChannelProfile(_currentChannel);
                profile.SubtitleTrackId = trackId;
                profile.UpdatedUtc = DateTimeOffset.UtcNow;
                _ = _settingsStore.SaveAsync(_settings);
            }
        }
    }

    private void DeinterlaceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_windowReady || DeinterlaceBox.SelectedItem is not ComboBoxItem item) return;
        var mode = item.Content?.ToString() ?? "Auto";
        _settings.Playback.DeinterlaceMode = mode;
        _playback?.ApplyDeinterlace(mode);
    }

    private void AudioDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AudioDelayValue is null) return;
        var milliseconds = (int)e.NewValue;
        AudioDelayValue.Text = $"{milliseconds:+0;-0;0} ms";
        if (!_windowReady) return;
        _settings.Playback.AudioDelayMilliseconds = milliseconds;
        _playback?.ApplyAudioDelay(milliseconds);
    }

    private void RefreshTrackControls(PlaybackSnapshot snapshot)
    {
        var signature = string.Join('|', snapshot.AudioTracks.Select(track => $"a{track.Id}:{track.Name}:{track.IsSelected}")) +
                        string.Join('|', snapshot.SubtitleTracks.Select(track => $"s{track.Id}:{track.Name}:{track.IsSelected}"));
        if (signature == _trackControlSignature) return;
        _trackControlSignature = signature;
        _updatingTrackControls = true;
        try
        {
            PopulateTrackBox(AudioTrackBox, snapshot.AudioTracks, "Default audio");
            PopulateTrackBox(SubtitleTrackBox, snapshot.SubtitleTracks, "Off");
        }
        finally
        {
            _updatingTrackControls = false;
        }
    }

    private static void PopulateTrackBox(ComboBox comboBox, IReadOnlyList<PlaybackTrack> tracks, string emptyLabel)
    {
        comboBox.Items.Clear();
        if (tracks.Count == 0)
        {
            comboBox.Items.Add(new ComboBoxItem { Content = emptyLabel, IsSelected = true, IsEnabled = false });
            return;
        }

        foreach (var track in tracks)
        {
            comboBox.Items.Add(new ComboBoxItem
            {
                Content = track.Name,
                Tag = track.Id,
                IsSelected = track.IsSelected
            });
        }

        if (comboBox.SelectedIndex < 0) comboBox.SelectedIndex = 0;
    }

    private async void OpenUpdate_Click(object sender, RoutedEventArgs e)
    {
        OpenUpdateModal();
        if (_appUpdateService.IsStoreManaged) return;
        await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (_appUpdateService.IsStoreManaged)
        {
            SetUpdateNavigationCurrent();
            return;
        }
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var result = await _appUpdateService.CheckAsync(_settings.Updates.Channel);
            if (result.State == AppUpdateState.Available)
            {
                UpdateNavigationText.Text = "UPDATE READY";
                UpdateNavigationButton.Background = new SolidColorBrush(Color.FromRgb(22, 61, 54));
                UpdateNavigationButton.BorderBrush = LiveBrush;
                UpdateNavigationButton.ToolTip = $"OrbitalVue {result.AvailableVersion} is ready to install";
            }
        }
        catch
        {
            // Startup checks are deliberately silent. Manual checks provide actionable error details.
        }
    }

    private void SetUpdateNavigationCurrent()
    {
        UpdateNavigationText.Text = _appUpdateService.IsStoreManaged ? "UPDATES" : "UPDATE";
        UpdateNavigationButton.ClearValue(BackgroundProperty);
        UpdateNavigationButton.ClearValue(BorderBrushProperty);
        UpdateNavigationButton.ToolTip = _appUpdateService.IsStoreManaged
            ? "Updates are managed by Microsoft Store"
            : "Check for a new OrbitalVue version";
    }

    private void OpenUpdateModal()
    {
        SelectComboByTag(UpdateChannelBox, _settings.Updates.Channel.ToString());
        UpdateRollbackCheck.IsChecked = _settings.Updates.AutomaticRollback;
        CurrentVersionText.Text = _appUpdateService.CurrentVersion;
        UpdateStateBadge.Background = new SolidColorBrush(Color.FromRgb(23, 54, 47));
        UpdateStateBadgeText.Foreground = LiveBrush;
        UpdateStateBadgeText.Text = _appUpdateService.IsStoreManaged ? "STORE" : "READY";
        UpdateStatusText.Text = _appUpdateService.IsStoreManaged
            ? "Updates are managed by Microsoft Store"
            : "Ready to check for a new version";
        UpdateDetailText.Text = _appUpdateService.IsStoreManaged
            ? "Microsoft Store installs signed OrbitalVue updates automatically according to your Store settings. No separate OrbitalVue download is required."
            : $"Checking the {UpdateChannelLabel} channel. Your playlists and settings stay on this PC.";
        UpdateSubtitleText.Text = _appUpdateService.IsStoreManaged
            ? "Microsoft Store keeps this installation current."
            : "Install the newest release without uninstalling your current version.";
        UpdateProgress.Visibility = Visibility.Collapsed;
        UpdateProgress.IsIndeterminate = false;
        UpdateProgress.Value = 0;
        UpdateOptionsPanel.Visibility = _appUpdateService.IsStoreManaged ? Visibility.Collapsed : Visibility.Visible;
        UpdateProtectionNote.Text = _appUpdateService.IsStoreManaged
            ? "Microsoft Store verifies, signs, installs, and updates this package. Your playlists and settings remain private on this PC."
            : "Every update is verified before installation. Rollback protection keeps one local last-known-good package until the new version proves it can run.";
        UpdateFooterLabel.Text = _appUpdateService.IsStoreManaged ? "MICROSOFT STORE UPDATE" : "IN-PLACE WINDOWS UPDATE";
        UpdateActionButton.Visibility = _appUpdateService.IsStoreManaged ? Visibility.Collapsed : Visibility.Visible;
        UpdateActionButton.Content = _appUpdateService.IsStoreManaged ? "Managed by Store" : "Check for updates";
        UpdateActionButton.IsEnabled = !_appUpdateService.IsStoreManaged;
        ShowModal(UpdateOverlay);
    }

    private void CloseUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!_updateBusy) HideModal(UpdateOverlay);
    }

    private async void UpdateAction_Click(object sender, RoutedEventArgs e)
    {
        if (_updateBusy) return;
        if (_appUpdateService.HasAvailableUpdate)
            await DownloadAndApplyUpdateAsync();
        else
            await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        SetUpdateBusy(true);
        UpdateStateBadgeText.Text = "CHECKING";
        UpdateStatusText.Text = "Checking for updates…";
        UpdateDetailText.Text = $"Contacting the OrbitalVue {UpdateChannelLabel} release service.";
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateProgress.IsIndeterminate = true;

        try
        {
            var result = await _appUpdateService.CheckAsync(_settings.Updates.Channel);
            switch (result.State)
            {
                case AppUpdateState.Available:
                    UpdateNavigationText.Text = "UPDATE READY";
                    UpdateNavigationButton.Background = new SolidColorBrush(Color.FromRgb(22, 61, 54));
                    UpdateNavigationButton.BorderBrush = LiveBrush;
                    UpdateStateBadge.Background = new SolidColorBrush(Color.FromRgb(23, 54, 47));
                    UpdateStateBadgeText.Foreground = LiveBrush;
                    UpdateStateBadgeText.Text = "AVAILABLE";
                    UpdateStatusText.Text = $"OrbitalVue {result.AvailableVersion} is ready";
                    UpdateDetailText.Text = "Download the update now. OrbitalVue will close, install it in place, and reopen automatically.";
                    UpdateActionButton.Content = "Download & restart";
                    break;
                case AppUpdateState.Current:
                    SetUpdateNavigationCurrent();
                    UpdateStateBadge.Background = new SolidColorBrush(Color.FromRgb(27, 41, 60));
                    UpdateStateBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(170, 182, 200));
                    UpdateStateBadgeText.Text = "CURRENT";
                    UpdateStatusText.Text = "You’re up to date";
                    UpdateDetailText.Text = $"OrbitalVue {result.CurrentVersion} is the newest published {UpdateChannelLabel} version.";
                    UpdateActionButton.Content = "Check again";
                    break;
                case AppUpdateState.DeveloperBuild:
                    UpdateStateBadge.Background = new SolidColorBrush(Color.FromRgb(62, 48, 27));
                    UpdateStateBadgeText.Foreground = WarningBrush;
                    UpdateStateBadgeText.Text = "DEV BUILD";
                    UpdateStatusText.Text = "Install OrbitalVue once to enable updates";
                    UpdateDetailText.Text = "This copy is running directly from a build folder. The installed release can update itself from this screen without uninstalling first.";
                    UpdateActionButton.Content = "Check again";
                    break;
                case AppUpdateState.StoreManaged:
                    SetUpdateNavigationCurrent();
                    UpdateStateBadge.Background = new SolidColorBrush(Color.FromRgb(27, 41, 60));
                    UpdateStateBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(170, 182, 200));
                    UpdateStateBadgeText.Text = "STORE";
                    UpdateStatusText.Text = "Updates are managed by Microsoft Store";
                    UpdateDetailText.Text = "Microsoft Store installs signed OrbitalVue updates automatically according to your Store settings. No separate OrbitalVue download is required.";
                    UpdateActionButton.Content = "Managed by Store";
                    break;
            }
        }
        catch (Exception exception)
        {
            UpdateStateBadge.Background = new SolidColorBrush(Color.FromRgb(66, 31, 38));
            UpdateStateBadgeText.Foreground = ErrorBrush;
            UpdateStateBadgeText.Text = "OFFLINE";
            UpdateStatusText.Text = "Couldn’t reach the update service";
            UpdateDetailText.Text = SafeUpdateErrorMessage(exception);
            UpdateActionButton.Content = "Try again";
        }
        finally
        {
            UpdateProgress.IsIndeterminate = false;
            UpdateProgress.Visibility = Visibility.Collapsed;
            SetUpdateBusy(false);
        }
    }

    private async Task DownloadAndApplyUpdateAsync()
    {
        if (_dvrRecording.Snapshot.IsActive)
        {
            UpdateStateBadge.Background = new SolidColorBrush(Color.FromRgb(59, 32, 39));
            UpdateStateBadgeText.Foreground = ErrorBrush;
            UpdateStateBadgeText.Text = "RECORDING";
            UpdateStatusText.Text = "Finish the active recording first";
            UpdateDetailText.Text = "Stop and save the DVR recording before installing an update so the transport-stream file can close safely.";
            return;
        }

        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
        _updateCancellation = new CancellationTokenSource();

        SetUpdateBusy(true);
        UpdateStateBadgeText.Text = "DOWNLOADING";
        UpdateStatusText.Text = "Downloading the update…";
        UpdateDetailText.Text = "Keep OrbitalVue open. It will restart as soon as the verified package is ready.";
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateProgress.IsIndeterminate = false;
        UpdateProgress.Value = 0;

        try
        {
            await _appUpdateService.DownloadAndRestartAsync(
                progress => _ = Dispatcher.InvokeAsync(() =>
                {
                    UpdateProgress.Value = progress;
                    UpdateDetailText.Text = $"Downloading and verifying the update • {progress}%";
                }),
                _settings.Updates.AutomaticRollback,
                _updateCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText.Text = "Update canceled";
            UpdateDetailText.Text = "No changes were installed. You can try again whenever you’re ready.";
        }
        catch (Exception exception)
        {
            UpdateStateBadge.Background = new SolidColorBrush(Color.FromRgb(66, 31, 38));
            UpdateStateBadgeText.Foreground = ErrorBrush;
            UpdateStateBadgeText.Text = "FAILED";
            UpdateStatusText.Text = "The update couldn’t be installed";
            UpdateDetailText.Text = SafeUpdateErrorMessage(exception);
        }
        finally
        {
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateActionButton.Content = _appUpdateService.HasAvailableUpdate ? "Try download again" : "Check again";
            SetUpdateBusy(false);
        }
    }

    private void SetUpdateBusy(bool busy)
    {
        _updateBusy = busy;
        UpdateActionButton.IsEnabled = !busy && !_appUpdateService.IsStoreManaged;
        UpdateCloseButton.IsEnabled = !busy;
        UpdateNavigationButton.IsEnabled = !busy;
    }

    private string UpdateChannelLabel => _settings.Updates.Channel == AppUpdateChannel.Stable ? "Stable" : "Preview";

    private async void UpdateChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_windowReady || UpdateChannelBox.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !Enum.TryParse<AppUpdateChannel>(tag, true, out var channel) || _settings.Updates.Channel == channel) return;
        _settings.Updates.Channel = channel;
        await _settingsStore.SaveAsync(_settings);
        _appUpdateService.ClearAvailableUpdate();
        SetUpdateNavigationCurrent();
        UpdateStatusText.Text = $"{UpdateChannelLabel} channel selected";
        UpdateDetailText.Text = channel == AppUpdateChannel.Stable
            ? "Stable installs only finished public releases. Choose Check for updates when you’re ready."
            : "Preview includes the early builds used to review new OrbitalVue features. Choose Check for updates when you’re ready.";
        UpdateActionButton.Content = "Check for updates";
    }

    private async void UpdateRollback_Changed(object sender, RoutedEventArgs e)
    {
        if (!_windowReady) return;
        _settings.Updates.AutomaticRollback = UpdateRollbackCheck.IsChecked == true;
        await _settingsStore.SaveAsync(_settings);
    }

    private async Task HandleUpdateRecoveryLaunchAsync(IReadOnlyList<string> arguments)
    {
        if (arguments.Contains("--update-rollback", StringComparer.OrdinalIgnoreCase))
        {
            var restored = await _appUpdateService.CompleteRollbackAsync();
            if (restored is not null)
            {
                await _appUpdateService.ReadAndClearRecoveryNoticeAsync();
                _settings.Updates.LastRollbackUtc = restored.RestoredUtc;
                _settings.Updates.LastRollbackVersion = restored.RestoredVersion;
                await _settingsStore.SaveAsync(_settings);
                FooterStatusDot.Fill = WarningBrush;
                FooterStatusText.Text = $"Update recovery restored OrbitalVue {restored.RestoredVersion}";
            }
            return;
        }

        var tokenIndex = arguments.ToList().FindIndex(argument => argument.Equals("--update-health-token", StringComparison.OrdinalIgnoreCase));
        if (tokenIndex >= 0 && tokenIndex + 1 < arguments.Count)
        {
            var token = arguments[tokenIndex + 1];
            _ = ConfirmUpdateHealthAfterStartupAsync(token);
            return;
        }

        var priorNotice = await _appUpdateService.ReadAndClearRecoveryNoticeAsync();
        if (priorNotice is not null)
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = $"Update recovery restored OrbitalVue {priorNotice.RestoredVersion}";
        }
    }

    private async Task ConfirmUpdateHealthAfterStartupAsync(string healthToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(12));
        if (!_windowReady || Application.Current.Dispatcher.HasShutdownStarted) return;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await _appUpdateService.ConfirmHealthyLaunchAsync(healthToken);
    }

    private void OpenCast_Click(object sender, RoutedEventArgs e) => OpenCastPanel();

    private void OpenCastPanel()
    {
        CastStateBadge.Background = new SolidColorBrush(Color.FromRgb(23, 54, 47));
        CastStateBadgeText.Foreground = LiveBrush;
        CastStateBadgeText.Text = _castService.IsSupported ? "READY" : "UNAVAILABLE";
        CastStatusText.Text = _castService.IsSupported
            ? "Windows will search for powered-on Miracast displays nearby—even devices that have not been paired with this PC."
            : "Wireless display casting requires Windows 10 or Windows 11 and compatible Miracast hardware.";
        FindCastDevicesButton.IsEnabled = _castService.IsSupported;
        ShowModal(CastOverlay);
    }

    private void CloseCast_Click(object sender, RoutedEventArgs e) => HideModal(CastOverlay);

    private async void FindCastDevices_Click(object sender, RoutedEventArgs e)
    {
        if (!_castService.IsSupported) return;

        CastStateBadgeText.Text = "OPENING";
        CastStatusText.Text = "Opening the Windows Cast panel. Select a nearby display to connect.";
        HideModal(CastOverlay);
        Activate();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(80);

        try
        {
            _castService.OpenNearbyDisplayPicker();
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = "Windows Cast opened • select a nearby wireless display";
        }
        catch (Exception exception) when (exception is Win32Exception or PlatformNotSupportedException)
        {
            OpenCastPanel();
            CastStateBadge.Background = new SolidColorBrush(Color.FromRgb(66, 31, 38));
            CastStateBadgeText.Foreground = ErrorBrush;
            CastStateBadgeText.Text = "UNAVAILABLE";
            CastStatusText.Text = exception.Message;
            FooterStatusDot.Fill = ErrorBrush;
            FooterStatusText.Text = "Windows Cast could not be opened";
        }
    }

    private void OpenCastDisplaySettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideModal(CastOverlay);
            _castService.OpenDisplaySettings();
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = "Windows display settings opened";
        }
        catch (Exception exception) when (exception is Win32Exception or PlatformNotSupportedException)
        {
            OpenCastPanel();
            CastStateBadge.Background = new SolidColorBrush(Color.FromRgb(66, 31, 38));
            CastStateBadgeText.Foreground = ErrorBrush;
            CastStateBadgeText.Text = "UNAVAILABLE";
            CastStatusText.Text = exception.Message;
        }
    }

    private void OpenDvr_Click(object sender, RoutedEventArgs e) => OpenDvrPanel();

    private void OpenDvrPanel()
    {
        if (!RecordingFolderBox.IsKeyboardFocusWithin)
            RecordingFolderBox.Text = ResolveRecordingsFolder(_settings.RecordingsFolder);
        ShowModal(DvrOverlay);
        UpdateDvrUi(_dvrRecording.Poll(DateTimeOffset.UtcNow));
    }

    private void CloseDvr_Click(object sender, RoutedEventArgs e)
    {
        PersistRecordingsFolder();
        HideModal(DvrOverlay);
    }

    private void ToggleRecording_Click(object sender, RoutedEventArgs e) => ToggleDvrRecording();

    private void DvrRecordNow_Click(object sender, RoutedEventArgs e) => ToggleDvrRecording();

    private void ToggleDvrRecording()
    {
        var snapshot = _dvrRecording.Snapshot;
        if (snapshot.IsActive)
        {
            snapshot = _dvrRecording.Stop("Recording stopped and saved");
            ApplyDvrScheduleState(snapshot);
            UpdateDvrUi(snapshot);
            FooterStatusDot.Fill = snapshot.State == DvrRecordingState.Completed ? LiveBrush : ErrorBrush;
            FooterStatusText.Text = snapshot.State == DvrRecordingState.Completed
                ? $"Recording saved • {Path.GetFileName(snapshot.OutputPath)}"
                : snapshot.Message ?? "The recording could not be saved";
            return;
        }

        var channel = ResolveRecordableChannel();
        if (channel is null)
        {
            OpenDvrPanel();
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "Choose a live channel before recording";
            return;
        }

        if (channel.Kind != ChannelKind.Live)
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "DVR recording is available for live channels";
            return;
        }

        try
        {
            var folder = PersistRecordingsFolder();
            if (!HasDvrStorageReserve(folder, out var storageMessage))
            {
                OpenDvrPanel();
                FooterStatusDot.Fill = WarningBrush;
                FooterStatusText.Text = storageMessage;
                return;
            }
            var program = GetGuideNowNext(channel, DateTimeOffset.UtcNow).Current;
            _handledDvrTerminalSignature = string.Empty;
            snapshot = _dvrRecording.Start(channel, folder, program?.Title);
            UpdateDvrUi(snapshot);
            FooterStatusDot.Fill = ErrorBrush;
            FooterStatusText.Text = $"Recording started • {channel.Name}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or UriFormatException)
        {
            FooterStatusDot.Fill = ErrorBrush;
            FooterStatusText.Text = SafeRecordingErrorMessage(exception);
            UpdateDvrUi(_dvrRecording.Snapshot);
        }
    }

    private ChannelItem? ResolveRecordableChannel()
    {
        if (_multiviewMode && _multiviewSession is not null)
            return _multiviewSession.Tiles[_multiviewSession.ActiveSlot].Channel;
        return _activeSignalFeed ?? _currentChannel;
    }

    private async Task ToggleScheduledRecordingAsync(GuideProgrammeBlock block)
    {
        if (block.Programme is null) return;
        if (block.Channel.Kind != ChannelKind.Live)
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "Only live TV programs can be scheduled for recording";
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (block.Programme.Stop <= now)
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "That program has already ended";
            return;
        }

        _settings.ScheduledRecordings ??= [];
        var existing = _settings.ScheduledRecordings.FirstOrDefault(recording =>
            SmartDvrPolicy.MatchesProgramme(recording, block.Channel, block.Programme));
        if (existing is not null)
        {
            if (existing.Status is "Canceled" or "Conflict" or "Failed" or "Missed")
            {
                existing.Status = "Scheduled";
                existing.Detail = null;
                FooterStatusDot.Fill = LiveBrush;
                FooterStatusText.Text = $"Recording restored • {block.Programme.Title}";
            }
            else
            {
                if (_dvrRecording.Snapshot.ScheduleId == existing.Id && _dvrRecording.Snapshot.IsActive)
                    _dvrRecording.Stop("Scheduled recording canceled");
                if (existing.SeriesRuleId is null)
                {
                    _settings.ScheduledRecordings.Remove(existing);
                }
                else
                {
                    existing.Status = "Canceled";
                    existing.Detail = "This series airing was skipped";
                }
                FooterStatusDot.Fill = IdleBrush;
                FooterStatusText.Text = $"Recording canceled • {block.Programme.Title}";
            }
        }
        else
        {
            var preferences = _settings.SmartDvr ?? new SmartDvrPreferences();
            var candidate = SmartDvrPolicy.CreateSchedule(
                block.Channel,
                block.Programme,
                preferences.StartPaddingMinutes,
                preferences.EndPaddingMinutes,
                preferences.DefaultPriority);
            var overlap = _settings.ScheduledRecordings.FirstOrDefault(recording =>
                (recording.Status is "Scheduled" or "Recording" or "Recovering") &&
                DvrRecordingService.SchedulesCompete(recording, candidate));
            if (overlap is not null)
            {
                var result = MessageBox.Show(
                    this,
                    $"{candidate.ProgramTitle} overlaps {overlap.ProgramTitle}. OrbitalVue records the higher-priority schedule and uses start time as the tie-breaker.\n\nSchedule it anyway?",
                    "Recording schedule conflict",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (result != MessageBoxResult.Yes)
                {
                    FooterStatusDot.Fill = WarningBrush;
                    FooterStatusText.Text = $"Schedule unchanged • conflict with {overlap.ProgramTitle}";
                    return;
                }
            }

            _settings.ScheduledRecordings.Add(candidate);
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = candidate.StartUtc <= now
                ? $"Recording queued now • {block.Programme.Title}"
                : $"Recording scheduled • {block.Programme.Title} at {candidate.StartUtc.ToLocalTime():h:mm tt} with padding";
        }

        await _settingsStore.SaveAsync(_settings);
        CheckScheduledRecordings();
        UpdateDvrUi(_dvrRecording.Poll(DateTimeOffset.UtcNow));
    }

    private async Task ToggleSeriesRecordingRuleAsync(GuideProgrammeBlock block)
    {
        if (block.Programme is null || block.Channel.Kind != ChannelKind.Live) return;
        if (block.Programme.Stop <= DateTimeOffset.UtcNow)
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "That series airing has already ended";
            return;
        }

        _settings.SeriesRecordingRules ??= [];
        var existing = _settings.SeriesRecordingRules.FirstOrDefault(rule =>
            rule.Enabled &&
            (rule.AnyChannel || rule.ChannelKey.Equals(block.Channel.StableKey, StringComparison.OrdinalIgnoreCase)) &&
            SmartDvrPolicy.NormalizeTitle(rule.ProgramTitle) == SmartDvrPolicy.NormalizeTitle(block.Programme.Title));
        if (existing is not null)
        {
            _settings.SeriesRecordingRules.Remove(existing);
            _settings.ScheduledRecordings.RemoveAll(recording =>
                recording.SeriesRuleId == existing.Id && (recording.Status is "Scheduled" or "Canceled" or "Recovering"));
            FooterStatusDot.Fill = IdleBrush;
            FooterStatusText.Text = $"Series recording stopped • {block.Programme.Title}";
        }
        else
        {
            var preferences = _settings.SmartDvr ?? new SmartDvrPreferences();
            _settings.SeriesRecordingRules.Add(new SeriesRecordingRule
            {
                ChannelKey = block.Channel.StableKey,
                ChannelName = block.Channel.Name,
                ProgramTitle = block.Programme.Title,
                Priority = preferences.DefaultPriority,
                StartPaddingMinutes = SmartDvrPolicy.ClampPadding(preferences.StartPaddingMinutes),
                EndPaddingMinutes = SmartDvrPolicy.ClampPadding(preferences.EndPaddingMinutes),
                EpisodeSelection = preferences.DefaultEpisodeSelection,
                KeepLatestCount = SmartDvrPolicy.ClampRetention(preferences.DefaultKeepLatestCount)
            });
            var generated = MaterializeSeriesRecordingRules();
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = $"Series rule saved • {generated:N0} upcoming airing{(generated == 1 ? string.Empty : "s")}";
        }

        await _settingsStore.SaveAsync(_settings);
        UpdateDvrUi(_dvrRecording.Poll(DateTimeOffset.UtcNow));
    }

    private int MaterializeSeriesRecordingRules()
    {
        if (_guideSchedule is null || _settings.SeriesRecordingRules is null) return 0;
        _settings.ScheduledRecordings ??= [];
        var now = DateTimeOffset.UtcNow;
        var changed = 0;
        foreach (var rule in _settings.SeriesRecordingRules.Where(rule => rule.Enabled))
        {
            var channels = _channels
                .Where(channel => channel.Kind == ChannelKind.Live &&
                                  (rule.AnyChannel || channel.StableKey.Equals(rule.ChannelKey, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(channel => channel.StableKey.Equals(rule.ChannelKey, StringComparison.OrdinalIgnoreCase))
                .ThenBy(channel => channel.Number)
                .ToList();
            foreach (var channel in channels)
            {
                foreach (var programme in GetGuideProgrammes(channel)
                             .Where(programme => programme.Stop.AddMinutes(rule.EndPaddingMinutes) > now)
                             .Where(programme => SmartDvrPolicy.RuleMatches(rule, channel, programme))
                             .Take(100))
                {
                    var existing = _settings.ScheduledRecordings.FirstOrDefault(recording =>
                        SmartDvrPolicy.MatchesProgramme(recording, channel, programme));
                    if (existing is not null)
                    {
                        if (existing.SeriesRuleId is null && existing.Status == "Scheduled")
                        {
                            existing.SeriesRuleId = rule.Id;
                            existing.Priority = rule.Priority;
                            existing.StartPaddingMinutes = SmartDvrPolicy.ClampPadding(rule.StartPaddingMinutes);
                            existing.EndPaddingMinutes = SmartDvrPolicy.ClampPadding(rule.EndPaddingMinutes);
                            existing.StartUtc = programme.Start.AddMinutes(-existing.StartPaddingMinutes);
                            existing.StopUtc = programme.Stop.AddMinutes(existing.EndPaddingMinutes);
                            existing.EpisodeKey = SmartDvrPolicy.CreateEpisodeKey(programme, channel, seriesIdentity: true);
                            existing.EpisodeLabel = programme.EpisodeLabel;
                            existing.IsNewEpisode = programme.IsNewEpisode;
                            changed++;
                        }
                        continue;
                    }

                    var candidate = SmartDvrPolicy.CreateSchedule(
                        channel,
                        programme,
                        rule.StartPaddingMinutes,
                        rule.EndPaddingMinutes,
                        rule.Priority,
                        rule.Id);
                    if (SmartDvrPolicy.IsDuplicateEpisode(_settings.ScheduledRecordings, candidate)) continue;
                    _settings.ScheduledRecordings.Add(candidate);
                    changed++;
                }
            }
        }
        return changed;
    }

    private void RebuildFutureSeriesSchedules(SeriesRecordingRule rule)
    {
        var now = DateTimeOffset.UtcNow;
        _settings.ScheduledRecordings.RemoveAll(recording =>
            recording.SeriesRuleId == rule.Id && recording.StartUtc > now && recording.Status == "Scheduled");
        MaterializeSeriesRecordingRules();
        _dvrScheduleSignature = string.Empty;
        _dvrWakeSignature = string.Empty;
    }

    private void ApplySeriesRetention(SeriesRecordingRule rule)
    {
        var keep = SmartDvrPolicy.ClampRetention(rule.KeepLatestCount);
        if (keep <= 0) return;
        var completed = _settings.ScheduledRecordings
            .Where(recording => recording.SeriesRuleId == rule.Id && (recording.Status is "Completed" or "Partial"))
            .OrderByDescending(recording => recording.GuideStopUtc)
            .ThenByDescending(recording => recording.CreatedUtc)
            .ToList();
        foreach (var expired in completed.Skip(keep))
        {
            var paths = expired.OutputPaths
                .Append(expired.OutputPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList();
            foreach (var path in paths)
            {
                try
                {
                    if (File.Exists(path)) _dvrRecording.DeleteRecording(path, _settings.RecordingsFolder);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    expired.Detail = "Retention cleanup will retry after the recording file is released";
                    continue;
                }
            }
            expired.Status = "Expired";
            expired.Detail = $"Automatically removed • this series keeps the newest {keep}";
        }
    }

    private void CheckScheduledRecordings()
    {
        _settings.ScheduledRecordings ??= [];
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        foreach (var recording in _settings.ScheduledRecordings)
        {
            recording.OutputPaths ??= [];
            if (recording.Status == "Recording" && _dvrRecording.Snapshot.ScheduleId != recording.Id)
            {
                if (recording.StopUtc > now)
                {
                    ScheduleRecordingRecovery(recording, "Recorder resumed after Windows or OrbitalVue restarted", now);
                }
                else
                {
                    recording.Status = HasPlayableRecordingSegment(recording) ? "Partial" : "Missed";
                    recording.Detail = HasPlayableRecordingSegment(recording)
                        ? "A playable segment was preserved before the recorder stopped"
                        : "The recorder stopped before media could be saved";
                }
                changed = true;
            }

            if ((recording.Status is "Scheduled" or "Recovering") && recording.StopUtc <= now)
            {
                recording.Status = HasPlayableRecordingSegment(recording) ? "Partial" : "Missed";
                recording.Detail = HasPlayableRecordingSegment(recording)
                    ? "The provider stream ended, but playable recording segments were preserved"
                    : "The background recorder was not available before the program ended";
                changed = true;
            }
        }

        var recorderSnapshot = _dvrRecording.Snapshot;
        if (recorderSnapshot.IsActive && now - _lastDvrStorageGuardUtc >= TimeSpan.FromSeconds(15))
        {
            _lastDvrStorageGuardUtc = now;
            var folder = ResolveRecordingsFolder(_settings.RecordingsFolder);
            if (!HasDvrStorageReserve(folder, out var storageMessage))
            {
                recorderSnapshot = _dvrRecording.Stop(storageMessage);
                ApplyDvrScheduleState(recorderSnapshot);
                changed = true;
                FooterStatusDot.Fill = WarningBrush;
                FooterStatusText.Text = storageMessage;
            }
        }

        var dueRecordings = _settings.ScheduledRecordings
            .Where(recording => IsRecordingReadyToStart(recording, now))
            .ToList();

        if (recorderSnapshot.IsActive && dueRecordings.Count > 0)
        {
            var activeSchedule = recorderSnapshot.ScheduleId is Guid activeId
                ? _settings.ScheduledRecordings.FirstOrDefault(recording => recording.Id == activeId)
                : null;
            var actionableDue = activeSchedule is null
                ? dueRecordings.Where(recording => now >= recording.GuideStartUtc).ToList()
                : dueRecordings.Where(recording =>
                    !DvrRecordingService.IsPaddingOnlyOverlap(activeSchedule, recording) ||
                    now >= recording.GuideStartUtc).ToList();
            var preferredDue = SmartDvrPolicy.SelectPreferredDue(actionableDue, now);
            var paddingHandoff = activeSchedule is not null && preferredDue is not null &&
                                 DvrRecordingService.IsPaddingOnlyOverlap(activeSchedule, preferredDue) &&
                                 now >= preferredDue.GuideStartUtc && preferredDue.Priority >= activeSchedule.Priority;
            if (activeSchedule is not null && preferredDue is not null &&
                (paddingHandoff || SmartDvrPolicy.IsPreferredOver(preferredDue, activeSchedule)))
            {
                recorderSnapshot = _dvrRecording.Stop(paddingHandoff
                    ? $"Handed off to next scheduled program • {preferredDue.ProgramTitle}"
                    : $"Stopped for higher-priority recording • {preferredDue.ProgramTitle}");
                ApplyDvrScheduleState(recorderSnapshot);
                changed = true;
            }
            else
            {
                foreach (var blocked in actionableDue)
                {
                    blocked.Status = "Conflict";
                    blocked.Detail = activeSchedule is null
                        ? "Skipped because a manual recording was already active"
                        : $"Skipped for {activeSchedule.ProgramTitle} • {activeSchedule.Priority.ToString().ToLowerInvariant()} priority";
                    changed = true;
                }
            }
        }

        if (!recorderSnapshot.IsActive)
        {
            dueRecordings = _settings.ScheduledRecordings
                .Where(recording => IsRecordingReadyToStart(recording, now))
                .ToList();
            var due = SmartDvrPolicy.SelectPreferredDue(dueRecordings, now);
            foreach (var blocked in dueRecordings.Where(recording => recording.Id != due?.Id))
            {
                if (due is not null && DvrRecordingService.IsPaddingOnlyOverlap(due, blocked) &&
                    now < blocked.GuideStartUtc)
                    continue;
                blocked.Status = "Conflict";
                blocked.Detail = $"Skipped for {due!.ProgramTitle} • {due.Priority.ToString().ToLowerInvariant()} priority";
                changed = true;
            }

            var channel = due is null
                ? null
                : _channels.FirstOrDefault(candidate => candidate.StableKey.Equals(due.ChannelKey, StringComparison.OrdinalIgnoreCase));
            if (due is not null && channel is not null)
            {
                try
                {
                    var folder = PersistRecordingsFolder();
                    if (!HasDvrStorageReserve(folder, out var storageMessage))
                    {
                        due.Status = "Failed";
                        due.Detail = storageMessage;
                        changed = true;
                        FooterStatusDot.Fill = WarningBrush;
                        FooterStatusText.Text = $"Scheduled recording blocked • {storageMessage}";
                        if (changed) _ = _settingsStore.SaveAsync(_settings);
                        return;
                    }
                    _handledDvrTerminalSignature = string.Empty;
                    var recordingFeed = ResolveBestSignalFeed(channel);
                    var snapshot = _dvrRecording.Start(recordingFeed, folder, due.ProgramTitle, due.StopUtc, due.Id);
                    due.Status = "Recording";
                    due.NextRecoveryUtc = null;
                    due.Detail = due.RecoveryAttempts > 0
                        ? $"Recovered stream • segment {due.RecoveryAttempts + 1} is recording"
                        : "Recording in progress";
                    due.OutputPath = snapshot.OutputPath;
                    AddRecordingOutputPath(due, snapshot.OutputPath);
                    changed = true;
                    FooterStatusDot.Fill = ErrorBrush;
                    FooterStatusText.Text = $"Scheduled recording started • {due.ProgramTitle}";
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or UriFormatException)
                {
                    ScheduleRecordingRecovery(due, SafeRecordingErrorMessage(exception), now);
                    changed = true;
                    FooterStatusDot.Fill = ErrorBrush;
                    FooterStatusText.Text = due.Status == "Recovering"
                        ? $"Recording interrupted • retrying {due.ProgramTitle}"
                        : $"Scheduled recording failed • {due.ProgramTitle}";
                }
            }
            else if (due is not null)
            {
                due.Status = "Failed";
                due.Detail = "The playlist no longer contains this channel";
                changed = true;
            }
        }

        if (changed) _ = _settingsStore.SaveAsync(_settings);
    }

    private void ApplyDvrScheduleState(DvrRecordingSnapshot snapshot)
    {
        if (snapshot.ScheduleId is not Guid scheduleId) return;
        var scheduled = _settings.ScheduledRecordings?.FirstOrDefault(recording => recording.Id == scheduleId);
        if (scheduled is null) return;

        var previousStatus = scheduled.Status;
        var previousOutputPath = scheduled.OutputPath;
        AddRecordingOutputPath(scheduled, snapshot.OutputPath);
        var desiredStatus = snapshot.State switch
        {
            DvrRecordingState.Starting or DvrRecordingState.Recording or DvrRecordingState.Stopping => "Recording",
            DvrRecordingState.Completed => "Completed",
            DvrRecordingState.Failed => "Failed",
            _ => scheduled.Status
        };
        var terminalSignature = $"{scheduleId:N}:{snapshot.State}:{snapshot.OutputPath}";
        var terminalAlreadyHandled =
            (snapshot.State is DvrRecordingState.Completed or DvrRecordingState.Failed) &&
            terminalSignature == _handledDvrTerminalSignature;
        if (terminalAlreadyHandled) return;

        if (snapshot.State == DvrRecordingState.Failed && scheduled.StopUtc > DateTimeOffset.UtcNow)
        {
            ScheduleRecordingRecovery(scheduled, snapshot.Message ?? "The provider stream ended unexpectedly", DateTimeOffset.UtcNow);
            desiredStatus = scheduled.Status;
        }
        else if (snapshot.State == DvrRecordingState.Failed && HasPlayableRecordingSegment(scheduled))
        {
            desiredStatus = "Partial";
            scheduled.Detail = "Recovery limit reached • playable recording segments were preserved";
        }

        var changed = previousStatus != desiredStatus || previousOutputPath != snapshot.OutputPath;
        scheduled.Status = desiredStatus;
        scheduled.OutputPath = snapshot.OutputPath;
        if (desiredStatus is not ("Recovering" or "Partial"))
            scheduled.Detail = snapshot.State == DvrRecordingState.Completed && scheduled.RecoveryAttempts > 0
                ? $"Recording completed across {scheduled.OutputPaths.Count:N0} recovered segment{(scheduled.OutputPaths.Count == 1 ? string.Empty : "s")}"
                : snapshot.Message;
        if (snapshot.State is DvrRecordingState.Completed or DvrRecordingState.Failed)
            _handledDvrTerminalSignature = terminalSignature;
        if ((desiredStatus is "Completed" or "Partial") && scheduled.SeriesRuleId is Guid seriesRuleId)
        {
            var rule = _settings.SeriesRecordingRules.FirstOrDefault(candidate => candidate.Id == seriesRuleId);
            if (rule is not null) ApplySeriesRetention(rule);
        }
        if (changed) _ = _settingsStore.SaveAsync(_settings);
    }

    private bool ScheduleRecordingRecovery(ScheduledRecording recording, string message, DateTimeOffset now)
    {
        var maximumAttempts = Math.Clamp(_settings.SmartDvr?.MaximumRecoveryAttempts ?? 3, 1, 5);
        recording.LastInterruption = message;
        if (recording.RecoveryAttempts >= maximumAttempts || recording.StopUtc <= now)
        {
            recording.Status = HasPlayableRecordingSegment(recording) ? "Partial" : "Failed";
            recording.NextRecoveryUtc = null;
            recording.Detail = HasPlayableRecordingSegment(recording)
                ? $"Recovery stopped after {recording.RecoveryAttempts:N0} attempt{(recording.RecoveryAttempts == 1 ? string.Empty : "s")} • playable segments preserved"
                : $"Recovery limit reached • {message}";
            return false;
        }

        recording.RecoveryAttempts++;
        recording.Status = "Recovering";
        recording.NextRecoveryUtc = now + SmartDvrPolicy.RecoveryDelay(recording.RecoveryAttempts);
        recording.Detail = $"{message} • retry {recording.RecoveryAttempts}/{maximumAttempts} at {recording.NextRecoveryUtc.Value.ToLocalTime():h:mm:ss tt}";
        return true;
    }

    private static bool IsRecordingReadyToStart(ScheduledRecording recording, DateTimeOffset now) =>
        recording.StopUtc > now &&
        (recording.Status == "Scheduled" && recording.StartUtc <= now ||
         recording.Status == "Recovering" && (recording.NextRecoveryUtc ?? now) <= now);

    private static void AddRecordingOutputPath(ScheduledRecording recording, string? outputPath)
    {
        recording.OutputPaths ??= [];
        if (string.IsNullOrWhiteSpace(outputPath) || recording.OutputPaths.Contains(outputPath, StringComparer.OrdinalIgnoreCase)) return;
        recording.OutputPaths.Add(outputPath);
    }

    private static bool HasPlayableRecordingSegment(ScheduledRecording recording) =>
        recording.OutputPaths
            .Append(recording.OutputPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(path =>
            {
                try { return File.Exists(path) && new FileInfo(path).Length > 0; }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
            });

    private void NormalizeScheduledRecordings()
    {
        _settings.ScheduledRecordings ??= [];
        var now = DateTimeOffset.UtcNow;
        foreach (var recording in _settings.ScheduledRecordings.Where(recording => recording.Status == "Recording"))
        {
            recording.OutputPaths ??= [];
            if (recording.StopUtc > now)
                ScheduleRecordingRecovery(recording, "Ready to resume after OrbitalVue restarted", now);
            else
            {
                recording.Status = HasPlayableRecordingSegment(recording) ? "Partial" : "Missed";
                recording.Detail = HasPlayableRecordingSegment(recording) ? "Playable segment preserved before shutdown" : "OrbitalVue closed before completion";
            }
        }
        foreach (var recording in _settings.ScheduledRecordings)
            recording.OutputPaths ??= [];
        _settings.ScheduledRecordings.RemoveAll(recording =>
            (recording.Status is "Completed" or "Partial" or "Expired" or "Missed" or "Failed" or "Conflict" or "Canceled") &&
            recording.StopUtc < now.AddDays(-14));
    }

    private void UpdateDvrUi(DvrRecordingSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;
        var recordableChannel = ResolveRecordableChannel();
        var elapsed = snapshot.Elapsed(now);
        DvrElapsedText.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        DvrSizeText.Text = FormatDvrBytes(snapshot.BytesWritten);
        var averageMegabits = elapsed.TotalSeconds > 1 ? snapshot.BytesWritten * 8d / elapsed.TotalSeconds / 1_000_000d : 0;
        DvrRateText.Text = snapshot.IsActive
            ? averageMegabits > 0 ? $"HEALTHY • {averageMegabits:0.0} Mbps" : "WAITING FOR MEDIA"
            : "RECORDER READY";
        DvrRemainingText.Text = snapshot.IsActive && snapshot.StopUtc is DateTimeOffset stopUtc
            ? $"{FormatTimeshiftDuration(stopUtc > now ? stopUtc - now : TimeSpan.Zero)} LEFT"
            : snapshot.IsActive ? "MANUAL" : "IDLE";
        DvrActiveDot.Fill = snapshot.IsActive ? ErrorBrush : IdleBrush;
        RecordButton.IsEnabled = snapshot.IsActive || recordableChannel?.Kind == ChannelKind.Live;

        if (snapshot.IsActive)
        {
            DvrCurrentTitle.Text = $"Recording • {snapshot.ChannelName}";
            DvrCurrentDetail.Text = string.IsNullOrWhiteSpace(snapshot.ProgramTitle)
                ? snapshot.Message ?? "Recording the original live transport stream"
                : $"{snapshot.ProgramTitle} • {snapshot.Message}";
            DvrStateBadge.Background = new SolidColorBrush(Color.FromRgb(59, 32, 39));
            DvrStateBadgeText.Foreground = ErrorBrush;
            DvrStateBadgeText.Text = snapshot.State == DvrRecordingState.Starting ? "STARTING" : "RECORDING";
            DvrActionButton.Content = "Stop & save";
            DvrNavigationText.Text = "REC";
            DvrNavigationDot.Fill = ErrorBrush;
            DvrNavigationButton.Background = new SolidColorBrush(Color.FromRgb(59, 32, 39));
            DvrNavigationButton.BorderBrush = ErrorBrush;
            RecordButtonGlyph.Text = "■";
            RecordButton.ToolTip = $"Stop recording {snapshot.ChannelName}";
        }
        else
        {
            DvrNavigationText.Text = "DVR";
            DvrNavigationDot.Fill = IdleBrush;
            DvrNavigationButton.ClearValue(BackgroundProperty);
            DvrNavigationButton.ClearValue(BorderBrushProperty);
            RecordButtonGlyph.Text = "●";
            RecordButton.ToolTip = recordableChannel?.Kind == ChannelKind.Live
                ? $"Record {recordableChannel.Name}"
                : "Choose a live channel to record";
            DvrActionButton.Content = "Record now";
            DvrActionButton.IsEnabled = recordableChannel?.Kind == ChannelKind.Live;

            if (snapshot.State == DvrRecordingState.Completed)
            {
                DvrCurrentTitle.Text = "Recording saved";
                DvrCurrentDetail.Text = Path.GetFileName(snapshot.OutputPath) ?? snapshot.Message ?? "The recording is ready.";
                DvrStateBadge.Background = new SolidColorBrush(Color.FromRgb(23, 54, 47));
                DvrStateBadgeText.Foreground = LiveBrush;
                DvrStateBadgeText.Text = "SAVED";
            }
            else if (snapshot.State == DvrRecordingState.Failed)
            {
                var recovering = snapshot.ScheduleId is Guid failedScheduleId
                    ? _settings.ScheduledRecordings.FirstOrDefault(recording => recording.Id == failedScheduleId && recording.Status == "Recovering")
                    : null;
                DvrCurrentTitle.Text = recovering is null ? "Recording could not be completed" : $"Restoring • {recovering.ProgramTitle}";
                DvrCurrentDetail.Text = recovering?.Detail ?? snapshot.Message ?? "The provider stream ended before media could be saved.";
                DvrStateBadge.Background = new SolidColorBrush(recovering is null ? Color.FromRgb(66, 31, 38) : Color.FromRgb(62, 48, 27));
                DvrStateBadgeText.Foreground = recovering is null ? ErrorBrush : WarningBrush;
                DvrStateBadgeText.Text = recovering is null ? "FAILED" : "RECOVERING";
            }
            else
            {
                var canRecordCurrentChannel = recordableChannel?.Kind == ChannelKind.Live;
                DvrCurrentTitle.Text = canRecordCurrentChannel ? $"Ready • {recordableChannel!.Name}" : "Ready to record";
                DvrCurrentDetail.Text = canRecordCurrentChannel
                    ? "Record the current live channel without interrupting playback."
                    : "Choose a live channel, then press Record.";
                DvrStateBadge.Background = new SolidColorBrush(Color.FromRgb(23, 36, 50));
                DvrStateBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(117, 185, 255));
                DvrStateBadgeText.Text = "READY";
            }
        }

        if (snapshot.IsActive) DvrActionButton.IsEnabled = true;
        var nextRecording = (_settings.ScheduledRecordings ?? [])
            .Where(recording => (recording.Status is "Scheduled" or "Recovering" or "Recording") && recording.StopUtc > now)
            .OrderBy(recording => recording.Status == "Recording" ? DateTimeOffset.MinValue : recording.StartUtc)
            .FirstOrDefault();
        DvrNextRecordingText.Text = nextRecording is null
            ? "Nothing scheduled"
            : nextRecording.Status == "Recording"
                ? $"Now • {nextRecording.ProgramTitle}"
                : $"{nextRecording.GuideStartUtc.ToLocalTime():ddd h:mm tt} • {nextRecording.ProgramTitle}";
        var smartPreferences = _settings.SmartDvr ?? new SmartDvrPreferences();
        DvrBackgroundStatusText.Text = !smartPreferences.BackgroundRecordingEnabled
            ? "Disabled"
            : _hiddenToTray
                ? "Running in tray"
                : _dvrWakeTimer.NextWakeUtc is DateTimeOffset wakeUtc
                    ? $"Armed • {wakeUtc.ToLocalTime():ddd h:mm tt}"
                    : "Ready • closes to tray";
        var scheduleSignature = $"calendar:{_selectedDvrCalendarDate?.ToString("O") ?? "all"}::" + string.Join('|', (_settings.ScheduledRecordings ?? [])
            .OrderBy(recording => recording.Id)
            .Select(recording => $"{recording.Id:N}:{recording.Status}:{recording.Priority}:{recording.StartUtc:O}:{recording.StopUtc:O}:{recording.RecoveryAttempts}:{recording.NextRecoveryUtc:O}:{recording.Detail}")) +
            "::" + string.Join('|', (_settings.SeriesRecordingRules ?? [])
                .OrderBy(rule => rule.Id)
                .Select(rule => $"{rule.Id:N}:{rule.Enabled}:{rule.Priority}:{rule.StartPaddingMinutes}:{rule.EndPaddingMinutes}:{rule.EpisodeSelection}:{rule.KeepLatestCount}:{rule.AnyChannel}"));
        if (!string.Equals(_dvrScheduleSignature, scheduleSignature, StringComparison.Ordinal))
        {
            var conflictingIds = DvrRecordingService.FindConflictingScheduleIds(_settings.ScheduledRecordings ?? []);
            var conflictWinners = SmartDvrPolicy.FindConflictWinners(_settings.ScheduledRecordings ?? []);
            var calendarSchedules = (_settings.ScheduledRecordings ?? [])
                .Where(recording => recording.StopUtc > now.AddDays(-1) && recording.StartUtc < now.AddDays(14))
                .ToList();
            var calendarRows = new List<DvrCalendarDayRow>
            {
                CreateDvrCalendarDay(null, "All", calendarSchedules, _selectedDvrCalendarDate is null)
            };
            calendarRows.AddRange(Enumerable.Range(0, 7).Select(offset =>
            {
                var date = DateOnly.FromDateTime(DateTime.Today.AddDays(offset));
                return CreateDvrCalendarDay(date, date.ToString("ddd"), calendarSchedules, _selectedDvrCalendarDate == date);
            }));
            DvrCalendarList.ItemsSource = calendarRows;

            var scheduleRows = (_settings.ScheduledRecordings ?? [])
                .Where(recording => _selectedDvrCalendarDate is null ||
                                    DateOnly.FromDateTime(recording.GuideStartUtc.ToLocalTime().DateTime) == _selectedDvrCalendarDate)
                .OrderBy(recording => recording.Status is "Scheduled" or "Recording" or "Recovering" ? 0 : 1)
                .ThenByDescending(recording => recording.Priority)
                .ThenBy(recording => recording.StartUtc)
                .Take(20)
                .Select(recording => new DvrScheduleRow(
                    recording,
                    conflictingIds.Contains(recording.Id),
                    conflictWinners.Contains(recording.Id)))
                .ToList();
            DvrScheduleList.ItemsSource = scheduleRows;
            DvrScheduleEmptyText.Visibility = scheduleRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DvrConflictText.Text = conflictingIds.Count == 0
                ? "RIGHT-CLICK A GUIDE PROGRAM"
                : $"⚠ {conflictingIds.Count:N0} OVERLAPPING • PRIORITY RESOLVES AUTOMATICALLY";

            var seriesRows = (_settings.SeriesRecordingRules ?? [])
                .Where(rule => rule.Enabled)
                .OrderBy(rule => rule.ProgramTitle, StringComparer.OrdinalIgnoreCase)
                .Select(rule => new DvrSeriesRuleRow(
                    rule,
                    (_settings.ScheduledRecordings ?? []).Count(recording =>
                        recording.SeriesRuleId == rule.Id && (recording.Status is "Scheduled" or "Recovering"))))
                .ToList();
            DvrSeriesRuleList.ItemsSource = seriesRows;
            DvrSeriesRuleEmptyText.Visibility = seriesRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DvrSeriesRuleCountText.Text = seriesRows.Count == 0
                ? "RIGHT-CLICK A GUIDE PROGRAM"
                : $"{seriesRows.Count:N0} ACTIVE RULE{(seriesRows.Count == 1 ? string.Empty : "S")}";
            _dvrScheduleSignature = scheduleSignature;
        }

        var librarySignature = $"{snapshot.State}:{snapshot.OutputPath}:{snapshot.BytesWritten}:{_settings.RecordingsFolder}:{_currentRecordingPath}";
        var terminalChanged = (snapshot.State is DvrRecordingState.Completed or DvrRecordingState.Failed) &&
                              !string.Equals(_dvrLibrarySignature, librarySignature, StringComparison.Ordinal);
        var visibleRefreshDue = DvrOverlay.Visibility == Visibility.Visible &&
                                now - _lastDvrLibraryRefreshUtc >= TimeSpan.FromSeconds(2);
        if (visibleRefreshDue || terminalChanged)
        {
            var library = _dvrRecording.ListRecentRecordings(_settings.RecordingsFolder);
            var activeOutput = snapshot.IsActive ? snapshot.OutputPath : null;
            var rows = library.Select(recording =>
            {
                var sourceSchedule = (_settings.ScheduledRecordings ?? []).FirstOrDefault(schedule =>
                    (schedule.OutputPaths ?? []).Contains(recording.FilePath, StringComparer.OrdinalIgnoreCase) ||
                    string.Equals(schedule.OutputPath, recording.FilePath, StringComparison.OrdinalIgnoreCase));
                var statusLabel = sourceSchedule?.Status switch
                {
                    "Partial" => "PARTIAL • PLAYABLE",
                    "Recording" => "RECORDING NOW",
                    "Recovering" => "RECOVERY SEGMENT",
                    "Completed" when sourceSchedule.RecoveryAttempts > 0 => "RECOVERED",
                    _ => null
                };
                if (statusLabel is not null) recording = recording with { StatusLabel = statusLabel };
                var key = DvrRecordingService.CreateLibraryKey(recording.FilePath);
                _settings.RecordingPlaybackProgress.TryGetValue(key, out var progress);
                var isPlaying = string.Equals(_currentRecordingPath, recording.FilePath, StringComparison.OrdinalIgnoreCase);
                var canDelete = !isPlaying && !string.Equals(activeOutput, recording.FilePath, StringComparison.OrdinalIgnoreCase);
                return new DvrLibraryRow(recording, key, progress, isPlaying, canDelete);
            }).ToList();
            DvrLibraryList.ItemsSource = rows;
            DvrLibraryEmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var storage = _dvrRecording.GetStorageSnapshot(_settings.RecordingsFolder);
            DvrStorageText.Text = storage.IsAvailable
                ? $"{FormatDvrBytes(storage.FreeBytes)} free of {FormatDvrBytes(storage.TotalBytes)}  •  {storage.RecordingCount:N0} recording{(storage.RecordingCount == 1 ? string.Empty : "s")} use {FormatDvrBytes(storage.RecordingBytes)}  •  {smartPreferences.StorageReserveGigabytes:N0} GB protected"
                : "Storage information is unavailable for this folder";
            DvrStorageProgress.Value = storage.DriveUsedPercent;
            var estimatedHours = DvrBackgroundPolicy.EstimateCapacityHours(storage, smartPreferences.StorageReserveGigabytes);
            DvrCapacityText.Text = storage.IsAvailable
                ? $"~{estimatedHours:0.0} hours at 8 Mbps"
                : "Storage unavailable";
            _dvrLibrarySignature = librarySignature;
            _lastDvrLibraryRefreshUtc = now;
        }
    }

    private async void CancelScheduledRecording_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DvrScheduleRow row) return;
        var scheduled = _settings.ScheduledRecordings.FirstOrDefault(recording => recording.Id == row.Id);
        if (scheduled is null) return;
        if (_dvrRecording.Snapshot.ScheduleId == scheduled.Id && _dvrRecording.Snapshot.IsActive)
            _dvrRecording.Stop("Scheduled recording canceled");
        if (scheduled.SeriesRuleId is null)
        {
            _settings.ScheduledRecordings.Remove(scheduled);
        }
        else
        {
            scheduled.Status = "Canceled";
            scheduled.Detail = "This series airing was skipped";
        }
        await _settingsStore.SaveAsync(_settings);
        UpdateDvrUi(_dvrRecording.Poll(DateTimeOffset.UtcNow));
        FooterStatusText.Text = $"Recording canceled • {scheduled.ProgramTitle}";
    }

    private void SelectDvrCalendarDay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DvrCalendarDayRow row) return;
        _selectedDvrCalendarDate = row.Date;
        _dvrScheduleSignature = string.Empty;
        UpdateDvrUi(_dvrRecording.Poll(DateTimeOffset.UtcNow));
    }

    private static DvrCalendarDayRow CreateDvrCalendarDay(
        DateOnly? date,
        string label,
        IReadOnlyList<ScheduledRecording> recordings,
        bool selected)
    {
        var matching = recordings.Where(recording => date is null ||
            DateOnly.FromDateTime(recording.GuideStartUtc.ToLocalTime().DateTime) == date).ToList();
        var estimatedBytes = matching.Sum(recording => Math.Max(0, (recording.StopUtc - recording.StartUtc).TotalSeconds) * 1_000_000d);
        return new DvrCalendarDayRow(date, label, matching.Count, estimatedBytes, selected);
    }

    private async void PrioritizeScheduledRecording_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DvrScheduleRow row) return;
        var scheduled = _settings.ScheduledRecordings.FirstOrDefault(recording => recording.Id == row.Id);
        if (scheduled is null || scheduled.Status is not ("Scheduled" or "Recovering")) return;
        scheduled.Priority = scheduled.Priority == DvrSchedulePriority.High
            ? DvrSchedulePriority.Normal
            : (DvrSchedulePriority)((int)scheduled.Priority + 1);
        await _settingsStore.SaveAsync(_settings);
        UpdateDvrUi(_dvrRecording.Poll(DateTimeOffset.UtcNow));
        FooterStatusDot.Fill = LiveBrush;
        FooterStatusText.Text = $"{scheduled.Priority} priority • {scheduled.ProgramTitle}";
    }

    private async void RemoveSeriesRule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DvrSeriesRuleRow row) return;
        var rule = _settings.SeriesRecordingRules.FirstOrDefault(candidate => candidate.Id == row.Id);
        if (rule is null) return;
        _settings.SeriesRecordingRules.Remove(rule);
        _settings.ScheduledRecordings.RemoveAll(recording =>
            recording.SeriesRuleId == rule.Id && (recording.Status is "Scheduled" or "Canceled" or "Recovering"));
        await _settingsStore.SaveAsync(_settings);
        UpdateDvrUi(_dvrRecording.Poll(DateTimeOffset.UtcNow));
        FooterStatusDot.Fill = IdleBrush;
        FooterStatusText.Text = $"Series rule removed • {rule.ProgramTitle}";
    }

    private async void PrioritizeSeriesRule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DvrSeriesRuleRow row) return;
        var rule = _settings.SeriesRecordingRules.FirstOrDefault(candidate => candidate.Id == row.Id);
        if (rule is null) return;
        rule.Priority = rule.Priority == DvrSchedulePriority.High
            ? DvrSchedulePriority.Normal
            : (DvrSchedulePriority)((int)rule.Priority + 1);
        foreach (var recording in _settings.ScheduledRecordings.Where(recording =>
                     recording.SeriesRuleId == rule.Id && recording.Status == "Scheduled"))
            recording.Priority = rule.Priority;
        await _settingsStore.SaveAsync(_settings);
        UpdateDvrUi(_dvrRecording.Poll(DateTimeOffset.UtcNow));
        FooterStatusDot.Fill = LiveBrush;
        FooterStatusText.Text = $"{rule.Priority} priority for series • {rule.ProgramTitle}";
    }

    private async void ToggleSeriesEpisodeMode_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DvrSeriesRuleRow row) return;
        var rule = _settings.SeriesRecordingRules.FirstOrDefault(candidate => candidate.Id == row.Id);
        if (rule is null) return;
        rule.EpisodeSelection = rule.EpisodeSelection == DvrEpisodeSelection.NewEpisodesOnly
            ? DvrEpisodeSelection.AllEpisodes
            : DvrEpisodeSelection.NewEpisodesOnly;
        RebuildFutureSeriesSchedules(rule);
        await _settingsStore.SaveAsync(_settings);
        UpdateDvrUi(_dvrRecording.Poll(DateTimeOffset.UtcNow));
        FooterStatusText.Text = $"{rule.ProgramTitle} • {(rule.EpisodeSelection == DvrEpisodeSelection.NewEpisodesOnly ? "new episodes only" : "all airings")}";
    }

    private async void CycleSeriesRetention_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DvrSeriesRuleRow row) return;
        var rule = _settings.SeriesRecordingRules.FirstOrDefault(candidate => candidate.Id == row.Id);
        if (rule is null) return;
        rule.KeepLatestCount = SmartDvrPolicy.NextRetention(rule.KeepLatestCount);
        ApplySeriesRetention(rule);
        await _settingsStore.SaveAsync(_settings);
        UpdateDvrUi(_dvrRecording.Poll(DateTimeOffset.UtcNow));
        FooterStatusText.Text = rule.KeepLatestCount == 0
            ? $"{rule.ProgramTitle} • keep every recording"
            : $"{rule.ProgramTitle} • keep the newest {rule.KeepLatestCount}";
    }

    private async void ToggleSeriesChannelScope_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DvrSeriesRuleRow row) return;
        var rule = _settings.SeriesRecordingRules.FirstOrDefault(candidate => candidate.Id == row.Id);
        if (rule is null) return;
        rule.AnyChannel = !rule.AnyChannel;
        RebuildFutureSeriesSchedules(rule);
        await _settingsStore.SaveAsync(_settings);
        UpdateDvrUi(_dvrRecording.Poll(DateTimeOffset.UtcNow));
        FooterStatusText.Text = $"{rule.ProgramTitle} • {(rule.AnyChannel ? "match any channel" : "match this channel")}";
    }

    private async void DvrApplySmartSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.SmartDvr ??= new SmartDvrPreferences();
        var previousTimeshiftEnabled = _settings.SmartDvr.LiveTimeshiftEnabled;
        var previousTimeshiftMinutes = _settings.SmartDvr.LiveTimeshiftMinutes;
        _settings.SmartDvr.StartPaddingMinutes = ReadTaggedInt(DvrStartPaddingBox, 1);
        _settings.SmartDvr.EndPaddingMinutes = ReadTaggedInt(DvrEndPaddingBox, 2);
        _settings.SmartDvr.StorageReserveGigabytes = ReadTaggedInt(DvrStorageReserveBox, 5);
        _settings.SmartDvr.DefaultPriority = ReadTaggedPriority(DvrDefaultPriorityBox, DvrSchedulePriority.Normal);
        _settings.SmartDvr.BackgroundRecordingEnabled = DvrBackgroundRecordingCheck.IsChecked == true;
        _settings.SmartDvr.WakeForRecordings = DvrWakeForRecordingCheck.IsChecked == true;
        _settings.SmartDvr.LiveTimeshiftEnabled = DvrTimeshiftCheck.IsChecked == true;
        _settings.SmartDvr.LiveTimeshiftMinutes = SmartDvrPolicy.ClampTimeshiftMinutes(ReadTaggedInt(DvrTimeshiftWindowBox, 60));
        _settings.SmartDvr.MaximumRecoveryAttempts = Math.Clamp(ReadTaggedInt(DvrRecoveryAttemptsBox, 3), 1, 5);
        await _settingsStore.SaveAsync(_settings);
        _dvrWakeSignature = string.Empty;
        RefreshDvrWakeTimer();
        if (previousTimeshiftEnabled != _settings.SmartDvr.LiveTimeshiftEnabled ||
            previousTimeshiftMinutes != _settings.SmartDvr.LiveTimeshiftMinutes)
        {
            var channel = _currentChannel;
            CreatePlaybackEngine();
            if (channel is not null) RetuneCurrentPlayback(GetOrCreateChannelProfile(channel));
        }
        _lastDvrLibraryRefreshUtc = DateTimeOffset.MinValue;
        UpdateDvrUi(_dvrRecording.Poll(DateTimeOffset.UtcNow));
        FooterStatusDot.Fill = LiveBrush;
        FooterStatusText.Text = "Smart DVR defaults saved for new recordings";
    }

    private void ApplySmartDvrSettingsToControls()
    {
        var preferences = _settings.SmartDvr ?? new SmartDvrPreferences();
        SelectTaggedItem(DvrStartPaddingBox, preferences.StartPaddingMinutes.ToString());
        SelectTaggedItem(DvrEndPaddingBox, preferences.EndPaddingMinutes.ToString());
        SelectTaggedItem(DvrStorageReserveBox, preferences.StorageReserveGigabytes.ToString());
        SelectTaggedItem(DvrDefaultPriorityBox, preferences.DefaultPriority.ToString());
        DvrBackgroundRecordingCheck.IsChecked = preferences.BackgroundRecordingEnabled;
        DvrWakeForRecordingCheck.IsChecked = preferences.WakeForRecordings;
        DvrTimeshiftCheck.IsChecked = preferences.LiveTimeshiftEnabled;
        SelectTaggedItem(DvrTimeshiftWindowBox, SmartDvrPolicy.ClampTimeshiftMinutes(preferences.LiveTimeshiftMinutes).ToString());
        SelectTaggedItem(DvrRecoveryAttemptsBox, Math.Clamp(preferences.MaximumRecoveryAttempts, 1, 5).ToString());
    }

    private static void SelectTaggedItem(ComboBox comboBox, string tag)
    {
        var match = comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
        comboBox.SelectedItem = match ?? comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private static int ReadTaggedInt(ComboBox comboBox, int fallback) =>
        comboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var value)
            ? value
            : fallback;

    private static DvrSchedulePriority ReadTaggedPriority(ComboBox comboBox, DvrSchedulePriority fallback) =>
        comboBox.SelectedItem is ComboBoxItem item &&
        Enum.TryParse<DvrSchedulePriority>(item.Tag?.ToString(), true, out var value)
            ? value
            : fallback;

    private bool HasDvrStorageReserve(string folder, out string message)
    {
        var reserve = _settings.SmartDvr?.StorageReserveGigabytes ?? 5;
        var storage = _dvrRecording.GetStorageSnapshot(folder);
        if (SmartDvrPolicy.MeetsStorageReserve(storage, reserve))
        {
            message = string.Empty;
            return true;
        }

        message = $"At least {reserve:N0} GB of free drive space is protected; clear storage or lower the reserve";
        return false;
    }

    private async void BrowseRecordingsFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the OrbitalVue recordings folder",
            InitialDirectory = ResolveRecordingsFolder(RecordingFolderBox.Text),
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        RecordingFolderBox.Text = dialog.FolderName;
        _settings.RecordingsFolder = ResolveRecordingsFolder(dialog.FolderName);
        _lastDvrLibraryRefreshUtc = DateTimeOffset.MinValue;
        await _settingsStore.SaveAsync(_settings);
        UpdateDvrUi(_dvrRecording.Snapshot);
    }

    private void OpenRecordingsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = PersistRecordingsFolder();
            Directory.CreateDirectory(folder);
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add(folder);
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or Win32Exception)
        {
            FooterStatusDot.Fill = ErrorBrush;
            FooterStatusText.Text = SafeRecordingErrorMessage(exception);
        }
    }

    private void OpenRecordingFileLocation_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DvrLibraryRow item || !File.Exists(item.FilePath)) return;
        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add($"/select,{item.FilePath}");
        Process.Start(startInfo);
    }

    private void PlayRecording_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DvrLibraryRow item || !File.Exists(item.FilePath)) return;
        if (_multiviewMode) SetMultiviewMode(false);
        HideModal(DvrOverlay);
        WatchNavigation.IsChecked = true;
        ChannelList.SelectedItem = null;
        var channel = new ChannelItem
        {
            Number = 0,
            Name = item.Name,
            Group = "DVR Library",
            Url = new Uri(Path.GetFullPath(item.FilePath)).AbsoluteUri,
            Kind = ChannelKind.Recording
        };
        TuneChannel(channel, item.FilePath);
        FooterStatusDot.Fill = LiveBrush;
        FooterStatusText.Text = item.CanResume
            ? $"Opening saved recording • resume ready at {FormatPlaybackTime(item.Progress!.PositionMilliseconds)}"
            : $"Opening saved recording • {item.Name}";
    }

    private async void DeleteRecording_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DvrLibraryRow item) return;
        var result = MessageBox.Show(
            this,
            $"Delete {item.Name}?\n\nThis permanently removes the saved recording from this PC.",
            "Delete recording",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            _dvrRecording.DeleteRecording(item.FilePath, _settings.RecordingsFolder);
            _settings.RecordingPlaybackProgress.Remove(item.LibraryKey);
            await _settingsStore.SaveAsync(_settings);
            _dvrLibrarySignature = string.Empty;
            _lastDvrLibraryRefreshUtc = DateTimeOffset.MinValue;
            UpdateDvrUi(_dvrRecording.Poll(DateTimeOffset.UtcNow));
            FooterStatusDot.Fill = LiveBrush;
            FooterStatusText.Text = $"Recording deleted • {item.Name}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            FooterStatusDot.Fill = ErrorBrush;
            FooterStatusText.Text = exception.Message;
        }
    }

    private string PersistRecordingsFolder()
    {
        var folder = ResolveRecordingsFolder(RecordingFolderBox.Text);
        RecordingFolderBox.Text = folder;
        if (!string.Equals(_settings.RecordingsFolder, folder, StringComparison.OrdinalIgnoreCase))
        {
            _settings.RecordingsFolder = folder;
            _lastDvrLibraryRefreshUtc = DateTimeOffset.MinValue;
            _ = _settingsStore.SaveAsync(_settings);
        }
        return folder;
    }

    private static string ResolveRecordingsFolder(string? folder)
    {
        try { return DvrRecordingService.NormalizeRecordingsFolder(folder); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return DvrRecordingService.DefaultRecordingsFolder;
        }
    }

    private static string FormatDvrBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824d:0.00} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576d:0.0} MB";
        if (bytes >= 1_024) return $"{bytes / 1_024d:0.0} KB";
        return $"{bytes:N0} bytes";
    }

    private void ToggleMiniPlayer_Click(object sender, RoutedEventArgs e) => ToggleMiniPlayer();

    private void ToggleMiniPlayer()
    {
        if (IsAnyModalVisible()) return;
        if (_isMiniPlayer)
        {
            ExitMiniPlayer();
            return;
        }

        if (_multiviewMode)
        {
            FooterStatusDot.Fill = WarningBrush;
            FooterStatusText.Text = "Mini Player is available from the single-channel Watch view";
            return;
        }

        if (_isFullscreen) ExitFullscreen();
        EnterMiniPlayer();
    }

    private void EnterMiniPlayer()
    {
        if (_isMiniPlayer) return;
        _windowStateBeforeMini = WindowState;
        _windowBoundsBeforeMini = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        _topmostBeforeMini = Topmost;
        _minimumWidthBeforeMini = MinWidth;
        _minimumHeightBeforeMini = MinHeight;
        _navWidthBeforeMini = NavColumn.Width;
        _catalogWidthBeforeMini = CatalogColumn.Width;
        _inspectorWidthBeforeMini = InspectorColumn.Width;
        _footerHeightBeforeMini = FooterRow.Height;
        _playerHeaderHeightBeforeMini = PlayerHeaderRow.Height;
        _playerControlsHeightBeforeMini = PlayerControlsRow.Height;
        _playerMarginBeforeMini = PlayerPanel.Margin;
        _playerCornerRadiusBeforeMini = PlayerVideoFrame.CornerRadius;
        _playerBorderBeforeMini = PlayerVideoFrame.BorderThickness;

        WindowState = WindowState.Normal;
        _isMiniPlayer = true;
        NavColumn.Width = new GridLength(0);
        CatalogColumn.Width = new GridLength(0);
        InspectorColumn.Width = new GridLength(0);
        FooterRow.Height = new GridLength(0);
        PlayerHeaderRow.Height = new GridLength(0);
        PlayerControlsRow.Height = new GridLength(0);
        PlayerPanel.Margin = new Thickness(0);
        PlayerVideoFrame.CornerRadius = new CornerRadius(0);
        PlayerVideoFrame.BorderThickness = new Thickness(0);
        BroadcastTitle.Visibility = Visibility.Collapsed;
        UpdateNavigationButton.Visibility = Visibility.Collapsed;
        MiniPlayerButtonText.Text = "FULL APP";
        MiniPlayerButton.ToolTip = "Return to the complete OrbitalVue workspace (Ctrl+Shift+M)";

        MinWidth = 480;
        MinHeight = 300;
        Width = 680;
        Height = 430;
        Topmost = _settings.MiniPlayerAlwaysOnTop;

        var workingArea = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        Left = Math.Clamp(_windowBoundsBeforeMini.Left, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - Width));
        Top = Math.Clamp(_windowBoundsBeforeMini.Top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - Height));
        RefreshPlayerSurfaceVisibility();
    }

    private void ExitMiniPlayer()
    {
        if (!_isMiniPlayer) return;
        _isMiniPlayer = false;
        NavColumn.Width = _navWidthBeforeMini;
        CatalogColumn.Width = _catalogWidthBeforeMini;
        InspectorColumn.Width = _inspectorWidthBeforeMini;
        FooterRow.Height = _footerHeightBeforeMini;
        PlayerHeaderRow.Height = _playerHeaderHeightBeforeMini;
        PlayerControlsRow.Height = _playerControlsHeightBeforeMini;
        PlayerPanel.Margin = _playerMarginBeforeMini;
        PlayerVideoFrame.CornerRadius = _playerCornerRadiusBeforeMini;
        PlayerVideoFrame.BorderThickness = _playerBorderBeforeMini;
        BroadcastTitle.Visibility = Visibility.Visible;
        UpdateNavigationButton.Visibility = Visibility.Visible;
        MiniPlayerButtonText.Text = "MINI";
        MiniPlayerButton.ToolTip = "Compact always-on-top player (Ctrl+Shift+M)";
        Topmost = _topmostBeforeMini;
        MinWidth = _minimumWidthBeforeMini;
        MinHeight = _minimumHeightBeforeMini;

        WindowState = WindowState.Normal;
        if (!_windowBoundsBeforeMini.IsEmpty)
        {
            Left = _windowBoundsBeforeMini.Left;
            Top = _windowBoundsBeforeMini.Top;
            Width = _windowBoundsBeforeMini.Width;
            Height = _windowBoundsBeforeMini.Height;
        }
        if (_windowStateBeforeMini == WindowState.Maximized) WindowState = WindowState.Maximized;
        RefreshPlayerSurfaceVisibility();
    }

    private void ToggleFullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (IsAnyModalVisible()) return;

        try
        {
            if (_isFullscreen) ExitFullscreen();
            else
            {
                if (_isMiniPlayer) ExitMiniPlayer();
                EnterFullscreen();
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            _isFullscreen = false;
            ApplyFullscreenPresentation(false);
            Mouse.OverrideCursor = null;
            FooterStatusDot.Fill = ErrorBrush;
            FooterStatusText.Text = $"Fullscreen unavailable • {exception.Message}";
        }
    }

    private void EnterFullscreen()
    {
        _fullscreenWindow.Enter(this);
        _isFullscreen = true;
        ApplyFullscreenPresentation(true);
        ShowFullscreenChrome();
        RefreshPlayerSurfaceVisibility();
    }

    private void ExitFullscreen()
    {
        _fullscreenChromeTimer.Stop();
        FullscreenHint.Visibility = Visibility.Collapsed;
        FullscreenHud.Visibility = Visibility.Collapsed;
        QuickTuneOverlay.Visibility = Visibility.Collapsed;
        Mouse.OverrideCursor = null;
        _isFullscreen = false;
        ApplyFullscreenPresentation(false);
        _fullscreenWindow.Exit(this);
        RefreshPlayerSurfaceVisibility();
    }

    private void ApplyFullscreenPresentation(bool fullscreen)
    {
        ShellRoot.RowDefinitions[0].Height = fullscreen ? new GridLength(0) : new GridLength(52);
        ShellRoot.RowDefinitions[2].Height = fullscreen ? new GridLength(0) : new GridLength(34);
        NavColumn.Width = fullscreen ? new GridLength(0) : new GridLength(82);
        CatalogColumn.Width = fullscreen ? new GridLength(0) : new GridLength(368);
        InspectorColumn.Width = fullscreen ? new GridLength(0) : new GridLength(286);

        PlayerHeaderRow.Height = fullscreen ? new GridLength(0) : GridLength.Auto;
        PlayerControlsRow.Height = fullscreen ? new GridLength(0) : GridLength.Auto;
        PlayerPanel.Margin = fullscreen ? new Thickness(0) : new Thickness(22, 18, 22, 18);
        PlayerVideoFrame.CornerRadius = fullscreen ? new CornerRadius(0) : new CornerRadius(18);
        PlayerVideoFrame.BorderThickness = fullscreen ? new Thickness(0) : new Thickness(1);

        MultiviewHeaderRow.Height = fullscreen ? new GridLength(0) : GridLength.Auto;
        MultiviewStatusRow.Height = fullscreen ? new GridLength(0) : GridLength.Auto;
        MultiviewTiles.Margin = fullscreen ? new Thickness(0) : new Thickness(18, 0, 18, 18);
        _multiviewSession?.SetFullscreenPresentation(fullscreen && _multiviewMode);
        ShellRoot.Background = fullscreen ? System.Windows.Media.Brushes.Black : (Brush)FindResource("CanvasBrush");
    }

    private void ShowFullscreenChrome()
    {
        if (!_isFullscreen) return;
        Mouse.OverrideCursor = null;
        FullscreenHint.Visibility = !_multiviewMode && _currentChannel is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        FullscreenHud.Visibility = !_multiviewMode && _currentChannel is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateFullscreenHud(_playback?.GetSnapshot());
        _fullscreenChromeTimer.Stop();
        _fullscreenChromeTimer.Start();
    }

    private void HideFullscreenChrome(object? sender, EventArgs e)
    {
        _fullscreenChromeTimer.Stop();
        FullscreenHint.Visibility = Visibility.Collapsed;
        if (QuickTuneOverlay.Visibility != Visibility.Visible && !_showRecoveryOverlay)
            FullscreenHud.Visibility = Visibility.Collapsed;
        if (_isFullscreen && IsActive) Mouse.OverrideCursor = Cursors.None;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isFullscreen) ShowFullscreenChrome();
    }

    private void VideoOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || IsAnyModalVisible()) return;
        ToggleFullscreen();
        e.Handled = true;
    }

    private void OpenQuickTune_Click(object sender, RoutedEventArgs e) => OpenQuickTune();

    private void OpenQuickTune(string initialText = "")
    {
        if (_channels.Count == 0 || _multiviewMode || IsAnyModalVisible()) return;
        Mouse.OverrideCursor = null;
        _fullscreenChromeTimer.Stop();
        FullscreenHud.Visibility = _isFullscreen ? Visibility.Visible : Visibility.Collapsed;
        QuickTuneOverlay.Visibility = Visibility.Visible;
        QuickTuneBox.Text = initialText;
        QuickTuneBox.CaretIndex = QuickTuneBox.Text.Length;
        RefreshQuickTuneResults();
        QuickTuneBox.Focus();
        if (initialText.Length == 0) QuickTuneBox.SelectAll();
    }

    private void CloseQuickTune_Click(object sender, RoutedEventArgs e) => CloseQuickTune();

    private void CloseQuickTune()
    {
        QuickTuneOverlay.Visibility = Visibility.Collapsed;
        if (_isFullscreen) ShowFullscreenChrome();
    }

    private void QuickTuneBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (QuickTuneList is not null) RefreshQuickTuneResults();
    }

    private void RefreshQuickTuneResults()
    {
        var query = QuickTuneBox.Text.Trim();
        IEnumerable<ChannelItem> results;
        if (query.Length == 0)
        {
            var recentOrder = (_settings.RecentChannelKeys ?? [])
                .Select((key, index) => (key, index))
                .GroupBy(pair => pair.key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToDictionary(pair => pair.key, pair => pair.index, StringComparer.OrdinalIgnoreCase);
            results = _channels
                .Where(channel => recentOrder.ContainsKey(channel.StableKey) || channel.IsFavorite)
                .OrderBy(channel => recentOrder.GetValueOrDefault(channel.StableKey, int.MaxValue))
                .ThenByDescending(channel => channel.IsFavorite)
                .ThenBy(channel => channel.Number);
        }
        else if (int.TryParse(query, out var number))
        {
            results = _channels
                .Where(channel => channel.Number.ToString().StartsWith(query, StringComparison.Ordinal))
                .OrderByDescending(channel => channel.Number == number)
                .ThenBy(channel => channel.Number);
        }
        else
        {
            results = _channels
                .Where(channel => channel.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                  channel.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                  channel.TvgName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                                  GetSignalRoute(channel)?.Feeds.Any(feed =>
                                      feed.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                      feed.SourceName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) == true)
                .OrderByDescending(channel => channel.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(channel => channel.IsFavorite)
                .ThenBy(channel => channel.Number);
        }

        var materialized = results.Take(10).ToList();
        if (materialized.Count == 0 && query.Length == 0) materialized = _channels.Take(10).ToList();
        QuickTuneList.ItemsSource = materialized;
        QuickTuneList.SelectedIndex = materialized.Count > 0 ? 0 : -1;
    }

    private void QuickTuneBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseQuickTune();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            TuneQuickTuneSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && QuickTuneList.Items.Count > 0)
        {
            QuickTuneList.SelectedIndex = Math.Min(QuickTuneList.Items.Count - 1, QuickTuneList.SelectedIndex + 1);
            QuickTuneList.ScrollIntoView(QuickTuneList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && QuickTuneList.Items.Count > 0)
        {
            QuickTuneList.SelectedIndex = Math.Max(0, QuickTuneList.SelectedIndex - 1);
            QuickTuneList.ScrollIntoView(QuickTuneList.SelectedItem);
            e.Handled = true;
        }
    }

    private void QuickTuneList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => TuneQuickTuneSelection();

    private void TuneQuickTuneSelection()
    {
        if (QuickTuneList.SelectedItem is not ChannelItem channel) return;
        CloseQuickTune();
        WatchFromGuide(channel);
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!_isFullscreen || _multiviewMode || IsAnyModalVisible() || QuickTuneOverlay.Visibility == Visibility.Visible) return;
        if (Keyboard.FocusedElement is TextBox or PasswordBox or ComboBox) return;
        if (string.IsNullOrWhiteSpace(e.Text) || !e.Text.All(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))) return;
        OpenQuickTune(e.Text);
        e.Handled = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var altEnter = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) &&
                       (e.Key == Key.Enter || e.Key == Key.System && e.SystemKey == Key.Enter);
        var miniPlayerShortcut = e.Key == Key.M &&
                                 Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                                 Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var castShortcut = e.Key == Key.C &&
                           Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                           Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var recordShortcut = e.Key == Key.R &&
                             Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                             Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (recordShortcut && !IsAnyModalVisible())
        {
            ToggleDvrRecording();
            e.Handled = true;
        }
        else if (castShortcut && !IsAnyModalVisible())
        {
            OpenCastPanel();
            e.Handled = true;
        }
        else if (miniPlayerShortcut && !IsAnyModalVisible())
        {
            ToggleMiniPlayer();
            e.Handled = true;
        }
        else if ((e.Key == Key.F11 || altEnter) && !IsAnyModalVisible())
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (QuickTuneOverlay.Visibility == Visibility.Visible) CloseQuickTune();
            else if (SignalRoutingOverlay.Visibility == Visibility.Visible) HideModal(SignalRoutingOverlay);
            else if (MappingOverlay.Visibility == Visibility.Visible) HideModal(MappingOverlay);
            else if (ImportOverlay.Visibility == Visibility.Visible && !_isLoading) HideModal(ImportOverlay);
            else if (PlaylistHealthOverlay.Visibility == Visibility.Visible && !_isLoading) HideModal(PlaylistHealthOverlay);
            else if (ChannelHealthOverlay.Visibility == Visibility.Visible) HideModal(ChannelHealthOverlay);
            else if (SettingsOverlay.Visibility == Visibility.Visible) HideModal(SettingsOverlay);
            else if (UpdateOverlay.Visibility == Visibility.Visible && !_updateBusy) HideModal(UpdateOverlay);
            else if (CastOverlay.Visibility == Visibility.Visible) HideModal(CastOverlay);
            else if (DvrOverlay.Visibility == Visibility.Visible) HideModal(DvrOverlay);
            else if (_isFullscreen) ToggleFullscreen();
            else if (_multiviewMode && _multiviewLayout == MultiviewLayout.Focus)
            {
                _multiviewLayout = MultiviewLayout.Quad;
                UpdateMultiviewLayout();
                _ = PersistMultiviewAsync();
            }
        }
        else if (QuickTuneOverlay.Visibility == Visibility.Visible)
        {
            return;
        }
        else if (e.Key == Key.MediaPlayPause && !IsAnyModalVisible())
        {
            if (_multiviewMode) _multiviewSession?.ToggleActivePause();
            else PlayPause_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.MediaStop && !IsAnyModalVisible())
        {
            if (_multiviewMode) _multiviewSession?.StopAll();
            else _playback?.Stop();
            e.Handled = true;
        }
        else if ((e.Key is Key.MediaNextTrack or Key.MediaPreviousTrack) && !IsAnyModalVisible())
        {
            TuneRelative(e.Key == Key.MediaNextTrack ? 1 : -1);
            e.Handled = true;
        }
        else if (e.Key == Key.VolumeMute && !IsAnyModalVisible())
        {
            Mute_Click(sender, e);
            e.Handled = true;
        }
        else if ((e.Key is Key.VolumeUp or Key.VolumeDown) && !IsAnyModalVisible())
        {
            VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + (e.Key == Key.VolumeUp ? 5 : -5), VolumeSlider.Minimum, VolumeSlider.Maximum);
            e.Handled = true;
        }
        else if (_multiviewMode && Keyboard.Modifiers == ModifierKeys.None && e.Key is >= Key.D1 and <= Key.D4)
        {
            EnsureMultiviewSession();
            var index = e.Key - Key.D1;
            if (_multiviewLayout == MultiviewLayout.Duo && index >= 2)
                _multiviewLayout = MultiviewLayout.Quad;
            _multiviewSession!.SelectSlot(index);
            UpdateMultiviewLayout();
            _ = PersistMultiviewAsync();
            e.Handled = true;
        }
        else if (_multiviewMode && Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.M)
        {
            EnsureMultiviewSession();
            _multiviewSession!.SetAudioSlot(_multiviewSession.ActiveSlot);
            UpdateMultiviewPresentation();
            _ = PersistMultiviewAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.Up or Key.Down)
        {
            TuneRelative(e.Key == Key.Up ? -1 : 1);
            e.Handled = true;
        }
        else if (_isFullscreen && !_multiviewMode && Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Up or Key.Down)
        {
            TuneRelative(e.Key == Key.Up ? -1 : 1);
            e.Handled = true;
        }
        else if (_isFullscreen && !_multiviewMode && Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.PageUp or Key.PageDown)
        {
            TuneRelativeGroup(e.Key == Key.PageUp ? -1 : 1);
            e.Handled = true;
        }
        else if (!_multiviewMode && Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Back && _previousChannel is not null)
        {
            TunePreviousChannel();
            e.Handled = true;
        }
        else if (e.Key == Key.D && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _currentChannel is not null)
        {
            _ = ToggleFavoriteAsync(_currentChannel);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && !IsAnyModalVisible() && QuickTuneOverlay.Visibility != Visibility.Visible)
        {
            if (_multiviewMode) _multiviewSession?.ToggleActivePause();
            else PlayPause_Click(sender, e);
            e.Handled = true;
        }
    }

    private void TuneRelative(int offset)
    {
        if (ChannelList.Items.Count == 0) return;
        var currentIndex = ChannelList.SelectedIndex;
        var nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + offset + ChannelList.Items.Count) % ChannelList.Items.Count;
        ChannelList.SelectedIndex = nextIndex;
        ChannelList.ScrollIntoView(ChannelList.SelectedItem);
    }

    private void TuneRelativeGroup(int offset)
    {
        if (_channels.Count == 0) return;
        var groups = _channels.Select(channel => channel.Group).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (groups.Count == 0) return;
        var currentGroup = _currentChannel?.Group ?? groups[0];
        var currentIndex = Math.Max(0, groups.FindIndex(group => group.Equals(currentGroup, StringComparison.OrdinalIgnoreCase)));
        var targetGroup = groups[(currentIndex + offset + groups.Count) % groups.Count];
        var target = _channels.FirstOrDefault(channel => channel.Group.Equals(targetGroup, StringComparison.OrdinalIgnoreCase));
        if (target is null) return;
        WatchFromGuide(target);
    }

    private void PreviousChannel_Click(object sender, RoutedEventArgs e) => TunePreviousChannel();

    private void TunePreviousChannel()
    {
        if (_previousChannel is null) return;
        var target = _previousChannel;
        WatchFromGuide(target);
    }

    private void ShowModal(Grid overlay)
    {
        if (_isMiniPlayer) ExitMiniPlayer();
        if (!ReferenceEquals(overlay, ImportOverlay)) ImportOverlay.Visibility = Visibility.Collapsed;
        if (!ReferenceEquals(overlay, PlaylistHealthOverlay)) PlaylistHealthOverlay.Visibility = Visibility.Collapsed;
        if (!ReferenceEquals(overlay, ChannelHealthOverlay)) ChannelHealthOverlay.Visibility = Visibility.Collapsed;
        if (!ReferenceEquals(overlay, SettingsOverlay)) SettingsOverlay.Visibility = Visibility.Collapsed;
        if (!ReferenceEquals(overlay, UpdateOverlay)) UpdateOverlay.Visibility = Visibility.Collapsed;
        if (!ReferenceEquals(overlay, CastOverlay)) CastOverlay.Visibility = Visibility.Collapsed;
        if (!ReferenceEquals(overlay, DvrOverlay)) DvrOverlay.Visibility = Visibility.Collapsed;
        if (!ReferenceEquals(overlay, SignalRoutingOverlay)) SignalRoutingOverlay.Visibility = Visibility.Collapsed;
        if (!ReferenceEquals(overlay, MappingOverlay)) MappingOverlay.Visibility = Visibility.Collapsed;

        _playerChromeSuppressed = true;
        RefreshPlayerSurfaceVisibility();
        overlay.Visibility = Visibility.Visible;
    }

    private void HideModal(Grid overlay)
    {
        overlay.Visibility = Visibility.Collapsed;
        if (ImportOverlay.Visibility != Visibility.Visible && PlaylistHealthOverlay.Visibility != Visibility.Visible && ChannelHealthOverlay.Visibility != Visibility.Visible && SettingsOverlay.Visibility != Visibility.Visible && UpdateOverlay.Visibility != Visibility.Visible && CastOverlay.Visibility != Visibility.Visible && DvrOverlay.Visibility != Visibility.Visible && SignalRoutingOverlay.Visibility != Visibility.Visible && MappingOverlay.Visibility != Visibility.Visible)
        {
            _playerChromeSuppressed = GuideWorkspace.Visibility == Visibility.Visible || _multiviewMode;
            RefreshPlayerSurfaceVisibility();
        }
    }

    private bool IsAnyModalVisible() =>
        ImportOverlay.Visibility == Visibility.Visible || PlaylistHealthOverlay.Visibility == Visibility.Visible || ChannelHealthOverlay.Visibility == Visibility.Visible || SettingsOverlay.Visibility == Visibility.Visible ||
        UpdateOverlay.Visibility == Visibility.Visible || CastOverlay.Visibility == Visibility.Visible || DvrOverlay.Visibility == Visibility.Visible || SignalRoutingOverlay.Visibility == Visibility.Visible || MappingOverlay.Visibility == Visibility.Visible;

    private void RefreshPlayerSurfaceVisibility()
    {
        var hasChannel = _currentChannel is not null;
        var visibility = PlayerSurfaceVisibilityPolicy.Evaluate(
            hasChannel,
            WindowState == WindowState.Minimized,
            _playerChromeSuppressed,
            _multiviewMode,
            IsAnyModalVisible(),
            MultiviewWorkspace.Visibility == Visibility.Visible);

        // LibVLC renders overlay content in a separate native window. Collapse the
        // overlay children themselves as well as the surface so they cannot escape
        // WPF modal z-order while a dialog is open.
        NativeVideoOverlay.Visibility = visibility.ShowVideoSurface ? Visibility.Visible : Visibility.Collapsed;
        PlayerTopStatus.Visibility = visibility.ShowVideoSurface && _showPlayerTopStatus ? Visibility.Visible : Visibility.Collapsed;
        BufferOverlay.Visibility = visibility.ShowVideoSurface && _showBufferOverlay ? Visibility.Visible : Visibility.Collapsed;
        RecoveryOverlay.Visibility = visibility.ShowVideoSurface && _showRecoveryOverlay ? Visibility.Visible : Visibility.Collapsed;
        VideoSurface.Visibility = visibility.ShowVideoSurface ? Visibility.Visible : Visibility.Collapsed;
        EmptyPlayerState.Visibility = visibility.ShowEmptyState ? Visibility.Visible : Visibility.Collapsed;
        MultiviewTiles.Visibility = visibility.ShowMultiview ? Visibility.Visible : Visibility.Collapsed;

        if (visibility.ShowVideoSurface) VideoSurface.InvalidateVisual();
        if (visibility.ShowMultiview) MultiviewTiles.InvalidateVisual();
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        _fullscreenWindow.SetActive(this, true);
        if (_isFullscreen) ShowFullscreenChrome();
        RefreshPlayerSurfaceVisibility();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        _fullscreenChromeTimer.Stop();
        FullscreenHint.Visibility = Visibility.Collapsed;
        FullscreenHud.Visibility = Visibility.Collapsed;
        QuickTuneOverlay.Visibility = Visibility.Collapsed;
        Mouse.OverrideCursor = null;
        _fullscreenWindow.SetActive(this, false);
        RefreshPlayerSurfaceVisibility();
    }

    private void Window_StateChanged(object? sender, EventArgs e) => RefreshPlayerSurfaceVisibility();

    private void InitializeTrayRecorder()
    {
        if (_automationRun || _trayIcon is not null) return;
        var menu = new System.Windows.Forms.ContextMenuStrip();
        _trayStatusItem = new System.Windows.Forms.ToolStripMenuItem("Background recorder ready") { Enabled = false };
        var openItem = new System.Windows.Forms.ToolStripMenuItem("Open OrbitalVue");
        openItem.Click += (_, _) => Dispatcher.BeginInvoke(OpenFromBackground);
        var dvrItem = new System.Windows.Forms.ToolStripMenuItem("Open DVR center");
        dvrItem.Click += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            OpenFromBackground();
            OpenDvrPanel();
        });
        _trayStopRecordingItem = new System.Windows.Forms.ToolStripMenuItem("Stop and save recording") { Enabled = false };
        _trayStopRecordingItem.Click += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!_dvrRecording.Snapshot.IsActive) return;
            var snapshot = _dvrRecording.Stop("Recording stopped and saved from the tray");
            ApplyDvrScheduleState(snapshot);
            UpdateDvrUi(snapshot);
        });
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit OrbitalVue");
        exitItem.Click += (_, _) => Dispatcher.BeginInvoke(ExitFromTray);
        menu.Items.AddRange([
            _trayStatusItem,
            new System.Windows.Forms.ToolStripSeparator(),
            openItem,
            dvrItem,
            _trayStopRecordingItem,
            new System.Windows.Forms.ToolStripSeparator(),
            exitItem
        ]);

        System.Drawing.Icon icon;
        try
        {
            icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty)
                   ?? System.Drawing.SystemIcons.Application;
        }
        catch
        {
            icon = System.Drawing.SystemIcons.Application;
        }

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "OrbitalVue • background recorder ready",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(OpenFromBackground);
    }

    private void HideToBackground(bool showNotice = true)
    {
        if (_hiddenToTray || _automationRun) return;
        CaptureSignalTelemetry(_playback?.GetSnapshot(), persist: true);
        SaveCurrentRecordingProgress(force: true);
        if (_isFullscreen) ExitFullscreen();
        _multiviewSession?.StopAll();
        _playback?.Stop();
        ShowInTaskbar = false;
        Hide();
        _hiddenToTray = true;
        UpdateTrayRecorderStatus(_dvrRecording.Snapshot);
        if (showNotice && !_trayNoticeShown && _trayIcon is not null)
        {
            _trayNoticeShown = true;
            _trayIcon.ShowBalloonTip(
                2500,
                "OrbitalVue recorder is still running",
                "Scheduled recordings and wake timers remain armed. Double-click the tray icon to reopen OrbitalVue.",
                System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void OpenFromBackground()
    {
        if (!_hiddenToTray)
        {
            Activate();
            return;
        }
        _hiddenToTray = false;
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        ShowActivated = true;
        Activate();
        RefreshPlayerSurfaceVisibility();
    }

    public void ActivateFromSecondaryInstance()
    {
        OpenFromBackground();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Topmost = true;
        Topmost = _isMiniPlayer && _settings.MiniPlayerAlwaysOnTop;
        Activate();
    }

    private void ExitFromTray()
    {
        var futureSchedules = (_settings.ScheduledRecordings ?? []).Count(recording =>
            (recording.Status is "Scheduled" or "Recovering") && recording.StopUtc > DateTimeOffset.UtcNow);
        if (!_dvrRecording.Snapshot.IsActive && futureSchedules > 0)
        {
            OpenFromBackground();
            var result = MessageBox.Show(
                this,
                $"{futureSchedules:N0} upcoming recording{(futureSchedules == 1 ? string.Empty : "s")} will not start after OrbitalVue exits.\n\nExit anyway?",
                "Exit background recorder",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return;
        }
        _allowFinalClose = true;
        Close();
    }

    private void UpdateTrayRecorderStatus(DvrRecordingSnapshot snapshot)
    {
        if (_trayIcon is null) return;
        var next = (_settings.ScheduledRecordings ?? [])
            .Where(recording => (recording.Status is "Scheduled" or "Recovering") && recording.StopUtc > DateTimeOffset.UtcNow)
            .OrderBy(recording => recording.Status == "Recovering" ? recording.NextRecoveryUtc ?? recording.StartUtc : recording.StartUtc)
            .FirstOrDefault();
        var status = snapshot.IsActive
            ? $"Recording • {snapshot.ChannelName}"
            : next is null
                ? "Background recorder ready"
                : $"Next • {next.ProgramTitle} at {next.GuideStartUtc.ToLocalTime():h:mm tt}";
        _trayIcon.Text = status.Length <= 63 ? status : status[..63];
        if (_trayStatusItem is not null) _trayStatusItem.Text = status;
        if (_trayStopRecordingItem is not null) _trayStopRecordingItem.Enabled = snapshot.IsActive;
    }

    private void RefreshDvrWakeTimer()
    {
        var preferences = _settings.SmartDvr ?? new SmartDvrPreferences();
        var now = DateTimeOffset.UtcNow;
        var next = DvrBackgroundPolicy.CreateWakePlan(_settings.ScheduledRecordings ?? [], preferences, now);
        var signature = next is null
            ? $"none:{preferences.BackgroundRecordingEnabled}:{preferences.WakeForRecordings}"
            : $"{next.ScheduleId:N}:{next.WakeUtc:O}:{preferences.BackgroundRecordingEnabled}:{preferences.WakeForRecordings}";
        if (signature == _dvrWakeSignature) return;
        _dvrWakeSignature = signature;
        if (!preferences.BackgroundRecordingEnabled || next is null)
        {
            _dvrWakeTimer.Cancel();
            return;
        }
        _dvrWakeTimer.Schedule(next.WakeUtc, next.ResumeSystem);
    }

    private void DvrWakeTimer_Triggered(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        _dvrWakeSignature = string.Empty;
        CheckScheduledRecordings();
        var snapshot = _dvrRecording.Poll(DateTimeOffset.UtcNow);
        ApplyDvrScheduleState(snapshot);
        UpdateDvrUi(snapshot);
        RefreshDvrWakeTimer();
    });

    private void Application_SessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        _allowFinalClose = true;
        if (!_dvrRecording.Snapshot.IsActive) return;
        var snapshot = _dvrRecording.Stop("Recording saved because Windows is shutting down");
        ApplyDvrScheduleState(snapshot);
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) Maximize_Click(sender, e);
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (_isMiniPlayer)
        {
            ExitMiniPlayer();
            return;
        }
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        CaptureSignalTelemetry(_playback?.GetSnapshot(), persist: true);
        SaveCurrentRecordingProgress(force: true);
        PersistRecordingsFolder();
        if (!_allowFinalClose && !_automationRun && _settings.SmartDvr.BackgroundRecordingEnabled)
        {
            e.Cancel = true;
            HideToBackground();
            return;
        }
        if (_dvrRecording.Snapshot.IsActive)
        {
            var result = MessageBox.Show(
                this,
                $"OrbitalVue is recording {_dvrRecording.Snapshot.ChannelName}. Closing now will stop and save the recording.\n\nStop recording and close OrbitalVue?",
                "Recording in progress",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                _allowFinalClose = false;
                return;
            }
            var snapshot = _dvrRecording.Stop("Recording stopped when OrbitalVue closed");
            ApplyDvrScheduleState(snapshot);
            _ = _settingsStore.SaveAsync(_settings);
        }

        if (_isFullscreen) ExitFullscreen();
        using var mediaStopCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var mediaStop = EndCurrentMediaPlaybackReporting(
            _playback?.GetSnapshot(),
            mediaStopCancellation.Token);
        try
        {
            mediaStop.GetAwaiter().GetResult();
        }
        catch
        {
            // App shutdown must not be held open by an unavailable media server.
        }
        _mediaCenterSource.CancelAllPlaybackReportingSessions();
        ResetArtworkLoading();
        if (!_automationRun)
            _sessionRecoveryService.CompleteAsync(CreateSessionSnapshot()).GetAwaiter().GetResult();
        _telemetryTimer.Stop();
        _fullscreenChromeTimer.Stop();
        _sleepTimer.Stop();
        Mouse.OverrideCursor = null;
        _loadCancellation?.Cancel();
        _updateCancellation?.Cancel();
        CancelMediaPlaybackResolution(clearResume: true);
        _guideCancellation?.Cancel();
        CancelPlexAccountSignInCore("Plex account sign-in was canceled because OrbitalVue is closing.");
        _displayRefreshRate?.Dispose();
        VideoSurface.MediaPlayer = null;
        MultiviewTiles.ItemsSource = null;
        _multiviewSession?.Dispose();
        _playback?.Dispose();
        _dvrRecording.Dispose();
        _recordingPowerGuard.Dispose();
        Application.Current.SessionEnding -= Application_SessionEnding;
        _dvrWakeTimer.Triggered -= DvrWakeTimer_Triggered;
        _dvrWakeTimer.Dispose();
        _premiumStore.StateChanged -= PremiumStore_StateChanged;
        _premiumStore.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private static string SafeErrorMessage(Exception exception) => exception switch
    {
        FileNotFoundException => "That playlist file could not be found. Choose it again.",
        UnauthorizedAccessException => "Windows denied access to that playlist file.",
        HttpRequestException http when http.StatusCode is not null => $"The provider returned HTTP {(int)http.StatusCode.Value}. Check the account or URL.",
        HttpRequestException => "The provider could not be reached. Check the URL and network connection.",
        ArgumentException argument => argument.Message,
        InvalidDataException invalidData => invalidData.Message,
        _ => "The source could not be read. Verify the address or file and try again."
    };

    private static string SafeGuideErrorMessage(Exception exception) => exception switch
    {
        FileNotFoundException => "The XMLTV file could not be found.",
        UnauthorizedAccessException => "Windows denied access to the XMLTV file.",
        HttpRequestException http when http.StatusCode is not null => $"The guide source returned HTTP {(int)http.StatusCode.Value}.",
        HttpRequestException => "The guide source could not be reached.",
        InvalidDataException invalidData => invalidData.Message,
        ArgumentException argument => argument.Message,
        _ => "The guide data could not be read."
    };

    private static string SafeRecordingErrorMessage(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Windows denied access to the recordings folder. Choose another folder.",
        DirectoryNotFoundException => "The recordings folder is no longer available. Choose it again.",
        PathTooLongException => "The recordings folder path is too long. Choose a shorter location.",
        IOException => "Windows could not write the recording. Check the drive and available space.",
        UriFormatException => "That channel has an invalid stream address and cannot be recorded.",
        ArgumentException argument => argument.Message,
        InvalidOperationException invalid => invalid.Message,
        Win32Exception => "Windows could not open the recordings folder.",
        _ => "The live channel could not be recorded."
    };

    private static string SafeUpdateErrorMessage(Exception exception) => exception switch
    {
        HttpRequestException => "Check your internet connection and try again. OrbitalVue could not read the public release feed.",
        TaskCanceledException => "The update service took too long to respond. Try again in a moment.",
        UnauthorizedAccessException => "Windows blocked access to the update folder. Restart OrbitalVue normally and try again.",
        IOException => "Windows could not save the update package. Check free disk space and try again.",
        _ => "The current installation was left unchanged. Try again, or use the newest installer if the problem continues."
    };
}
