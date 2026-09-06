using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace OrbitalVue.Player;

public partial class MainWindow
{
    private DispatcherTimer? _guideRenderRecoveryTimer;
    private ICollectionView? _guideTimelineRowsRecoveryView;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((MainWindow)sender).StartGuideRenderRecovery()),
            handledEventsToo: true);
    }

    private void StartGuideRenderRecovery()
    {
        if (_guideRenderRecoveryTimer is not null) return;

        _guideRenderRecoveryTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => RecoverGuidePresentation(),
            Dispatcher);

        GuideFilterBox.SelectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(RecoverGuidePresentation, DispatcherPriority.Background);
        GuideSearchBox.TextChanged += (_, _) =>
            Dispatcher.BeginInvoke(RecoverGuidePresentation, DispatcherPriority.Background);
        Closed += (_, _) => _guideRenderRecoveryTimer?.Stop();

        _guideRenderRecoveryTimer.Start();
        Dispatcher.BeginInvoke(RecoverGuidePresentation, DispatcherPriority.Loaded);
    }

    private void RecoverGuidePresentation()
    {
        if (_guideSchedule is null || _channels.Count == 0) return;

        // A successful guide refresh can finish before the paired timeline ItemsControls have
        // materialised their shared collection view. The schedule and coverage are then valid,
        // but the UI can remain on the empty-state overlay indefinitely. Rebuild the model rows
        // first if necessary, then bind independent views to the channel and programme panes.
        if (_guideTimelineRows.Count == 0 && _channels.Any(channel => channel.Kind == Models.ChannelKind.Live))
            RebuildGuideTimeline();

        var expectedTimelineCount = _guideTimelineRows.Count(row => FilterGuideTimelineRow(row));
        var expectedListCount = _guideRows.Count(row => FilterGuideRow(row));

        if (expectedTimelineCount > 0 &&
            (GuideTimelineChannels.Items.Count != expectedTimelineCount ||
             GuideTimelineRows.Items.Count != expectedTimelineCount))
        {
            RebindGuideTimelineViews();
        }

        if (expectedListCount > 0 && GuideList.Items.Count != expectedListCount)
        {
            var listView = new ListCollectionView(_guideRows.ToList())
            {
                Filter = FilterGuideRow
            };
            _guideView = listView;
            GuideList.ItemsSource = listView;
        }

        var expectedVisibleCount = _guideTimelineMode ? expectedTimelineCount : expectedListCount;
        GuideEmptyState.Visibility = expectedVisibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateGuideCoveragePresentation();
    }

    private void RebindGuideTimelineViews()
    {
        var channelView = new ListCollectionView(_guideTimelineRows.ToList())
        {
            Filter = FilterGuideTimelineRow
        };
        var programmeView = new ListCollectionView(_guideTimelineRows.ToList())
        {
            Filter = FilterGuideTimelineRow
        };

        _guideTimelineView = channelView;
        _guideTimelineRowsRecoveryView = programmeView;
        GuideTimelineChannels.ItemsSource = channelView;
        GuideTimelineRows.ItemsSource = programmeView;
    }

    private void UpdateGuideCoveragePresentation()
    {
        if (_guideSchedule is null) return;

        var liveChannels = _channels.Count(channel => channel.Kind == Models.ChannelKind.Live);
        if (liveChannels == 0) return;

        var matched = _channels.Count(channel =>
            channel.Kind == Models.ChannelKind.Live && GetGuideProgrammes(channel).Count > 0);
        var manualCount = _guideMappings.Count(mapping =>
            _channels.Any(channel => channel.GuideMappingKey == mapping.Key));
        var percentage = matched * 100d / liveChannels;

        GuideCoverageText.Text = manualCount > 0
            ? $"{matched:N0}/{liveChannels:N0} matched • {percentage:0}% coverage • {manualCount:N0} manual"
            : $"{matched:N0}/{liveChannels:N0} matched • {percentage:0}% coverage";
    }
}
