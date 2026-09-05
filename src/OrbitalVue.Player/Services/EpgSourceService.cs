using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using OrbitalVue.Player.Models;

namespace OrbitalVue.Player.Services;

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
        await using var responseContent = await WrapCompressionByHeaderAsync(responseStream, cancellationToken);
        return await _parser.ParseAsync(responseContent, uri.Host, channels, additionalChannelIds, progress, cancellationToken);
    }

    private static Stream WrapCompression(Stream stream, string source) =>
        source.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(stream, CompressionMode.Decompress)
            : stream;

    private static async ValueTask<Stream> WrapCompressionByHeaderAsync(Stream stream, CancellationToken cancellationToken)
    {
        var prefix = new byte[2];
        var length = 0;
        while (length < prefix.Length)
        {
            var read = await stream.ReadAsync(prefix.AsMemory(length, prefix.Length - length), cancellationToken);
            if (read == 0) break;
            length += read;
        }

        var replay = new PrefixReadStream(prefix, length, stream);
        return length == 2 && prefix[0] == 0x1F && prefix[1] == 0x8B
            ? new GZipStream(replay, CompressionMode.Decompress)
            : replay;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OrbitalVue", "5.8.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/gzip"));
        return client;
    }

    private sealed class PrefixReadStream(byte[] prefix, int prefixLength, Stream inner) : Stream
    {
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var copied = CopyPrefix(buffer.AsSpan(offset, count));
            return copied == count ? copied : copied + inner.Read(buffer, offset + copied, count - copied);
        }

        public override int Read(Span<byte> buffer)
        {
            var copied = CopyPrefix(buffer);
            return copied == buffer.Length ? copied : copied + inner.Read(buffer[copied..]);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var copied = CopyPrefix(buffer.Span);
            return copied == buffer.Length
                ? copied
                : copied + await inner.ReadAsync(buffer[copied..], cancellationToken);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // The caller owns the HTTP response stream; this wrapper only replays the bytes used for sniffing.
            base.Dispose(disposing);
        }

        private int CopyPrefix(Span<byte> destination)
        {
            var available = prefixLength - _offset;
            if (available <= 0 || destination.Length == 0) return 0;
            var count = Math.Min(available, destination.Length);
            prefix.AsSpan(_offset, count).CopyTo(destination);
            _offset += count;
            return count;
        }
    }
}
