using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Windows.Media;
using StreamVue.Player.Services;

namespace StreamVue.Player.Models;

public enum ChannelKind
{
    Live,
    Movie,
    Series,
    Recording,
    Replay
}

public sealed class ChannelItem : INotifyPropertyChanged
{
    private bool _isFavorite;
    private string? _currentProgramTitle;
    private string? _nextProgramTitle;
    private string? _currentProgramTime;
    private string? _signalRouteKey;
    private int _signalFeedCount = 1;
    private ImageSource? _artworkSource;

    public required int Number { get; init; }
    public required string Name { get; init; }
    public required string Url { get; init; }
    public string Group { get; init; } = "Uncategorized";
    public string? LogoUrl { get; init; }
    public string? TvgId { get; init; }
    public string? TvgName { get; init; }
    public string? UserAgent { get; init; }
    public string? Referrer { get; init; }
    public ChannelKind Kind { get; init; }
    public Guid? SourceId { get; init; }
    public string? SourceName { get; init; }
    public string? CatchupMode { get; init; }
    public string? CatchupSource { get; init; }
    public int CatchupDays { get; init; }
    public int CatchupCorrectionMinutes { get; init; }
    public long DurationMilliseconds { get; init; }
    public long ResumePositionMilliseconds { get; init; }
    public bool IsPlayed { get; init; }
    public string? MediaLibraryTitle { get; init; }
    public string? SeriesTitle { get; init; }
    public int? SeasonNumber { get; init; }
    public int? EpisodeNumber { get; init; }
    public int? ReleaseYear { get; init; }
    public DateTimeOffset? AddedAtUtc { get; init; }
    public DateTimeOffset? LastPlayedAtUtc { get; init; }
    public bool HasCatchup => Kind == ChannelKind.Live && !string.IsNullOrWhiteSpace(CatchupSource);
    public bool IsProtectedMedia => MediaCenterSecurity.IsPlaybackLocator(Url);
    public bool CanResume => ResumePositionMilliseconds >= 30_000 &&
                             (DurationMilliseconds <= 0 || ResumePositionMilliseconds < DurationMilliseconds - 30_000);
    public bool HasWatchProgress => IsProtectedMedia && CanResume && DurationMilliseconds > 0;
    public double WatchProgressPercent => HasWatchProgress
        ? Math.Clamp(ResumePositionMilliseconds * 100d / DurationMilliseconds, 0, 100)
        : 0;
    public string? WatchProgressLabel
    {
        get
        {
            if (!HasWatchProgress) return null;
            var minutesRemaining = Math.Max(1, (DurationMilliseconds - ResumePositionMilliseconds) / 60_000);
            return $"Continue • {WatchProgressPercent:0}% • {FormatMinutes(minutesRemaining)} left";
        }
    }

    public string? LibraryMetadataLine
    {
        get
        {
            if (!IsProtectedMedia) return null;
            var parts = new List<string>(3);
            if (Kind == ChannelKind.Series && !string.IsNullOrWhiteSpace(SeriesTitle)) parts.Add(SeriesTitle);
            else if (!string.IsNullOrWhiteSpace(MediaLibraryTitle)) parts.Add(MediaLibraryTitle);
            if (ReleaseYear is > 1800 and < 3000) parts.Add(ReleaseYear.Value.ToString());
            if (DurationMilliseconds > 0) parts.Add(FormatDuration(DurationMilliseconds));
            return parts.Count == 0 ? Group : string.Join(" • ", parts);
        }
    }

    public string? SignalRouteKey
    {
        get => _signalRouteKey;
        set
        {
            if (_signalRouteKey == value) return;
            _signalRouteKey = value;
            OnPropertyChanged();
        }
    }

    public int SignalFeedCount
    {
        get => _signalFeedCount;
        set
        {
            var normalized = Math.Max(1, value);
            if (_signalFeedCount == normalized) return;
            _signalFeedCount = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAlternateFeeds));
            OnPropertyChanged(nameof(SignalFeedLabel));
        }
    }

    public bool HasAlternateFeeds => SignalFeedCount > 1;

    public string SignalFeedLabel => HasAlternateFeeds
        ? $"{SignalFeedCount:N0} FEEDS"
        : IsProtectedMedia && IsPlayed
            ? "WATCHED"
            : IsProtectedMedia && CanResume
                ? "RESUME"
                : KindLabel;

    [JsonIgnore]
    public ImageSource? ArtworkSource
    {
        get => _artworkSource;
        set
        {
            if (ReferenceEquals(_artworkSource, value)) return;
            _artworkSource = value;
            OnPropertyChanged();
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteGlyph));
            OnPropertyChanged(nameof(FavoriteLabel));
        }
    }

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    public string FavoriteLabel => IsFavorite ? "Remove from favorites" : "Add to favorites";

    public string? CurrentProgramTitle
    {
        get => _currentProgramTitle;
        private set
        {
            if (_currentProgramTitle == value) return;
            _currentProgramTitle = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GuideLine));
        }
    }

    public string? NextProgramTitle
    {
        get => _nextProgramTitle;
        private set
        {
            if (_nextProgramTitle == value) return;
            _nextProgramTitle = value;
            OnPropertyChanged();
        }
    }

    public string? CurrentProgramTime
    {
        get => _currentProgramTime;
        private set
        {
            if (_currentProgramTime == value) return;
            _currentProgramTime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GuideLine));
        }
    }

    public string? GuideLine => string.IsNullOrWhiteSpace(CurrentProgramTitle)
        ? null
        : $"NOW  {CurrentProgramTitle}  •  {CurrentProgramTime}";

    public void ApplyGuide(EpgNowNext? guide)
    {
        CurrentProgramTitle = guide?.Current?.Title;
        CurrentProgramTime = guide?.Current?.LocalTimeRange;
        NextProgramTitle = guide?.Next?.Title;
    }

    public string StableKey
    {
        get
        {
            var endpoint = Url.Trim();
            var queryOrFragment = endpoint.IndexOfAny(['?', '#']);
            if (queryOrFragment >= 0) endpoint = endpoint[..queryOrFragment];
            var identity = !string.IsNullOrWhiteSpace(TvgId)
                ? $"tvg:{TvgId.Trim().ToUpperInvariant()}|name:{Name.Trim().ToUpperInvariant()}|group:{Group.Trim().ToUpperInvariant()}|endpoint:{endpoint}"
                : $"name:{Name.Trim().ToUpperInvariant()}|group:{Group.Trim().ToUpperInvariant()}|endpoint:{endpoint}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        }
    }

    public string GuideMappingKey
    {
        get
        {
            var identity = !string.IsNullOrWhiteSpace(TvgId)
                ? $"tvg:{TvgId.Trim().ToUpperInvariant()}|name:{Name.Trim().ToUpperInvariant()}|group:{Group.Trim().ToUpperInvariant()}"
                : $"name:{Name.Trim().ToUpperInvariant()}|group:{Group.Trim().ToUpperInvariant()}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        }
    }

    public string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "TV";
            if (words.Length == 1) return words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant();
            return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
        }
    }

    public string KindLabel => Kind switch
    {
        ChannelKind.Movie => "MOVIE",
        ChannelKind.Series => "SERIES",
        ChannelKind.Recording => "RECORDING",
        ChannelKind.Replay => "REPLAY",
        _ => "LIVE"
    };

    public string SearchText =>
        $"{Name}\n{Group}\n{TvgName}\n{SourceName}\n{MediaLibraryTitle}\n{SeriesTitle}\n{ReleaseYear}".ToUpperInvariant();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatMinutes(long totalMinutes)
    {
        if (totalMinutes < 60) return $"{totalMinutes}m";
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
    }

    private static string FormatDuration(long milliseconds) =>
        FormatMinutes(Math.Max(1, milliseconds / 60_000));
}
