using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public sealed class EpgSourceService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly XmlTvParser _parser = new();

    public Task<EpgSchedule> LoadAsync(
        string source,
        IReadOnlyList<ChannelItem> channels,
        IProgress<PlaylistProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        LoadAsync(source, channels, null, progress, cancellationToken);

    public async Task<EpgSchedule> LoadAsync(
        string source,
        IReadOnlyList<ChannelItem> channels,
        IReadOnlyCollection<string>? additionalChannelIds,
        IProgress<PlaylistProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(source))
        {
            progress?.Report(new PlaylistProgress(0, "Reading local TV guide…"));
            await using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024, true);
            await using var localContent = WrapCompression(file, source);
            return await _parser.ParseAsync(localContent, Path.GetFileNameWithoutExtension(source), channels, additionalChannelIds, progress, cancellationToken);
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Enter a complete XMLTV file path or HTTP/HTTPS address.", nameof(source));

        progress?.Report(new PlaylistProgress(0, "Connecting to TV guide provider…"));
        using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var compressed = uri.AbsolutePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                         response.Content.Headers.ContentType?.MediaType is "application/gzip" or "application/x-gzip";
        await using var responseContent = compressed
            ? new GZipStream(responseStream, CompressionMode.Decompress)
            : responseStream;
        return await _parser.ParseAsync(responseContent, uri.Host, channels, additionalChannelIds, progress, cancellationToken);
    }

    private static Stream WrapCompression(Stream stream, string source) =>
        source.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(stream, CompressionMode.Decompress)
            : stream;

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StreamVue", "3.3.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/gzip"));
        return client;
    }
}
