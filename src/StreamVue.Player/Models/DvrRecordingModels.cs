using System.IO;

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
    public string Status { get; set; } = "Scheduled";
    public string? Detail { get; set; }
    public string? OutputPath { get; set; }
}

public sealed record DvrScheduleRow(ScheduledRecording Recording, bool HasConflict = false)
{
    public Guid Id => Recording.Id;
    public string ProgramTitle => Recording.ProgramTitle;
    public string ChannelName => Recording.ChannelName;
    public string TimeText => $"{Recording.StartUtc.ToLocalTime():ddd, MMM d • h:mm tt} – {Recording.StopUtc.ToLocalTime():h:mm tt}";
    public string StatusText => HasConflict ? "CONFLICT" : Recording.Status.ToUpperInvariant();
    public string DetailText => HasConflict
        ? "Overlaps another recording; StreamVue can capture one channel at a time"
        : string.IsNullOrWhiteSpace(Recording.Detail)
        ? Recording.Status switch
        {
            "Scheduled" => "StreamVue must be open when the program begins",
            "Recording" => "Recording is in progress",
            "Completed" => "Saved to the StreamVue recordings folder",
            "Missed" => "StreamVue was not available during this program",
            "Failed" => "The provider stream could not be recorded",
            _ => Recording.Status
        }
        : Recording.Detail!;
    public bool CanCancel => Recording.Status is "Scheduled" or "Recording";
}

public sealed record DvrLibraryItem(string FilePath, DateTimeOffset ModifiedUtc, long Bytes)
{
    public string Name => Path.GetFileNameWithoutExtension(FilePath);
    public string Detail => $"{ModifiedUtc.ToLocalTime():MMM d, yyyy • h:mm tt}  •  {FormatBytes(Bytes)}";

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
