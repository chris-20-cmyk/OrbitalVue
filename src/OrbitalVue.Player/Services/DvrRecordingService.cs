using System.IO;
using System.Security.Cryptography;
using System.Text;
using LibVLCSharp.Shared;
using OrbitalVue.Player.Models;

namespace OrbitalVue.Player.Services;

public sealed class DvrRecordingService : IDisposable
{
    private readonly object _gate = new();
    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private Media? _media;
    private DvrRecordingSnapshot _snapshot = DvrRecordingSnapshot.Idle;
    private string? _terminalError;
    private bool _disposed;

    public DvrRecordingSnapshot Snapshot
    {
        get
        {
            lock (_gate) return _snapshot;
        }
    }

    public static string DefaultRecordingsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "OrbitalVue Recordings");

    public static string LegacyDefaultRecordingsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "OrbitalVue Recordings");

    public DvrRecordingSnapshot Start(
        ChannelItem channel,
        string? recordingsFolder,
        string? programTitle = null,
        DateTimeOffset? stopUtc = null,
        Guid? scheduleId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (channel.Kind != ChannelKind.Live)
            throw new InvalidOperationException("Only live channels can be recorded.");

        lock (_gate)
        {
            if (_snapshot.IsActive)
                throw new InvalidOperationException($"{_snapshot.ChannelName ?? "Another channel"} is already being recorded.");

            var folder = NormalizeRecordingsFolder(recordingsFolder);
            Directory.CreateDirectory(folder);
            var outputPath = CreateUniqueOutputPath(folder, channel.Name, programTitle, DateTimeOffset.Now);
            _terminalError = null;
            _snapshot = new DvrRecordingSnapshot(
                DvrRecordingState.Starting,
                channel.StableKey,
                channel.Name,
                programTitle,
                outputPath,
                DateTimeOffset.UtcNow,
                stopUtc,
                ScheduleId: scheduleId,
                Message: "Opening a private recording stream");

            try
            {
                Core.Initialize();
                _libVlc ??= new LibVLC("--intf=dummy", "--no-video-title-show", "--no-snapshot-preview", "--quiet");
                _mediaPlayer = new MediaPlayer(_libVlc) { Mute = true };
                _mediaPlayer.Playing += RecordingPlayer_Playing;
                _mediaPlayer.EncounteredError += RecordingPlayer_EncounteredError;
                _mediaPlayer.EndReached += RecordingPlayer_EndReached;

                _media = new Media(_libVlc, new Uri(channel.Url));
                _media.AddOption(":network-caching=4000");
                _media.AddOption(":live-caching=4000");
                _media.AddOption(":http-reconnect");
                _media.AddOption(":sout-all");
                _media.AddOption(":sout-keep");
                _media.AddOption(BuildSoutOption(outputPath));
                if (!string.IsNullOrWhiteSpace(channel.UserAgent))
                    _media.AddOption($":http-user-agent={channel.UserAgent}");
                if (!string.IsNullOrWhiteSpace(channel.Referrer))
                    _media.AddOption($":http-referrer={channel.Referrer}");

                _mediaPlayer.Media = _media;
                if (!_mediaPlayer.Play())
                    throw new InvalidOperationException("LibVLC could not open the recording stream.");
                return _snapshot;
            }
            catch
            {
                CleanupPlayer();
                DeleteEmptyOutput(_snapshot.OutputPath);
                _snapshot = _snapshot with
                {
                    State = DvrRecordingState.Failed,
                    Message = "The recording stream could not be started"
                };
                throw;
            }
        }
    }

    public DvrRecordingSnapshot Poll(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_snapshot.IsActive) return _snapshot;

            if (!string.IsNullOrWhiteSpace(_terminalError))
                return StopCore(completed: false, _terminalError);

            var bytes = ReadLength(_snapshot.OutputPath);
            var state = _mediaPlayer?.IsPlaying == true || bytes > 0
                ? DvrRecordingState.Recording
                : DvrRecordingState.Starting;
            var message = state == DvrRecordingState.Recording
                ? "Recording live transport stream"
                : "Waiting for the provider stream";
            _snapshot = _snapshot with { State = state, BytesWritten = bytes, Message = message };

            if (_snapshot.StopUtc is not null && now >= _snapshot.StopUtc.Value)
                return StopCore(completed: true, "Scheduled recording complete");

            if (_snapshot.StartedUtc is not null &&
                now - _snapshot.StartedUtc.Value > TimeSpan.FromSeconds(45) &&
                state == DvrRecordingState.Starting && bytes == 0)
                return StopCore(completed: false, "The provider did not deliver recordable media in time");

            return _snapshot;
        }
    }

    public DvrRecordingSnapshot Stop(string message = "Recording saved")
    {
        lock (_gate)
        {
            if (!_snapshot.IsActive) return _snapshot;
            return StopCore(completed: true, message);
        }
    }

    public IReadOnlyList<DvrLibraryItem> ListRecentRecordings(string? recordingsFolder, int maximum = 30)
    {
        var folder = NormalizeRecordingsFolder(recordingsFolder);
        if (!Directory.Exists(folder)) return [];
        try
        {
            return Directory.EnumerateFiles(folder, "*.ts", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(file => file.Length > 0)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(Math.Clamp(maximum, 1, 50))
                .Select(file => new DvrLibraryItem(file.FullName, file.LastWriteTimeUtc, file.Length))
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public DvrStorageSnapshot GetStorageSnapshot(string? recordingsFolder)
    {
        var folder = NormalizeRecordingsFolder(recordingsFolder);
        try
        {
            var files = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.ts", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .Where(file => file.Length > 0)
                    .ToList()
                : [];
            var root = Path.GetPathRoot(folder);
            if (string.IsNullOrWhiteSpace(root)) return new DvrStorageSnapshot(false, 0, 0, files.Sum(file => file.Length), files.Count);
            var drive = new DriveInfo(root);
            return new DvrStorageSnapshot(
                drive.IsReady,
                drive.IsReady ? drive.TotalSize : 0,
                drive.IsReady ? drive.AvailableFreeSpace : 0,
                files.Sum(file => file.Length),
                files.Count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new DvrStorageSnapshot(false, 0, 0, 0, 0);
        }
    }

    public void DeleteRecording(string filePath, string? recordingsFolder)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var folder = NormalizeRecordingsFolder(recordingsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(filePath);
        var parent = Path.GetDirectoryName(candidate)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(parent, folder, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(candidate).Equals(".ts", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("OrbitalVue only deletes transport-stream files from the selected recordings folder.");

        lock (_gate)
        {
            if (_snapshot.IsActive && string.Equals(_snapshot.OutputPath, candidate, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Stop and save the active recording before deleting it.");
            if (!File.Exists(candidate)) throw new FileNotFoundException("That recording no longer exists.", candidate);
            File.Delete(candidate);
        }
    }

    public static string CreateLibraryKey(string filePath)
    {
        var normalized = Path.GetFullPath(filePath).Trim().ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static bool SchedulesOverlap(ScheduledRecording first, ScheduledRecording second) =>
        first.StartUtc < second.StopUtc && second.StartUtc < first.StopUtc;

    public static bool IsPaddingOnlyOverlap(ScheduledRecording first, ScheduledRecording second) =>
        SchedulesOverlap(first, second) &&
        (first.GuideStopUtc <= second.GuideStartUtc || second.GuideStopUtc <= first.GuideStartUtc);

    public static bool SchedulesCompete(ScheduledRecording first, ScheduledRecording second) =>
        SchedulesOverlap(first, second) && !IsPaddingOnlyOverlap(first, second);

    public static IReadOnlySet<Guid> FindConflictingScheduleIds(IEnumerable<ScheduledRecording> recordings)
    {
        var active = recordings
            .Where(recording => recording.Status is "Scheduled" or "Recording" or "Recovering")
            .OrderBy(recording => recording.StartUtc)
            .ToList();
        var conflicts = new HashSet<Guid>();
        for (var firstIndex = 0; firstIndex < active.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < active.Count; secondIndex++)
            {
                if (active[secondIndex].StartUtc >= active[firstIndex].StopUtc) break;
                if (!SchedulesCompete(active[firstIndex], active[secondIndex])) continue;
                conflicts.Add(active[firstIndex].Id);
                conflicts.Add(active[secondIndex].Id);
            }
        }
        return conflicts;
    }

    public static string NormalizeRecordingsFolder(string? folder)
    {
        if (!string.IsNullOrWhiteSpace(folder)) return Path.GetFullPath(folder.Trim());
        return Directory.Exists(LegacyDefaultRecordingsFolder) && !Directory.Exists(DefaultRecordingsFolder)
            ? LegacyDefaultRecordingsFolder
            : DefaultRecordingsFolder;
    }

    public static string CreateRecordingFileName(string channelName, string? programTitle, DateTimeOffset localStart)
    {
        var title = CleanFileSegment(channelName, 70);
        var program = string.IsNullOrWhiteSpace(programTitle) ? string.Empty : $" - {CleanFileSegment(programTitle, 80)}";
        return $"{localStart:yyyy-MM-dd HH-mm-ss} - {title}{program}.ts";
    }

    public static string BuildSoutOption(string outputPath)
    {
        var normalized = Path.GetFullPath(outputPath).Replace('\\', '/').Replace("'", "\\'", StringComparison.Ordinal);
        return $":sout=#std{{access=file,mux=ts,dst='{normalized}'}}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_gate)
        {
            if (_snapshot.IsActive) StopCore(completed: true, "Recording stopped when OrbitalVue closed");
            CleanupPlayer();
            _libVlc?.Dispose();
            _libVlc = null;
            _disposed = true;
        }
    }

    private DvrRecordingSnapshot StopCore(bool completed, string message)
    {
        var bytes = ReadLength(_snapshot.OutputPath);
        _snapshot = _snapshot with { State = DvrRecordingState.Stopping, BytesWritten = bytes, Message = message };
        CleanupPlayer();
        bytes = ReadLength(_snapshot.OutputPath);
        if (bytes == 0) DeleteEmptyOutput(_snapshot.OutputPath);
        _snapshot = _snapshot with
        {
            State = completed && bytes > 0 ? DvrRecordingState.Completed : DvrRecordingState.Failed,
            BytesWritten = bytes,
            Message = completed && bytes == 0 ? "No recordable media was written" : message
        };
        return _snapshot;
    }

    private void RecordingPlayer_Playing(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_snapshot.State == DvrRecordingState.Starting)
                _snapshot = _snapshot with { State = DvrRecordingState.Recording, Message = "Recording live transport stream" };
        }
    }

    private void RecordingPlayer_EncounteredError(object? sender, EventArgs e)
    {
        lock (_gate) _terminalError = "The provider closed the recording stream";
    }

    private void RecordingPlayer_EndReached(object? sender, EventArgs e)
    {
        lock (_gate) _terminalError = "The recording stream ended unexpectedly";
    }

    private void CleanupPlayer()
    {
        if (_mediaPlayer is not null)
        {
            _mediaPlayer.Playing -= RecordingPlayer_Playing;
            _mediaPlayer.EncounteredError -= RecordingPlayer_EncounteredError;
            _mediaPlayer.EndReached -= RecordingPlayer_EndReached;
            if (_mediaPlayer.IsPlaying || _mediaPlayer.Media is not null) _mediaPlayer.Stop();
            _mediaPlayer.Media = null;
        }

        _media?.Dispose();
        _media = null;
        _mediaPlayer?.Dispose();
        _mediaPlayer = null;
        _terminalError = null;
    }

    private static string CreateUniqueOutputPath(
        string folder,
        string channelName,
        string? programTitle,
        DateTimeOffset localStart)
    {
        var baseName = Path.GetFileNameWithoutExtension(CreateRecordingFileName(channelName, programTitle, localStart));
        var candidate = Path.Combine(folder, baseName + ".ts");
        for (var index = 2; File.Exists(candidate); index++)
            candidate = Path.Combine(folder, $"{baseName} ({index}).ts");
        return candidate;
    }

    private static string CleanFileSegment(string value, int maximumLength)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim()) builder.Append(invalid.Contains(character) ? ' ' : character);
        var cleaned = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        if (cleaned.Length == 0) cleaned = "OrbitalVue";
        if (cleaned.Length > maximumLength) cleaned = cleaned[..maximumLength].TrimEnd(' ', '.');
        var stem = cleaned.Split('.')[0];
        if (ReservedWindowsNames.Contains(stem)) cleaned = "_" + cleaned;
        return cleaned;
    }

    private static long ReadLength(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static void DeleteEmptyOutput(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length == 0) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };
}
