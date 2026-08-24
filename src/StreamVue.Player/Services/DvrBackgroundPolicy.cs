using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public sealed record DvrWakePlan(Guid ScheduleId, string ProgramTitle, DateTimeOffset WakeUtc, bool ResumeSystem);

public static class DvrBackgroundPolicy
{
    public static DvrWakePlan? CreateWakePlan(
        IEnumerable<ScheduledRecording> recordings,
        SmartDvrPreferences preferences,
        DateTimeOffset now)
    {
        if (!preferences.BackgroundRecordingEnabled) return null;
        return recordings
            .Where(recording => (recording.Status is "Scheduled" or "Recovering") && recording.StopUtc > now)
            .Select(recording => new
            {
                Recording = recording,
                WakeUtc = recording.Status == "Recovering"
                    ? recording.NextRecoveryUtc ?? now.AddSeconds(2)
                    : recording.StartUtc.AddMinutes(-2)
            })
            .Where(candidate => candidate.WakeUtc > now.AddSeconds(2))
            .OrderBy(candidate => candidate.WakeUtc)
            .ThenByDescending(candidate => candidate.Recording.Priority)
            .ThenBy(candidate => candidate.Recording.CreatedUtc)
            .Select(candidate => new DvrWakePlan(
                candidate.Recording.Id,
                candidate.Recording.ProgramTitle,
                candidate.WakeUtc,
                preferences.WakeForRecordings))
            .FirstOrDefault();
    }

    public static double EstimateCapacityHours(DvrStorageSnapshot storage, int reserveGigabytes, double megabitsPerSecond = 8d)
    {
        if (!storage.IsAvailable || megabitsPerSecond <= 0) return 0;
        var usableBytes = Math.Max(0, storage.FreeBytes - SmartDvrPolicy.StorageReserveBytes(reserveGigabytes));
        var bytesPerSecond = megabitsPerSecond * 1_000_000d / 8d;
        return usableBytes / bytesPerSecond / 3600d;
    }
}
