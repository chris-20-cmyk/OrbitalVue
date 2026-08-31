using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public sealed class XtreamSourceService
{
    private static readonly HttpClient HttpClient = CreateClient();

    public async Task<PlaylistResult> LoadAsync(
        string server,
        string username,
        string password,
        IProgress<PlaylistProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var baseUri = NormalizeServer(server);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Username and password are required.");

        progress?.Report(new PlaylistProgress(0, "Authenticating with Xtream provider…"));
        var categoriesTask = GetJsonAsync(baseUri, username, password, "get_live_categories", cancellationToken);
        var streamsTask = GetJsonAsync(baseUri, username, password, "get_live_streams", cancellationToken);
        await Task.WhenAll(categoriesTask, streamsTask);

        var categoryNames = new Dictionary<string, string>();
        using (var categories = await categoriesTask)
        {
            if (categories.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var category in categories.RootElement.EnumerateArray())
                {
                    var id = ReadString(category, "category_id");
                    var name = ReadString(category, "category_name");
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) categoryNames[id] = name;
                }
            }
        }

        var channels = new List<ChannelItem>();
        using (var streams = await streamsTask)
        {
            if (streams.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The provider did not return a valid live channel list.");

            foreach (var item in streams.RootElement.EnumerateArray())
            {
                var streamId = ReadString(item, "stream_id");
                if (string.IsNullOrWhiteSpace(streamId)) continue;
                var categoryId = ReadString(item, "category_id");
                categoryNames.TryGetValue(categoryId, out var group);
                var extension = ReadString(item, "container_extension");
                if (string.IsNullOrWhiteSpace(extension)) extension = "ts";
                var streamUrl = new Uri(baseUri, $"live/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}/{streamId}.{extension}");
                var archiveEnabled = ReadString(item, "tv_archive") is "1" or "true" or "True";
                _ = int.TryParse(ReadString(item, "tv_archive_duration"), out var archiveDays);

                channels.Add(new ChannelItem
                {
                    Number = channels.Count + 1,
                    Name = ReadString(item, "name") is { Length: > 0 } name ? name : $"Channel {channels.Count + 1}",
                    Url = streamUrl.ToString(),
                    Group = string.IsNullOrWhiteSpace(group) ? "Live TV" : group,
                    LogoUrl = NullIfBlank(ReadString(item, "stream_icon")),
                    TvgId = NullIfBlank(ReadString(item, "epg_channel_id")),
                    Kind = ChannelKind.Live,
                    CatchupMode = archiveEnabled ? "xtream" : null,
                    CatchupSource = archiveEnabled
                        ? new Uri(baseUri, $"timeshift/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}/{{duration_minutes}}/{{Y}}-{{m}}-{{d}}:{{H}}-{{M}}/{streamId}.{extension}").ToString()
                        : null,
                    CatchupDays = archiveEnabled ? Math.Max(1, archiveDays) : 0
                });

                if (channels.Count % 1_000 == 0)
                    progress?.Report(new PlaylistProgress(channels.Count, $"Indexed {channels.Count:N0} live channels"));
            }
        }

        if (channels.Count == 0) throw new InvalidDataException("The Xtream account returned no live channels.");
        progress?.Report(new PlaylistProgress(channels.Count, $"Ready — {channels.Count:N0} channels"));
        var guideSource = new Uri(baseUri,
            $"xmltv.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}");
        return new PlaylistResult(channels, baseUri.Host, baseUri.ToString(), DateTimeOffset.Now, guideSource.ToString());
    }

    private static async Task<JsonDocument> GetJsonAsync(Uri baseUri, string username, string password, string action, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(baseUri,
            $"player_api.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&action={action}");
        using var response = await HttpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static Uri NormalizeServer(string server)
    {
        var value = server.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = $"http://{value}";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Enter a valid Xtream server address.");
        var builder = new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/", Query = string.Empty, Fragment = string.Empty };
        return builder.Uri;
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StreamVue", "5.5.0"));
        return client;
    }
}
