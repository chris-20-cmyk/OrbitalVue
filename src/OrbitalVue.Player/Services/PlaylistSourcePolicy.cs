using System.IO;
using OrbitalVue.Player.Models;

namespace OrbitalVue.Player.Services;

public static class PlaylistSourcePolicy
{
    private static readonly HashSet<string> SupportedSourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "file", "url", "xtream", "plex", "emby"
    };

    public static bool NormalizeSettings(AppSettings settings)
    {
        settings.PlaylistSources ??= [];
        var changed = false;
        if (settings.PlaylistSources.Count == 0 &&
            IsSupportedSourceType(settings.LastSourceType) &&
            !string.IsNullOrWhiteSpace(settings.LastSource))
        {
            settings.PlaylistSources.Add(Create(
                settings.LastSourceType!,
                settings.LastSource!,
                sortOrder: 0));
            changed = true;
        }

        var ordered = settings.PlaylistSources
            .Where(source => source is not null)
            .OrderBy(source => source.SortOrder)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var ids = new HashSet<Guid>();
        var sourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<PlaylistSourceDefinition>(ordered.Count);
        foreach (var source in ordered)
        {
            var normalizedType = NormalizeSourceType(source.SourceType);
            var normalizedValue = source.SourceValue?.Trim() ?? string.Empty;
            var sourceKey = $"{normalizedType}|{NormalizeSourceValue(normalizedType, normalizedValue)}";
            if (!sourceKeys.Add(sourceKey))
            {
                changed = true;
                continue;
            }
            if (source.Id == Guid.Empty || !ids.Add(source.Id))
            {
                source.Id = Guid.NewGuid();
                ids.Add(source.Id);
                changed = true;
            }
            if (!string.Equals(source.SourceType, normalizedType, StringComparison.Ordinal))
            {
                source.SourceType = normalizedType;
                changed = true;
            }
            if (!string.Equals(source.SourceValue, normalizedValue, StringComparison.Ordinal))
            {
                source.SourceValue = normalizedValue;
                changed = true;
            }
            if (string.IsNullOrWhiteSpace(source.Name))
            {
                source.Name = CreateDefaultName(normalizedType, normalizedValue);
                changed = true;
            }
            else if (!string.Equals(source.Name, source.Name.Trim(), StringComparison.Ordinal))
            {
                source.Name = source.Name.Trim();
                changed = true;
            }
            if (source.SortOrder != normalized.Count)
            {
                source.SortOrder = normalized.Count;
                changed = true;
            }
            normalized.Add(source);
        }

        if (!settings.PlaylistSources.SequenceEqual(normalized))
        {
            settings.PlaylistSources = normalized;
            changed = true;
        }
        return changed;
    }

    public static PlaylistSourceDefinition GetOrAdd(
        AppSettings settings,
        string sourceType,
        string sourceValue,
        string? name = null)
    {
        NormalizeSettings(settings);
        var existing = settings.PlaylistSources.FirstOrDefault(source => Matches(source, sourceType, sourceValue));
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(name) &&
                (string.IsNullOrWhiteSpace(existing.Name) || existing.Name == CreateDefaultName(sourceType, sourceValue)))
                existing.Name = name.Trim();
            return existing;
        }

        var created = Create(sourceType, sourceValue, name, settings.PlaylistSources.Count);
        settings.PlaylistSources.Add(created);
        return created;
    }

    public static PlaylistSourceDefinition? Find(AppSettings settings, string sourceType, string sourceValue)
    {
        settings.PlaylistSources ??= [];
        return settings.PlaylistSources.FirstOrDefault(source => Matches(source, sourceType, sourceValue));
    }

    public static PlaylistSourceDefinition Create(
        string sourceType,
        string sourceValue,
        string? name = null,
        int sortOrder = 0) => new()
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? CreateDefaultName(sourceType, sourceValue)
            : name.Trim(),
        SourceType = NormalizeSourceType(sourceType),
        SourceValue = sourceValue.Trim(),
        SortOrder = Math.Max(0, sortOrder)
    };

    public static bool Matches(PlaylistSourceDefinition source, string sourceType, string sourceValue) =>
        string.Equals(source.SourceType, NormalizeSourceType(sourceType), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            NormalizeSourceValue(source.SourceType, source.SourceValue),
            NormalizeSourceValue(sourceType, sourceValue),
            StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedSourceType(string? sourceType) =>
        !string.IsNullOrWhiteSpace(sourceType) && SupportedSourceTypes.Contains(sourceType.Trim());

    public static string NormalizeSourceType(string? sourceType) =>
        IsSupportedSourceType(sourceType) ? sourceType!.Trim().ToLowerInvariant() : "file";

    public static string NormalizeSourceValue(string sourceType, string? sourceValue)
    {
        var value = sourceValue?.Trim() ?? string.Empty;
        if (NormalizeSourceType(sourceType) == "file")
        {
            try { return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return value; }
        }
        return value.TrimEnd('/', '\\');
    }

    public static string CreateDefaultName(string sourceType, string sourceValue)
    {
        sourceType = NormalizeSourceType(sourceType);
        if (sourceType == "file")
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(sourceValue.Trim());
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch
            {
            }
            return "Local playlist";
        }

        var candidate = sourceValue.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal)) candidate = $"http://{candidate}";
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? sourceType switch
            {
                "xtream" => $"{uri.Host} account",
                "plex" => $"{uri.Host} Plex",
                "emby" => $"{uri.Host} Emby",
                _ => uri.Host
            }
            : sourceType switch
            {
                "xtream" => "Xtream account",
                "plex" => "Plex library",
                "emby" => "Emby library",
                _ => "M3U source"
            };
    }
}

public static class PlaylistMergePolicy
{
    public static PlaylistMergeSummary Merge(IEnumerable<PlaylistSourceSnapshot> snapshots)
    {
        var ordered = snapshots
            .Where(snapshot => snapshot.Source.IsEnabled)
            .OrderBy(snapshot => snapshot.Source.SortOrder)
            .ThenBy(snapshot => snapshot.Source.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var merged = new List<ChannelItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inputCount = 0;
        var duplicateCount = 0;
        foreach (var snapshot in ordered)
        {
            foreach (var channel in snapshot.Playlist.Channels)
            {
                inputCount++;
                if (!seen.Add(channel.StableKey))
                {
                    duplicateCount++;
                    continue;
                }
                merged.Add(CloneForSource(channel, snapshot.Source, merged.Count + 1));
            }
        }

        var guideSources = ordered
            .SelectMany(snapshot => (snapshot.Playlist.GuideSource ?? string.Empty)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var loadedAt = ordered.Count == 0
            ? DateTimeOffset.UtcNow
            : ordered.Max(snapshot => snapshot.Playlist.LoadedAt);
        var displayName = ordered.Count switch
        {
            0 => "No enabled sources",
            1 => ordered[0].Playlist.DisplayName,
            _ => $"{ordered.Count:N0} sources • unified library"
        };
        var result = new PlaylistResult(
            merged,
            displayName,
            "multi-source library",
            loadedAt,
            guideSources.Count == 0 ? null : string.Join(Environment.NewLine, guideSources));
        return new PlaylistMergeSummary(result, ordered.Count, inputCount, duplicateCount);
    }

    private static ChannelItem CloneForSource(ChannelItem channel, PlaylistSourceDefinition source, int number) => new()
    {
        Number = number,
        Name = channel.Name,
        Url = channel.Url,
        Group = channel.Group,
        LogoUrl = channel.LogoUrl,
        TvgId = channel.TvgId,
        TvgName = channel.TvgName,
        UserAgent = channel.UserAgent,
        Referrer = channel.Referrer,
        CatchupMode = channel.CatchupMode,
        CatchupSource = channel.CatchupSource,
        CatchupDays = channel.CatchupDays,
        CatchupCorrectionMinutes = channel.CatchupCorrectionMinutes,
        DurationMilliseconds = channel.DurationMilliseconds,
        ResumePositionMilliseconds = channel.ResumePositionMilliseconds,
        IsPlayed = channel.IsPlayed,
        Kind = channel.Kind,
        SourceId = source.Id,
        SourceName = source.Name
    };
}
