using System.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LibVLCSharp.Shared;
using NSec.Cryptography;
using StreamVue.Player.Models;
using StreamVue.Player.Playback;
using StreamVue.Player.Services;

if (args is ["--cast-shortcut-self-test"])
{
    try
    {
        var castShortcutProbe = new WindowsCastService();
        castShortcutProbe.OpenNearbyDisplayPicker();
        await Task.Delay(900);
        castShortcutProbe.OpenNearbyDisplayPicker();
        Console.WriteLine("Windows Cast picker shortcut: PASS");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Windows Cast picker shortcut: FAIL - {exception.Message}");
        return 1;
    }
}

if (args is ["--dvr-self-test"])
{
    try
    {
        return await RunDvrSelfTestAsync();
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Live DVR transport-stream recording: FAIL - {exception.Message}");
        return 1;
    }
}

var testRoot = Path.Combine(Path.GetTempPath(), $"streamvue-feature-probe-{Guid.NewGuid():N}");
var settingsPath = Path.Combine(testRoot, "settings.json");
var cachePath = Path.Combine(testRoot, "playlist-cache.bin");
var sourceCacheDirectory = Path.Combine(testRoot, "playlist-caches.v2");
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

    RunPremiumAccessSelfTest();
    await RunMicrosoftStorePremiumSelfTestAsync();
    await RunMediaCenterSelfTestAsync(testRoot);

    var store = new AppSettingsStore(settingsPath);
    var seriesRuleId = Guid.NewGuid();
    var playlistSourceId = Guid.NewGuid();
    var expected = new AppSettings
    {
        LastSourceType = "file",
        LastSource = "playlist.m3u",
        PlaylistSources =
        [
            new PlaylistSourceDefinition
            {
                Id = playlistSourceId,
                Name = "Primary lineup",
                SourceType = "file",
                SourceValue = "playlist.m3u",
                LastAttemptUtc = DateTimeOffset.UtcNow,
                LastSuccessUtc = DateTimeOffset.UtcNow,
                ChannelCount = 2
            }
        ],
        LastChannelKey = first.StableKey,
        LastPlaylistRefreshUtc = DateTimeOffset.UtcNow,
        ResumeLastChannelOnStartup = true,
        MiniPlayerAlwaysOnTop = true,
        RestoreUnexpectedSession = true,
        Updates = new AppUpdatePreferences
        {
            Channel = AppUpdateChannel.Stable,
            AutomaticRollback = true
        },
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
        SignalRouting = new SignalRoutingPreferences
        {
            Enabled = true,
            AutomaticFailover = true,
            MaximumAutomaticSwitches = 4,
            FeedHealth = new Dictionary<string, SignalFeedHealth>(StringComparer.OrdinalIgnoreCase)
            {
                [first.StableKey] = new SignalFeedHealth
                {
                    LogicalChannelKey = "route-probe",
                    ChannelName = first.Name,
                    SourceName = "Primary",
                    Preference = SignalFeedPreference.Preferred,
                    SuccessfulStarts = 9,
                    FailedStarts = 1,
                    BufferEvents = 2,
                    LastStartupMilliseconds = 1_120,
                    LastResolutionHeight = 1080,
                    LastInputBitrateMbps = 7.4,
                    LastSuccessUtc = DateTimeOffset.UtcNow
                }
            },
            ManualRoutes =
            [
                new ManualSignalRoute { Name = "News route", FeedKeys = [first.StableKey, distinct.StableKey] }
            ],
            SeparatedFeedKeys = ["SEPARATED-PROBE"]
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
        RecordingsFolder = Path.Combine(testRoot, "recordings"),
        SmartDvr = new SmartDvrPreferences
        {
            StartPaddingMinutes = 5,
            EndPaddingMinutes = 10,
            StorageReserveGigabytes = 20,
            DefaultPriority = DvrSchedulePriority.High,
            BackgroundRecordingEnabled = true,
            WakeForRecordings = true,
            LiveTimeshiftEnabled = true,
            LiveTimeshiftMinutes = 120,
            MaximumRecoveryAttempts = 5,
            DefaultEpisodeSelection = DvrEpisodeSelection.NewEpisodesOnly,
            DefaultKeepLatestCount = 5
        },
        SeriesRecordingRules =
        [
            new SeriesRecordingRule
            {
                Id = seriesRuleId,
                ChannelKey = first.StableKey,
                ChannelName = first.Name,
                ProgramTitle = "Late Report",
                Priority = DvrSchedulePriority.High,
                StartPaddingMinutes = 5,
                EndPaddingMinutes = 10,
                EpisodeSelection = DvrEpisodeSelection.NewEpisodesOnly,
                KeepLatestCount = 3,
                AnyChannel = true
            }
        ],
        ScheduledRecordings =
        [
            new ScheduledRecording
            {
                ChannelKey = first.StableKey,
                ChannelName = first.Name,
                ProgramTitle = "Late Report",
                StartUtc = DateTimeOffset.UtcNow.AddHours(2),
                StopUtc = DateTimeOffset.UtcNow.AddHours(3),
                Priority = DvrSchedulePriority.High,
                SeriesRuleId = seriesRuleId,
                EpisodeKey = "LATE REPORT|S2:E5",
                EpisodeLabel = "S02E05",
                IsNewEpisode = true,
                RecoveryAttempts = 1,
                NextRecoveryUtc = DateTimeOffset.UtcNow.AddHours(2),
                OutputPaths = [Path.Combine(testRoot, "recordings", "segment-1.ts")]
            }
        ],
        RecordingPlaybackProgress = new Dictionary<string, DvrPlaybackProgress>(StringComparer.OrdinalIgnoreCase)
        {
            ["RECORDING-PROBE"] = new DvrPlaybackProgress
            {
                PositionMilliseconds = 420_000,
                DurationMilliseconds = 3_600_000,
                UpdatedUtc = DateTimeOffset.UtcNow
            }
        },
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
    if (actual.PlaylistSources.Count != 1 || actual.PlaylistSources[0].Id != playlistSourceId ||
        actual.PlaylistSources[0].Name != "Primary lineup" || actual.PlaylistSources[0].ChannelCount != 2)
        throw new InvalidOperationException("Playlist source catalog did not persist.");
    if (actual.LastChannelKey != first.StableKey || !actual.ResumeLastChannelOnStartup || !actual.MiniPlayerAlwaysOnTop ||
        !actual.RestoreUnexpectedSession || actual.Updates.Channel != AppUpdateChannel.Stable || !actual.Updates.AutomaticRollback)
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
    if (!actual.SignalRouting.Enabled || !actual.SignalRouting.AutomaticFailover ||
        actual.SignalRouting.MaximumAutomaticSwitches != 4 ||
        !actual.SignalRouting.FeedHealth.TryGetValue(first.StableKey, out var persistedSignalHealth) ||
        persistedSignalHealth.Preference != SignalFeedPreference.Preferred || persistedSignalHealth.SuccessfulStarts != 9 ||
        persistedSignalHealth.FailedStarts != 1 || persistedSignalHealth.LastResolutionHeight != 1080 ||
        persistedSignalHealth.LastInputBitrateMbps != 7.4 || actual.SignalRouting.ManualRoutes.Count != 1 ||
        actual.SignalRouting.ManualRoutes[0].FeedKeys.Count != 2 || actual.SignalRouting.SeparatedFeedKeys.Count != 1)
        throw new InvalidOperationException("Smart signal preferences or private feed history did not persist.");
    if (actual.PlaylistHealth.ChannelCount != 2 || actual.PlaylistHealth.AddedChannels != 1 ||
        actual.PlaylistHealth.LastSuccessUtc is null)
        throw new InvalidOperationException("Playlist health history did not persist.");
    if (actual.ProgramReminders.Count != 1 || actual.ProgramReminders[0].ProgramTitle != "Evening Report")
        throw new InvalidOperationException("Program reminders did not persist.");
    if (actual.RecordingsFolder != Path.Combine(testRoot, "recordings") ||
        actual.ScheduledRecordings.Count != 1 || actual.ScheduledRecordings[0].ProgramTitle != "Late Report" ||
        actual.ScheduledRecordings[0].Status != "Scheduled" || actual.ScheduledRecordings[0].Priority != DvrSchedulePriority.High ||
        actual.SmartDvr.StartPaddingMinutes != 5 || actual.SmartDvr.EndPaddingMinutes != 10 ||
        actual.SmartDvr.StorageReserveGigabytes != 20 || actual.SmartDvr.DefaultPriority != DvrSchedulePriority.High ||
        !actual.SmartDvr.BackgroundRecordingEnabled || !actual.SmartDvr.WakeForRecordings ||
        !actual.SmartDvr.LiveTimeshiftEnabled || actual.SmartDvr.LiveTimeshiftMinutes != 120 ||
        actual.SmartDvr.MaximumRecoveryAttempts != 5 || actual.SmartDvr.DefaultEpisodeSelection != DvrEpisodeSelection.NewEpisodesOnly ||
        actual.SmartDvr.DefaultKeepLatestCount != 5 ||
        actual.SeriesRecordingRules.Count != 1 || actual.SeriesRecordingRules[0].Id != seriesRuleId ||
        actual.SeriesRecordingRules[0].EpisodeSelection != DvrEpisodeSelection.NewEpisodesOnly ||
        actual.SeriesRecordingRules[0].KeepLatestCount != 3 || !actual.SeriesRecordingRules[0].AnyChannel ||
        actual.ScheduledRecordings[0].EpisodeLabel != "S02E05" || actual.ScheduledRecordings[0].RecoveryAttempts != 1 ||
        actual.ScheduledRecordings[0].OutputPaths.Count != 1 ||
        !actual.RecordingPlaybackProgress.TryGetValue("RECORDING-PROBE", out var playbackProgress) ||
        playbackProgress.PositionMilliseconds != 420_000 || playbackProgress.DurationMilliseconds != 3_600_000)
        throw new InvalidOperationException("DVR folder, schedule, or recording resume position did not persist.");
    if (actual.Multiview.Layout != MultiviewLayout.Quad.ToString() || actual.Multiview.ActiveSlot != 2 ||
        actual.Multiview.AudioSlot != 1 || actual.Multiview.ChannelKeys.Count != MultiviewSession.MaximumTiles ||
        actual.Multiview.ChannelKeys[0] != first.StableKey || actual.Multiview.ChannelKeys[2] != distinct.StableKey ||
        actual.Multiview.SavedLayouts.Count != 1 || actual.Multiview.SavedLayouts[0].Name != "News desk")
        throw new InvalidOperationException("Multiview layout, audio focus, or channel assignments did not persist.");

    var legacySettingsPath = Path.Combine(testRoot, "legacy-settings.json");
    await File.WriteAllTextAsync(legacySettingsPath, """
        {
          "LastSourceType": "file",
          "ScheduledRecordings": []
        }
        """);
    var migratedSettings = await new AppSettingsStore(legacySettingsPath).LoadAsync();
    if (migratedSettings.SmartDvr is null || migratedSettings.SmartDvr.StartPaddingMinutes != 1 ||
        migratedSettings.SmartDvr.EndPaddingMinutes != 2 || migratedSettings.SeriesRecordingRules is null ||
        !migratedSettings.SmartDvr.BackgroundRecordingEnabled || !migratedSettings.SmartDvr.WakeForRecordings ||
        !migratedSettings.SmartDvr.LiveTimeshiftEnabled || migratedSettings.SmartDvr.LiveTimeshiftMinutes != 60 ||
        migratedSettings.SmartDvr.MaximumRecoveryAttempts != 3)
        throw new InvalidOperationException("Pre-3.6 settings did not receive safe background DVR and timeshift defaults.");

    var legacyCatalogSettings = new AppSettings
    {
        LastSourceType = " URL ",
        LastSource = "https://lineup.invalid/primary/"
    };
    if (!PlaylistSourcePolicy.NormalizeSettings(legacyCatalogSettings) || legacyCatalogSettings.PlaylistSources.Count != 1 ||
        legacyCatalogSettings.PlaylistSources[0].SourceType != "url" ||
        legacyCatalogSettings.PlaylistSources[0].SourceValue != "https://lineup.invalid/primary/" ||
        legacyCatalogSettings.PlaylistSources[0].Name != "lineup.invalid")
        throw new InvalidOperationException("The 3.6 playlist connection did not migrate into the 3.7 source catalog.");
    var migratedSource = legacyCatalogSettings.PlaylistSources[0];
    legacyCatalogSettings.PlaylistSources.Add(new PlaylistSourceDefinition
    {
        Id = Guid.NewGuid(),
        Name = "Duplicate",
        SourceType = "URL",
        SourceValue = "https://lineup.invalid/primary",
        SortOrder = 50
    });
    legacyCatalogSettings.PlaylistSources.Add(new PlaylistSourceDefinition
    {
        Id = migratedSource.Id,
        Name = "  Sports account  ",
        SourceType = "XTREAM",
        SourceValue = "sports.invalid/",
        SortOrder = 75
    });
    if (!PlaylistSourcePolicy.NormalizeSettings(legacyCatalogSettings) || legacyCatalogSettings.PlaylistSources.Count != 2 ||
        legacyCatalogSettings.PlaylistSources.Select(source => source.Id).Distinct().Count() != 2 ||
        legacyCatalogSettings.PlaylistSources.Select(source => source.SortOrder).SequenceEqual([0, 1]) == false ||
        legacyCatalogSettings.PlaylistSources[1].Name != "Sports account" ||
        PlaylistSourcePolicy.GetOrAdd(legacyCatalogSettings, "url", "https://lineup.invalid/primary/").Id != migratedSource.Id)
        throw new InvalidOperationException("Playlist source normalization, deduplication, or identity preservation failed.");

    var regional = new ChannelItem
    {
        Number = 41,
        Name = "Regional Weather",
        Group = "Local",
        Url = "https://regional.invalid/weather.ts",
        TvgId = "regional.weather",
        Kind = ChannelKind.Live
    };
    var primarySource = PlaylistSourcePolicy.Create("url", "https://primary.invalid/list.m3u", "Primary", 1);
    var regionalSource = PlaylistSourcePolicy.Create("file", Path.Combine(testRoot, "regional.m3u"), "Regional", 0);
    var disabledSource = PlaylistSourcePolicy.Create("url", "https://disabled.invalid/list.m3u", "Disabled", 2);
    disabledSource.IsEnabled = false;
    var merge = PlaylistMergePolicy.Merge(
    [
        new PlaylistSourceSnapshot(
            primarySource,
            new PlaylistResult([first, distinct], "Primary", "primary", DateTimeOffset.UtcNow, "https://guide.invalid/primary.xml")),
        new PlaylistSourceSnapshot(
            regionalSource,
            new PlaylistResult([first, regional], "Regional", "regional", DateTimeOffset.UtcNow.AddMinutes(1), "https://guide.invalid/regional.xml")),
        new PlaylistSourceSnapshot(
            disabledSource,
            new PlaylistResult([distinct], "Disabled", "disabled", DateTimeOffset.UtcNow.AddMinutes(2)))
    ]);
    if (merge.SourceCount != 2 || merge.InputChannelCount != 4 || merge.DuplicateChannelCount != 1 ||
        merge.Playlist.Channels.Count != 3 ||
        !merge.Playlist.Channels.Select(channel => channel.Number).SequenceEqual([1, 2, 3]) ||
        merge.Playlist.Channels[0].StableKey != first.StableKey ||
        merge.Playlist.Channels[0].SourceId != regionalSource.Id || merge.Playlist.Channels[0].SourceName != "Regional" ||
        merge.Playlist.Channels[2].SourceId != primarySource.Id ||
        merge.Playlist.GuideSource is null || !merge.Playlist.GuideSource.Contains("primary.xml", StringComparison.Ordinal) ||
        !merge.Playlist.GuideSource.Contains("regional.xml", StringComparison.Ordinal))
        throw new InvalidOperationException("Multi-source ordering, provenance, exact deduplication, or guide merging failed.");

    var alternateFeed = new ChannelItem
    {
        Number = 101,
        Name = "US: World News FHD (D)",
        Group = "International News",
        Url = "https://backup.invalid/world-news.m3u8",
        TvgId = "world.news.backup",
        Kind = ChannelKind.Live,
        SourceId = Guid.NewGuid(),
        SourceName = "Backup provider"
    };
    var unrelatedFeed = new ChannelItem
    {
        Number = 102,
        Name = "City News HD",
        Group = "Newsroom",
        Url = "https://backup.invalid/city-news.m3u8",
        TvgId = "city.news",
        Kind = ChannelKind.Live,
        SourceName = "Backup provider"
    };
    var signalRoutes = SmartSignalRoutingPolicy.BuildRoutes([first, alternateFeed, unrelatedFeed]);
    var worldNewsRoute = signalRoutes.Single(route => route.Feeds.Contains(first));
    if (signalRoutes.Count != 2 || worldNewsRoute.FeedCount != 2 ||
        worldNewsRoute.Representative != first || first.SignalFeedCount != 2 || !first.HasAlternateFeeds ||
        SmartSignalRoutingPolicy.CreateLogicalChannelKey(first) == SmartSignalRoutingPolicy.CreateLogicalChannelKey(unrelatedFeed))
        throw new InvalidOperationException(
            $"Equivalent-feed matching did not create a stable logical channel route " +
            $"(routes={signalRoutes.Count}, feeds={worldNewsRoute.FeedCount}, first={EpgSchedule.SignatureName(first.Name)}, alternate={EpgSchedule.SignatureName(alternateFeed.Name)})." );

    var tvgAliasRoutes = SmartSignalRoutingPolicy.BuildRoutes([
        new ChannelItem { Number = 201, Name = "Provider Label One", Group = "News", Url = "https://one.invalid/feed.ts", TvgId = "shared.channel", Kind = ChannelKind.Live },
        new ChannelItem { Number = 202, Name = "Completely Different Label", Group = "International", Url = "https://two.invalid/feed.ts", TvgId = "shared.channel", Kind = ChannelKind.Live }
    ]);
    var scheduleVariantRoutes = SmartSignalRoutingPolicy.BuildRoutes([
        new ChannelItem { Number = 203, Name = "Premium Movies East", Group = "Movies", Url = "https://one.invalid/east.ts", TvgId = "premium.movies", Kind = ChannelKind.Live },
        new ChannelItem { Number = 204, Name = "Premium Movies West", Group = "Movies", Url = "https://two.invalid/west.ts", TvgId = "premium.movies", Kind = ChannelKind.Live }
    ]);
    var numberedChannelRoutes = SmartSignalRoutingPolicy.BuildRoutes([
        new ChannelItem { Number = 205, Name = "Local News 12 HD", Group = "Local", Url = "https://one.invalid/news-12.ts", TvgId = "local.news.12", Kind = ChannelKind.Live },
        new ChannelItem { Number = 206, Name = "Local News 12 FHD", Group = "Local", Url = "https://two.invalid/news-12.ts", TvgId = "backup.local.news.12", Kind = ChannelKind.Live },
        new ChannelItem { Number = 207, Name = "Local News 13 HD", Group = "Local", Url = "https://one.invalid/news-13.ts", TvgId = "local.news.13", Kind = ChannelKind.Live }
    ]);
    if (tvgAliasRoutes.Count != 1 || scheduleVariantRoutes.Count != 2 ||
        numberedChannelRoutes.Count != 2 || numberedChannelRoutes.Single(route => route.FeedCount == 2).Representative.Name != "Local News 12 HD")
        throw new InvalidOperationException("TVG aliases, numbered channels, or East/West schedules were not grouped safely.");

    var routingPreferences = new SignalRoutingPreferences();
    var primaryHealth = SmartSignalRoutingPolicy.GetOrCreateHealth(routingPreferences, first, worldNewsRoute.Key);
    primaryHealth.SuccessfulStarts = 1;
    primaryHealth.FailedStarts = 4;
    primaryHealth.BufferEvents = 12;
    primaryHealth.Reconnects = 5;
    primaryHealth.LastStartupMilliseconds = 8_500;
    primaryHealth.LastFailureUtc = DateTimeOffset.UtcNow;
    var backupHealth = SmartSignalRoutingPolicy.GetOrCreateHealth(routingPreferences, alternateFeed, worldNewsRoute.Key);
    backupHealth.SuccessfulStarts = 12;
    backupHealth.FailedStarts = 0;
    backupHealth.LastStartupMilliseconds = 850;
    backupHealth.LastResolutionHeight = 1080;
    backupHealth.LastInputBitrateMbps = 8.2;
    backupHealth.LastSuccessUtc = DateTimeOffset.UtcNow;
    if (SmartSignalRoutingPolicy.SelectBestFeed(worldNewsRoute, routingPreferences) != alternateFeed)
        throw new InvalidOperationException("Signal scoring did not favor the faster, more reliable feed.");
    primaryHealth.Preference = SignalFeedPreference.Preferred;
    if (SmartSignalRoutingPolicy.SelectBestFeed(worldNewsRoute, routingPreferences) != first)
        throw new InvalidOperationException("A manually preferred feed did not take priority.");
    primaryHealth.Preference = SignalFeedPreference.Blocked;
    if (SmartSignalRoutingPolicy.SelectBestFeed(worldNewsRoute, routingPreferences) != alternateFeed)
        throw new InvalidOperationException("A Never use feed remained eligible for routing.");
    var attemptedFeeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { alternateFeed.StableKey };
    if (SmartSignalRoutingPolicy.SelectBestFeed(worldNewsRoute, routingPreferences, attemptedFeeds) is not null)
        throw new InvalidOperationException("Failover selection retried an attempted or blocked feed.");
    if (SmartSignalRoutingPolicy.ParseResolutionHeight("3840×2160") != 2160 ||
        SmartSignalRoutingPolicy.ParseResolutionHeight("1080p") != 1080)
        throw new InvalidOperationException("Signal quality resolution parsing failed.");

    var manualRouting = new SignalRoutingPreferences();
    SmartSignalRoutingPolicy.LinkFeedToRoute(manualRouting, worldNewsRoute, unrelatedFeed);
    var manuallyLinkedRoutes = SmartSignalRoutingPolicy.BuildRoutes([first, alternateFeed, unrelatedFeed], manualRouting);
    if (manuallyLinkedRoutes.Count != 1 || manuallyLinkedRoutes[0].FeedCount != 3)
        throw new InvalidOperationException("The manual Signal Route editor did not combine selected feeds.");
    if (!SmartSignalRoutingPolicy.ToggleFeedSeparation(manualRouting, unrelatedFeed))
        throw new InvalidOperationException("The manual Signal Route editor did not mark a feed as separate.");
    var manuallySeparatedRoutes = SmartSignalRoutingPolicy.BuildRoutes([first, alternateFeed, unrelatedFeed], manualRouting);
    if (manuallySeparatedRoutes.Count != 2 || manuallySeparatedRoutes.Single(route => route.Feeds.Contains(unrelatedFeed)).FeedCount != 1)
        throw new InvalidOperationException("A manually separated feed was regrouped automatically.");

    var catchupM3u = """
        #EXTM3U url-tvg="https://guide.invalid/guide.xml"
        #EXTINF:-1 tvg-id="archive.news" tvg-name="Archive News" group-title="News" catchup="default" catchup-days="7" catchup-correction="1" catchup-source="https://archive.invalid/replay?start={utc}&stop={utcend}&duration={duration}",Archive News
        https://live.invalid/archive-news.ts
        """;
    await using var catchupStream = new MemoryStream(Encoding.UTF8.GetBytes(catchupM3u));
    var catchupPlaylist = await new M3uPlaylistParser().ParseAsync(catchupStream, "Catch-up probe", "memory");
    var catchupChannel = catchupPlaylist.Channels.Single();
    var replayStart = DateTimeOffset.UtcNow.AddHours(-2);
    var replayProgramme = new EpgProgram(
        "archive.news", "Archived Bulletin", null, "News", replayStart, replayStart.AddMinutes(30));
    var replayChannel = CatchupReplayPolicy.CreateReplayChannel(catchupChannel, replayProgramme);
    if (!catchupChannel.HasCatchup || catchupChannel.CatchupDays != 7 || catchupChannel.CatchupCorrectionMinutes != 60 ||
        replayChannel.Kind != ChannelKind.Replay || !replayChannel.Url.Contains("duration=1800", StringComparison.Ordinal) ||
        replayChannel.Url.Contains("{utc}", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("M3U catch-up metadata or replay URL expansion failed.");

    var catchupHealth = ChannelHealthPolicy.Analyze(
        SmartSignalRoutingPolicy.BuildRoutes([catchupChannel]),
        new SignalRoutingPreferences(),
        _ => true);
    if (catchupHealth.ReplayReady != 1 || catchupHealth.Rows.Single().HasReplay == false || catchupHealth.MissingLogo != 1)
        throw new InvalidOperationException("Channel Health Center replay or metadata analysis failed.");

    var sessionJournalPath = Path.Combine(testRoot, "session-journal.json");
    var interruptedService = new SessionRecoveryService(sessionJournalPath);
    var sessionStart = new SessionRecoverySnapshot(
        true, Guid.Empty, DateTimeOffset.UtcNow, first.StableKey, "Watch", string.Empty, string.Empty,
        "All", DateTimeOffset.UtcNow, true, false);
    if (await interruptedService.BeginAsync(sessionStart) is not null)
        throw new InvalidOperationException("A new session was incorrectly reported as interrupted.");
    await interruptedService.HeartbeatAsync(sessionStart with { Workspace = "Guide", GuideSearch = "evening" });
    var recoveringService = new SessionRecoveryService(sessionJournalPath);
    var recoveredSession = await recoveringService.BeginAsync(sessionStart);
    if (recoveredSession?.Workspace != "Guide" || recoveredSession.GuideSearch != "evening" || recoveredSession.ChannelKey != first.StableKey)
        throw new InvalidOperationException("Interrupted session state was not recovered from the journal.");
    await recoveringService.CompleteAsync(sessionStart);
    var cleanRestartService = new SessionRecoveryService(sessionJournalPath);
    if (await cleanRestartService.BeginAsync(sessionStart) is not null)
        throw new InvalidOperationException("A cleanly closed session was incorrectly recovered.");
    await cleanRestartService.CompleteAsync(sessionStart);

    var signalGuideNow = DateTimeOffset.UtcNow;
    var primaryProgramme = new EpgProgram("world.news", "World Report", null, "News", signalGuideNow.AddMinutes(-20), signalGuideNow.AddMinutes(10));
    var backupProgramme = new EpgProgram("world.news.alt", "World Report", null, "News", signalGuideNow.AddMinutes(-20), signalGuideNow.AddMinutes(10));
    var nextProgramme = new EpgProgram("world.news.alt", "Market Close", null, "News", signalGuideNow.AddMinutes(10), signalGuideNow.AddMinutes(40));
    var unifiedProgrammes = SmartSignalRoutingPolicy.MergeProgrammes([[primaryProgramme], [backupProgramme, nextProgramme]]);
    var unifiedNowNext = SmartSignalRoutingPolicy.GetNowNext(unifiedProgrammes, signalGuideNow);
    if (unifiedProgrammes.Count != 2 || unifiedNowNext.Current?.Title != "World Report" || unifiedNowNext.Next?.Title != "Market Close")
        throw new InvalidOperationException("Equivalent-feed guide listings were not unified.");

    var liveSource = PlaylistSourcePolicy.Create("url", "https://live.invalid/list.m3u", "Live source", 0);
    var cacheOnlySource = PlaylistSourcePolicy.Create("file", Path.Combine(testRoot, "cache-only.m3u"), "Cache only", 1);
    cacheOnlySource.RefreshOnStartup = false;
    var fallbackSource = PlaylistSourcePolicy.Create("url", "https://fallback.invalid/list.m3u", "Fallback source", 2);
    var failedSource = PlaylistSourcePolicy.Create("url", "https://failed.invalid/list.m3u", "Failed source", 3);
    var cachedSnapshots = new Dictionary<string, CachedPlaylist>(StringComparer.OrdinalIgnoreCase)
    {
        [cacheOnlySource.SourceValue] = new(
            new PlaylistResult([distinct], "Cache only", "encrypted cache", DateTimeOffset.UtcNow.AddMinutes(-8)),
            DateTimeOffset.UtcNow.AddMinutes(-8)),
        [fallbackSource.SourceValue] = new(
            new PlaylistResult([regional], "Fallback source", "encrypted cache", DateTimeOffset.UtcNow.AddMinutes(-5)),
            DateTimeOffset.UtcNow.AddMinutes(-5))
    };
    var liveRefreshCalls = new List<Guid>();
    var cacheWrites = new List<string>();
    var refreshService = new PlaylistSourceRefreshService(
        (source, _, _) =>
        {
            liveRefreshCalls.Add(source.Id);
            if (source.Id == liveSource.Id)
                return Task.FromResult(new PlaylistResult(
                    [first],
                    "Live source",
                    "provider",
                    DateTimeOffset.UtcNow));
            throw new HttpRequestException("Provider unavailable");
        },
        (_, sourceValue, _) => Task.FromResult(cachedSnapshots.GetValueOrDefault(sourceValue)),
        (_, sourceValue, _, _) =>
        {
            cacheWrites.Add(sourceValue);
            return Task.CompletedTask;
        });
    var refreshSummary = await refreshService.RefreshAsync(
        [liveSource, cacheOnlySource, fallbackSource, failedSource],
        source => source.RefreshOnStartup);
    var liveOutcome = refreshSummary.Outcomes.Single(outcome => outcome.Source.Id == liveSource.Id);
    var cacheOnlyOutcome = refreshSummary.Outcomes.Single(outcome => outcome.Source.Id == cacheOnlySource.Id);
    var fallbackOutcome = refreshSummary.Outcomes.Single(outcome => outcome.Source.Id == fallbackSource.Id);
    var failedOutcome = refreshSummary.Outcomes.Single(outcome => outcome.Source.Id == failedSource.Id);
    if (!refreshSummary.HasPlaylist || refreshSummary.Merge is null ||
        refreshSummary.LiveSourceCount != 1 || refreshSummary.CachedSourceCount != 2 ||
        refreshSummary.FallbackSourceCount != 1 || refreshSummary.FailedSourceCount != 1 ||
        liveRefreshCalls.Count != 3 || liveRefreshCalls.Contains(cacheOnlySource.Id) ||
        cacheWrites.Count != 1 || cacheWrites[0] != liveSource.SourceValue ||
        liveOutcome.Mode != PlaylistSourceLoadMode.Live || cacheOnlyOutcome.Mode != PlaylistSourceLoadMode.CachedOnly ||
        fallbackOutcome.Mode != PlaylistSourceLoadMode.CachedFallback || failedOutcome.Mode != PlaylistSourceLoadMode.Failed ||
        liveSource.LastSuccessUtc is null || liveSource.LastError is not null || liveSource.ChannelCount != 1 ||
        fallbackSource.LastError != "The provider could not be reached." || !fallbackSource.UsedCachedFallback ||
        failedSource.LastError != "The provider could not be reached." || failedOutcome.Playlist is not null ||
        refreshSummary.Merge.SourceCount != 3 || refreshSummary.Merge.Playlist.Channels.Count != 3 ||
        refreshSummary.Merge.Playlist.Channels[0].SourceId != liveSource.Id ||
        refreshSummary.Merge.Playlist.Channels[1].SourceId != cacheOnlySource.Id ||
        refreshSummary.Merge.Playlist.Channels[2].SourceId != fallbackSource.Id)
        throw new InvalidOperationException("Per-source startup refresh, encrypted fallback isolation, or unified merge coordination failed.");

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

    var multiSourceCache = new PlaylistCacheStore(cachePath, sourceCacheDirectory);
    var migratedLegacyCache = await multiSourceCache.TryLoadAsync("url", "https://provider.invalid/list.m3u?token=secret");
    if (migratedLegacyCache?.Playlist.Channels.Count != 2)
        throw new InvalidOperationException("The 3.6 playlist cache was not available through the 3.7 cache layout.");
    var primaryCacheSource = "https://primary.invalid/list.m3u?token=primary-secret";
    var regionalCacheSource = Path.Combine(testRoot, "regional.m3u");
    await multiSourceCache.SaveAsync("url", primaryCacheSource,
        new PlaylistResult([first, distinct], "Primary", "primary", DateTimeOffset.UtcNow));
    await multiSourceCache.SaveAsync("file", regionalCacheSource,
        new PlaylistResult([regional], "Regional", "regional", DateTimeOffset.UtcNow));
    var perSourceCacheFiles = Directory.GetFiles(sourceCacheDirectory, "*.bin");
    var primaryCached = await multiSourceCache.TryLoadAsync("url", primaryCacheSource);
    var regionalCached = await multiSourceCache.TryLoadAsync("file", regionalCacheSource);
    if (perSourceCacheFiles.Length != 2 || primaryCached?.Playlist.Channels.Count != 2 ||
        regionalCached?.Playlist.Channels.Single().Name != regional.Name)
        throw new InvalidOperationException("Independent encrypted playlist caches were not preserved per source.");
    foreach (var path in perSourceCacheFiles)
    {
        var cacheText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));
        if (cacheText.Contains("primary-secret", StringComparison.Ordinal) ||
            cacheText.Contains(regional.Name, StringComparison.Ordinal))
            throw new InvalidOperationException("A per-source playlist cache exposed provider data as clear text.");
    }
    await multiSourceCache.DeleteAsync("url", primaryCacheSource);
    if (await multiSourceCache.TryLoadAsync("url", primaryCacheSource) is not null ||
        await multiSourceCache.TryLoadAsync("file", regionalCacheSource) is null)
        throw new InvalidOperationException("Deleting one playlist cache affected the wrong source.");
    await multiSourceCache.SaveAsync("url", primaryCacheSource,
        new PlaylistResult([first, distinct], "Primary", "primary", DateTimeOffset.UtcNow));

    var credentialStore = new XtreamCredentialStore(credentialPath);
    await credentialStore.SaveAsync(new XtreamCredentials("https://provider.invalid", "probe-user", "probe-password"));
    await credentialStore.SaveAsync(new XtreamCredentials("https://sports.invalid", "sports-user", "sports-password"));
    var credentials = await credentialStore.TryLoadAsync("https://provider.invalid/");
    var sportsCredentials = await credentialStore.TryLoadAsync("sports.invalid/");
    if (credentials is null || credentials.Username != "probe-user" || credentials.Password != "probe-password" ||
        sportsCredentials is null || sportsCredentials.Username != "sports-user" || sportsCredentials.Password != "sports-password")
        throw new InvalidOperationException("Protected multi-account Xtream credential round-trip failed.");
    var credentialText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(credentialPath));
    if (credentialText.Contains("probe-password", StringComparison.Ordinal) || credentialText.Contains("sports-password", StringComparison.Ordinal))
        throw new InvalidOperationException("An Xtream password was persisted as clear text.");
    await Task.WhenAll(Enumerable.Range(1, 4).Select(index => credentialStore.SaveAsync(
        new XtreamCredentials($"https://account-{index}.invalid", $"user-{index}", $"password-{index}"))));
    for (var index = 1; index <= 4; index++)
    {
        var concurrentCredentials = await credentialStore.TryLoadAsync($"account-{index}.invalid");
        if (concurrentCredentials?.Username != $"user-{index}")
            throw new InvalidOperationException("Concurrent Xtream account writes lost a protected account.");
    }
    await credentialStore.DeleteAsync("https://sports.invalid/");
    if (await credentialStore.TryLoadAsync("sports.invalid") is not null ||
        await credentialStore.TryLoadAsync("provider.invalid") is null)
        throw new InvalidOperationException("Removing one Xtream account affected the wrong account.");

    var legacyCredentialPath = Path.Combine(testRoot, "legacy-xtream-credentials.bin");
    var legacyCredentialBytes = JsonSerializer.SerializeToUtf8Bytes(
        new XtreamCredentials("https://legacy.invalid", "legacy-user", "legacy-password"));
    try
    {
        var protectedLegacyCredentials = ProtectedData.Protect(
            legacyCredentialBytes,
            Encoding.UTF8.GetBytes("StreamVue.XtreamCredentials.v1"),
            DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(legacyCredentialPath, protectedLegacyCredentials);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(legacyCredentialBytes);
    }
    var legacyCredentialStore = new XtreamCredentialStore(legacyCredentialPath);
    var legacyCredentials = await legacyCredentialStore.TryLoadAsync("legacy.invalid/");
    if (legacyCredentials?.Password != "legacy-password")
        throw new InvalidOperationException("The 3.6 Xtream credential did not migrate into the 3.7 account vault.");
    var migratedProtectedBytes = await File.ReadAllBytesAsync(legacyCredentialPath);
    var migratedClearBytes = ProtectedData.Unprotect(
        migratedProtectedBytes,
        Encoding.UTF8.GetBytes("StreamVue.XtreamCredentials.v1"),
        DataProtectionScope.CurrentUser);
    try
    {
        using var migratedDocument = JsonDocument.Parse(migratedClearBytes);
        if (migratedDocument.RootElement.GetProperty("Version").GetInt32() != 2 ||
            migratedDocument.RootElement.GetProperty("Accounts").GetArrayLength() != 1)
            throw new InvalidOperationException("The migrated Xtream account vault did not use the 3.7 envelope.");
    }
    finally
    {
        CryptographicOperations.ZeroMemory(migratedClearBytes);
    }

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
          <programme channel="TNT.HD.us2" start="{guideNow.AddMinutes(-15):yyyyMMddHHmmss zzz}" stop="{guideNow.AddMinutes(45):yyyyMMddHHmmss zzz}"><title>Live Sports Center</title><episode-num system="xmltv_ns">1.4.</episode-num><new /></programme>
          <programme channel="TNT.HD.us2" start="{guideNow.AddMinutes(45):yyyyMMddHHmmss zzz}" stop="{guideNow.AddMinutes(105):yyyyMMddHHmmss zzz}"><title>Prime Movie</title><previously-shown /></programme>
          <programme channel="KMBC-DT.us_locals1" start="{guideNow.AddMinutes(-10):yyyyMMddHHmmss zzz}" stop="{guideNow.AddMinutes(20):yyyyMMddHHmmss zzz}"><title>Local News</title></programme>
        </tv>
        """;
    await using var xmlStream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var guide = await new XmlTvParser().ParseAsync(xmlStream, "Feature probe guide", [tnt, local]);
    if (guide.GetNowNext(tnt, guideNow).Current?.Title != "Live Sports Center" ||
        guide.GetNowNext(tnt, guideNow).Next?.Title != "Prime Movie" ||
        guide.GetNowNext(local, guideNow).Current?.Title != "Local News")
        throw new InvalidOperationException("XMLTV Now/Next or broadcast call-sign matching failed.");
    var episodeProgramme = guide.GetNowNext(tnt, guideNow).Current;
    if (episodeProgramme?.SeasonNumber != 2 || episodeProgramme.EpisodeNumber != 5 ||
        episodeProgramme.EpisodeLabel != "S02E05" || episodeProgramme.IsNewEpisode != true)
        throw new InvalidOperationException("XMLTV episode identity or new-episode metadata parsing failed.");
    if (guide.ChannelCatalog.Count != 2 || !guide.ChannelCatalog.ContainsKey("TNT.HD.US2"))
        throw new InvalidOperationException("The lightweight XMLTV channel catalog was not retained.");

    var epgCache = new EpgCacheStore(epgCachePath);
    await epgCache.SaveAsync("public-us-pack", guide);
    var cachedGuide = await epgCache.TryLoadAsync("public-us-pack");
    if (cachedGuide?.GetNowNext(tnt, guideNow).Current?.Title != "Live Sports Center" || cachedGuide.ChannelCatalog.Count != 2 ||
        cachedGuide.GetNowNext(tnt, guideNow).Current?.EpisodeLabel != "S02E05" ||
        cachedGuide.GetNowNext(tnt, guideNow).Current?.IsNewEpisode != true)
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
    var recoverableSignalError = new PlaybackStatus(PlaybackState.Error, "Playback error", TechnicalDetail: "Reconnect pending");
    var terminalSignalError = new PlaybackStatus(PlaybackState.Error, "The signal could not be restored", TechnicalDetail: "Recovery exhausted", IsTerminalFailure: true);
    if (!partialBuffer.ShouldShowBufferOverlay || completeBuffer.ShouldShowBufferOverlay || playing.ShouldShowBufferOverlay)
        throw new InvalidOperationException("Buffer overlay visibility policy failed.");
    if (recoverableSignalError.IsTerminalFailure || !terminalSignalError.IsTerminalFailure)
        throw new InvalidOperationException("Playback recovery did not distinguish recoverable errors from terminal feed failures.");

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
    var storeManagedUpdates = new AppUpdateService(Path.Combine(testRoot, "store-updates"), storeManagedOverride: true);
    var storeManagedResult = await storeManagedUpdates.CheckAsync();
    if (!storeManagedUpdates.IsStoreManaged || storeManagedUpdates.HasAvailableUpdate ||
        storeManagedResult.State != AppUpdateState.StoreManaged)
        throw new InvalidOperationException("Microsoft Store update isolation failed.");
    try
    {
        await storeManagedUpdates.DownloadAndRestartAsync(_ => { });
        throw new InvalidOperationException("Microsoft Store update isolation accepted a Velopack download.");
    }
    catch (InvalidOperationException exception) when (exception.Message == "Microsoft Store installs are updated by Microsoft Store.")
    {
    }

    var recordingStart = new DateTimeOffset(2026, 8, 22, 20, 15, 0, TimeSpan.Zero);
    var recordingFileName = DvrRecordingService.CreateRecordingFileName("US: CON? Sports", "Final / Highlights", recordingStart);
    var recordingPath = Path.Combine(testRoot, "recordings", recordingFileName);
    var soutOption = DvrRecordingService.BuildSoutOption(recordingPath);
    if (recordingFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
        !recordingFileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
        !soutOption.StartsWith(":sout=#std{access=file,mux=ts,dst='", StringComparison.Ordinal) ||
        soutOption.Contains("provider.invalid", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("DVR output naming or transport-stream routing failed.");
    Directory.CreateDirectory(Path.GetDirectoryName(recordingPath)!);
    await File.WriteAllBytesAsync(recordingPath, new byte[4_096]);
    await File.WriteAllBytesAsync(Path.Combine(Path.GetDirectoryName(recordingPath)!, "empty.ts"), []);
    using (var dvrLibrary = new DvrRecordingService())
    {
        var recent = dvrLibrary.ListRecentRecordings(Path.GetDirectoryName(recordingPath));
        if (recent.Count != 1 || recent[0].Bytes != 4_096 || recent[0].FilePath != recordingPath)
            throw new InvalidOperationException("DVR recording library indexing failed.");
        var storage = dvrLibrary.GetStorageSnapshot(Path.GetDirectoryName(recordingPath));
        if (!storage.IsAvailable || storage.RecordingCount != 1 || storage.RecordingBytes != 4_096 || storage.FreeBytes <= 0)
            throw new InvalidOperationException("DVR storage reporting failed.");
        var firstKey = DvrRecordingService.CreateLibraryKey(recordingPath);
        var secondKey = DvrRecordingService.CreateLibraryKey(recordingPath.ToUpperInvariant());
        if (firstKey.Length != 64 || !firstKey.Equals(secondKey, StringComparison.Ordinal))
            throw new InvalidOperationException("DVR recording identity is not stable or privacy-safe.");

        var deletePath = Path.Combine(Path.GetDirectoryName(recordingPath)!, "delete-me.ts");
        await File.WriteAllBytesAsync(deletePath, [1, 2, 3]);
        dvrLibrary.DeleteRecording(deletePath, Path.GetDirectoryName(recordingPath));
        if (File.Exists(deletePath)) throw new InvalidOperationException("DVR safe delete did not remove the selected recording.");
        try
        {
            dvrLibrary.DeleteRecording(settingsPath, Path.GetDirectoryName(recordingPath));
            throw new InvalidOperationException("DVR safe delete accepted a file outside the recordings folder.");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("selected recordings folder", StringComparison.Ordinal))
        {
        }
    }

    var conflictStart = DateTimeOffset.UtcNow.AddHours(4);
    var conflictFirst = new ScheduledRecording
    {
        ChannelKey = first.StableKey,
        ChannelName = first.Name,
        ProgramTitle = "Game One",
        StartUtc = conflictStart,
        StopUtc = conflictStart.AddHours(2),
        Priority = DvrSchedulePriority.Low
    };
    var conflictSecond = new ScheduledRecording
    {
        ChannelKey = distinct.StableKey,
        ChannelName = distinct.Name,
        ProgramTitle = "News Special",
        StartUtc = conflictStart.AddMinutes(30),
        StopUtc = conflictStart.AddHours(1),
        Priority = DvrSchedulePriority.High
    };
    var noConflict = new ScheduledRecording
    {
        ChannelKey = first.StableKey,
        ChannelName = first.Name,
        ProgramTitle = "Later Show",
        StartUtc = conflictStart.AddHours(2),
        StopUtc = conflictStart.AddHours(3)
    };
    var conflictIds = DvrRecordingService.FindConflictingScheduleIds([conflictFirst, conflictSecond, noConflict]);
    if (conflictIds.Count != 2 || !conflictIds.Contains(conflictFirst.Id) || !conflictIds.Contains(conflictSecond.Id) || conflictIds.Contains(noConflict.Id))
        throw new InvalidOperationException("DVR schedule conflict detection failed.");
    var conflictWinners = SmartDvrPolicy.FindConflictWinners([conflictFirst, conflictSecond, noConflict]);
    if (SmartDvrPolicy.SelectPreferred([conflictFirst, conflictSecond])?.Id != conflictSecond.Id ||
        !conflictWinners.Contains(conflictSecond.Id) || conflictWinners.Contains(conflictFirst.Id))
        throw new InvalidOperationException("Smart DVR priority resolution failed.");

    var guideProgramme = new EpgProgram(
        "probe.channel",
        "News Special",
        null,
        "News",
        conflictStart,
        conflictStart.AddHours(1));
    var paddedSchedule = SmartDvrPolicy.CreateSchedule(first, guideProgramme, 5, 10, DvrSchedulePriority.High, seriesRuleId);
    var seriesRule = expected.SeriesRecordingRules[0];
    if (paddedSchedule.StartUtc != guideProgramme.Start.AddMinutes(-5) ||
        paddedSchedule.StopUtc != guideProgramme.Stop.AddMinutes(10) ||
        paddedSchedule.GuideStartUtc != guideProgramme.Start || paddedSchedule.GuideStopUtc != guideProgramme.Stop ||
        paddedSchedule.SeriesRuleId != seriesRuleId || !SmartDvrPolicy.MatchesProgramme(paddedSchedule, first, guideProgramme))
        throw new InvalidOperationException("Smart DVR schedule padding or guide identity failed.");
    var matchingRule = new SeriesRecordingRule
    {
        ChannelKey = first.StableKey,
        ChannelName = first.Name,
        ProgramTitle = guideProgramme.Title
    };
    if (!SmartDvrPolicy.RuleMatches(matchingRule, first, guideProgramme))
        throw new InvalidOperationException("Smart DVR series matching failed.");
    if (!SmartDvrPolicy.MeetsStorageReserve(new DvrStorageSnapshot(true, 100L << 30, 21L << 30, 0, 0), 20) ||
        SmartDvrPolicy.MeetsStorageReserve(new DvrStorageSnapshot(true, 100L << 30, 19L << 30, 0, 0), 20))
        throw new InvalidOperationException("Smart DVR storage reserve policy failed.");
    var adjacentFirst = SmartDvrPolicy.CreateSchedule(
        first,
        guideProgramme,
        2,
        5,
        DvrSchedulePriority.Normal);
    var adjacentProgramme = guideProgramme with
    {
        Title = "News Special — Hour Two",
        Start = guideProgramme.Stop,
        Stop = guideProgramme.Stop.AddHours(1)
    };
    var adjacentSecond = SmartDvrPolicy.CreateSchedule(
        first,
        adjacentProgramme,
        2,
        5,
        DvrSchedulePriority.Normal);
    if (!DvrRecordingService.SchedulesOverlap(adjacentFirst, adjacentSecond) ||
        DvrRecordingService.SchedulesCompete(adjacentFirst, adjacentSecond) ||
        DvrRecordingService.FindConflictingScheduleIds([adjacentFirst, adjacentSecond]).Count != 0 ||
        SmartDvrPolicy.SelectPreferredDue([adjacentFirst, adjacentSecond], adjacentSecond.GuideStartUtc.AddMinutes(1))?.Id != adjacentSecond.Id)
        throw new InvalidOperationException("Smart DVR padding-only schedule handoff failed.");

    var repeatProgramme = guideProgramme with
    {
        EpisodeId = "provider-episode-205",
        SeasonNumber = 2,
        EpisodeNumber = 5,
        IsNewEpisode = false
    };
    var newProgramme = repeatProgramme with { IsNewEpisode = true };
    var newOnlyRule = new SeriesRecordingRule
    {
        ChannelKey = matchingRule.ChannelKey,
        ChannelName = matchingRule.ChannelName,
        ProgramTitle = matchingRule.ProgramTitle,
        EpisodeSelection = DvrEpisodeSelection.NewEpisodesOnly,
        AnyChannel = true,
        KeepLatestCount = 3
    };
    var alternateChannel = new ChannelItem
    {
        Number = 99,
        Name = "Alternate News",
        Group = "News",
        Url = "https://provider.invalid/live/alternate-news.ts",
        Kind = ChannelKind.Live
    };
    if (SmartDvrPolicy.RuleMatches(newOnlyRule, alternateChannel, repeatProgramme) ||
        !SmartDvrPolicy.RuleMatches(newOnlyRule, alternateChannel, newProgramme))
        throw new InvalidOperationException("New-episode-only or any-channel series matching failed.");
    var firstEpisodeSchedule = SmartDvrPolicy.CreateSchedule(first, newProgramme, 1, 2, DvrSchedulePriority.Normal, newOnlyRule.Id);
    var duplicateEpisodeSchedule = SmartDvrPolicy.CreateSchedule(alternateChannel, newProgramme, 1, 2, DvrSchedulePriority.Normal, newOnlyRule.Id);
    if (firstEpisodeSchedule.EpisodeLabel != "S02E05" ||
        firstEpisodeSchedule.EpisodeKey != duplicateEpisodeSchedule.EpisodeKey ||
        !SmartDvrPolicy.IsDuplicateEpisode([firstEpisodeSchedule], duplicateEpisodeSchedule))
        throw new InvalidOperationException("Series episode identity or cross-channel duplicate prevention failed.");
    if (SmartDvrPolicy.ClampRetention(2) != 3 || SmartDvrPolicy.NextRetention(3) != 5 ||
        SmartDvrPolicy.ClampTimeshiftMinutes(31) != 60 ||
        SmartDvrPolicy.RecoveryDelay(1) != TimeSpan.FromSeconds(2) ||
        SmartDvrPolicy.RecoveryDelay(2) != TimeSpan.FromSeconds(5) ||
        SmartDvrPolicy.RecoveryDelay(3) != TimeSpan.FromSeconds(10))
        throw new InvalidOperationException("DVR retention, timeshift, or staged recovery policy failed.");

    var wakeNow = DateTimeOffset.UtcNow;
    var wakeSchedule = new ScheduledRecording
    {
        ChannelKey = first.StableKey,
        ChannelName = first.Name,
        ProgramTitle = "Wake probe",
        StartUtc = wakeNow.AddMinutes(15),
        StopUtc = wakeNow.AddHours(1)
    };
    var wakePreferences = new SmartDvrPreferences
    {
        BackgroundRecordingEnabled = true,
        WakeForRecordings = true,
        StorageReserveGigabytes = 5
    };
    var wakePlan = DvrBackgroundPolicy.CreateWakePlan([wakeSchedule], wakePreferences, wakeNow);
    var capacity = DvrBackgroundPolicy.EstimateCapacityHours(
        new DvrStorageSnapshot(true, 100L << 30, 15L << 30, 0, 0),
        wakePreferences.StorageReserveGigabytes,
        8);
    if (wakePlan?.ScheduleId != wakeSchedule.Id || wakePlan.WakeUtc != wakeSchedule.StartUtc.AddMinutes(-2) ||
        !wakePlan.ResumeSystem || capacity < 2.9 || capacity > 3.1)
        throw new InvalidOperationException("Background DVR wake planning or storage-capacity estimate failed.");
    using (var powerGuard = new WindowsRecordingPowerGuard())
    {
        powerGuard.SetActive(true);
        powerGuard.SetActive(false);
    }
    if (OperatingSystem.IsWindows())
    {
        using var wakeTimer = new WindowsWakeTimer();
        if (!wakeTimer.IsAvailable)
            throw new InvalidOperationException("The Windows background DVR wake timer could not be created.");
        var wakeTriggered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wakeTimer.Triggered += (_, _) => wakeTriggered.TrySetResult();
        if (!wakeTimer.Schedule(DateTimeOffset.UtcNow.AddMilliseconds(1_250), resumeSystem: false) ||
            wakeTimer.NextWakeUtc is null)
            throw new InvalidOperationException("The Windows background DVR wake timer could not be armed.");
        await wakeTriggered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        wakeTimer.Cancel();
        if (wakeTimer.NextWakeUtc is not null)
            throw new InvalidOperationException("The Windows background DVR wake timer did not cancel cleanly.");

        var instanceScope = $"StreamVue.FeatureProbe.{Guid.NewGuid():N}";
        using var primaryInstance = new StreamVueSingleInstance(waitForPreviousInstance: false, scope: instanceScope);
        if (!primaryInstance.IsPrimary)
            throw new InvalidOperationException("The first StreamVue process did not acquire the single-instance coordinator.");
        var activationRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primaryInstance.ActivationRequested += (_, _) => activationRequested.TrySetResult();
        await Task.Run(() =>
        {
            using var secondaryInstance = new StreamVueSingleInstance(waitForPreviousInstance: false, scope: instanceScope);
            if (secondaryInstance.IsPrimary)
                throw new InvalidOperationException("A second StreamVue process acquired the active recorder instance.");
            secondaryInstance.SignalPrimary();
        });
        await activationRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    var castService = new WindowsCastService();
    if (!castService.IsSupported || WindowsCastService.NearbyDisplayShortcut != "Windows + K" ||
        WindowsCastService.DisplaySettingsUri != "ms-settings:display")
        throw new InvalidOperationException("The Windows nearby-display casting entry points are unavailable or misconfigured.");

    var maintenance = new StreamVueMaintenanceService(testRoot);
    var managedCachePath = Path.Combine(testRoot, "playlist-cache.v1.bin");
    var managedMediaCredentialPath = Path.Combine(testRoot, "media-center-credentials.v1.bin");
    var managedMediaCredentialSnapshot = await File.ReadAllBytesAsync(managedMediaCredentialPath);
    await File.WriteAllBytesAsync(managedCachePath, [1, 2, 3, 4]);
    var managedSourceCacheFiles = Directory.GetFiles(sourceCacheDirectory, "*.bin");
    var managedSourceCacheSnapshots = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in managedSourceCacheFiles)
        managedSourceCacheSnapshots[Path.GetFileName(path)] = await File.ReadAllBytesAsync(path);
    var backupPath = Path.Combine(testRoot, "probe.streamvue-backup");
    var backupCount = await maintenance.CreateBackupAsync(backupPath);
    if (backupCount != 3 + managedSourceCacheSnapshots.Count || !File.Exists(backupPath))
        throw new InvalidOperationException("StreamVue backup creation did not capture the expected protected data.");
    using (var backupArchive = ZipFile.OpenRead(backupPath))
    {
        var protectedSettings = backupArchive.GetEntry("data/settings.json.protected");
        if (protectedSettings is null || backupArchive.GetEntry("data/settings.json") is not null)
            throw new InvalidOperationException("The backup did not protect its settings payload.");
        if (managedSourceCacheSnapshots.Keys.Any(name =>
                backupArchive.GetEntry($"data/playlist-caches.v2/{name}") is null))
            throw new InvalidOperationException("The backup omitted one or more per-source playlist caches.");
        if (backupArchive.GetEntry("data/media-center-credentials.v1.bin") is null)
            throw new InvalidOperationException("The backup omitted the protected media-center credential vault.");
        using var protectedReader = new StreamReader(protectedSettings.Open());
        if ((await protectedReader.ReadToEndAsync()).Contains("playlist.m3u", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The backup exposed the playlist source as clear text.");
    }

    await new AppSettingsStore(settingsPath).SaveAsync(new AppSettings { LastSourceType = "changed" });
    await File.WriteAllBytesAsync(managedCachePath, [9, 9]);
    await File.WriteAllBytesAsync(managedMediaCredentialPath, [9, 9, 9]);
    foreach (var path in Directory.GetFiles(sourceCacheDirectory, "*.bin"))
        await File.WriteAllBytesAsync(path, [9, 9, 9]);
    var staleSourceCachePath = Path.Combine(sourceCacheDirectory, $"{new string('C', 64)}.bin");
    await File.WriteAllBytesAsync(staleSourceCachePath, [8, 8, 8]);
    var restoredCount = await maintenance.RestoreBackupAsync(backupPath);
    var restoredSettings = await new AppSettingsStore(settingsPath).LoadAsync();
    var restoredCache = await File.ReadAllBytesAsync(managedCachePath);
    var sourceCachesRestored = managedSourceCacheSnapshots.All(snapshot =>
        File.Exists(Path.Combine(sourceCacheDirectory, snapshot.Key)) &&
        File.ReadAllBytes(Path.Combine(sourceCacheDirectory, snapshot.Key)).SequenceEqual(snapshot.Value));
    if (restoredCount != 3 + managedSourceCacheSnapshots.Count || restoredSettings.LastChannelKey != first.StableKey ||
        !restoredSettings.ResumeLastChannelOnStartup || !restoredCache.SequenceEqual(new byte[] { 1, 2, 3, 4 }) ||
        !sourceCachesRestored || !File.ReadAllBytes(managedMediaCredentialPath).SequenceEqual(managedMediaCredentialSnapshot) ||
        File.Exists(staleSourceCachePath) ||
        !File.Exists(Path.Combine(testRoot, "before-last-restore.streamvue-backup")))
        throw new InvalidOperationException("StreamVue backup restore or automatic rollback protection failed.");

    var legacyBackupPath = Path.Combine(testRoot, "legacy-3.6.streamvue-backup");
    var legacyRestoreRoot = Path.Combine(testRoot, "legacy-restore");
    var legacySettingsBytes = JsonSerializer.SerializeToUtf8Bytes(new AppSettings
    {
        LastSourceType = "url",
        LastSource = "https://legacy-backup.invalid/list.m3u"
    });
    try
    {
        var protectedLegacySettings = ProtectedData.Protect(
            legacySettingsBytes,
            Encoding.UTF8.GetBytes("StreamVue.PortableBackup.v1"),
            DataProtectionScope.CurrentUser);
        await using var legacyBackupStream = File.Create(legacyBackupPath);
        using var legacyArchive = new ZipArchive(legacyBackupStream, ZipArchiveMode.Create, leaveOpen: false);
        var legacyManifestEntry = legacyArchive.CreateEntry("manifest.json");
        await using (var manifestStream = legacyManifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(manifestStream, new
            {
                Product = "StreamVue",
                FormatVersion = 1,
                CreatedUtc = DateTimeOffset.UtcNow,
                EncryptionScope = "Windows current-user encryption; restore with the same Windows account.",
                Files = new[] { "settings.json" }
            });
        }
        var legacySettingsEntry = legacyArchive.CreateEntry("data/settings.json.protected");
        await using var legacySettingsStream = legacySettingsEntry.Open();
        await legacySettingsStream.WriteAsync(protectedLegacySettings);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(legacySettingsBytes);
    }
    var legacyRestoreCount = await new StreamVueMaintenanceService(legacyRestoreRoot).RestoreBackupAsync(legacyBackupPath);
    var restoredLegacySettings = await new AppSettingsStore(Path.Combine(legacyRestoreRoot, "settings.json")).LoadAsync();
    if (legacyRestoreCount != 1 || restoredLegacySettings.LastSource != "https://legacy-backup.invalid/list.m3u")
        throw new InvalidOperationException("A StreamVue 3.6 backup could not be restored by 3.7.");

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
    Console.WriteLine("3.6-to-3.7 playlist source migration: PASS");
    Console.WriteLine("Multi-source normalization, ordering, provenance, and exact deduplication: PASS");
    Console.WriteLine("Equivalent-feed logical channel matching: PASS");
    Console.WriteLine("Manual Signal Route linking and separation: PASS");
    Console.WriteLine("Signal quality scoring, preference, exclusion, and failover selection: PASS");
    Console.WriteLine("Unified duplicate-feed guide schedule: PASS");
    Console.WriteLine("M3U and Xtream catch-up replay URL expansion: PASS");
    Console.WriteLine("Channel Health Center metadata and route analysis: PASS");
    Console.WriteLine("Interrupted session journal and clean-close recovery: PASS");
    Console.WriteLine("Per-source startup refresh, offline fallback isolation, and unified recovery: PASS");
    Console.WriteLine("Reconnect preferences: PASS");
    Console.WriteLine("Playlist refresh status: PASS");
    Console.WriteLine("Startup channel resume preference: PASS");
    Console.WriteLine("Mini Player always-on-top preference: PASS");
    Console.WriteLine("Recent-channel history: PASS");
    Console.WriteLine("Per-channel playback profiles: PASS");
    Console.WriteLine("Playlist health persistence: PASS");
    Console.WriteLine("Program reminder persistence: PASS");
    Console.WriteLine("DVR schedule persistence: PASS");
    Console.WriteLine("Pre-3.6 background DVR settings migration: PASS");
    Console.WriteLine("Safe transport-stream recording output: PASS");
    Console.WriteLine("DVR recording library indexing: PASS");
    Console.WriteLine("DVR playback resume persistence: PASS");
    Console.WriteLine("DVR storage reporting and safe delete: PASS");
    Console.WriteLine("DVR schedule conflict detection: PASS");
    Console.WriteLine("Smart DVR series, episode deduplication, retention, padding, priority, boundary handoff, and storage guard: PASS");
    Console.WriteLine("Background DVR wake planning and capacity estimate: PASS");
    Console.WriteLine("Windows background DVR wake timer: PASS");
    Console.WriteLine("Single-instance background activation: PASS");
    Console.WriteLine("Staged interrupted-recording recovery policy: PASS");
    Console.WriteLine("Persistent four-view assignments: PASS");
    Console.WriteLine("Saved multiview layouts: PASS");
    Console.WriteLine("Single-audio multiview policy: PASS");
    Console.WriteLine("Independent encrypted multi-source playlist caches: PASS");
    Console.WriteLine("Protected multi-account Xtream vault and legacy migration: PASS");
    Console.WriteLine("Protected Plex and Emby catalog, credentials, just-in-time playback, and watch-progress synchronization: PASS");
    Console.WriteLine("Personal/store premium entitlement policy: PASS");
    Console.WriteLine("Microsoft Store durable add-on verification and revocation: PASS");
    Console.WriteLine("XMLTV Now/Next, episode metadata, and call-sign matching: PASS");
    Console.WriteLine("Encrypted offline guide cache: PASS");
    Console.WriteLine("Protected multi-source guide configuration: PASS");
    Console.WriteLine("Lightweight XMLTV channel catalog: PASS");
    Console.WriteLine("Encrypted manual guide mappings: PASS");
    Console.WriteLine("Mapped-channel supplemental programme loading: PASS");
    Console.WriteLine("Expanded aspect-ratio mapping: PASS");
    Console.WriteLine("Completed-buffer overlay dismissal: PASS");
    Console.WriteLine("Terminal-only automatic feed failover trigger: PASS");
    Console.WriteLine("Per-channel smart buffer policy: PASS");
    Console.WriteLine("Playback IQ fast-tune planning: PASS");
    Console.WriteLine("Playback IQ staged recovery planning: PASS");
    Console.WriteLine("Frozen-stream watchdog policy: PASS");
    Console.WriteLine("Zero-video decoder fallback policy: PASS");
    Console.WriteLine("Playback IQ startup deadline: PASS");
    Console.WriteLine("Adaptive display cadence policy: PASS");
    Console.WriteLine("Monitor-accurate fullscreen bounds: PASS");
    Console.WriteLine("Background and multi-monitor video visibility: PASS");
    Console.WriteLine("Encrypted recovery-safe settings and multi-source cache backup/restore: PASS");
    Console.WriteLine("StreamVue 3.6 backup restore compatibility: PASS");
    Console.WriteLine("Privacy-filtered diagnostics bundle: PASS");
    Console.WriteLine("Nearby unpaired wireless-display casting: PASS");
    Console.WriteLine("Public update channel configuration: PASS");
    Console.WriteLine("Microsoft Store-managed update isolation: PASS");
    Console.WriteLine("Stable/Preview update and automatic rollback preferences: PASS");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"StreamVue feature verification: FAIL - {exception.Message}");
    return 1;
}
finally
{
    if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
}

static void RunPremiumAccessSelfTest()
{
    var personal = PremiumAccessPolicy.Evaluate("personal", hasVerifiedStorePurchase: false);
    if (!personal.CanUseMediaCenters || personal.AccessState != PremiumAccessState.Included ||
        personal.ReceiptVerification != "not-required" || personal.ProductId is not null)
        throw new InvalidOperationException("Personal builds did not include premium media centers.");

    var locked = PremiumAccessPolicy.Evaluate("store", hasVerifiedStorePurchase: false);
    if (locked.CanUseMediaCenters || locked.AccessState != PremiumAccessState.Unavailable)
        throw new InvalidOperationException("An unverified store build exposed premium media centers.");

    var incompleteVerification = PremiumAccessPolicy.Evaluate("store", hasVerifiedStorePurchase: true);
    if (incompleteVerification.CanUseMediaCenters)
        throw new InvalidOperationException("A store boolean without a store product identifier unlocked premium access.");

    var verified = PremiumAccessPolicy.Evaluate(
        "store",
        hasVerifiedStorePurchase: true,
        productId: "com.streamvue.personal-media-centers");
    if (!verified.CanUseMediaCenters || verified.AccessState != PremiumAccessState.Verified ||
        verified.ReceiptVerification != "verified")
        throw new InvalidOperationException("A verified one-time store purchase did not unlock premium access.");

    if (PremiumAccessPolicy.Evaluate("typo", false).CanUseMediaCenters)
        throw new InvalidOperationException("An unknown distribution mode failed open.");
}

static async Task RunMicrosoftStorePremiumSelfTestAsync()
{
    var invalid = PremiumStoreConfiguration.Evaluate("store", "premium.é");
    if (invalid.ProductId is not null)
        throw new InvalidOperationException("The Microsoft Store configuration accepted a non-ASCII product ID.");

    var personalFactoryCalled = false;
    using (var personal = new MicrosoftStorePremiumService(
               PremiumStoreConfiguration.Evaluate("personal", null),
               _ =>
               {
                   personalFactoryCalled = true;
                   return new MicrosoftStoreProbeClient();
               }))
    {
        await personal.StartAsync(1);
        if (personalFactoryCalled || !personal.State.Access.CanUseMediaCenters)
            throw new InvalidOperationException("A personal Windows build contacted the Microsoft Store or lost included access.");
    }

    const string productId = "com.streamvue.personal-media-centers";
    var client = new MicrosoftStoreProbeClient
    {
        Product = new MicrosoftStoreProduct(productId, "9NPROBE12345", "Lifetime media centers", "$14.99")
    };
    using var service = new MicrosoftStorePremiumService(
        PremiumStoreConfiguration.Evaluate("store", productId),
        _ => client);
    if (service.State.Access.CanUseMediaCenters)
        throw new InvalidOperationException("An injected Windows Store configuration did not start fail-closed.");
    await service.StartAsync(1);
    if (service.State.Access.CanUseMediaCenters || !service.State.CanPurchase ||
        service.State.FormattedPrice != "$14.99" || client.LicenseQueries != 1)
        throw new InvalidOperationException("The unowned durable add-on did not remain locked with localized purchase UI.");

    client.NextPurchaseOutcome = MicrosoftStorePurchaseOutcome.Succeeded;
    await service.PurchaseAsync();
    if (service.State.Access.CanUseMediaCenters)
        throw new InvalidOperationException("A purchase-dialog result unlocked Windows access without a Store license.");

    client.OwnsProduct = true;
    await service.RestoreAsync();
    if (!service.State.Access.CanUseMediaCenters ||
        service.State.Access.AccessState != PremiumAccessState.Verified)
        throw new InvalidOperationException("A valid Microsoft Store durable license did not unlock premium access.");

    var revocation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    service.StateChanged += (_, state) =>
    {
        if (!state.Access.CanUseMediaCenters) revocation.TrySetResult();
    };
    client.OwnsProduct = false;
    client.RaiseLicenseChanged();
    await revocation.Task.WaitAsync(TimeSpan.FromSeconds(5));
    if (service.State.Access.CanUseMediaCenters)
        throw new InvalidOperationException("A Microsoft Store license revocation did not fail closed.");
}

static async Task RunMediaCenterSelfTestAsync(string testRoot)
{
    const string plexBaseUrl = "https://plex.local:32400";
    const string embyBaseUrl = "https://emby.local:8920";
    const string plexToken = "plex-probe-secret-token";
    const string embyPassword = "emby-probe-password";
    const string embyToken = "emby-probe-secret-token";

    var lockedHandler = new MediaCenterProbeHandler(plexToken, embyPassword, embyToken);
    var lockedService = new MediaCenterSourceService(
        new MediaCenterCredentialStore(Path.Combine(testRoot, "locked-media-center-credentials.bin")),
        new HttpClient(lockedHandler),
        "streamvue-win-locked-probe",
        PremiumAccessPolicy.Evaluate("store", hasVerifiedStorePurchase: false));
    try
    {
        await lockedService.ConnectPlexAsync(plexBaseUrl, plexToken, null, allowInsecureHttp: false);
        throw new InvalidOperationException("A locked Windows store service accepted a Plex connection.");
    }
    catch (InvalidOperationException exception) when (
        exception.Message.Contains("one-time store purchase", StringComparison.OrdinalIgnoreCase))
    {
    }
    if (lockedHandler.Requests.Count != 0)
        throw new InvalidOperationException("A locked Windows store service reached the media-server network.");

    var runtimeAccess = PremiumAccessPolicy.Evaluate("store", hasVerifiedStorePurchase: false);
    var runtimeHandler = new MediaCenterProbeHandler(plexToken, embyPassword, embyToken);
    var runtimeService = new MediaCenterSourceService(
        new MediaCenterCredentialStore(Path.Combine(testRoot, "runtime-media-center-credentials.bin")),
        new HttpClient(runtimeHandler),
        "streamvue-win-runtime-probe",
        premiumAccessProvider: () => runtimeAccess);
    try
    {
        await runtimeService.ConnectPlexAsync(plexBaseUrl, plexToken, null, allowInsecureHttp: false);
        throw new InvalidOperationException("A locked runtime entitlement accepted a Plex connection.");
    }
    catch (InvalidOperationException exception) when (
        exception.Message.Contains("one-time store purchase", StringComparison.OrdinalIgnoreCase))
    {
    }
    if (runtimeHandler.Requests.Count != 0)
        throw new InvalidOperationException("A locked runtime entitlement reached the media-server network.");
    runtimeAccess = PremiumAccessPolicy.Evaluate(
        "store",
        hasVerifiedStorePurchase: true,
        productId: "com.streamvue.personal-media-centers");
    var runtimePlaylist = await runtimeService.ConnectPlexAsync(
        plexBaseUrl,
        plexToken,
        "Runtime Plex",
        allowInsecureHttp: false);
    if (runtimePlaylist.Channels.Count != 1 || runtimeHandler.Requests.Count == 0)
        throw new InvalidOperationException("An existing Windows media-center service did not observe verified runtime access.");
    var runtimePlayback = await runtimeService.ResolvePlaybackAsync(runtimePlaylist.Channels[0]);
    runtimeAccess = PremiumAccessPolicy.Evaluate("store", hasVerifiedStorePurchase: false);
    var requestsBeforeRuntimeRevocation = runtimeHandler.Requests.Count;
    await runtimeService.StopPlaybackReportingAsync(
        runtimePlayback.ReportingSessionId!,
        60_000,
        7_200_000);
    runtimeAccess = PremiumAccessPolicy.Evaluate(
        "store",
        hasVerifiedStorePurchase: true,
        productId: "com.streamvue.personal-media-centers");
    try
    {
        await runtimeService.ReportPlaybackAsync(
            runtimePlayback.ReportingSessionId!,
            MediaCenterPlaybackState.Playing,
            60_000,
            7_200_000);
        throw new InvalidOperationException("A playback reporting session survived premium entitlement revocation.");
    }
    catch (InvalidOperationException exception) when (
        exception.Message.Contains("no longer active", StringComparison.OrdinalIgnoreCase))
    {
    }
    if (runtimeHandler.Requests.Count != requestsBeforeRuntimeRevocation)
        throw new InvalidOperationException("Premium revocation emitted a protected playback report.");

    if (MediaCenterSecurity.NormalizeBaseUrl($"{plexBaseUrl}/") != plexBaseUrl)
        throw new InvalidOperationException("Media-center server normalization was not canonical.");
    try
    {
        MediaCenterSecurity.NormalizeBaseUrl("https://user:password@plex.local:32400?token=secret");
        throw new InvalidOperationException("A credential-bearing media-center address was accepted.");
    }
    catch (ArgumentException)
    {
    }
    try
    {
        MediaCenterSecurity.RequireAllowedTransport("http://192.168.1.20:8096", allowInsecureHttp: false);
        throw new InvalidOperationException("Unconfirmed HTTP media-center credentials were accepted.");
    }
    catch (ArgumentException)
    {
    }
    try
    {
        MediaCenterSecurity.ResolveServerPath(plexBaseUrl, "https://attacker.invalid/video.mkv");
        throw new InvalidOperationException("A cross-origin media-center playback path was accepted.");
    }
    catch (InvalidDataException)
    {
    }

    var credentialPath = Path.Combine(testRoot, "media-center-credentials.v1.bin");
    var credentialStore = new MediaCenterCredentialStore(credentialPath);
    var handler = new MediaCenterProbeHandler(plexToken, embyPassword, embyToken);
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    var service = new MediaCenterSourceService(credentialStore, http, "streamvue-win-feature-probe");

    var plex = await service.ConnectPlexAsync(
        plexBaseUrl,
        plexToken,
        "Probe Plex",
        allowInsecureHttp: false);
    if (plex.Channels is not [var plexItem] ||
        !plexItem.IsProtectedMedia ||
        plexItem.Kind != ChannelKind.Movie ||
        plexItem.ResumePositionMilliseconds != 60_000 ||
        plexItem.Url.Contains(plexToken, StringComparison.Ordinal))
        throw new InvalidOperationException("The Plex catalog did not produce one token-free resumable movie.");
    var plexLocator = MediaCenterSecurity.ParsePlaybackLocator(plexItem.Url);
    if (plexLocator.Provider != "plex" || plexLocator.ServerId != "plex-server-1" || plexLocator.ItemId != "100")
        throw new InvalidOperationException("The Plex catalog locator was not canonical.");

    var savedPlex = await credentialStore.TryLoadForSourceAsync("plex", plexBaseUrl);
    if (savedPlex is null || savedPlex.AccessToken != plexToken)
        throw new InvalidOperationException("The Windows-protected Plex credential did not round-trip.");
    try
    {
        MediaCenterSecurity.ValidateCredential(savedPlex with
        {
            Binding = savedPlex.Binding with { CredentialId = "mc-plex-tampered" }
        });
        throw new InvalidOperationException("A media-center credential with a tampered server binding was accepted.");
    }
    catch (InvalidDataException)
    {
    }
    var resolvedPlex = await service.ResolvePlaybackAsync(plexItem);
    var resolvedPlexUri = new Uri(resolvedPlex.Url);
    if (!resolvedPlexUri.Query.Contains($"X-Plex-Token={Uri.EscapeDataString(plexToken)}", StringComparison.Ordinal) ||
        resolvedPlexUri.Query.Contains("upstream-plex-token", StringComparison.Ordinal) ||
        resolvedPlexUri.Host != "plex.local")
        throw new InvalidOperationException("Plex playback did not materialize only the bound credential on the original server.");
    if (string.IsNullOrWhiteSpace(resolvedPlex.ReportingSessionId) ||
        resolvedPlex.ReportingSessionId.Contains(plexToken, StringComparison.Ordinal) ||
        resolvedPlex.ReportingSessionId.Length != 32)
        throw new InvalidOperationException("Plex playback did not return an opaque reporting-session handle.");
    var requestCountBeforeInvalidReport = handler.Requests.Count;
    try
    {
        await service.ReportPlaybackAsync(
            "00000000000000000000000000000000",
            MediaCenterPlaybackState.Playing,
            60_000,
            7_200_000);
        throw new InvalidOperationException("An unknown media-center reporting session was accepted.");
    }
    catch (InvalidOperationException exception) when (
        exception.Message.Contains("no longer active", StringComparison.OrdinalIgnoreCase))
    {
    }
    if (handler.Requests.Count != requestCountBeforeInvalidReport)
        throw new InvalidOperationException("An unknown reporting session reached the media-server network.");
    try
    {
        await service.ReportPlaybackAsync(
            resolvedPlex.ReportingSessionId,
            (MediaCenterPlaybackState)99,
            60_000,
            7_200_000);
        throw new InvalidOperationException("An invalid media-center playback state was accepted.");
    }
    catch (ArgumentOutOfRangeException)
    {
    }
    if (handler.Requests.Count != requestCountBeforeInvalidReport)
        throw new InvalidOperationException("An invalid playback state reached the media-server network.");
    await service.ReportPlaybackAsync(
        resolvedPlex.ReportingSessionId,
        MediaCenterPlaybackState.Playing,
        60_000,
        7_200_000,
        volume: 82);
    await service.ReportPlaybackAsync(
        resolvedPlex.ReportingSessionId,
        MediaCenterPlaybackState.Paused,
        75_000,
        7_200_000,
        volume: 82);
    await service.StopPlaybackReportingAsync(
        resolvedPlex.ReportingSessionId,
        90_000,
        7_200_000,
        volume: 82);
    var plexTimelines = handler.Requests
        .Where(request => request.Host == "plex.local" && request.Path == "/:/timeline")
        .ToList();
    if (plexTimelines.Count != 3 ||
        plexTimelines.Any(request => request.Method != "POST" ||
            request.Headers.GetValueOrDefault("X-Plex-Token") != plexToken ||
            request.Headers.GetValueOrDefault("X-Plex-Session-Identifier") != resolvedPlex.ReportingSessionId))
        throw new InvalidOperationException("Plex timeline reporting was not bound to the protected playback session.");
    var plexTimelineQueries = plexTimelines
        .Select(request => Uri.UnescapeDataString(new Uri(request.Uri).Query))
        .ToList();
    if (!plexTimelineQueries[0].Contains("state=playing", StringComparison.Ordinal) ||
        !plexTimelineQueries[1].Contains("state=paused", StringComparison.Ordinal) ||
        !plexTimelineQueries[2].Contains("state=stopped", StringComparison.Ordinal) ||
        plexTimelineQueries.Any(query =>
            !query.Contains("ratingKey=100", StringComparison.Ordinal) ||
            !query.Contains("key=/library/metadata/100", StringComparison.Ordinal) ||
            query.Contains(plexToken, StringComparison.Ordinal)))
        throw new InvalidOperationException("Plex timeline reporting did not preserve state, item, and secret-isolation semantics.");

    var emby = await service.ConnectEmbyAsync(
        embyBaseUrl,
        "feature-probe",
        embyPassword,
        "Probe Emby",
        allowInsecureHttp: false);
    if (emby.Channels is not [var embyItem] ||
        !embyItem.IsProtectedMedia ||
        embyItem.Kind != ChannelKind.Series ||
        embyItem.ResumePositionMilliseconds != 90_000 ||
        embyItem.Url.Contains(embyToken, StringComparison.Ordinal) ||
        embyItem.Url.Contains(embyPassword, StringComparison.Ordinal))
        throw new InvalidOperationException("The Emby catalog did not produce one token-free resumable episode.");
    var embyLocator = MediaCenterSecurity.ParsePlaybackLocator(embyItem.Url);
    if (embyLocator.Provider != "emby" || embyLocator.ServerId != "emby-server-1" || embyLocator.ItemId != "200")
        throw new InvalidOperationException("The Emby catalog locator was not canonical.");
    var resolvedEmby = await service.ResolvePlaybackAsync(embyItem);
    var resolvedEmbyUri = new Uri(resolvedEmby.Url);
    if (!resolvedEmbyUri.Query.Contains($"api_key={Uri.EscapeDataString(embyToken)}", StringComparison.Ordinal) ||
        resolvedEmbyUri.Query.Contains("upstream-emby-token", StringComparison.Ordinal) ||
        resolvedEmbyUri.Host != "emby.local")
        throw new InvalidOperationException("Emby playback did not materialize only the bound credential on the original server.");
    if (string.IsNullOrWhiteSpace(resolvedEmby.ReportingSessionId) ||
        resolvedEmby.ReportingSessionId.Contains(embyToken, StringComparison.Ordinal) ||
        resolvedEmby.ReportingSessionId.Length != 32)
        throw new InvalidOperationException("Emby playback did not return an opaque reporting-session handle.");
    await service.ReportPlaybackAsync(
        resolvedEmby.ReportingSessionId,
        MediaCenterPlaybackState.Playing,
        90_000,
        2_700_000,
        volume: 70);
    await service.ReportPlaybackAsync(
        resolvedEmby.ReportingSessionId,
        MediaCenterPlaybackState.Playing,
        100_000,
        2_700_000,
        volume: 70);
    await service.ReportPlaybackAsync(
        resolvedEmby.ReportingSessionId,
        MediaCenterPlaybackState.Paused,
        105_000,
        2_700_000,
        isMuted: true,
        volume: 70);
    await service.ReportPlaybackAsync(
        resolvedEmby.ReportingSessionId,
        MediaCenterPlaybackState.Playing,
        110_000,
        2_700_000,
        volume: 70);
    await service.StopPlaybackReportingAsync(
        resolvedEmby.ReportingSessionId,
        120_000,
        2_700_000,
        volume: 70);
    var embyReports = handler.Requests
        .Where(request => request.Host == "emby.local" && request.Path.StartsWith("/emby/Sessions/Playing", StringComparison.Ordinal))
        .ToList();
    var expectedEmbyPaths = new[]
    {
        "/emby/Sessions/Playing",
        "/emby/Sessions/Playing/Progress",
        "/emby/Sessions/Playing/Progress",
        "/emby/Sessions/Playing/Progress",
        "/emby/Sessions/Playing/Stopped"
    };
    if (embyReports.Count != expectedEmbyPaths.Length ||
        embyReports.Where((request, index) =>
            request.Method != "POST" ||
            request.Path != expectedEmbyPaths[index] ||
            request.Headers.GetValueOrDefault("X-Emby-Token") != embyToken).Any())
        throw new InvalidOperationException("Emby playback check-ins did not follow the start/progress/stop lifecycle.");
    for (var index = 0; index < embyReports.Count; index++)
    {
        var body = embyReports[index].Body ?? throw new InvalidOperationException("An Emby playback check-in had no body.");
        if (body.Contains(embyToken, StringComparison.Ordinal) || body.Contains(embyPassword, StringComparison.Ordinal))
            throw new InvalidOperationException("An Emby secret leaked into playback progress metadata.");
        using var reportDocument = JsonDocument.Parse(body);
        var report = reportDocument.RootElement;
        if (report.GetProperty("ItemId").GetString() != "200" ||
            report.GetProperty("MediaSourceId").GetString() != "source-200" ||
            report.GetProperty("PlaySessionId").GetString() != "play-session-1" ||
            report.GetProperty("PlayMethod").GetString() != "DirectPlay")
            throw new InvalidOperationException("An Emby playback check-in lost its bound playback context.");
    }
    using (var pausedReport = JsonDocument.Parse(embyReports[2].Body!))
    {
        if (!pausedReport.RootElement.GetProperty("IsPaused").GetBoolean() ||
            !pausedReport.RootElement.GetProperty("IsMuted").GetBoolean() ||
            pausedReport.RootElement.GetProperty("PositionTicks").GetInt64() != 1_050_000_000 ||
            pausedReport.RootElement.GetProperty("EventName").GetString() != "Pause")
            throw new InvalidOperationException("Emby pause progress did not preserve state and position.");
    }
    using (var timeReport = JsonDocument.Parse(embyReports[1].Body!))
    using (var resumedReport = JsonDocument.Parse(embyReports[3].Body!))
    {
        if (timeReport.RootElement.GetProperty("EventName").GetString() != "TimeUpdate" ||
            resumedReport.RootElement.GetProperty("EventName").GetString() != "Unpause")
            throw new InvalidOperationException("Emby progress check-ins did not identify time, pause, and unpause events.");
    }
    var requestCountAfterStops = handler.Requests.Count;
    await service.StopPlaybackReportingAsync(resolvedEmby.ReportingSessionId, 120_000, 2_700_000);
    if (handler.Requests.Count != requestCountAfterStops)
        throw new InvalidOperationException("A completed playback session emitted duplicate stop check-ins.");

    var plexIdentity = handler.Requests.First(request => request.Host == "plex.local" && request.Path == "/identity");
    if (plexIdentity.Headers.ContainsKey("X-Plex-Token") || plexIdentity.Uri.Contains(plexToken, StringComparison.Ordinal))
        throw new InvalidOperationException("The Plex token was sent before public server identity verification.");
    var embyIdentity = handler.Requests.First(request => request.Host == "emby.local" && request.Path == "/emby/System/Info/Public");
    if (embyIdentity.Headers.ContainsKey("X-Emby-Token") ||
        embyIdentity.Headers.GetValueOrDefault("X-Emby-Authorization")?.Contains("Token=", StringComparison.OrdinalIgnoreCase) == true)
        throw new InvalidOperationException("The Emby token was sent before public server identity verification.");
    if (!handler.Requests.Any(request => request.Headers.GetValueOrDefault("X-Plex-Token") == plexToken) ||
        !handler.Requests.Any(request => request.Headers.GetValueOrDefault("X-Emby-Token") == embyToken))
        throw new InvalidOperationException("Protected media-center catalog requests did not carry their bound credentials.");

    var serializedCatalogs = JsonSerializer.Serialize(new[] { plex, emby });
    if (serializedCatalogs.Contains(plexToken, StringComparison.Ordinal) ||
        serializedCatalogs.Contains(embyToken, StringComparison.Ordinal) ||
        serializedCatalogs.Contains(embyPassword, StringComparison.Ordinal))
        throw new InvalidOperationException("A media-center secret leaked into the playlist model.");

    var cacheDirectory = Path.Combine(testRoot, "media-center-playlist-caches");
    var cache = new PlaylistCacheStore(Path.Combine(testRoot, "media-center-legacy-cache.bin"), cacheDirectory);
    await cache.SaveAsync("plex", plexBaseUrl, plex);
    await cache.SaveAsync("emby", embyBaseUrl, emby);
    var cachedPlex = await cache.TryLoadAsync("plex", plexBaseUrl);
    var cachedEmby = await cache.TryLoadAsync("emby", embyBaseUrl);
    if (cachedPlex?.Playlist.Channels is not [var cachedPlexItem] || !cachedPlexItem.IsProtectedMedia ||
        cachedEmby?.Playlist.Channels is not [var cachedEmbyItem] || !cachedEmbyItem.IsProtectedMedia ||
        cachedPlexItem.ResumePositionMilliseconds != 60_000 || cachedEmbyItem.ResumePositionMilliseconds != 90_000)
        throw new InvalidOperationException("Protected media-center cache metadata did not round-trip.");

    var protectedFiles = Directory.EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories)
        .Append(credentialPath)
        .ToList();
    foreach (var protectedFile in protectedFiles)
    {
        var protectedText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(protectedFile));
        if (protectedText.Contains(plexToken, StringComparison.Ordinal) ||
            protectedText.Contains(embyToken, StringComparison.Ordinal) ||
            protectedText.Contains(embyPassword, StringComparison.Ordinal))
            throw new InvalidOperationException("A media-center secret was written in clear text.");
    }

    var reloadedPlex = await service.LoadSavedAsync("plex", plexBaseUrl);
    var reloadedEmby = await service.LoadSavedAsync("emby", embyBaseUrl);
    if (reloadedPlex.Channels.Count != 1 || reloadedEmby.Channels.Count != 1)
        throw new InvalidOperationException("Saved media-center credentials could not refresh their catalogs.");

    var deletedSourcePlayback = await service.ResolvePlaybackAsync(plexItem);
    var requestsBeforeSourceDelete = handler.Requests.Count;
    await service.DeleteCredentialAsync("plex", plexBaseUrl);
    try
    {
        await service.ReportPlaybackAsync(
            deletedSourcePlayback.ReportingSessionId!,
            MediaCenterPlaybackState.Playing,
            120_000,
            7_200_000);
        throw new InvalidOperationException("Deleting a media-center credential left its playback reporter active.");
    }
    catch (InvalidOperationException exception) when (
        exception.Message.Contains("no longer active", StringComparison.OrdinalIgnoreCase))
    {
    }
    if (handler.Requests.Count != requestsBeforeSourceDelete)
        throw new InvalidOperationException("A deleted media-center source emitted a playback report.");

    await RunPlexAccountDiscoverySelfTestAsync(testRoot, plexToken, embyPassword, embyToken);
}

static async Task RunPlexAccountDiscoverySelfTestAsync(
    string testRoot,
    string plexToken,
    string embyPassword,
    string embyToken)
{
    const string accountToken = "plex-probe-account-token";
    const string secureConnection = "https://plex.local:32400";
    const string insecureConnection = "http://plex.local:32400";

    var lockedHandler = new MediaCenterProbeHandler(plexToken, embyPassword, embyToken, accountToken);
    var lockedService = new MediaCenterSourceService(
        new MediaCenterCredentialStore(Path.Combine(testRoot, "locked-plex-account-credentials.bin")),
        new HttpClient(lockedHandler),
        "streamvue-locked-plex-account-probe",
        PremiumAccessPolicy.Evaluate("store", hasVerifiedStorePurchase: false),
        plexIdentityStore: new PlexDeviceIdentityStore(Path.Combine(testRoot, "locked-plex-device.bin")));
    try
    {
        await lockedService.StartPlexAccountSignInAsync();
        throw new InvalidOperationException("A locked Windows Store service started Plex account discovery.");
    }
    catch (InvalidOperationException exception) when (
        exception.Message.Contains("one-time store purchase", StringComparison.OrdinalIgnoreCase))
    {
    }
    if (lockedHandler.Requests.Count != 0)
        throw new InvalidOperationException("Locked Plex account discovery reached the network.");

    var identityPath = Path.Combine(testRoot, "plex-account-device-identity.bin");
    var credentialPath = Path.Combine(testRoot, "plex-account-credentials.bin");
    var handler = new MediaCenterProbeHandler(plexToken, embyPassword, embyToken, accountToken);
    var service = new MediaCenterSourceService(
        new MediaCenterCredentialStore(credentialPath),
        new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) },
        "streamvue-plex-account-probe",
        plexIdentityStore: new PlexDeviceIdentityStore(identityPath));

    var challenge = await service.StartPlexAccountSignInAsync();
    if (!challenge.AuthorizationUrl.StartsWith("https://app.plex.tv/auth#?", StringComparison.Ordinal) ||
        challenge.Code != "SVUE-700" ||
        handler.PlexPublicJwk is null ||
        handler.PlexPublicJwk.ContainsKey("d"))
        throw new InvalidOperationException("Windows Plex sign-in did not create a strong public-key PIN challenge.");
    var identityText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(identityPath));
    if (identityText.Contains("Ed25519", StringComparison.Ordinal) ||
        identityText.Contains("streamvue-plex-account-probe", StringComparison.Ordinal) ||
        handler.PlexPublicJwk.Values.Any(value => identityText.Contains(value, StringComparison.Ordinal)))
        throw new InvalidOperationException("The DPAPI Plex device identity exposed clear signing material.");

    var discovery = await service.WaitForPlexAccountServersAsync(challenge);
    if (!handler.PlexDeviceProofVerified ||
        discovery.Servers is not [var server] ||
        server.ServerId != "plex-server-1" ||
        server.Connections.Count != 2 ||
        server.PreferredConnection?.Url != secureConnection)
        throw new InvalidOperationException("Signed Plex account discovery did not return the expected token-free server choices.");
    var serializedDiscovery = JsonSerializer.Serialize(discovery);
    if (serializedDiscovery.Contains(accountToken, StringComparison.Ordinal) ||
        serializedDiscovery.Contains(plexToken, StringComparison.Ordinal))
        throw new InvalidOperationException("Plex account discovery exposed an account or server token.");

    try
    {
        await service.ConnectDiscoveredPlexServerAsync(
            discovery.SessionId,
            server.ServerId,
            "https://attacker.invalid:32400",
            allowInsecureHttp: false);
        throw new InvalidOperationException("Plex account discovery accepted an unlisted server address.");
    }
    catch (InvalidDataException)
    {
    }
    try
    {
        await service.ConnectDiscoveredPlexServerAsync(
            discovery.SessionId,
            server.ServerId,
            insecureConnection,
            allowInsecureHttp: false);
        throw new InvalidOperationException("Plex account discovery accepted HTTP without explicit consent.");
    }
    catch (ArgumentException)
    {
    }
    var playlist = await service.ConnectDiscoveredPlexServerAsync(
        discovery.SessionId,
        server.ServerId,
        secureConnection,
        allowInsecureHttp: false);
    if (playlist.Channels.Count != 1)
        throw new InvalidOperationException("The selected Plex account server did not load its catalog.");
    var saved = await new MediaCenterCredentialStore(credentialPath)
        .TryLoadForSourceAsync("plex", secureConnection);
    if (saved?.AccessToken != plexToken || saved.Binding.ServerId != "plex-server-1")
        throw new InvalidOperationException("The discovered Plex credential was not bound after public identity verification.");

    var mismatchHandler = new MediaCenterProbeHandler(plexToken, embyPassword, embyToken, accountToken)
    {
        PlexIdentityServerId = "changed-plex-server"
    };
    var mismatchCredentialPath = Path.Combine(testRoot, "mismatched-plex-account-credentials.bin");
    var mismatchService = new MediaCenterSourceService(
        new MediaCenterCredentialStore(mismatchCredentialPath),
        new HttpClient(mismatchHandler),
        "streamvue-mismatched-plex-account-probe",
        plexIdentityStore: new PlexDeviceIdentityStore(Path.Combine(testRoot, "mismatched-plex-device.bin")));
    var mismatchChallenge = await mismatchService.StartPlexAccountSignInAsync();
    var mismatchDiscovery = await mismatchService.WaitForPlexAccountServersAsync(mismatchChallenge);
    try
    {
        await mismatchService.ConnectDiscoveredPlexServerAsync(
            mismatchDiscovery.SessionId,
            "plex-server-1",
            secureConnection,
            allowInsecureHttp: false);
        throw new InvalidOperationException("A changed Plex server identity was accepted.");
    }
    catch (InvalidDataException)
    {
    }
    if (File.Exists(mismatchCredentialPath))
        throw new InvalidOperationException("A changed Plex server identity wrote a credential.");

    var cancellationChallenge = await mismatchService.StartPlexAccountSignInAsync();
    var cancellationDiscovery = await mismatchService.WaitForPlexAccountServersAsync(cancellationChallenge);
    mismatchService.CancelPlexAccountDiscovery(cancellationDiscovery.SessionId);
    try
    {
        await mismatchService.ConnectDiscoveredPlexServerAsync(
            cancellationDiscovery.SessionId,
            "plex-server-1",
            secureConnection,
            allowInsecureHttp: false);
        throw new InvalidOperationException("A cancelled Plex discovery lease remained usable.");
    }
    catch (InvalidOperationException exception) when (
        exception.Message.Contains("expired", StringComparison.OrdinalIgnoreCase))
    {
    }

    var runtimeAccess = PremiumAccessPolicy.Evaluate(
        "store",
        hasVerifiedStorePurchase: true,
        productId: "com.streamvue.personal-media-centers");
    var revocationHandler = new MediaCenterProbeHandler(plexToken, embyPassword, embyToken, accountToken);
    var revocationService = new MediaCenterSourceService(
        new MediaCenterCredentialStore(Path.Combine(testRoot, "revoked-plex-account-credentials.bin")),
        new HttpClient(revocationHandler),
        "streamvue-revoked-plex-account-probe",
        premiumAccessProvider: () => runtimeAccess,
        plexIdentityStore: new PlexDeviceIdentityStore(Path.Combine(testRoot, "revoked-plex-device.bin")));
    var revocationChallenge = await revocationService.StartPlexAccountSignInAsync();
    var revocationDiscovery = await revocationService.WaitForPlexAccountServersAsync(revocationChallenge);
    runtimeAccess = PremiumAccessPolicy.Evaluate("store", hasVerifiedStorePurchase: false);
    try
    {
        await revocationService.ConnectDiscoveredPlexServerAsync(
            revocationDiscovery.SessionId,
            "plex-server-1",
            secureConnection,
            allowInsecureHttp: false);
        throw new InvalidOperationException("A revoked premium entitlement retained Plex discovery access.");
    }
    catch (InvalidOperationException exception) when (
        exception.Message.Contains("one-time store purchase", StringComparison.OrdinalIgnoreCase))
    {
    }
    runtimeAccess = PremiumAccessPolicy.Evaluate(
        "store",
        hasVerifiedStorePurchase: true,
        productId: "com.streamvue.personal-media-centers");
    try
    {
        await revocationService.ConnectDiscoveredPlexServerAsync(
            revocationDiscovery.SessionId,
            "plex-server-1",
            secureConnection,
            allowInsecureHttp: false);
        throw new InvalidOperationException("A Plex discovery lease survived entitlement revocation.");
    }
    catch (InvalidOperationException exception) when (
        exception.Message.Contains("expired", StringComparison.OrdinalIgnoreCase))
    {
    }

    Console.WriteLine("Signed Plex account discovery, protected device identity, and revocable server lease: PASS");
}

static async Task<int> RunDvrSelfTestAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"streamvue-dvr-probe-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var wavePath = Path.Combine(root, "source.wav");
        var sourcePath = Path.Combine(root, "source.ts");
        await WriteProbeWaveAsync(wavePath, TimeSpan.FromSeconds(5));
        await CreateProbeTransportStreamAsync(wavePath, sourcePath);
        var channel = new ChannelItem
        {
            Number = 1,
            Name = "DVR Probe",
            Group = "Quality assurance",
            Url = new Uri(sourcePath).AbsoluteUri,
            Kind = ChannelKind.Live
        };

        DvrRecordingSnapshot snapshot;
        using (var dvr = new DvrRecordingService())
        {
            snapshot = dvr.Start(channel, Path.Combine(root, "recordings"), "Original quality self-test");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(120);
                snapshot = dvr.Poll(DateTimeOffset.UtcNow);
                if (snapshot.State == DvrRecordingState.Failed)
                    throw new InvalidOperationException(snapshot.Message ?? "The DVR recording self-test failed.");
                if (snapshot.State == DvrRecordingState.Recording)
                {
                    await Task.Delay(1_250);
                    break;
                }
            }
            snapshot = dvr.Stop("DVR self-test complete");
        }

        if (snapshot.State != DvrRecordingState.Completed || snapshot.BytesWritten < 4_096 ||
            string.IsNullOrWhiteSpace(snapshot.OutputPath) || !File.Exists(snapshot.OutputPath))
            throw new InvalidOperationException(
                $"The DVR did not finalize a playable transport-stream file " +
                $"(state={snapshot.State}, bytes={snapshot.BytesWritten:N0}, exists={File.Exists(snapshot.OutputPath)}, " +
                $"message={snapshot.Message ?? "none"}).");

        var recordingChannel = new ChannelItem
        {
            Number = 0,
            Name = "DVR Playback Probe",
            Group = "DVR Library",
            Url = new Uri(snapshot.OutputPath).AbsoluteUri,
            Kind = ChannelKind.Recording
        };
        using (var playback = new NativePlaybackEngine(new PlaybackPreferences()))
        {
            if (!playback.Play(recordingChannel))
                throw new InvalidOperationException("The native player rejected the saved DVR recording.");
            var playbackDeadline = DateTimeOffset.UtcNow.AddSeconds(6);
            PlaybackSnapshot playbackSnapshot;
            do
            {
                await Task.Delay(120);
                playbackSnapshot = playback.GetSnapshot();
            } while (!playbackSnapshot.IsPlaying && DateTimeOffset.UtcNow < playbackDeadline);
            if (!playbackSnapshot.IsPlaying || playbackSnapshot.TuneStrategy != "Local recording")
                throw new InvalidOperationException("The saved DVR recording did not enter local-recording playback mode.");
            if (playbackSnapshot.Length > 500 && !playback.SeekTo(playbackSnapshot.Length / 2))
                throw new InvalidOperationException("The saved DVR recording could not be seeked.");
            playback.Stop();
        }

        Console.WriteLine($"Live DVR transport-stream recording: PASS ({snapshot.BytesWritten:N0} bytes)");
        Console.WriteLine("Native DVR library playback and seeking: PASS");
        return 0;
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static async Task CreateProbeTransportStreamAsync(string wavePath, string outputPath)
{
    Core.Initialize();
    using var libVlc = new LibVLC("--intf=dummy", "--no-video-title-show", "--quiet");
    using var mediaPlayer = new MediaPlayer(libVlc);
    using var media = new Media(libVlc, new Uri(wavePath));
    var normalized = Path.GetFullPath(outputPath).Replace('\\', '/').Replace("'", "\\'", StringComparison.Ordinal);
    media.AddOption(":sout=#transcode{acodec=mpga,ab=128,channels=1,samplerate=48000}:" +
                    $"std{{access=file,mux=ts,dst='{normalized}'}}");
    media.AddOption(":sout-all");

    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    void OnEndReached(object? sender, EventArgs e) => completed.TrySetResult();
    void OnError(object? sender, EventArgs e) => completed.TrySetException(
        new InvalidOperationException("LibVLC could not generate the DVR transport-stream fixture."));
    mediaPlayer.EndReached += OnEndReached;
    mediaPlayer.EncounteredError += OnError;
    try
    {
        mediaPlayer.Media = media;
        if (!mediaPlayer.Play())
            throw new InvalidOperationException("LibVLC could not start the DVR transport-stream fixture.");
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(12));
    }
    finally
    {
        mediaPlayer.EndReached -= OnEndReached;
        mediaPlayer.EncounteredError -= OnError;
        if (mediaPlayer.IsPlaying) mediaPlayer.Stop();
        mediaPlayer.Media = null;
    }

    if (!File.Exists(outputPath) || new FileInfo(outputPath).Length < 4_096)
        throw new InvalidOperationException("LibVLC generated an empty DVR transport-stream fixture.");
}

static async Task WriteProbeWaveAsync(string path, TimeSpan duration)
{
    const int sampleRate = 48_000;
    const short channels = 1;
    const short bitsPerSample = 16;
    var sampleCount = (int)(sampleRate * duration.TotalSeconds);
    var dataLength = sampleCount * channels * bitsPerSample / 8;
    await using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
    writer.Write(Encoding.ASCII.GetBytes("RIFF"));
    writer.Write(36 + dataLength);
    writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
    writer.Write(16);
    writer.Write((short)1);
    writer.Write(channels);
    writer.Write(sampleRate);
    writer.Write(sampleRate * channels * bitsPerSample / 8);
    writer.Write((short)(channels * bitsPerSample / 8));
    writer.Write(bitsPerSample);
    writer.Write(Encoding.ASCII.GetBytes("data"));
    writer.Write(dataLength);
    for (var sample = 0; sample < sampleCount; sample++)
    {
        var value = (short)(Math.Sin(2 * Math.PI * 440 * sample / sampleRate) * short.MaxValue * 0.18);
        writer.Write(value);
    }
    await stream.FlushAsync();
}

sealed record MediaCenterProbeRequest(
    string Method,
    string Uri,
    string Host,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    string? Body);

sealed class MediaCenterProbeHandler(
    string plexToken,
    string embyPassword,
    string embyToken,
    string plexAccountToken = "plex-probe-account-token") : HttpMessageHandler
{
    public List<MediaCenterProbeRequest> Requests { get; } = [];
    public IReadOnlyDictionary<string, string>? PlexPublicJwk { get; private set; }
    public bool PlexDeviceProofVerified { get; private set; }
    public string PlexIdentityServerId { get; init; } = "plex-server-1";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("The media-center probe received no URI.");
        var headers = request.Headers
            .ToDictionary(pair => pair.Key, pair => string.Join(",", pair.Value), StringComparer.OrdinalIgnoreCase);
        if (request.Content is not null)
        {
            foreach (var pair in request.Content.Headers)
                headers[pair.Key] = string.Join(",", pair.Value);
        }
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new MediaCenterProbeRequest(
            request.Method.Method,
            uri.ToString(),
            uri.Host,
            uri.AbsolutePath,
            headers,
            body));

        string? json = uri.Host switch
        {
            "plex.local" => PlexResponse(request, uri, headers),
            "emby.local" => EmbyResponse(request, uri, headers, body),
            "clients.plex.tv" => PlexClientsResponse(request, uri, headers, body),
            "plex.tv" => PlexAccountResponse(request, uri, headers),
            _ => null
        };
        if (json is null)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":\"probe route not found\"}", Encoding.UTF8, "application/json")
            };
        }
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private string? PlexResponse(
        HttpRequestMessage request,
        Uri uri,
        IReadOnlyDictionary<string, string> headers)
    {
        if (request.Method == HttpMethod.Get && uri.AbsolutePath == "/identity")
        {
            if (headers.ContainsKey("X-Plex-Token"))
                throw new InvalidOperationException("The Plex identity probe received a token.");
            return JsonSerializer.Serialize(new
            {
                MediaContainer = new
                {
                    machineIdentifier = PlexIdentityServerId,
                    friendlyName = "Probe Plex Server",
                    version = "1.42.0"
                }
            });
        }
        RequireHeader(headers, "X-Plex-Token", plexToken);
        return uri.AbsolutePath switch
        {
            "/library/sections" => """
                                   {"MediaContainer":{"Directory":[{"key":"1","title":"Movies","type":"movie"}]}}
                                   """,
            "/library/sections/1/all" => """
                                         {"MediaContainer":{"totalSize":1,"Metadata":[{"ratingKey":"100","title":"Probe Movie","type":"movie","duration":7200000,"viewOffset":60000,"viewCount":0}]}}
                                         """,
            "/library/metadata/100" => """
                                       {"MediaContainer":{"Metadata":[{"Media":[{"Part":[{"key":"/library/parts/part-1/file.mkv?X-Plex-Token=upstream-plex-token"}]}]}]}}
                                       """,
            "/:/timeline" when request.Method == HttpMethod.Post => """{"MediaContainer":{"size":0}}""",
            _ => null
        };
    }

    private string? PlexClientsResponse(
        HttpRequestMessage request,
        Uri uri,
        IReadOnlyDictionary<string, string> headers,
        string? body)
    {
        if (request.Method == HttpMethod.Post && uri.AbsolutePath == "/api/v2/pins")
        {
            using var document = JsonDocument.Parse(body ?? throw new InvalidOperationException("The Plex PIN request had no body."));
            var root = document.RootElement;
            if (!root.TryGetProperty("strong", out var strong) || strong.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("jwk", out var jwk) || jwk.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("The Windows Plex PIN request was not strong-signed.");
            var publicJwk = jwk.EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value.GetString() ?? string.Empty,
                StringComparer.Ordinal);
            if (publicJwk.ContainsKey("d") ||
                publicJwk.GetValueOrDefault("kty") != "OKP" ||
                publicJwk.GetValueOrDefault("crv") != "Ed25519" ||
                publicJwk.GetValueOrDefault("alg") != "EdDSA" ||
                string.IsNullOrWhiteSpace(publicJwk.GetValueOrDefault("x")) ||
                string.IsNullOrWhiteSpace(publicJwk.GetValueOrDefault("kid")))
                throw new InvalidOperationException("The Windows Plex PIN request exposed an invalid public JWK.");
            PlexPublicJwk = publicJwk;
            return """
                   {"id":700,"code":"SVUE-700","expiresIn":300}
                   """;
        }
        if (request.Method == HttpMethod.Get && uri.AbsolutePath == "/api/v2/pins/700")
        {
            var proof = ReadQueryValue(uri, "deviceJWT")
                ?? throw new InvalidOperationException("The Plex PIN poll omitted its device proof.");
            VerifyPlexDeviceProof(proof, headers);
            PlexDeviceProofVerified = true;
            return JsonSerializer.Serialize(new { authToken = plexAccountToken, expiresIn = 300 });
        }
        if (request.Method == HttpMethod.Get && uri.AbsolutePath == "/api/v2/resources")
        {
            RequireHeader(headers, "X-Plex-Token", plexAccountToken);
            return JsonSerializer.Serialize(new[]
            {
                new
                {
                    name = "Probe Plex Server",
                    clientIdentifier = "plex-server-1",
                    provides = "server",
                    owned = true,
                    accessToken = plexToken,
                    connections = new object[]
                    {
                        new { uri = "http://plex.local:32400", local = true, relay = false, IPv6 = false },
                        new { uri = "https://plex.local:32400", local = true, relay = false, IPv6 = false }
                    }
                }
            });
        }
        return null;
    }

    private string? PlexAccountResponse(
        HttpRequestMessage request,
        Uri uri,
        IReadOnlyDictionary<string, string> headers)
    {
        if (request.Method != HttpMethod.Get || uri.AbsolutePath != "/api/v2/user") return null;
        RequireHeader(headers, "X-Plex-Token", plexAccountToken);
        return """{"id":42,"username":"feature-probe"}""";
    }

    private void VerifyPlexDeviceProof(
        string compactJwt,
        IReadOnlyDictionary<string, string> headers)
    {
        var jwk = PlexPublicJwk ?? throw new InvalidOperationException("The Plex proof arrived before its public JWK.");
        var parts = compactJwt.Split('.');
        if (parts.Length != 3) throw new InvalidOperationException("The Plex device proof was not a compact JWT.");
        using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
        using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        if (header.RootElement.GetProperty("alg").GetString() != "EdDSA" ||
            header.RootElement.GetProperty("kid").GetString() != jwk["kid"] ||
            payload.RootElement.GetProperty("aud").GetString() != "plex.tv" ||
            payload.RootElement.GetProperty("iss").GetString() != headers.GetValueOrDefault("X-Plex-Client-Identifier"))
            throw new InvalidOperationException("The Plex device proof claims were invalid.");
        var issuedAt = payload.RootElement.GetProperty("iat").GetInt64();
        var expiresAt = payload.RootElement.GetProperty("exp").GetInt64();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (issuedAt > now + 60 || expiresAt <= now || expiresAt - issuedAt > 300)
            throw new InvalidOperationException("The Plex device proof lifetime was invalid.");
        var publicKey = PublicKey.Import(
            SignatureAlgorithm.Ed25519,
            Base64UrlDecode(jwk["x"]),
            KeyBlobFormat.RawPublicKey);
        if (!SignatureAlgorithm.Ed25519.Verify(
                publicKey,
                Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"),
                Base64UrlDecode(parts[2])))
            throw new InvalidOperationException("The Plex device proof signature was invalid.");
    }

    private static string? ReadQueryValue(Uri uri, string name)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (Uri.UnescapeDataString(pair[0]) == name)
                return pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
        }
        return null;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }

    private string? EmbyResponse(
        HttpRequestMessage request,
        Uri uri,
        IReadOnlyDictionary<string, string> headers,
        string? body)
    {
        if (request.Method == HttpMethod.Get && uri.AbsolutePath == "/emby/System/Info/Public")
        {
            if (headers.ContainsKey("X-Emby-Token") ||
                headers.GetValueOrDefault("X-Emby-Authorization")?.Contains("Token=", StringComparison.OrdinalIgnoreCase) == true)
                throw new InvalidOperationException("The Emby identity probe received a token.");
            return """
                   {"Id":"emby-server-1","ServerName":"Probe Emby Server","Version":"4.9.0"}
                   """;
        }
        if (request.Method == HttpMethod.Post && uri.AbsolutePath == "/emby/Users/AuthenticateByName")
        {
            if (headers.ContainsKey("X-Emby-Token") || body?.Contains(embyPassword, StringComparison.Ordinal) != true)
                throw new InvalidOperationException("The Emby authentication request was malformed.");
            return JsonSerializer.Serialize(new
            {
                AccessToken = embyToken,
                ServerId = "emby-server-1",
                User = new { Id = "user-1", Name = "Feature Probe" }
            });
        }
        RequireHeader(headers, "X-Emby-Token", embyToken);
        return uri.AbsolutePath switch
        {
            "/emby/Users/user-1/Views" => """
                                           {"Items":[{"Id":"library-1","Name":"Shows","CollectionType":"tvshows"}]}
                                           """,
            "/emby/Users/user-1/Items" => """
                                           {"TotalRecordCount":1,"Items":[{"Id":"200","Name":"Probe Episode","Type":"Episode","SeriesName":"Probe Series","ParentIndexNumber":1,"IndexNumber":2,"RunTimeTicks":27000000000,"UserData":{"PlaybackPositionTicks":900000000,"Played":false}}]}
                                           """,
            "/emby/Items/200/PlaybackInfo" => """
                                                {"PlaySessionId":"play-session-1","MediaSources":[{"Id":"source-200","Container":"mkv","SupportsDirectPlay":true,"SupportsDirectStream":true,"SupportsTranscoding":true,"DirectStreamUrl":"/Videos/200/stream.mkv?api_key=upstream-emby-token","RequiredHttpHeaders":{"Referer":"https://emby.local/player"}}]}
                                                """,
            "/emby/Sessions/Playing" when request.Method == HttpMethod.Post => "{}",
            "/emby/Sessions/Playing/Progress" when request.Method == HttpMethod.Post => "{}",
            "/emby/Sessions/Playing/Stopped" when request.Method == HttpMethod.Post => "{}",
            _ => null
        };
    }

    private static void RequireHeader(
        IReadOnlyDictionary<string, string> headers,
        string name,
        string expected)
    {
        if (!headers.TryGetValue(name, out var actual) || actual != expected)
            throw new InvalidOperationException($"The media-center probe expected the {name} credential header.");
    }
}

sealed class MicrosoftStoreProbeClient : IMicrosoftStoreClient
{
    public MicrosoftStoreProduct? Product { get; init; }
    public bool OwnsProduct { get; set; }
    public MicrosoftStorePurchaseOutcome NextPurchaseOutcome { get; set; } =
        MicrosoftStorePurchaseOutcome.NotPurchased;
    public int LicenseQueries { get; private set; }

    public event EventHandler? LicenseChanged;

    public Task<MicrosoftStoreProduct?> GetDurableProductAsync(string productId) =>
        Task.FromResult(Product?.ProductId == productId ? Product : null);

    public Task<bool> OwnsDurableProductAsync(string productId)
    {
        LicenseQueries++;
        return Task.FromResult(OwnsProduct && Product?.ProductId == productId);
    }

    public Task<MicrosoftStorePurchaseOutcome> PurchaseAsync(string productId) =>
        Task.FromResult(Product?.ProductId == productId
            ? NextPurchaseOutcome
            : MicrosoftStorePurchaseOutcome.Unknown);

    public void RaiseLicenseChanged() => LicenseChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
    }
}
