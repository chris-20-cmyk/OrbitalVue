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

public sealed record DvrScheduleRow(ScheduledRecording Recording)
{
    public Guid Id => Recording.Id;
    public string ProgramTitle => Recording.ProgramTitle;
    public string ChannelName => Recording.ChannelName;
    public string TimeText => $"{Recording.StartUtc.ToLocalTime():ddd, MMM d • h:mm tt} – {Recording.StopUtc.ToLocalTime():h:mm tt}";
    public string StatusText => Recording.Status.ToUpperInvariant();
    public string DetailText => string.IsNullOrWhiteSpace(Recording.Detail)
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
