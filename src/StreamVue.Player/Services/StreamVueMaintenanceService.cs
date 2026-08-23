using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StreamVue.Player.Models;
using StreamVue.Player.Playback;

namespace StreamVue.Player.Services;

public sealed record StreamVueDiagnosticContext(
    int ChannelCount,
    int GroupCount,
    string? SourceType,
    bool UsedCachedFallback,
    int GuideSourceCount,
    string? CurrentChannelKey,
    PlaybackSnapshot? Playback);

public sealed class StreamVueMaintenanceService
{
    private const string BackupProduct = "StreamVue";
    private const int BackupFormatVersion = 1;
    private const int MaximumCrashLogBytes = 256 * 1024;
    private static readonly byte[] BackupEntropy = Encoding.UTF8.GetBytes("StreamVue.PortableBackup.v1");

    private static readonly string[] KnownDataFiles =
    [
        "settings.json",
        "playlist-cache.v1.bin",
        "epg-cache.v1.bin",
        "guide-source.v1.bin",
        "epg-mappings.v1.bin",
        "xtream-credentials.v1.bin"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataRoot;

    public StreamVueMaintenanceService(string? dataRoot = null)
    {
        _dataRoot = dataRoot ?? Path.GetDirectoryName(StreamVueDataPaths.Resolve("settings.json"))!;
    }

    public async Task ExportDiagnosticsAsync(
        string destinationPath,
        AppSettings settings,
        StreamVueDiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        var fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        var temporaryPath = fullDestination + ".tmp";
        File.Delete(temporaryPath);

        try
        {
            await using (var file = File.Create(temporaryPath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                var report = new
                {
                Format = "StreamVue diagnostics v1",
                CreatedUtc = DateTimeOffset.UtcNow,
                App = new
                {
                    Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                    Runtime = RuntimeInformation.FrameworkDescription,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString()
                },
                System = new
                {
                    OperatingSystem = RuntimeInformation.OSDescription,
                    LogicalProcessors = Environment.ProcessorCount,
                    Is64BitProcess = Environment.Is64BitProcess
                },
                Library = new
                {
                    context.ChannelCount,
                    context.GroupCount,
                    SourceType = context.SourceType ?? "none",
                    context.UsedCachedFallback,
                    context.GuideSourceCount,
                    CurrentChannel = Fingerprint(context.CurrentChannelKey)
                },
                Playback = context.Playback,
                Preferences = settings.Playback,
                SavedData = new
                {
                    FavoriteCount = settings.FavoriteChannelKeys?.Count ?? 0,
                    RecentChannelCount = settings.RecentChannelKeys?.Count ?? 0,
                    ChannelProfileCount = settings.ChannelProfiles?.Count ?? 0,
                    ReminderCount = settings.ProgramReminders?.Count ?? 0,
                    ScheduledRecordingCount = settings.ScheduledRecordings?.Count ?? 0,
                    SeriesRecordingRuleCount = settings.SeriesRecordingRules?.Count ?? 0,
                    SmartDvr = new
                    {
                        settings.SmartDvr?.StartPaddingMinutes,
                        settings.SmartDvr?.EndPaddingMinutes,
                        settings.SmartDvr?.StorageReserveGigabytes,
                        DefaultPriority = settings.SmartDvr?.DefaultPriority.ToString()
                    },
                    SavedMultiviewLayoutCount = settings.Multiview?.SavedLayouts?.Count ?? 0,
                    PlaylistHealth = new
                    {
                        settings.PlaylistHealth?.LastAttemptUtc,
                        settings.PlaylistHealth?.LastSuccessUtc,
                        settings.PlaylistHealth?.ChannelCount,
                        settings.PlaylistHealth?.AddedChannels,
                        settings.PlaylistHealth?.RemovedChannels,
                        settings.PlaylistHealth?.UsedCachedFallback,
                        HasRecordedError = !string.IsNullOrWhiteSpace(settings.PlaylistHealth?.LastError)
                    }
                },
                Privacy = "Playlist addresses, credentials, channel names, guide titles, and account details are excluded."
                };

                await WriteJsonEntryAsync(archive, "diagnostics.json", report, cancellationToken);

                var crashLogPath = Path.Combine(_dataRoot, "crash.log");
                if (File.Exists(crashLogPath))
                {
                    var crashText = await ReadTailAsync(crashLogPath, MaximumCrashLogBytes, cancellationToken);
                    crashText = RedactDiagnosticText(crashText);
                    var crashEntry = archive.CreateEntry("crash-log-redacted.txt", CompressionLevel.Optimal);
                    await using var crashStream = crashEntry.Open();
                    await using var writer = new StreamWriter(crashStream, new UTF8Encoding(false));
                    await writer.WriteAsync(crashText.AsMemory(), cancellationToken);
                }
            }

            File.Move(temporaryPath, fullDestination, overwrite: true);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    public async Task<int> CreateBackupAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        var fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        var temporaryPath = fullDestination + ".tmp";
        File.Delete(temporaryPath);

        var files = KnownDataFiles.Where(name => File.Exists(Path.Combine(_dataRoot, name))).ToArray();
        if (files.Length == 0) throw new InvalidOperationException("StreamVue does not have any saved data to back up yet.");

        try
        {
            await using (var file = File.Create(temporaryPath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteJsonEntryAsync(archive, "manifest.json", new BackupManifest(
                    BackupProduct,
                    BackupFormatVersion,
                    DateTimeOffset.UtcNow,
                    "Windows current-user encryption; restore with the same Windows account.",
                    files), cancellationToken);

                foreach (var name in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entryName = name == "settings.json" ? "data/settings.json.protected" : $"data/{name}";
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    await using var target = entry.Open();
                    if (name == "settings.json")
                    {
                        var clearBytes = await File.ReadAllBytesAsync(Path.Combine(_dataRoot, name), cancellationToken);
                        try
                        {
                            var protectedBytes = ProtectedData.Protect(clearBytes, BackupEntropy, DataProtectionScope.CurrentUser);
                            await target.WriteAsync(protectedBytes, cancellationToken);
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(clearBytes);
                        }
                    }
                    else
                    {
                        await using var source = File.OpenRead(Path.Combine(_dataRoot, name));
                        await source.CopyToAsync(target, cancellationToken);
                    }
                }
            }

            File.Move(temporaryPath, fullDestination, overwrite: true);
            return files.Length;
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    public async Task<int> RestoreBackupAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var fullSource = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSource)) throw new FileNotFoundException("The StreamVue backup could not be found.", fullSource);

        Directory.CreateDirectory(_dataRoot);
        var stagingRoot = Path.Combine(_dataRoot, $".restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            using var archive = ZipFile.OpenRead(fullSource);
            var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("This is not a StreamVue backup.");
            BackupManifest? manifest;
            await using (var manifestStream = manifestEntry.Open())
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, JsonOptions, cancellationToken);

            if (manifest is null || manifest.Product != BackupProduct || manifest.FormatVersion != BackupFormatVersion)
                throw new InvalidDataException("This backup format is not supported by this version of StreamVue.");
            if (manifest.Files.Length == 0 || manifest.Files.Any(name => !KnownDataFiles.Contains(name, StringComparer.Ordinal)))
                throw new InvalidDataException("The backup contains an unexpected data-file list.");

            foreach (var name in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryName = name == "settings.json" ? "data/settings.json.protected" : $"data/{name}";
                var entry = archive.GetEntry(entryName) ?? throw new InvalidDataException($"The backup is missing {name}.");
                var stagedPath = Path.Combine(stagingRoot, name);
                await using var source = entry.Open();
                if (name == "settings.json")
                {
                    await using var protectedBuffer = new MemoryStream();
                    await source.CopyToAsync(protectedBuffer, cancellationToken);
                    var clearBytes = ProtectedData.Unprotect(protectedBuffer.ToArray(), BackupEntropy, DataProtectionScope.CurrentUser);
                    try
                    {
                        await File.WriteAllBytesAsync(stagedPath, clearBytes, cancellationToken);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(clearBytes);
                    }
                }
                else
                {
                    await using var target = File.Create(stagedPath);
                    await source.CopyToAsync(target, cancellationToken);
                }
            }

            var rollbackPath = Path.Combine(_dataRoot, "before-last-restore.streamvue-backup");
            if (string.Equals(Path.GetFullPath(rollbackPath), fullSource, StringComparison.OrdinalIgnoreCase))
                rollbackPath = Path.Combine(_dataRoot, "before-last-restore-previous.streamvue-backup");
            if (KnownDataFiles.Any(name => File.Exists(Path.Combine(_dataRoot, name))))
                await CreateBackupAsync(rollbackPath, cancellationToken);

            foreach (var name in KnownDataFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(_dataRoot, name);
                var staged = Path.Combine(stagingRoot, name);
                if (!File.Exists(staged))
                {
                    File.Delete(destination);
                    continue;
                }

                var replacement = destination + ".restore-tmp";
                File.Copy(staged, replacement, overwrite: true);
                File.Move(replacement, destination, overwrite: true);
            }

            return manifest.Files.Length;
        }
        finally
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string name,
        T value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    private static string? Fingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..12];
    }

    private static async Task<string> ReadTailAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var length = (int)Math.Min(stream.Length, maximumBytes);
        stream.Seek(-length, SeekOrigin.End);
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        return Encoding.UTF8.GetString(buffer);
    }

    internal static string RedactDiagnosticText(string text)
    {
        var redacted = Regex.Replace(text, @"https?://[^\s""'<>]+", "<url>", RegexOptions.IgnoreCase);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
            redacted = redacted.Replace(profile, "<user-profile>", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(Environment.UserName))
            redacted = redacted.Replace(Environment.UserName, "<user>", StringComparison.OrdinalIgnoreCase);
        return redacted;
    }

    private sealed record BackupManifest(
        string Product,
        int FormatVersion,
        DateTimeOffset CreatedUtc,
        string EncryptionScope,
        string[] Files);
}
