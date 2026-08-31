using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StreamVue.Player.Models;
using StreamVue.Player.Services;
using Image = System.Windows.Controls.Image;

namespace StreamVue.Player;

public partial class MainWindow
{
    private const int MaximumRetainedArtworkItems = 160;
    private readonly SemaphoreSlim _artworkLoadGate = new(4, 4);
    private readonly Dictionary<string, Task<ImageSource?>> _artworkInFlight = new(StringComparer.Ordinal);
    private readonly Queue<ChannelItem> _artworkRetention = new();
    private readonly HashSet<ChannelItem> _retainedArtworkItems = [];
    private CancellationTokenSource _artworkCancellation = new();

    private async void ChannelArtwork_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image { DataContext: ChannelItem channel }) return;
        await EnsureArtworkAsync(channel);
    }

    private void ShowChannelArtwork(ChannelItem channel)
    {
        InspectorArtwork.DataContext = channel;
        _ = EnsureArtworkAsync(channel);
    }

    private async Task EnsureArtworkAsync(ChannelItem channel)
    {
        if (channel.ArtworkSource is not null ||
            !_premiumAccess.CanUseMediaCenters ||
            !MediaCenterSecurity.IsArtworkLocator(channel.LogoUrl))
            return;

        var locator = channel.LogoUrl!;
        var cancellationToken = _artworkCancellation.Token;
        if (!_artworkInFlight.TryGetValue(locator, out var load))
        {
            load = LoadArtworkImageAsync(locator, cancellationToken);
            _artworkInFlight[locator] = load;
        }

        try
        {
            var image = await load;
            if (image is null || cancellationToken.IsCancellationRequested ||
                !string.Equals(channel.LogoUrl, locator, StringComparison.Ordinal))
                return;
            channel.ArtworkSource = image;
            RetainArtwork(channel);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Artwork is optional; initials remain visible if a server image is unavailable.
        }
        finally
        {
            if (_artworkInFlight.TryGetValue(locator, out var active) && ReferenceEquals(active, load))
                _artworkInFlight.Remove(locator);
        }
    }

    private async Task<ImageSource?> LoadArtworkImageAsync(string locator, CancellationToken cancellationToken)
    {
        var entered = false;
        try
        {
            await _artworkLoadGate.WaitAsync(cancellationToken);
            entered = true;
            var bytes = await _mediaCenterSource.LoadArtworkAsync(locator, 320, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return DecodeArtwork(bytes);
        }
        finally
        {
            if (entered) _artworkLoadGate.Release();
        }
    }

    private static ImageSource? DecodeArtwork(byte[] bytes)
    {
        if (bytes.Length == 0) return null;
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelWidth = 320;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void RetainArtwork(ChannelItem channel)
    {
        if (!_retainedArtworkItems.Add(channel)) return;
        _artworkRetention.Enqueue(channel);
        var remaining = _artworkRetention.Count;
        while (_retainedArtworkItems.Count > MaximumRetainedArtworkItems && remaining-- > 0)
        {
            var evicted = _artworkRetention.Dequeue();
            if (ReferenceEquals(evicted, _currentChannel))
            {
                _artworkRetention.Enqueue(evicted);
                continue;
            }
            _retainedArtworkItems.Remove(evicted);
            evicted.ArtworkSource = null;
        }
    }

    private void ResetArtworkLoading(bool reloadCurrent = false)
    {
        var previous = _artworkCancellation;
        _artworkCancellation = new CancellationTokenSource();
        try { previous.Cancel(); }
        catch (ObjectDisposedException) { }
        previous.Dispose();
        _mediaCenterSource.CancelAllArtworkRequests();
        _artworkInFlight.Clear();
        foreach (var item in _retainedArtworkItems) item.ArtworkSource = null;
        _retainedArtworkItems.Clear();
        _artworkRetention.Clear();
        if (reloadCurrent && _currentChannel is not null) ShowChannelArtwork(_currentChannel);
    }
}
