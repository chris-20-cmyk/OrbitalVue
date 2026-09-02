using System.Globalization;
using OrbitalVue.Player.Models;

namespace OrbitalVue.Player.Services;

public static class CatchupReplayPolicy
{
    public static ChannelItem CreateReplayChannel(ChannelItem channel, EpgProgram programme)
    {
        if (!channel.HasCatchup) throw new InvalidOperationException("This channel does not advertise a replay source.");
        if (programme.Stop > DateTimeOffset.UtcNow) throw new InvalidOperationException("Replay is available after the programme ends.");
        if (channel.CatchupDays > 0 && programme.Stop < DateTimeOffset.UtcNow.AddDays(-channel.CatchupDays))
            throw new InvalidOperationException($"This programme is outside the {channel.CatchupDays}-day replay window.");

        return new ChannelItem
        {
            Number = channel.Number,
            Name = programme.Title,
            Group = $"Replay • {channel.Name}",
            Url = BuildReplayUrl(channel, programme),
            LogoUrl = channel.LogoUrl,
            TvgId = channel.TvgId,
            TvgName = channel.TvgName,
            UserAgent = channel.UserAgent,
            Referrer = channel.Referrer,
            Kind = ChannelKind.Replay,
            SourceId = channel.SourceId,
            SourceName = channel.SourceName
        };
    }

    public static string BuildReplayUrl(ChannelItem channel, EpgProgram programme)
    {
        if (string.IsNullOrWhiteSpace(channel.CatchupSource))
            throw new InvalidOperationException("The playlist did not include a catch-up URL template.");
        var correction = TimeSpan.FromMinutes(channel.CatchupCorrectionMinutes);
        var start = programme.Start.Add(correction);
        var stop = programme.Stop.Add(correction);
        var startUnix = start.ToUnixTimeSeconds();
        var stopUnix = stop.ToUnixTimeSeconds();
        var durationSeconds = Math.Max(1, (long)(stop - start).TotalSeconds);
        var durationMinutes = Math.Max(1, (long)Math.Ceiling(durationSeconds / 60d));
        var offset = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - startUnix);
        var value = channel.CatchupSource.Trim();
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{utc}"] = startUnix.ToString(CultureInfo.InvariantCulture),
            ["{utcend}"] = stopUnix.ToString(CultureInfo.InvariantCulture),
            ["{lutc}"] = startUnix.ToString(CultureInfo.InvariantCulture),
            ["{lutcend}"] = stopUnix.ToString(CultureInfo.InvariantCulture),
            ["{start}"] = startUnix.ToString(CultureInfo.InvariantCulture),
            ["{end}"] = stopUnix.ToString(CultureInfo.InvariantCulture),
            ["{duration}"] = durationSeconds.ToString(CultureInfo.InvariantCulture),
            ["{duration_minutes}"] = durationMinutes.ToString(CultureInfo.InvariantCulture),
            ["{offset}"] = offset.ToString(CultureInfo.InvariantCulture),
            ["{Y}"] = start.UtcDateTime.ToString("yyyy", CultureInfo.InvariantCulture),
            ["{m}"] = start.UtcDateTime.ToString("MM", CultureInfo.InvariantCulture),
            ["{d}"] = start.UtcDateTime.ToString("dd", CultureInfo.InvariantCulture),
            ["{H}"] = start.UtcDateTime.ToString("HH", CultureInfo.InvariantCulture),
            ["{M}"] = start.UtcDateTime.ToString("mm", CultureInfo.InvariantCulture),
            ["{S}"] = start.UtcDateTime.ToString("ss", CultureInfo.InvariantCulture)
        };
        foreach (var replacement in replacements)
        {
            var name = replacement.Key.Trim('{', '}');
            value = value.Replace(replacement.Key, replacement.Value, StringComparison.OrdinalIgnoreCase)
                .Replace($"${name}$", replacement.Value, StringComparison.OrdinalIgnoreCase)
                .Replace($"${{{name}}}", replacement.Value, StringComparison.OrdinalIgnoreCase)
                .Replace($"${name}", replacement.Value, StringComparison.OrdinalIgnoreCase);
        }

        if (value.StartsWith('?') || value.StartsWith('&')) return channel.Url + value;
        return value;
    }
}
