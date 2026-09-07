using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrbitalVue.Player;

public partial class MainWindow
{
    // Smoke tests must verify generated templates, not just successfully parsed programme data.
    private GuideTimelineRenderState GetGuideTimelineRenderState() => new(
        _guideTimelineRows.Count(FilterGuideTimelineRow),
        GuideTimelineChannels.Items.Count,
        GuideTimelineRows.Items.Count,
        CountRenderedGuideRows(GuideTimelineChannels),
        CountRenderedGuideRows(GuideTimelineRows),
        GuideTimelinePanel.IsVisible && GuideTimelineChannels.IsVisible && GuideTimelineRows.IsVisible,
        GuideChannelScroll.ViewportWidth > 0 && GuideChannelScroll.ViewportHeight > 0 &&
        GuideTimelineScroll.ViewportWidth > 0 && GuideTimelineScroll.ViewportHeight > 0,
        GuideEmptyState.Visibility == Visibility.Visible);

    private static int CountRenderedGuideRows(ItemsControl control) => Enumerable.Range(0, control.Items.Count)
        .Count(index => control.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement
            { ActualWidth: > 0, ActualHeight: > 0 } container && HasRenderedGuideText(container));

    private static bool HasRenderedGuideText(DependencyObject element)
    {
        if (element is TextBlock { IsVisible: true, ActualWidth: > 0, ActualHeight: > 0 } text &&
            !string.IsNullOrWhiteSpace(text.Text)) return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (HasRenderedGuideText(VisualTreeHelper.GetChild(element, index))) return true;
        return false;
    }

    private sealed record GuideTimelineRenderState(
        int ExpectedRows,
        int ChannelItems,
        int ProgrammeItems,
        int RenderedChannelRows,
        int RenderedProgrammeRows,
        bool TimelineVisible,
        bool HasViewport,
        bool EmptyStateVisible)
    {
        public bool Passed => ExpectedRows > 0 &&
            ChannelItems == ExpectedRows && ProgrammeItems == ExpectedRows &&
            RenderedChannelRows == ExpectedRows && RenderedProgrammeRows == ExpectedRows &&
            TimelineVisible && HasViewport && !EmptyStateVisible;
    }
}
