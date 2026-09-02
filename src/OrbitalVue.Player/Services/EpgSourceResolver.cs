using System.IO;
using OrbitalVue.Player.Models;

namespace OrbitalVue.Player.Services;

public static class EpgSourceResolver
{
    public static string? Resolve(PlaylistResult playlist)
    {
        if (IsSupportedSource(playlist.GuideSource)) return playlist.GuideSource;

        foreach (var channel in playlist.Channels.Where(channel => channel.Kind == ChannelKind.Live && !string.IsNullOrWhiteSpace(channel.TvgId)))
        {
            if (!Uri.TryCreate(channel.Url, UriKind.Absolute, out var streamUri) || streamUri.Scheme is not ("http" or "https"))
                continue;
            var segments = streamUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3 || !long.TryParse(Path.GetFileNameWithoutExtension(segments[^1]), out _)) continue;

            var username = Uri.UnescapeDataString(segments[^3]);
            var password = Uri.UnescapeDataString(segments[^2]);
            if (username.Length == 0 || password.Length == 0) continue;
            var prefixSegments = segments[..^3];
            var prefix = prefixSegments.Length == 0 ? "/" : $"/{string.Join('/', prefixSegments)}/";
            var builder = new UriBuilder(streamUri)
            {
                Path = $"{prefix}xmltv.php",
                Query = $"username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}",
                Fragment = string.Empty
            };
            return builder.Uri.ToString();
        }

        return null;
    }

    private static bool IsSupportedSource(string? source) =>
        !string.IsNullOrWhiteSpace(source) &&
        (File.Exists(source) || Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file");
}
