using System.IO;
using System.Text.Json.Serialization;

namespace StreamVue.Player.Models;

public enum DvrRecordingState
{
    Idle,
    Starting,
    Recording,
    Stopping,
    Completed,
    Failed
}

public enum DvrSchedulePriority
{
    Low = 0,
    Normal = 1,
    High = 2
}

public enum DvrEpisodeSelection
{
    AllEpisodes,
    NewEpisodesOnly
}

public sealed class SmartDvrPreferences
{
    public int StartPaddingMinutes { get; set; } = 1;
    public int EndPaddingMinutes { get; set; } = 2;
    public int StorageReserveGigabytes { get; set; } = 5;
    public DvrSchedulePriority DefaultPriority { get; set; } = DvrSchedulePriority.Normal;
    public bool BackgroundRecordingEnabled { get; set; } = true;
    public bool WakeForRecordings { get; set; } = true;
    public bool LiveTimeshiftEnabled { get; set; } = true;
    public int LiveTimeshiftMinutes { get; set; } = 60;
    public int MaximumRecoveryAttempts { get; set; } = 3;
    public DvrEpisodeSelection DefaultEpisodeSelection { get; set; } = DvrEpisodeSelection.AllEpisodes;
    public int DefaultKeepLatestCount { get; set; }
}

public sealed class SeriesRecordingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ChannelKey { get; set; }
    public required string ChannelName { get; set; }
    public required string ProgramTitle { get; set; }
    public DvrSchedulePriority Priority { get; set; } = DvrSchedulePriority.Normal;
    public int StartPaddingMinutes { get; set; } = 1;
    public int EndPaddingMinutes { get; set; } = 2;
    public DvrEpisodeSelection EpisodeSelection { get; set; } = DvrEpisodeSelection.AllEpisodes;
    public int KeepLatestCount { get; set; }
    public bool AnyChannel { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record DvrRecordingSnapshot(
    DvrRecordingState State,
    string? ChannelKey = null,
    string? ChannelName = null,
    string? ProgramTitle = null,
    string? OutputPath = null,
    DateTimeOffset? StartedUtc = null,
    DateTimeOffset? StopUtc = null,
    long BytesWritten = 0,
    Guid? ScheduleId = null,
    string? Message = null)
{
    public static DvrRecordingSnapshot Idle { get; } = new(DvrRecordingState.Idle, Message: "Ready to record");

    public bool IsActive => State is DvrRecordingState.Starting or DvrRecordingState.Recording or DvrRecordingState.Stopping;

    public TimeSpan Elapsed(DateTimeOffset now) => StartedUtc is null
        ? TimeSpan.Zero
        : now - StartedUtc.Value < TimeSpan.Zero ? TimeSpan.Zero : now - StartedUtc.Value;
}

public sealed class ScheduledRecording
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ChannelKey { get; set; }
    public required string ChannelName { get; set; }
    public required string ProgramTitle { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset StopUtc { get; set; }
    public DateTimeOffset? ProgrammeStartUtc { get; set; }
    public DateTimeOffset? ProgrammeStopUtc { get; set; }
    public int StartPaddingMinutes { get; set; }
    public int EndPaddingMinutes { get; set; }
    public DvrSchedulePriority Priority { get; set; } = DvrSchedulePriority.Normal;
    public Guid? SeriesRuleId { get; set; }
    public string? EpisodeKey { get; set; }
    public string? EpisodeLabel { get; set; }
    public bool? IsNewEpisode { get; set; }
    public int RecoveryAttempts { get; set; }
    public DateTimeOffset? NextRecoveryUtc { get; set; }
    public string? LastInterruption { get; set; }
    public List<string> OutputPaths { get; set; } = [];
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "Scheduled";
    public string? Detail { get; set; }
    public string? OutputPath { get; set; }

    [JsonIgnore]
    public DateTimeOffset GuideStartUtc => ProgrammeStartUtc ?? StartUtc.AddMinutes(StartPaddingMinutes);

    [JsonIgnore]
    public DateTimeOffset GuideStopUtc => ProgrammeStopUtc ?? StopUtc.AddMinutes(-EndPaddingMinutes);
}

public sealed record DvrScheduleRow(
    ScheduledRecording Recording,
    bool HasConflict = false,
    bool WinsConflict = false)
{
    public Guid Id => Recording.Id;
    public string ProgramTitle => Recording.ProgramTitle;
    public string ChannelName => Recording.ChannelName;
    public string TimeText => $"{Recording.GuideStartUtc.ToLocalTime():ddd, MMM d • h:mm tt} – {Recording.GuideStopUtc.ToLocalTime():h:mm tt}";
    public string StatusText => Recording.Status switch
    {
        "Conflict" => "SKIPPED",
        "Recovering" => "RECOVERING",
        "Partial" => "PARTIAL",
        "Expired" => "AUTO-DELETED",
        _ when HasConflict && Recording.Status == "Scheduled" => WinsConflict ? "PRIORITY WIN" : "AT RISK",
        _ => Recording.Status.ToUpperInvariant()
    };
    public string DetailText => Recording.Status == "Conflict"
        ? Recording.Detail ?? "A higher-priority recording used the available tuner"
        : Recording.Status == "Recovering"
        ? Recording.Detail ?? "Stream interrupted • preparing another original-quality segment"
        : HasConflict && Recording.Status == "Scheduled"
        ? WinsConflict
            ? $"{PriorityText} priority wins this overlap"
            : $"{PriorityText} priority may yield to another schedule"
        : string.IsNullOrWhiteSpace(Recording.Detail)
        ? Recording.Status switch
        {
            "Scheduled" => Recording.SeriesRuleId is null
                ? "Background recorder is armed for this program"
                : "Added automatically by an advanced series rule",
            "Recording" => "Recording is in progress",
            "Completed" => "Saved to the OrbitalVue recordings folder",
            "Partial" => "A playable segment was preserved after the provider stream ended",
            "Missed" => "The recorder was not running during this program",
            "Failed" => "The provider stream could not be recorded",
            "Expired" => "Removed by this series rule's retention limit",
            _ => Recording.Status
        }
        : Recording.Detail!;
    public bool CanCancel => Recording.Status is "Scheduled" or "Recording" or "Recovering";
    public bool CanAdjustPriority => Recording.Status is "Scheduled" or "Recovering";
    public string PriorityText => Recording.Priority.ToString().ToUpperInvariant();
    public string SourceText => Recording.SeriesRuleId is null ? "ONE TIME" : "SERIES";
    public string PaddingText => Recording.StartPaddingMinutes == 0 && Recording.EndPaddingMinutes == 0
        ? "NO PADDING"
        : $"−{Recording.StartPaddingMinutes} / +{Recording.EndPaddingMinutes} MIN";
    public string EpisodeText => string.IsNullOrWhiteSpace(Recording.EpisodeLabel)
        ? Recording.IsNewEpisode == true ? "NEW EPISODE" : string.Empty
        : $"{Recording.EpisodeLabel}{(Recording.IsNewEpisode == true ? " • NEW" : string.Empty)}";
    public string EstimateText
    {
        get
        {
            var duration = Recording.StopUtc - Recording.StartUtc;
            var estimatedBytes = Math.Max(0, duration.TotalSeconds) * 1_000_000d;
            var estimate = estimatedBytes >= 1_073_741_824d
                ? $"~{estimatedBytes / 1_073_741_824d:0.0} GB"
                : $"~{estimatedBytes / 1_048_576d:0} MB";
            return $"{estimate} • {Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes)):N0} min";
        }
    }
    public string PriorityActionLabel => Recording.Priority == DvrSchedulePriority.High ? "Reset" : "↑ Priority";
}

public sealed record DvrSeriesRuleRow(SeriesRecordingRule Rule, int UpcomingCount)
{
    public Guid Id => Rule.Id;
    public string ProgramTitle => Rule.ProgramTitle;
    public string ChannelName => Rule.ChannelName;
    public string PriorityText => Rule.Priority.ToString().ToUpperInvariant();
    public string DetailText => $"{UpcomingCount:N0} upcoming • −{Rule.StartPaddingMinutes} / +{Rule.EndPaddingMinutes} min";
    public string EpisodeModeText => Rule.EpisodeSelection == DvrEpisodeSelection.NewEpisodesOnly ? "NEW ONLY" : "ALL AIRINGS";
    public string RetentionText => Rule.KeepLatestCount <= 0 ? "KEEP ALL" : $"KEEP {Rule.KeepLatestCount}";
    public string ChannelScopeText => Rule.AnyChannel ? "ANY CHANNEL" : "THIS CHANNEL";
    public string PriorityActionLabel => Rule.Priority == DvrSchedulePriority.High ? "Reset" : "↑ Priority";
}

public sealed record DvrCalendarDayRow(DateOnly? Date, string Label, int RecordingCount, double EstimatedBytes, bool IsSelected)
{
    public string DateText => Date is null ? "NEXT 14 DAYS" : Date.Value.ToString("ddd • MMM d");
    public string SummaryText
    {
        get
        {
            var size = EstimatedBytes >= 1_073_741_824d
                ? $"~{EstimatedBytes / 1_073_741_824d:0.0} GB"
                : $"~{EstimatedBytes / 1_048_576d:0} MB";
            return RecordingCount == 0 ? "No recordings" : $"{RecordingCount:N0} recording{(RecordingCount == 1 ? string.Empty : "s")} • {size}";
        }
    }
}

public sealed record DvrLibraryItem(string FilePath, DateTimeOffset ModifiedUtc, long Bytes, string? StatusLabel = null)
{
    public string Name => Path.GetFileNameWithoutExtension(FilePath);
    public string Detail => $"{ModifiedUtc.ToLocalTime():MMM d, yyyy • h:mm tt}  •  {FormatBytes(Bytes)}" +
                            (string.IsNullOrWhiteSpace(StatusLabel) ? string.Empty : $"  •  {StatusLabel}");

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824d:0.0} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576d:0.0} MB";
        if (bytes >= 1_024) return $"{bytes / 1_024d:0.0} KB";
        return $"{bytes:N0} bytes";
    }
}

public sealed class DvrPlaybackProgress
{
    public long PositionMilliseconds { get; set; }
    public long DurationMilliseconds { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed record DvrLibraryRow(
    DvrLibraryItem Recording,
    string LibraryKey,
    DvrPlaybackProgress? Progress,
    bool IsPlaying,
    bool CanDelete)
{
    public string FilePath => Recording.FilePath;
    public string Name => Recording.Name;
    public long Bytes => Recording.Bytes;
    public bool CanResume => Progress is
    {
        PositionMilliseconds: >= 30_000,
        DurationMilliseconds: > 0
    } && Progress.DurationMilliseconds - Progress.PositionMilliseconds >= 30_000;
    public string ActionLabel => IsPlaying ? "Playing" : CanResume ? "Resume" : "Play";
    public string Detail => CanResume
        ? $"{Recording.Detail}  •  Resume at {FormatDuration(Progress!.PositionMilliseconds)}"
        : Recording.Detail;
    public double ProgressPercent => CanResume
        ? Math.Clamp(Progress!.PositionMilliseconds / (double)Progress.DurationMilliseconds * 100d, 0d, 100d)
        : 0d;
    public bool ShowProgress => CanResume;

    private static string FormatDuration(long milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }
}

public sealed record DvrStorageSnapshot(
    bool IsAvailable,
    long TotalBytes,
    long FreeBytes,
    long RecordingBytes,
    int RecordingCount)
{
    public double DriveUsedPercent => TotalBytes <= 0
        ? 0
        : Math.Clamp((TotalBytes - FreeBytes) / (double)TotalBytes * 100d, 0d, 100d);
}
