using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public sealed partial class MediaCenterSourceService
{
    private const int MaximumPlaybackReportingSessions = 8;
    private static readonly TimeSpan PlaybackReportingSessionLifetime = TimeSpan.FromHours(18);
    private static readonly JsonSerializerOptions PlaybackReportJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _playbackReportingGate = new();
    private readonly Dictionary<string, MediaCenterPlaybackReportingSession> _playbackReportingSessions =
        new(StringComparer.Ordinal);

    public Task ReportPlaybackAsync(
        string reportingSessionId,
        MediaCenterPlaybackState state,
        long positionMilliseconds,
        long durationMilliseconds,
        bool isMuted = false,
        int volume = 100,
        CancellationToken cancellationToken = default)
    {
        RequirePremiumAccess();
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state), "The media-center playback state is invalid.");
        var session = FindPlaybackReportingSession(reportingSessionId)
            ?? throw new InvalidOperationException("This protected playback reporting session is no longer active.");
        var sequence = Interlocked.Increment(ref session.NextSequence);
        return ReportPlaybackCoreAsync(
            session,
            sequence,
            state,
            positionMilliseconds,
            durationMilliseconds,
            isMuted,
            volume,
            cancellationToken);
    }

    public Task StopPlaybackReportingAsync(
        string reportingSessionId,
        long positionMilliseconds,
        long durationMilliseconds,
        bool isMuted = false,
        int volume = 100,
        CancellationToken cancellationToken = default)
    {
        var session = TakePlaybackReportingSession(reportingSessionId);
        if (session is null) return Task.CompletedTask;
        if (!_premiumAccessProvider().CanUseMediaCenters)
        {
            session.LifetimeCancellation.Cancel();
            return Task.CompletedTask;
        }
        var sequence = Interlocked.Increment(ref session.NextSequence);
        return ReportPlaybackCoreAsync(
            session,
            sequence,
            MediaCenterPlaybackState.Stopped,
            positionMilliseconds,
            durationMilliseconds,
            isMuted,
            volume,
            cancellationToken);
    }

    public void CancelPlaybackReportingSession(string? reportingSessionId)
    {
        if (string.IsNullOrWhiteSpace(reportingSessionId)) return;
        MediaCenterPlaybackReportingSession? session;
        lock (_playbackReportingGate)
            _playbackReportingSessions.Remove(reportingSessionId, out session);
        session?.LifetimeCancellation.Cancel();
    }

    public void CancelAllPlaybackReportingSessions()
    {
        MediaCenterPlaybackReportingSession[] sessions;
        lock (_playbackReportingGate)
        {
            sessions = _playbackReportingSessions.Values.ToArray();
            _playbackReportingSessions.Clear();
        }
        foreach (var session in sessions) session.LifetimeCancellation.Cancel();
    }

    private void CancelPlaybackReportingSessionsForSource(string provider, string baseUrl)
    {
        MediaCenterPlaybackReportingSession[] sessions;
        lock (_playbackReportingGate)
        {
            sessions = _playbackReportingSessions.Values
                .Where(session =>
                    string.Equals(session.Credential.Binding.Provider, provider, StringComparison.Ordinal) &&
                    string.Equals(session.Credential.Binding.BaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var session in sessions) _playbackReportingSessions.Remove(session.Id);
        }
        foreach (var session in sessions) session.LifetimeCancellation.Cancel();
    }

    private string RegisterPlaybackReportingSession(
        MediaCenterLocator locator,
        MediaCenterCredential credential,
        string method,
        string? playSessionId = null,
        string? mediaSourceId = null)
    {
        credential = MediaCenterSecurity.ValidateCredential(credential);
        MediaCenterSecurity.AssertCredentialBinding(
            credential,
            locator.Provider,
            credential.Binding.BaseUrl,
            locator.ServerId);
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        var session = new MediaCenterPlaybackReportingSession(
            id,
            locator,
            credential,
            method,
            NormalizeOptionalIdentifier(playSessionId, "Emby play session identifier"),
            NormalizeOptionalIdentifier(mediaSourceId, "Emby media source identifier"),
            now,
            now.Add(PlaybackReportingSessionLifetime));
        lock (_playbackReportingGate)
        {
            PrunePlaybackReportingSessions(now);
            while (_playbackReportingSessions.Count >= MaximumPlaybackReportingSessions)
            {
                var oldest = _playbackReportingSessions.Values.MinBy(candidate => candidate.CreatedAt);
                if (oldest is null) break;
                _playbackReportingSessions.Remove(oldest.Id);
                oldest.LifetimeCancellation.Cancel();
            }
            _playbackReportingSessions.Add(id, session);
        }
        return id;
    }

    private MediaCenterPlaybackReportingSession? FindPlaybackReportingSession(string reportingSessionId)
    {
        reportingSessionId = MediaCenterSecurity.RequireIdentifier(
            reportingSessionId,
            "playback reporting session identifier");
        lock (_playbackReportingGate)
        {
            PrunePlaybackReportingSessions(DateTimeOffset.UtcNow);
            return _playbackReportingSessions.GetValueOrDefault(reportingSessionId);
        }
    }

    private MediaCenterPlaybackReportingSession? TakePlaybackReportingSession(string reportingSessionId)
    {
        reportingSessionId = MediaCenterSecurity.RequireIdentifier(
            reportingSessionId,
            "playback reporting session identifier");
        lock (_playbackReportingGate)
        {
            PrunePlaybackReportingSessions(DateTimeOffset.UtcNow);
            if (!_playbackReportingSessions.Remove(reportingSessionId, out var session)) return null;
            return session;
        }
    }

    private void PrunePlaybackReportingSessions(DateTimeOffset now)
    {
        foreach (var expired in _playbackReportingSessions.Values
                     .Where(session => session.ExpiresAt <= now)
                     .Select(session => session.Id)
                     .ToArray())
        {
            if (_playbackReportingSessions.Remove(expired, out var session))
                session.LifetimeCancellation.Cancel();
        }
    }

    private async Task ReportPlaybackCoreAsync(
        MediaCenterPlaybackReportingSession session,
        long sequence,
        MediaCenterPlaybackState state,
        long positionMilliseconds,
        long durationMilliseconds,
        bool isMuted,
        int volume,
        CancellationToken cancellationToken)
    {
        positionMilliseconds = Math.Max(0, positionMilliseconds);
        durationMilliseconds = Math.Max(0, durationMilliseconds);
        if (durationMilliseconds > 0)
            positionMilliseconds = Math.Min(positionMilliseconds, durationMilliseconds);
        volume = Math.Clamp(volume, 0, 100);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            session.LifetimeCancellation.Token);
        var effectiveCancellation = linkedCancellation.Token;
        await session.SerialGate.WaitAsync(effectiveCancellation).ConfigureAwait(false);
        try
        {
            if (sequence <= session.LastAppliedSequence) return;
            session.LastAppliedSequence = sequence;
            if (state == MediaCenterPlaybackState.Stopped && !session.HasStarted) return;
            if (session.ExpiresAt <= DateTimeOffset.UtcNow) return;
            RequirePremiumAccess();
            MediaCenterSecurity.AssertCredentialBinding(
                session.Credential,
                session.Locator.Provider,
                session.Credential.Binding.BaseUrl,
                session.Locator.ServerId);
            if (session.Locator.Provider == "plex")
            {
                await ReportPlexPlaybackAsync(
                    session,
                    state,
                    positionMilliseconds,
                    durationMilliseconds,
                    effectiveCancellation).ConfigureAwait(false);
            }
            else
            {
                await ReportEmbyPlaybackAsync(
                    session,
                    state,
                    positionMilliseconds,
                    durationMilliseconds,
                    isMuted,
                    volume,
                    effectiveCancellation).ConfigureAwait(false);
            }
            session.HasStarted = state != MediaCenterPlaybackState.Stopped;
            session.LastState = state;
        }
        finally
        {
            session.SerialGate.Release();
            if (state == MediaCenterPlaybackState.Stopped)
                session.LifetimeCancellation.Cancel();
        }
    }

    private Task ReportPlexPlaybackAsync(
        MediaCenterPlaybackReportingSession session,
        MediaCenterPlaybackState state,
        long positionMilliseconds,
        long durationMilliseconds,
        CancellationToken cancellationToken)
    {
        var url = MediaCenterSecurity.ResolveServerPath(session.Credential.Binding.BaseUrl, "/:/timeline");
        url = AddQuery(
            url,
            ("key", $"/library/metadata/{session.Locator.ItemId}"),
            ("ratingKey", session.Locator.ItemId),
            ("state", state.ToString().ToLowerInvariant()),
            ("time", positionMilliseconds.ToString()),
            ("duration", durationMilliseconds.ToString()));
        var headers = PlexHeaders(session.Credential.AccessToken);
        headers["X-Plex-Session-Identifier"] = session.Id;
        return SendForStatusAsync(HttpMethod.Post, url, headers, null, cancellationToken);
    }

    private Task ReportEmbyPlaybackAsync(
        MediaCenterPlaybackReportingSession session,
        MediaCenterPlaybackState state,
        long positionMilliseconds,
        long durationMilliseconds,
        bool isMuted,
        int volume,
        CancellationToken cancellationToken)
    {
        var playSessionId = session.PlaySessionId
            ?? throw new InvalidDataException("The Emby playback session has no server play-session identifier.");
        var mediaSourceId = session.MediaSourceId
            ?? throw new InvalidDataException("The Emby playback session has no media-source identifier.");
        var endpoint = state == MediaCenterPlaybackState.Stopped
            ? "/Sessions/Playing/Stopped"
            : session.HasStarted ? "/Sessions/Playing/Progress" : "/Sessions/Playing";
        var eventName = endpoint.EndsWith("/Progress", StringComparison.Ordinal)
            ? state == MediaCenterPlaybackState.Paused
                ? "Pause"
                : session.LastState == MediaCenterPlaybackState.Paused ? "Unpause" : "TimeUpdate"
            : null;
        var url = MediaCenterSecurity.ResolveServerPath(
            MediaCenterSecurity.EmbyApiBaseUrl(session.Credential.Binding.BaseUrl),
            endpoint);
        var body = JsonSerializer.Serialize(new
        {
            QueueableMediaTypes = new[] { "Video" },
            ItemId = session.Locator.ItemId,
            MediaSourceId = mediaSourceId,
            PlaySessionId = playSessionId,
            PositionTicks = checked(positionMilliseconds * 10_000),
            RunTimeTicks = checked(durationMilliseconds * 10_000),
            PlayMethod = ToEmbyPlayMethod(session.Method),
            IsPaused = state == MediaCenterPlaybackState.Paused,
            IsMuted = isMuted,
            VolumeLevel = volume,
            CanSeek = durationMilliseconds > 0,
            EventName = eventName
        }, PlaybackReportJsonOptions);
        return SendForStatusAsync(
            HttpMethod.Post,
            url,
            EmbyHeaders(session.Credential.AccessToken, session.Credential.Binding.UserId),
            body,
            cancellationToken);
    }

    private async Task SendForStatusAsync(
        HttpMethod method,
        string url,
        IReadOnlyDictionary<string, string> headers,
        string? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        foreach (var (name, value) in headers)
        {
            if (!HeaderNamePattern().IsMatch(name) || ContainsAny(value, '\r', '\n'))
                throw new InvalidDataException("A media-center request header is invalid.");
            request.Headers.TryAddWithoutValidation(name, value);
        }
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static string? NormalizeOptionalIdentifier(string? value, string label) =>
        string.IsNullOrWhiteSpace(value) ? null : MediaCenterSecurity.RequireIdentifier(value, label);

    private static string ToEmbyPlayMethod(string method) => method switch
    {
        "direct-play" => "DirectPlay",
        "direct-stream" => "DirectStream",
        "transcode" => "Transcode",
        _ => throw new InvalidDataException("The Emby playback method is not supported for progress reporting.")
    };

    private sealed class MediaCenterPlaybackReportingSession(
        string id,
        MediaCenterLocator locator,
        MediaCenterCredential credential,
        string method,
        string? playSessionId,
        string? mediaSourceId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        public string Id { get; } = id;
        public MediaCenterLocator Locator { get; } = locator;
        public MediaCenterCredential Credential { get; } = credential;
        public string Method { get; } = method;
        public string? PlaySessionId { get; } = playSessionId;
        public string? MediaSourceId { get; } = mediaSourceId;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public SemaphoreSlim SerialGate { get; } = new(1, 1);
        public CancellationTokenSource LifetimeCancellation { get; } = new();
        public long NextSequence;
        public long LastAppliedSequence;
        public bool HasStarted;
        public MediaCenterPlaybackState? LastState;
    }
}
