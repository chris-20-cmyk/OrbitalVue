using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using OrbitalVue.Player;
using OrbitalVue.Player.Models;
using OrbitalVue.Player.Services;

internal static class GuideTimelineRenderProbe
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static int Run(string? artifactDirectory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RunOnDispatcher(artifactDirectory); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90)))
        {
            Console.Error.WriteLine("Guide timeline rendering: FAIL - WPF dispatcher timed out.");
            Environment.Exit(1);
        }
        if (failure is not null)
        {
            Console.Error.WriteLine($"Guide timeline rendering: FAIL - {failure}");
            return 1;
        }
        Console.WriteLine("Guide timeline rendering: PASS");
        return 0;
    }

    private static void RunOnDispatcher(string? artifactDirectory)
    {
        var previousDataRoot = Environment.GetEnvironmentVariable(OrbitalVueDataPaths.OverrideEnvironmentVariable);
        var testRoot = Path.Combine(Path.GetTempPath(), $"orbitalvue-guide-render-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable(OrbitalVueDataPaths.OverrideEnvironmentVariable, testRoot);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow? window = null;
        try
        {
            // Use the production resources without App.StartupUri opening another window or native playback.
            var appXaml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "GuideProbeApp.xaml")).Root!;
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var resources = new XElement(presentation + "ResourceDictionary",
                appXaml.Attributes().Where(attribute => attribute.IsNamespaceDeclaration),
                appXaml.Element(presentation + "Application.Resources")!.Nodes());
            app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
            typeof(App).Assembly.GetType("OrbitalVue.Player.Controls.UiUsabilityService")!
                .GetMethod("Enable", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
            window = new MainWindow();
            // Load the production window/templates without startup downloads, playback or user settings.
            window.Loaded -= Method("Window_Loaded").CreateDelegate<RoutedEventHandler>(window);
            Set(window, "_automationRun", true);
            Set(window, "_allowFinalClose", true);
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = -20000;
            window.Show();
            Drain(window);

            var now = DateTimeOffset.UtcNow;
            var start = (DateTimeOffset)Call(window, "AlignTimelineStart", now)!;
            var groups = new[] { "News", "Sports", "Movies", "General" };
            var channels = Enumerable.Range(0, 139).Select(index => new ChannelItem
            {
                Number = index + 1,
                Name = $"{groups[index % 4]} channel {index + 1:000}",
                Group = groups[index % 4],
                TvgId = index == 0 ? "manual-only" : $"TEST{index}",
                Url = $"https://guide-render.invalid/{index}",
                Kind = ChannelKind.Live,
                IsFavorite = index % 5 == 0
            }).ToList();
            var programmes = Enumerable.Range(0, 113).ToDictionary(index => $"TEST{index}", index =>
                (IReadOnlyList<EpgProgram>)Enumerable.Range(0, index == 0 ? 165 : 105).Select(slot =>
                    new EpgProgram($"TEST{index}", $"Programme {index:000}-{slot:000}", null, "Fixture category",
                        start.AddHours(-2).AddMinutes(slot * 30), start.AddHours(-2).AddMinutes((slot + 1) * 30))).ToList());
            var schedule = new EpgSchedule(programmes, new Dictionary<string, string>(), "Synthetic render fixture", now);
            Check(schedule.ProgramCount == 11925, "Fixture must contain 11,925 programmes.");
            Set(window, "_channels", channels);
            Set(window, "_guideMappings", new Dictionary<string, string> { [channels[0].GuideMappingKey] = "TEST0" });
            Call(window, "ApplyGuideSchedule", schedule);
            Call(window, "SetGuideReadyStatus", schedule, "synthetic fixture");
            Call(window, "SetGuideMode", true);
            Call(window, "SetGuideViewMode", true);
            Drain(window);
            AssertRows(window, channels, "schedule loaded before opening guide");
            Check(Find<TextBlock>(window, "GuideCoverageText").Text.Contains("81%"), "Expected 81% coverage.");
            var rows = Get<IReadOnlyList<GuideTimelineRow>>(window, "_guideTimelineRows");
            Check(rows.Count(row => row.HasSchedule) == 113, "Expected 113 matched channels, including the manual match.");
            Check(rows[0].MappingStatus == "MANUAL MATCH", "Manual mapping was lost.");
            Check(rows.Skip(113).All(row => row.Blocks is [{ IsPlaceholder: true }]), "Unmatched channels need placeholder blocks.");

            Set(window, "_guideMappings", new Dictionary<string, string>());
            Call(window, "ApplyGuideSchedule", schedule);
            WaitForGuidePresentation(window, 139, "manual mapping cleared");
            Check(!Get<IReadOnlyList<GuideTimelineRow>>(window, "_guideTimelineRows")[0].HasSchedule &&
                  !Find<TextBlock>(window, "GuideCoverageText").Text.Contains("manual"),
                "Clearing a manual mapping did not update the rows and coverage synchronously.");
            Set(window, "_guideMappings", new Dictionary<string, string> { [channels[0].GuideMappingKey] = "TEST0" });
            Call(window, "ApplyGuideSchedule", schedule);
            Drain(window);
            AssertRows(window, channels, "manual mapping reapplied");

            if (artifactDirectory is not null)
            {
                Directory.CreateDirectory(artifactDirectory);
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(artifactDirectory, "guide-timeline.png"));
                encoder.Save(file);
            }

            // Reproduce the presentation failure independently of any one-second recovery timer.
            Find<ItemsControl>(window, "GuideTimelineChannels").ItemsSource = Array.Empty<GuideTimelineRow>();
            Find<ItemsControl>(window, "GuideTimelineRows").ItemsSource = Array.Empty<GuideTimelineRow>();
            Find<FrameworkElement>(window, "GuideEmptyState").Visibility = Visibility.Visible;
            Call(window, "RefreshGuideViews");
            Check(Find<ItemsControl>(window, "GuideTimelineChannels").Items.Count == 139 &&
                  Find<ItemsControl>(window, "GuideTimelineRows").Items.Count == 139,
                "Refresh left populated timeline controls empty before dispatcher/timer recovery.");
            Check(Find<FrameworkElement>(window, "GuideEmptyState").Visibility == Visibility.Collapsed,
                "Refresh left the preparation overlay on a populated model.");
            AssertRows(window, channels, "synchronous rebind after detached sources");
            Check(RenderStatePassed(window), "Live-guide smoke verifier rejected populated rendered controls.");
            var programmeControl = Find<ItemsControl>(window, "GuideTimelineRows");
            var originalTemplate = programmeControl.Template;
            programmeControl.Template = new ControlTemplate(typeof(ItemsControl));
            Drain(window);
            Check(programmeControl.Items.Count == 139 && !RenderStatePassed(window),
                "Live-guide smoke verifier accepted items with no rendered programme templates.");
            programmeControl.Template = originalTemplate;
            AssertRows(window, channels, "restored programme template");

            foreach (var filter in new[] { "Favorites", "Unmatched", "Sports", "Movies", "News", "All" })
            {
                SelectFilter(window, filter);
                var expected = filter switch
                {
                    "Favorites" => channels.Where(channel => channel.IsFavorite).ToList(),
                    "Unmatched" => channels.Skip(113).ToList(),
                    "All" => channels,
                    _ => channels.Where(channel => channel.Group == filter).ToList()
                };
                AssertRows(window, expected, filter);
            }
            Find<TextBox>(window, "GuideSearchBox").Text = "Programme 001-";
            AssertRows(window, [channels[1]], "programme search");
            Find<TextBox>(window, "GuideSearchBox").Text = "no matching channel or programme";
            AssertRows(window, [], "empty search");
            Find<TextBox>(window, "GuideSearchBox").Clear();
            AssertRows(window, channels, "cleared search");
            SelectFilter(window, "Favorites");
            channels[1].IsFavorite = true;
            Call(window, "RefreshGuideViews");
            AssertRows(window, channels.Where(channel => channel.IsFavorite).ToList(), "favorite changed");
            SelectFilter(window, "All");

            Call(window, "GuideNextWindow_Click", null, new RoutedEventArgs());
            Check(Get<DateTimeOffset>(window, "_guideWindowStart") == start.AddMinutes(90), "Next did not advance 90 minutes.");
            AssertRows(window, channels, "next window");
            Call(window, "GuidePreviousWindow_Click", null, new RoutedEventArgs());
            Check(Get<DateTimeOffset>(window, "_guideWindowStart") == start, "Previous did not restore the window.");
            // A window without programmes must still show every channel's placeholder.
            Set(window, "_guideWindowStart", start.AddDays(-3));
            Call(window, "RebuildGuideTimeline");
            Call(window, "RefreshGuideViews");
            AssertRows(window, channels, "window outside schedule");
            Check(Get<IReadOnlyList<GuideTimelineRow>>(window, "_guideTimelineRows").All(row => row.Blocks.All(block => block.IsPlaceholder)),
                "Off-window schedules must retain placeholders.");
            Call(window, "GuideJumpNow_Click", null, new RoutedEventArgs());
            AssertRows(window, channels, "jump to now");

            var timelineScroll = Find<ScrollViewer>(window, "GuideTimelineScroll");
            var channelScroll = Find<ScrollViewer>(window, "GuideChannelScroll");
            timelineScroll.ScrollToVerticalOffset(760);
            timelineScroll.ScrollToHorizontalOffset(240);
            Drain(window);
            Check(timelineScroll.VerticalOffset > 0 && timelineScroll.HorizontalOffset > 0, "Timeline must scroll on both axes.");
            Check(Math.Abs(timelineScroll.VerticalOffset - channelScroll.VerticalOffset) < 1, "Channel scroll lost vertical alignment.");
            Check(Math.Abs(timelineScroll.HorizontalOffset - Find<ScrollViewer>(window, "GuideTimeHeaderScroll").HorizontalOffset) < 1,
                "Time header lost horizontal alignment.");
            channelScroll.ScrollToVerticalOffset(1520);
            Drain(window);
            Check(Math.Abs(timelineScroll.VerticalOffset - channelScroll.VerticalOffset) < 1, "Reverse vertical sync failed.");
            Call(window, "RefreshGuideViews");
            Drain(window);
            Check(Math.Abs(timelineScroll.VerticalOffset - channelScroll.VerticalOffset) < 1, "Rebinding broke vertical alignment.");

            Call(window, "SetGuideViewMode", false);
            Drain(window);
            Check(Find<ListBox>(window, "GuideList").Items.Count == 139 && Find<ListBox>(window, "GuideList").IsVisible,
                "Now/Next did not render the live lineup.");
            Find<TextBox>(window, "GuideSearchBox").Text = channels[1].Name;
            Drain(window);
            Check(Find<ListBox>(window, "GuideList").Items.Count == 1, "Now/Next search failed.");
            Find<TextBox>(window, "GuideSearchBox").Text = "no matches";
            Check(Find<FrameworkElement>(window, "GuideEmptyState").Visibility == Visibility.Visible, "Now/Next empty state failed.");
            Find<TextBox>(window, "GuideSearchBox").Clear();
            Call(window, "ApplyGuideSchedule", schedule);
            Call(window, "SetGuideViewMode", true);
            Drain(window);
            AssertRows(window, channels, "schedule refresh while in Now/Next");

            if (artifactDirectory is not null)
                File.WriteAllText(Path.Combine(artifactDirectory, "guide-render-result.json"), JsonSerializer.Serialize(new
                {
                    passed = true, programmes = schedule.ProgramCount, matchedChannels = 113, timelineRows = 139,
                    validation = "Synthetic Windows WPF window, production templates, dispatcher and visual containers; no live provider data"
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            window?.Close();
            app.Shutdown();
            Environment.SetEnvironmentVariable(OrbitalVueDataPaths.OverrideEnvironmentVariable, previousDataRoot);
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    private static void AssertRows(MainWindow window, IReadOnlyList<ChannelItem> expected, string scenario)
    {
        WaitForGuidePresentation(window, expected.Count, scenario);
        var channels = Find<ItemsControl>(window, "GuideTimelineChannels");
        var programmes = Find<ItemsControl>(window, "GuideTimelineRows");
        Check(channels.Items.Cast<GuideTimelineRow>().Select(row => row.Channel).SequenceEqual(expected), $"{scenario}: channel rows differ.");
        Check(programmes.Items.Cast<GuideTimelineRow>().Select(row => row.Channel).SequenceEqual(expected), $"{scenario}: programme rows differ.");
        Check(Find<FrameworkElement>(window, "GuideEmptyState").Visibility == (expected.Count == 0 ? Visibility.Visible : Visibility.Collapsed),
            $"{scenario}: empty-state visibility disagrees with the filtered model.");
        if (expected.Count > 0)
        {
            foreach (var control in new[] { channels, programmes })
            {
                Check(control.IsVisible && control.ActualWidth > 0 && control.ActualHeight > 0, $"{scenario}: pane has no visible layout.");
                for (var index = 0; index < expected.Count; index++)
                {
                    Check(control.ItemContainerGenerator.ContainerFromIndex(index) is ContentPresenter
                        { ActualWidth: > 0, ActualHeight: > 0 } presenter &&
                        Descendants<TextBlock>(presenter).Any(text => !string.IsNullOrWhiteSpace(text.Text) && text.ActualWidth > 0),
                        $"{scenario}: row {index} has items but no rendered template text.");
                }
            }
            Check(Descendants<Button>(programmes).Any(button => button.DataContext is GuideProgrammeBlock && button.ActualWidth > 0),
                $"{scenario}: no programme/placeholder cards were rendered.");
        }
        Console.WriteLine($"Guide render: {scenario}: {expected.Count} channel and programme rows; overlay {(expected.Count == 0 ? "visible" : "collapsed")}.");
    }

    private static void WaitForGuidePresentation(MainWindow window, int expectedRows, string scenario)
    {
        var expectedVersion = Get<int>(window, "_guidePresentationVersion");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            Drain(window);
            var rows = Get<IReadOnlyList<GuideTimelineRow>>(window, "_guideTimelineRows");
            var channels = Find<ItemsControl>(window, "GuideTimelineChannels");
            var programmes = Find<ItemsControl>(window, "GuideTimelineRows");
            if (expectedVersion == Get<int>(window, "_appliedGuidePresentationVersion") &&
                rows.Count > 0 &&
                channels.Items.Count == expectedRows &&
                programmes.Items.Count == expectedRows)
                return;
            Thread.Sleep(10);
        }

        throw new InvalidOperationException($"{scenario}: guide presentation did not complete within 10 seconds.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void SelectFilter(MainWindow window, string tag)
    {
        var box = Find<ComboBox>(window, "GuideFilterBox");
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == tag);
    }

    private static void Drain(MainWindow window)
    {
        DrainDispatcher(window.Dispatcher);
        window.UpdateLayout();
    }

    private static void DrainDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static MethodInfo Method(string name) => typeof(MainWindow).GetMethod(name, PrivateInstance | BindingFlags.Static)!;
    private static bool RenderStatePassed(MainWindow window)
    {
        var state = Call(window, "GetGuideTimelineRenderState")!;
        return (bool)state.GetType().GetProperty("Passed")!.GetValue(state)!;
    }
    private static object? Call(MainWindow window, string name, params object?[] args) => Method(name).Invoke(window, args);
    private static void Set(MainWindow window, string name, object value) => typeof(MainWindow).GetField(name, PrivateInstance)!.SetValue(window, value);
    private static T Get<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(window)!;
    private static T Find<T>(MainWindow window, string name) => (T)window.FindName(name);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

}
