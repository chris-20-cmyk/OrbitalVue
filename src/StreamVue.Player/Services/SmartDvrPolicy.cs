using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public static class SmartDvrPolicy
{
    public static ScheduledRecording CreateSchedule(
        ChannelItem channel,
        EpgProgram programme,
        int startPaddingMinutes,
        int endPaddingMinutes,
        DvrSchedulePriority priority,
        Guid? seriesRuleId = null)
    {
        startPaddingMinutes = ClampPadding(startPaddingMinutes);
        endPaddingMinutes = ClampPadding(endPaddingMinutes);
        return new ScheduledRecording
        {
            ChannelKey = channel.StableKey,
            ChannelName = channel.Name,
            ProgramTitle = programme.Title,
            ProgrammeStartUtc = programme.Start,
            ProgrammeStopUtc = programme.Stop,
            StartUtc = programme.Start.AddMinutes(-startPaddingMinutes),
            StopUtc = programme.Stop.AddMinutes(endPaddingMinutes),
            StartPaddingMinutes = startPaddingMinutes,
            EndPaddingMinutes = endPaddingMinutes,
            Priority = priority,
            SeriesRuleId = seriesRuleId
        };
    }

    public static bool MatchesProgramme(ScheduledRecording recording, ChannelItem channel, EpgProgram programme) =>
        recording.ChannelKey.Equals(channel.StableKey, StringComparison.OrdinalIgnoreCase) &&
        recording.GuideStartUtc == programme.Start &&
        NormalizeTitle(recording.ProgramTitle) == NormalizeTitle(programme.Title);

    public static bool RuleMatches(SeriesRecordingRule rule, ChannelItem channel, EpgProgram programme) =>
        rule.Enabled &&
        rule.ChannelKey.Equals(channel.StableKey, StringComparison.OrdinalIgnoreCase) &&
        NormalizeTitle(rule.ProgramTitle) == NormalizeTitle(programme.Title);

    public static ScheduledRecording? SelectPreferred(IEnumerable<ScheduledRecording> recordings) => recordings
        .OrderByDescending(recording => recording.Priority)
        .ThenBy(recording => recording.StartUtc)
        .ThenBy(recording => recording.CreatedUtc)
        .ThenBy(recording => recording.Id)
        .FirstOrDefault();

    public static ScheduledRecording? SelectPreferredDue(
        IEnumerable<ScheduledRecording> recordings,
        DateTimeOffset now) => recordings
        .OrderByDescending(recording => recording.Priority)
        .ThenByDescending(recording => recording.GuideStartUtc <= now && recording.GuideStopUtc > now)
        .ThenBy(recording => recording.StartUtc)
        .ThenBy(recording => recording.CreatedUtc)
        .ThenBy(recording => recording.Id)
        .FirstOrDefault();

    public static bool IsPreferredOver(ScheduledRecording candidate, ScheduledRecording current)
    {
        if (candidate.Priority != current.Priority) return candidate.Priority > current.Priority;
        if (candidate.StartUtc != current.StartUtc) return candidate.StartUtc < current.StartUtc;
        if (candidate.CreatedUtc != current.CreatedUtc) return candidate.CreatedUtc < current.CreatedUtc;
        return candidate.Id.CompareTo(current.Id) < 0;
    }

    public static IReadOnlySet<Guid> FindConflictWinners(IEnumerable<ScheduledRecording> recordings)
    {
        var active = recordings.Where(recording => recording.Status is "Scheduled" or "Recording").ToList();
        var winners = new HashSet<Guid>();
        foreach (var recording in active)
        {
            var overlapping = active.Where(candidate =>
                candidate.Id != recording.Id && DvrRecordingService.SchedulesCompete(recording, candidate));
            if (overlapping.Any() && overlapping.All(candidate => !IsPreferredOver(candidate, recording)))
                winners.Add(recording.Id);
        }
        return winners;
    }

    public static bool MeetsStorageReserve(DvrStorageSnapshot storage, int reserveGigabytes)
    {
        reserveGigabytes = Math.Clamp(reserveGigabytes, 0, 100);
        if (reserveGigabytes == 0 || !storage.IsAvailable) return true;
        return storage.FreeBytes >= reserveGigabytes * 1_073_741_824L;
    }

    public static long StorageReserveBytes(int reserveGigabytes) =>
        Math.Clamp(reserveGigabytes, 0, 100) * 1_073_741_824L;

    public static int ClampPadding(int minutes) => Math.Clamp(minutes, 0, 30);

    public static string NormalizeTitle(string? title) =>
        string.Join(' ', (title ?? string.Empty).Trim().ToUpperInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
