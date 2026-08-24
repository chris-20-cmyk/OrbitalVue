using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace StreamVue.Player.Models;

public enum ChannelKind
{
    Live,
    Movie,
    Series,
    Recording
}

public sealed class ChannelItem : INotifyPropertyChanged
{
    private bool _isFavorite;
    private string? _currentProgramTitle;
    private string? _nextProgramTitle;
    private string? _currentProgramTime;

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
        _ => "LIVE"
    };

    public string SearchText => $"{Name}\n{Group}\n{TvgName}\n{SourceName}".ToUpperInvariant();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
