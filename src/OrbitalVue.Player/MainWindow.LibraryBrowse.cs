using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using OrbitalVue.Player.Models;
using OrbitalVue.Player.Services;

namespace OrbitalVue.Player;

public partial class MainWindow
{
    private bool _libraryBrowseEnhancementsAttached;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += MainWindow_LibraryBrowseLoaded;
    }

    private void MainWindow_LibraryBrowseLoaded(object sender, RoutedEventArgs e)
    {
        if (_libraryBrowseEnhancementsAttached) return;
        _libraryBrowseEnhancementsAttached = true;

        if (AllFilter.Parent is StackPanel filterPanel)
        {
            foreach (var filter in filterPanel.Children.OfType<RadioButton>().ToList())
                filter.Checked += LibraryBrowseFilter_Checked;

            if (!filterPanel.Children.OfType<RadioButton>()
                    .Any(filter => string.Equals(filter.Tag?.ToString(), "Music", StringComparison.OrdinalIgnoreCase)))
            {
                var musicFilter = new RadioButton
                {
                    GroupName = "KindFilter",
                    Tag = MediaLibraryBrowseMode.Music.ToString(),
                    Content = "Music"
                };
                musicFilter.SetResourceReference(StyleProperty, "FilterPill");
                musicFilter.Checked += KindFilter_Checked;
                musicFilter.Checked += LibraryBrowseFilter_Checked;
                filterPanel.Children.Add(musicFilter);
            }
        }

        CategoryBox.SelectionChanged += LibraryCategoryBox_SelectionChanged;
        ScheduleLibraryBrowseHierarchy();
    }

    private void LibraryBrowseFilter_Checked(object sender, RoutedEventArgs e) =>
        ScheduleLibraryBrowseHierarchy();

    private void LibraryCategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ScheduleLibraryBrowseHierarchy();

    private void ScheduleLibraryBrowseHierarchy()
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            new Action(ApplyLibraryBrowseHierarchy));
    }

    private void ApplyLibraryBrowseHierarchy()
    {
        if (_channelView is null || _libraryBrowseMode != MediaLibraryBrowseMode.Series) return;

        var alreadyGroupedBySeries = _channelView.GroupDescriptions
            .OfType<PropertyGroupDescription>()
            .Any(group => string.Equals(
                group.PropertyName,
                nameof(ChannelItem.SeriesBrowseGroup),
                StringComparison.Ordinal));
        if (!alreadyGroupedBySeries)
        {
            _channelView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ChannelItem.SeriesBrowseGroup))
            {
                StringComparison = StringComparison.OrdinalIgnoreCase
            });
        }

        _channelView.SortDescriptions.Clear();
        _channelView.SortDescriptions.Add(new SortDescription(nameof(ChannelItem.SeriesBrowseGroup), ListSortDirection.Ascending));
        _channelView.SortDescriptions.Add(new SortDescription(nameof(ChannelItem.SeasonNumber), ListSortDirection.Ascending));
        _channelView.SortDescriptions.Add(new SortDescription(nameof(ChannelItem.EpisodeNumber), ListSortDirection.Ascending));
        _channelView.SortDescriptions.Add(new SortDescription(nameof(ChannelItem.Name), ListSortDirection.Ascending));
        _channelView.Refresh();
    }
}
