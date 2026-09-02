using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public sealed class PlaylistSourceService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly M3uPlaylistParser _parser = new();

    public Task<PlaylistResult> LoadFileAsync(
        string path,
        IProgress<PlaylistProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The selected playlist file no longer exists.", path);
        return _parser.ParseFileAsync(path, progress, cancellationToken);
    }

    public async Task<PlaylistResult> LoadUrlAsync(
        string url,
        IProgress<PlaylistProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Enter a complete HTTP or HTTPS playlist address.", nameof(url));
        }

        progress?.Report(new PlaylistProgress(0, "Connecting to playlist provider…"));
        using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var displayName = response.Content.Headers.ContentDisposition?.FileNameStar?.Trim('"')
                          ?? uri.Host;
        return await _parser.ParseAsync(stream, displayName, uri.ToString(), progress, cancellationToken);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OrbitalVue", "5.7.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/x-mpegurl"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.apple.mpegurl"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        return client;
    }
}
