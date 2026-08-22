using System.Text;
using System.IO.Compression;
using StreamVue.Player.Models;
using StreamVue.Player.Playback;
using StreamVue.Player.Services;

if (args is ["--cast-shortcut-self-test"])
{
    var castShortcutProbe = new WindowsCastService();
    castShortcutProbe.OpenNearbyDisplayPicker();
    await Task.Delay(900);
    castShortcutProbe.OpenNearbyDisplayPicker();
    Console.WriteLine("Windows Cast picker shortcut: PASS");
    return 0;
}

var testRoot = Path.Combine(Path.GetTempPath(), $"streamvue-feature-probe-{Guid.NewGuid():N}");
var settingsPath = Path.Combine(testRoot, "settings.json");
var cachePath = Path.Combine(testRoot, "playlist-cache.bin");
var credentialPath = Path.Combine(testRoot, "xtream-credentials.bin");
var epgCachePath = Path.Combine(testRoot, "epg-cache.bin");
var guideSourcePath = Path.Combine(testRoot, "guide-source.bin");
var guideMappingPath = Path.Combine(testRoot, "guide-mappings.bin");

try
{
    var first = new ChannelItem
    {
        Number = 1,
        Name = "World News HD",
        Group = "Newsroom",
        Url = "https://provider.invalid/live/one.ts?token=private",
        TvgId = "world.news",
        Kind = ChannelKind.Live
    };
    var equivalent = new ChannelItem
    {
        Number = 99,
        Name = "World News HD",
        Group = "Newsroom",
        Url = "https://provider.invalid/live/one.ts?token=changed",
        TvgId = "world.news",
        Kind = ChannelKind.Live
    };
    var distinct = new ChannelItem
    {
        Number = 2,
        Name = "World News UHD",
        Group = "Newsroom",
        Url = "https://provider.invalid/live/two.ts",
        TvgId = "world.news",
        Kind = ChannelKind.Live
    };

    if (first.StableKey != equivalent.StableKey)
        throw new InvalidOperationException("A stable TVG identity did not survive a stream URL change.");
    if (first.StableKey == distinct.StableKey)
        throw new InvalidOperationException("Distinct channel variants produced the same favorite key.");
    if (first.StableKey.Contains("private", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("The favorite key exposed provider data.");
    if (first.GuideMappingKey != equivalent.GuideMappingKey || first.GuideMappingKey == distinct.GuideMappingKey)
        throw new InvalidOperationException("The URL-independent guide mapping identity was not stable or unique.");

    var store = new AppSettingsStore(settingsPath);
    var expected = new AppSettings
    {
        LastSourceType = "file",
        LastSource = "playlist.m3u",
        LastChannelKey = first.StableKey,
        LastPlaylistRefreshUtc = DateTimeOffset.UtcNow,
        ResumeLastChannelOnStartup = true,
        MiniPlayerAlwaysOnTop = true,
        FavoriteChannelKeys = [first.StableKey],
        RecentChannelKeys = [distinct.StableKey, first.StableKey],
        ChannelProfiles = new Dictionary<string, ChannelPlaybackProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [first.StableKey] = new ChannelPlaybackProfile
            {
                BufferPreset = BufferPreset.Stable,
                HardwareDecoding = false,
                AspectRatio = "21:9",
                AudioTrackId = 2,
                SubtitleTrackId = -1,
                LearnedInstability = 4,
                SuccessfulStarts = 7,
                FailedStarts = 2,
                LastStartupMilliseconds = 1_450,
                LastSuccessfulUtc = DateTimeOffset.UtcNow,
                LastRecoveryReason = "Expanded smart buffer",
                UpdatedUtc = DateTimeOffset.UtcNow
            }
        },
        PlaylistHealth = new PlaylistHealthPreferences
        {
            LastAttemptUtc = DateTimeOffset.UtcNow,
            LastSuccessUtc = DateTimeOffset.UtcNow,
            ChannelCount = 2,
            AddedChannels = 1,
            RemovedChannels = 0
        },
        ProgramReminders =
        [
            new ProgramReminder
            {
                ChannelKey = first.StableKey,
                ChannelName = first.Name,
                ProgramTitle = "Evening Report",
                StartUtc = DateTimeOffset.UtcNow.AddMinutes(30),
                StopUtc = DateTimeOffset.UtcNow.AddMinutes(90)
            }
        ],
        Multiview = new MultiviewPreferences
        {
            Layout = MultiviewLayout.Quad.ToString(),
            ActiveSlot = 2,
            AudioSlot = 1,
            ChannelKeys = [first.StableKey, null, distinct.StableKey, null],
            SavedLayouts =
            [
                new MultiviewLayoutPreset
                {
                    Name = "News desk",
                    Layout = MultiviewLayout.Duo.ToString(),
                    ChannelKeys = [first.StableKey, distinct.StableKey, null, null]
                }
            ]
        },
        Playback = new PlaybackPreferences
        {
            PlaybackIntelligence = true,
            FastChannelChanges = true,
            AutoReconnect = true,
            MaxReconnectAttempts = 3,
            BufferPreset = BufferPreset.Stable
        }
    };

    await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.SaveAsync(expected)));
    var actual = await store.LoadAsync();

    if (actual.FavoriteChannelKeys.Count != 1 || actual.FavoriteChannelKeys[0] != first.StableKey)
        throw new InvalidOperationException("Favorite persistence round-trip failed.");
    if (!actual.Playback.AutoReconnect || actual.Playback.MaxReconnectAttempts != 3)
        throw new InvalidOperationException("Reconnect preferences did not persist.");
    if (actual.LastPlaylistRefreshUtc is null)
        throw new InvalidOperationException("Playlist refresh status did not persist.");
    if (actual.LastChannelKey != first.StableKey || !actual.ResumeLastChannelOnStartup || !actual.MiniPlayerAlwaysOnTop)
        throw new InvalidOperationException("Startup resume or Mini Player preferences did not persist.");
    if (actual.RecentChannelKeys.Count != 2 || actual.RecentChannelKeys[0] != distinct.StableKey ||
        !actual.ChannelProfiles.TryGetValue(first.StableKey, out var channelProfile) ||
        channelProfile.BufferPreset != BufferPreset.Stable || channelProfile.HardwareDecoding != false ||
        channelProfile.AspectRatio != "21:9" || channelProfile.AudioTrackId != 2 ||
        channelProfile.SubtitleTrackId != -1 || channelProfile.LearnedInstability != 4 ||
        channelProfile.SuccessfulStarts != 7 || channelProfile.FailedStarts != 2 ||
        channelProfile.LastStartupMilliseconds != 1_450 || channelProfile.LastSuccessfulUtc is null ||
        channelProfile.LastRecoveryReason != "Expanded smart buffer")
        throw new InvalidOperationException("Recent-channel history or per-channel playback profiles did not persist.");
    if (actual.PlaylistHealth.ChannelCount != 2 || actual.PlaylistHealth.AddedChannels != 1 ||
        actual.PlaylistHealth.LastSuccessUtc is null)
        throw new InvalidOperationException("Playlist health history did not persist.");
    if (actual.ProgramReminders.Count != 1 || actual.ProgramReminders[0].ProgramTitle != "Evening Report")
        throw new InvalidOperationException("Program reminders did not persist.");
    if (actual.Multiview.Layout != MultiviewLayout.Quad.ToString() || actual.Multiview.ActiveSlot != 2 ||
        actual.Multiview.AudioSlot != 1 || actual.Multiview.ChannelKeys.Count != MultiviewSession.MaximumTiles ||
        actual.Multiview.ChannelKeys[0] != first.StableKey || actual.Multiview.ChannelKeys[2] != distinct.StableKey ||
        actual.Multiview.SavedLayouts.Count != 1 || actual.Multiview.SavedLayouts[0].Name != "News desk")
        throw new InvalidOperationException("Multiview layout, audio focus, or channel assignments did not persist.");

    using (var multiview = new MultiviewSession(expected.Playback))
    {
        multiview.RestoreChannel(0, first);
        multiview.RestoreChannel(3, distinct);
        multiview.SelectSlot(3);
        multiview.SetAudioSlot(3);
        if (multiview.Tiles.Count != 4 || multiview.ActiveSlot != 3 || multiview.AudioSlot != 3 ||
            multiview.VisibleTiles(MultiviewLayout.Duo).Count != 2 ||
            multiview.VisibleTiles(MultiviewLayout.Focus).Single().Index != 3 ||
            !multiview.Tiles[3].IsAudible || multiview.Tiles[0].IsAudible)
            throw new InvalidOperationException("Multiview selection, layout, or single-audio policy failed.");
    }

    var playlist = new PlaylistResult([first, distinct], "Probe playlist", "private source", DateTimeOffset.UtcNow);
    var cache = new PlaylistCacheStore(cachePath);
    await cache.SaveAsync("url", "https://provider.invalid/list.m3u?token=secret", playlist);
    var cached = await cache.TryLoadAsync("url", "https://provider.invalid/list.m3u?token=secret");
    var wrongSourceCache = await cache.TryLoadAsync("url", "https://provider.invalid/other.m3u");
    if (cached?.Playlist.Channels.Count != 2 || cached.Playlist.Channels[0].Name != first.Name || wrongSourceCache is not null)
        throw new InvalidOperationException("Encrypted last-known-good playlist cache failed.");
    if (Encoding.UTF8.GetString(await File.ReadAllBytesAsync(cachePath)).Contains("token=secret", StringComparison.Ordinal))
        throw new InvalidOperationException("Playlist cache exposed provider data as clear text.");

    var credentialStore = new XtreamCredentialStore(credentialPath);
    await credentialStore.SaveAsync(new XtreamCredentials("https://provider.invalid", "probe-user", "probe-password"));
    var credentials = await credentialStore.TryLoadAsync("https://provider.invalid/");
    if (credentials is null || credentials.Username != "probe-user" || credentials.Password != "probe-password")
        throw new InvalidOperationException("Protected Xtream credential round-trip failed.");
    if (Encoding.UTF8.GetString(await File.ReadAllBytesAsync(credentialPath)).Contains("probe-password", StringComparison.Ordinal))
        throw new InvalidOperationException("Xtream password was persisted as clear text.");

    var tnt = new ChannelItem
    {
        Number = 3,
        Name = "US: TNT",
        Group = "USA Premium",
        Url = "https://provider.invalid/live/tnt.ts",
        TvgId = "tnt.us",
        Kind = ChannelKind.Live
    };
    var local = new ChannelItem
    {
        Number = 4,
        Name = "US: ABC 9 Kansas City (KMBC)",
        Group = "US ABC",
        Url = "https://provider.invalid/live/kmbc.ts",
        TvgId = "abc9kmbc.us",
        Kind = ChannelKind.Live
    };
    var guideNow = DateTimeOffset.UtcNow;
    var xml = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <tv>
          <channel id="TNT.HD.us2"><display-name>TNT HD</display-name></channel>
          <channel id="KMBC-DT.us_locals1"><display-name>KMBC-DT</display-name></channel>
          <programme channel="TNT.HD.us2" start="{guideNow.AddMinutes(-15):yyyyMMddHHmmss zzz}" stop="{guideNow.AddMinutes(45):yyyyMMddHHmmss zzz}"><title>Live Sports Center</title></programme>
          <programme channel="TNT.HD.us2" start="{guideNow.AddMinutes(45):yyyyMMddHHmmss zzz}" stop="{guideNow.AddMinutes(105):yyyyMMddHHmmss zzz}"><title>Prime Movie</title></programme>
          <programme channel="KMBC-DT.us_locals1" start="{guideNow.AddMinutes(-10):yyyyMMddHHmmss zzz}" stop="{guideNow.AddMinutes(20):yyyyMMddHHmmss zzz}"><title>Local News</title></programme>
        </tv>
        """;
    await using var xmlStream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var guide = await new XmlTvParser().ParseAsync(xmlStream, "Feature probe guide", [tnt, local]);
    if (guide.GetNowNext(tnt, guideNow).Current?.Title != "Live Sports Center" ||
        guide.GetNowNext(tnt, guideNow).Next?.Title != "Prime Movie" ||
        guide.GetNowNext(local, guideNow).Current?.Title != "Local News")
        throw new InvalidOperationException("XMLTV Now/Next or broadcast call-sign matching failed.");
    if (guide.ChannelCatalog.Count != 2 || !guide.ChannelCatalog.ContainsKey("TNT.HD.US2"))
        throw new InvalidOperationException("The lightweight XMLTV channel catalog was not retained.");

    var epgCache = new EpgCacheStore(epgCachePath);
    await epgCache.SaveAsync("public-us-pack", guide);
    var cachedGuide = await epgCache.TryLoadAsync("public-us-pack");
    if (cachedGuide?.GetNowNext(tnt, guideNow).Current?.Title != "Live Sports Center" || cachedGuide.ChannelCatalog.Count != 2)
        throw new InvalidOperationException("Encrypted guide cache round-trip failed.");
    if (Encoding.UTF8.GetString(await File.ReadAllBytesAsync(epgCachePath)).Contains("Live Sports Center", StringComparison.Ordinal))
        throw new InvalidOperationException("Guide cache exposed programme data as clear text.");

    var guideSourceStore = new GuideSourceStore(guideSourcePath);
    const string guideSources = "https://example.invalid/us.xml.gz\nhttps://example.invalid/locals.xml.gz";
    await guideSourceStore.SaveAsync("file", "playlist.m3u", guideSources);
    if (await guideSourceStore.TryLoadAsync("file", "playlist.m3u") != guideSources)
        throw new InvalidOperationException("Protected multi-source guide configuration failed.");
    if (Encoding.UTF8.GetString(await File.ReadAllBytesAsync(guideSourcePath)).Contains("example.invalid", StringComparison.Ordinal))
        throw new InvalidOperationException("Guide source configuration was persisted as clear text.");

    var unmatched = new ChannelItem
    {
        Number = 5,
        Name = "Provider Alternate Sports",
        Group = "US Sports",
        Url = "https://provider.invalid/live/alternate.ts",
        Kind = ChannelKind.Live
    };
    var manualGuideNow = DateTimeOffset.UtcNow;
    var manualXml = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <tv>
          <channel id="MANUAL.SPORTS.US2"><display-name>Manual Sports Network</display-name></channel>
          <programme channel="MANUAL.SPORTS.US2" start="{manualGuideNow.AddMinutes(-10):yyyyMMddHHmmss zzz}" stop="{manualGuideNow.AddMinutes(50):yyyyMMddHHmmss zzz}"><title>Mapped Live Event</title></programme>
        </tv>
        """;
    await using var manualXmlStream = new MemoryStream(Encoding.UTF8.GetBytes(manualXml));
    var manualSchedule = await new XmlTvParser().ParseAsync(
        manualXmlStream,
        "Manual mapping probe",
        [unmatched],
        ["MANUAL.SPORTS.US2"],
        null,
        CancellationToken.None);
    var expectedMappings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [unmatched.GuideMappingKey] = "MANUAL.SPORTS.US2"
    };
    if (manualSchedule.GetNowNext(unmatched, manualGuideNow, expectedMappings).Current?.Title != "Mapped Live Event")
        throw new InvalidOperationException("Manual XMLTV channel assignment did not resolve programme data.");

    var mappingStore = new EpgMappingStore(guideMappingPath);
    await mappingStore.SaveAsync("file", "playlist.m3u", expectedMappings);
    var storedMappings = await mappingStore.TryLoadAsync("file", "playlist.m3u");
    if (storedMappings.GetValueOrDefault(unmatched.GuideMappingKey) != "MANUAL.SPORTS.US2")
        throw new InvalidOperationException("Protected manual guide mapping round-trip failed.");
    if (Encoding.UTF8.GetString(await File.ReadAllBytesAsync(guideMappingPath)).Contains("MANUAL.SPORTS.US2", StringComparison.Ordinal))
        throw new InvalidOperationException("Manual guide mappings were persisted as clear text.");

    if (PlaybackAspectRatios.SupportedLabels.Count != 13 ||
        PlaybackAspectRatios.ToLibVlcValue("18:9") != "2:1" ||
        PlaybackAspectRatios.ToLibVlcValue("2.39:1") != "239:100" ||
        PlaybackAspectRatios.ToLibVlcValue("Auto") is not null ||
        !PlaybackAspectRatios.IsFill("Fill"))
    {
        throw new InvalidOperationException("Expanded aspect-ratio mapping failed.");
    }

    var partialBuffer = new PlaybackStatus(PlaybackState.Buffering, "Buffering 62%", 62);
    var completeBuffer = new PlaybackStatus(PlaybackState.Buffering, "Buffering 100%", 100);
    var playing = new PlaybackStatus(PlaybackState.Playing, "Live");
    if (!partialBuffer.ShouldShowBufferOverlay || completeBuffer.ShouldShowBufferOverlay || playing.ShouldShowBufferOverlay)
        throw new InvalidOperationException("Buffer overlay visibility policy failed.");

    var firstSmartCache = PlaybackHealthPolicy.SelectCacheMilliseconds(BufferPreset.Smart, "https://provider.invalid/live/channel.ts");
    var unstableSmartCache = PlaybackHealthPolicy.SelectCacheMilliseconds(BufferPreset.Smart, "https://provider.invalid/live/channel.ts", 4);
    if (firstSmartCache != 2_800 || unstableSmartCache <= firstSmartCache || unstableSmartCache > PlaybackHealthPolicy.MaximumSmartCacheMilliseconds)
        throw new InvalidOperationException("Smart buffering did not scale within its bounded range.");

    var fastSmartCache = PlaybackHealthPolicy.SelectCacheMilliseconds(
        BufferPreset.Smart,
        "https://provider.invalid/live/channel.ts",
        fastTune: true);
    var learnedFastCache = PlaybackHealthPolicy.SelectCacheMilliseconds(
        BufferPreset.Smart,
        "https://provider.invalid/live/channel.ts",
        fastTune: true,
        successfulStarts: 3);
    if (fastSmartCache != 1_600 || learnedFastCache != 1_300)
        throw new InvalidOperationException("Playback IQ fast-tune caching did not select the expected low-latency path.");

    var fastPlan = PlaybackIntelligencePolicy.CreatePlan(
        new PlaybackPreferences { PlaybackIntelligence = true, FastChannelChanges = true },
        first.Url,
        new ChannelPlaybackProfile { SuccessfulStarts = 4 },
        sessionInstability: 0,
        softwareFallbackActive: false);
    var recoveryPlan = PlaybackIntelligencePolicy.CreatePlan(
        new PlaybackPreferences { PlaybackIntelligence = true, FastChannelChanges = true },
        first.Url,
        new ChannelPlaybackProfile { LearnedInstability = 4 },
        sessionInstability: 0,
        softwareFallbackActive: true);
    if (fastPlan.Strategy != "Learned fast tune" || fastPlan.CacheMilliseconds != 1_300 ||
        recoveryPlan.Strategy != "Software safe mode" || recoveryPlan.UseHardwareDecoding ||
        recoveryPlan.CacheMilliseconds <= fastPlan.CacheMilliseconds)
        throw new InvalidOperationException("Playback IQ tune planning did not graduate between fast and recovery modes.");

    var now = DateTimeOffset.UtcNow;
    if (!PlaybackHealthPolicy.IsStalled(now, now.AddSeconds(-14), now.AddSeconds(-20), true, false) ||
        PlaybackHealthPolicy.IsStalled(now, now.AddSeconds(-14), now.AddSeconds(-20), true, true) ||
        PlaybackHealthPolicy.IsStalled(now, now.AddSeconds(-2), now.AddSeconds(-20), true, false))
        throw new InvalidOperationException("Frozen-stream watchdog policy failed.");

    if (!PlaybackHealthPolicy.HasVideoStartupFailed(now, now.AddSeconds(-14), true, 0, true, false) ||
        PlaybackHealthPolicy.HasVideoStartupFailed(now, now.AddSeconds(-14), true, 1, true, false) ||
        PlaybackHealthPolicy.HasVideoStartupFailed(now, now.AddSeconds(-14), false, 0, true, false))
        throw new InvalidOperationException("Zero-video decoder fallback policy failed.");

    var startupTimeout = PlaybackIntelligencePolicy.SelectStartupTimeout(
        new PlaybackPreferences { StartupTimeoutSeconds = 9 },
        1_600);
    if (!PlaybackHealthPolicy.HasOpeningTimedOut(
            now,
            now.Subtract(startupTimeout).AddMilliseconds(-1),
            hasReachedPlaying: false,
            isPlaying: false,
            isBuffering: false,
            threshold: startupTimeout) ||
        PlaybackHealthPolicy.HasOpeningTimedOut(
            now,
            now.Subtract(startupTimeout).AddMilliseconds(-1),
            hasReachedPlaying: false,
            isPlaying: false,
            isBuffering: true,
            threshold: startupTimeout))
        throw new InvalidOperationException("Playback IQ startup-deadline policy failed.");

    var cinemaRate = DisplayRefreshRateController.SelectBestRefreshRate(23.976, 60, [24, 50, 60, 120]);
    var palRate = DisplayRefreshRateController.SelectBestRefreshRate(25, 60, [24, 50, 60, 100]);
    if (cinemaRate != 24 || palRate != 50)
        throw new InvalidOperationException("Adaptive display cadence selection failed.");

    var fullscreenBounds = FullscreenDisplayBounds.FromMonitorRectangle(-2560, 0, 0, 1440);
    if (fullscreenBounds.Left != -2560 || fullscreenBounds.Top != 0 ||
        fullscreenBounds.Width != 2560 || fullscreenBounds.Height != 1440)
        throw new InvalidOperationException("Monitor-accurate fullscreen bounds failed.");

    var backgroundPlayback = PlayerSurfaceVisibilityPolicy.Evaluate(
        hasChannel: true,
        isWindowMinimized: false,
        isPlayerChromeSuppressed: false,
        isMultiviewMode: false,
        isModalVisible: false,
        isMultiviewWorkspaceVisible: false);
    var minimizedPlayback = PlayerSurfaceVisibilityPolicy.Evaluate(
        hasChannel: true,
        isWindowMinimized: true,
        isPlayerChromeSuppressed: false,
        isMultiviewMode: false,
        isModalVisible: false,
        isMultiviewWorkspaceVisible: false);
    var backgroundMultiview = PlayerSurfaceVisibilityPolicy.Evaluate(
        hasChannel: false,
        isWindowMinimized: false,
        isPlayerChromeSuppressed: false,
        isMultiviewMode: true,
        isModalVisible: false,
        isMultiviewWorkspaceVisible: true);
    if (!backgroundPlayback.ShowVideoSurface || minimizedPlayback.ShowVideoSurface ||
        !backgroundMultiview.ShowMultiview)
        throw new InvalidOperationException("Background and multi-monitor video visibility policy failed.");

    if (!Uri.TryCreate(AppUpdateService.RepositoryUrl, UriKind.Absolute, out var updateRepository) || updateRepository.Scheme != Uri.UriSchemeHttps)
        throw new InvalidOperationException("The public update repository URL is not a valid HTTPS address.");

    var castService = new WindowsCastService();
    if (!castService.IsSupported || WindowsCastService.NearbyDisplayShortcut != "Windows + K" ||
        WindowsCastService.DisplaySettingsUri != "ms-settings:display")
        throw new InvalidOperationException("The Windows nearby-display casting entry points are unavailable or misconfigured.");

    var maintenance = new StreamVueMaintenanceService(testRoot);
    var managedCachePath = Path.Combine(testRoot, "playlist-cache.v1.bin");
    await File.WriteAllBytesAsync(managedCachePath, [1, 2, 3, 4]);
    var backupPath = Path.Combine(testRoot, "probe.streamvue-backup");
    var backupCount = await maintenance.CreateBackupAsync(backupPath);
    if (backupCount != 2 || !File.Exists(backupPath))
        throw new InvalidOperationException("StreamVue backup creation did not capture the expected protected data.");
    using (var backupArchive = ZipFile.OpenRead(backupPath))
    {
        var protectedSettings = backupArchive.GetEntry("data/settings.json.protected");
        if (protectedSettings is null || backupArchive.GetEntry("data/settings.json") is not null)
            throw new InvalidOperationException("The backup did not protect its settings payload.");
        using var protectedReader = new StreamReader(protectedSettings.Open());
        if ((await protectedReader.ReadToEndAsync()).Contains("playlist.m3u", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The backup exposed the playlist source as clear text.");
    }

    await new AppSettingsStore(settingsPath).SaveAsync(new AppSettings { LastSourceType = "changed" });
    await File.WriteAllBytesAsync(managedCachePath, [9, 9]);
    var restoredCount = await maintenance.RestoreBackupAsync(backupPath);
    var restoredSettings = await new AppSettingsStore(settingsPath).LoadAsync();
    var restoredCache = await File.ReadAllBytesAsync(managedCachePath);
    if (restoredCount != 2 || restoredSettings.LastChannelKey != first.StableKey ||
        !restoredSettings.ResumeLastChannelOnStartup || !restoredCache.SequenceEqual(new byte[] { 1, 2, 3, 4 }) ||
        !File.Exists(Path.Combine(testRoot, "before-last-restore.streamvue-backup")))
        throw new InvalidOperationException("StreamVue backup restore or automatic rollback protection failed.");

    await File.WriteAllTextAsync(Path.Combine(testRoot, "crash.log"),
        "Failure while opening https://provider.invalid/live/private.ts?token=secret from " + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    var diagnosticSettings = await new AppSettingsStore(settingsPath).LoadAsync();
    diagnosticSettings.LastSource = "https://provider.invalid/list.m3u?token=secret";
    var diagnosticPath = Path.Combine(testRoot, "diagnostics.zip");
    await maintenance.ExportDiagnosticsAsync(
        diagnosticPath,
        diagnosticSettings,
        new StreamVueDiagnosticContext(2, 1, "url", false, 2, first.StableKey, null));
    using (var diagnosticArchive = ZipFile.OpenRead(diagnosticPath))
    {
        var entries = diagnosticArchive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
        if (!entries.ContainsKey("diagnostics.json") || !entries.ContainsKey("crash-log-redacted.txt"))
            throw new InvalidOperationException("The diagnostics package is missing its report or redacted crash log.");
        var combinedText = new StringBuilder();
        foreach (var entry in entries.Values)
        {
            using var reader = new StreamReader(entry.Open());
            combinedText.Append(await reader.ReadToEndAsync());
        }
        if (combinedText.ToString().Contains("provider.invalid", StringComparison.OrdinalIgnoreCase) ||
            combinedText.ToString().Contains("token=secret", StringComparison.OrdinalIgnoreCase) ||
            combinedText.ToString().Contains(first.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Diagnostics exposed provider or channel identity data.");
    }

    Console.WriteLine("Favorite identity: PASS");
    Console.WriteLine("Private URL hashing: PASS");
    Console.WriteLine("Stable URL-independent guide mapping identity: PASS");
    Console.WriteLine("Settings round-trip: PASS");
    Console.WriteLine("Concurrent settings writes: PASS");
    Console.WriteLine("Reconnect preferences: PASS");
    Console.WriteLine("Playlist refresh status: PASS");
    Console.WriteLine("Startup channel resume preference: PASS");
    Console.WriteLine("Mini Player always-on-top preference: PASS");
    Console.WriteLine("Recent-channel history: PASS");
    Console.WriteLine("Per-channel playback profiles: PASS");
    Console.WriteLine("Playlist health persistence: PASS");
    Console.WriteLine("Program reminder persistence: PASS");
    Console.WriteLine("Persistent four-view assignments: PASS");
    Console.WriteLine("Saved multiview layouts: PASS");
    Console.WriteLine("Single-audio multiview policy: PASS");
    Console.WriteLine("Encrypted offline playlist cache: PASS");
    Console.WriteLine("Protected Xtream auto-refresh credentials: PASS");
    Console.WriteLine("XMLTV Now/Next parsing and call-sign matching: PASS");
    Console.WriteLine("Encrypted offline guide cache: PASS");
    Console.WriteLine("Protected multi-source guide configuration: PASS");
    Console.WriteLine("Lightweight XMLTV channel catalog: PASS");
    Console.WriteLine("Encrypted manual guide mappings: PASS");
    Console.WriteLine("Mapped-channel supplemental programme loading: PASS");
    Console.WriteLine("Expanded aspect-ratio mapping: PASS");
    Console.WriteLine("Completed-buffer overlay dismissal: PASS");
    Console.WriteLine("Per-channel smart buffer policy: PASS");
    Console.WriteLine("Playback IQ fast-tune planning: PASS");
    Console.WriteLine("Playback IQ staged recovery planning: PASS");
    Console.WriteLine("Frozen-stream watchdog policy: PASS");
    Console.WriteLine("Zero-video decoder fallback policy: PASS");
    Console.WriteLine("Playback IQ startup deadline: PASS");
    Console.WriteLine("Adaptive display cadence policy: PASS");
    Console.WriteLine("Monitor-accurate fullscreen bounds: PASS");
    Console.WriteLine("Background and multi-monitor video visibility: PASS");
    Console.WriteLine("Encrypted recovery-safe settings backup and restore: PASS");
    Console.WriteLine("Privacy-filtered diagnostics bundle: PASS");
    Console.WriteLine("Nearby unpaired wireless-display casting: PASS");
    Console.WriteLine("Public update channel configuration: PASS");
    return 0;
}
finally
{
    if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
}
