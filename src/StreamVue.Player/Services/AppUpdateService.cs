using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
#if !STREAMVUE_STORE_BUILD
using Velopack;
using Velopack.Locators;
using Velopack.Sources;
#endif

namespace StreamVue.Player.Services;

public enum AppUpdateState
{
    Available,
    Current,
    DeveloperBuild,
    StoreManaged
}

public sealed record AppUpdateCheckResult(AppUpdateState State, string CurrentVersion, string? AvailableVersion = null);

public sealed record AppUpdateRecoveryNotice(string RestoredVersion, DateTimeOffset RestoredUtc);

public sealed class AppUpdateService
{
    // Public Velopack releases are published here. An environment override keeps
    // local feed testing possible without changing the production application.
    public const string RepositoryUrl = "https://github.com/chris-20-cmyk/StreamVue";

#if !STREAMVUE_STORE_BUILD
    private UpdateManager? _manager;
    private UpdateInfo? _availableUpdate;
#endif
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _recoveryDirectory;

    public string CurrentVersion { get; } = ReadCurrentVersion();
    public bool IsStoreManaged { get; }

    public bool HasAvailableUpdate
    {
        get
        {
#if STREAMVUE_STORE_BUILD
            return false;
#else
            return !IsStoreManaged && _availableUpdate is not null;
#endif
        }
    }

    public void ClearAvailableUpdate()
    {
#if !STREAMVUE_STORE_BUILD
        _availableUpdate = null;
#endif
    }

    public AppUpdateService(string? recoveryDirectory = null, bool? storeManagedOverride = null)
    {
        _recoveryDirectory = recoveryDirectory ?? StreamVueDataPaths.Resolve("update-recovery");
#if STREAMVUE_STORE_BUILD
        IsStoreManaged = true;
#else
        IsStoreManaged = storeManagedOverride ?? false;
#endif
    }

    public async Task<AppUpdateCheckResult> CheckAsync(AppUpdateChannel channel = AppUpdateChannel.Preview)
    {
        await _checkGate.WaitAsync();
        try
        {
#if STREAMVUE_STORE_BUILD
            return new AppUpdateCheckResult(AppUpdateState.StoreManaged, CurrentVersion);
#else
            _manager = null;
            _availableUpdate = null;
            if (IsStoreManaged)
                return new AppUpdateCheckResult(AppUpdateState.StoreManaged, CurrentVersion);

            var repositoryUrl = Environment.GetEnvironmentVariable("STREAMVUE_UPDATE_REPOSITORY") ?? RepositoryUrl;
            var source = new GithubSource(repositoryUrl, null, prerelease: channel == AppUpdateChannel.Preview);
            _manager = new UpdateManager(source);

            if (!_manager.IsInstalled)
                return new AppUpdateCheckResult(AppUpdateState.DeveloperBuild, CurrentVersion);

            _availableUpdate = await _manager.CheckForUpdatesAsync();
            if (_availableUpdate is null)
                return new AppUpdateCheckResult(AppUpdateState.Current, CurrentVersion);

            return new AppUpdateCheckResult(
                AppUpdateState.Available,
                CurrentVersion,
                _availableUpdate.TargetFullRelease.Version.ToString());
#endif
        }
        finally
        {
            _checkGate.Release();
        }
    }

    public async Task DownloadAndRestartAsync(
        Action<int> progress,
        bool automaticRollback = true,
        CancellationToken cancellationToken = default)
    {
#if STREAMVUE_STORE_BUILD
        await Task.CompletedTask;
        throw new InvalidOperationException("Microsoft Store installs are updated by Microsoft Store.");
#else
        if (IsStoreManaged)
            throw new InvalidOperationException("Microsoft Store installs are updated by Microsoft Store.");
        if (_manager is null || _availableUpdate is null)
            throw new InvalidOperationException("Check for an update before downloading it.");

        string? healthToken = null;
        try
        {
            if (automaticRollback)
                healthToken = await PrepareRollbackAsync(_manager, _availableUpdate, cancellationToken);

            await _manager.DownloadUpdatesAsync(_availableUpdate, progress, cancellationToken);
            if (!string.IsNullOrWhiteSpace(healthToken)) StartRollbackWatchdog(healthToken);
            _manager.ApplyUpdatesAndRestart(
                _availableUpdate.TargetFullRelease,
                string.IsNullOrWhiteSpace(healthToken)
                    ? []
                    : ["--update-health-token", healthToken]);
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(healthToken)) CancelPendingRollback(healthToken);
            throw;
        }
#endif
    }

    public async Task ConfirmHealthyLaunchAsync(string healthToken)
    {
        if (!IsSafeToken(healthToken)) return;
        Directory.CreateDirectory(_recoveryDirectory);
        var healthyPath = GetHealthyPath(healthToken);
        await File.WriteAllTextAsync(healthyPath, DateTimeOffset.UtcNow.ToString("O"));
        var pending = await ReadPendingAsync();
        if (pending?.HealthToken.Equals(healthToken, StringComparison.Ordinal) == true)
        {
            TryDelete(GetPendingPath());
            TryDelete(GetWatchdogPath(healthToken));
        }
    }

    public async Task<AppUpdateRecoveryNotice?> CompleteRollbackAsync()
    {
        var pending = await ReadPendingAsync();
        if (pending is null) return null;
        Directory.CreateDirectory(_recoveryDirectory);
        var notice = new AppUpdateRecoveryNotice(pending.CurrentVersion, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(GetNoticePath(), JsonSerializer.Serialize(notice, JsonOptions));
        TryDelete(GetPendingPath());
        TryDelete(GetHealthyPath(pending.HealthToken));
        TryDelete(GetWatchdogPath(pending.HealthToken));
        return notice;
    }

    public async Task<AppUpdateRecoveryNotice?> ReadAndClearRecoveryNoticeAsync()
    {
        try
        {
            if (!File.Exists(GetNoticePath())) return null;
            var json = await File.ReadAllTextAsync(GetNoticePath());
            var notice = JsonSerializer.Deserialize<AppUpdateRecoveryNotice>(json, JsonOptions);
            TryDelete(GetNoticePath());
            return notice;
        }
        catch
        {
            TryDelete(GetNoticePath());
            return null;
        }
    }

#if !STREAMVUE_STORE_BUILD
    private async Task<string?> PrepareRollbackAsync(
        UpdateManager manager,
        UpdateInfo update,
        CancellationToken cancellationToken)
    {
        if (!manager.IsInstalled) return null;
        var locator = VelopackLocator.Current;
        Directory.CreateDirectory(_recoveryDirectory);
        var packageDirectory = locator.PackagesDir;
        var rootAppDirectory = locator.RootAppDir;
        var updateExePath = locator.UpdateExePath;
        if (string.IsNullOrWhiteSpace(packageDirectory) || string.IsNullOrWhiteSpace(rootAppDirectory) ||
            string.IsNullOrWhiteSpace(updateExePath)) return null;
        if (!Directory.Exists(packageDirectory)) return null;
        var currentPackage = Directory.EnumerateFiles(packageDirectory, "*-full.nupkg", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (currentPackage is null) return null;

        var rollbackPackageDirectory = Path.Combine(_recoveryDirectory, "package");
        Directory.CreateDirectory(rollbackPackageDirectory);
        foreach (var stale in Directory.EnumerateFiles(rollbackPackageDirectory, "*.nupkg")) TryDelete(stale);
        var rollbackPackage = Path.Combine(rollbackPackageDirectory, currentPackage.Name);
        await using (var source = new FileStream(currentPackage.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true))
        await using (var destination = new FileStream(rollbackPackage, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
            await source.CopyToAsync(destination, cancellationToken);

        var token = Guid.NewGuid().ToString("N");
        var pending = new PendingRollback(
            token,
            CurrentVersion,
            update.TargetFullRelease.Version.ToString(),
            rootAppDirectory,
            packageDirectory,
            updateExePath,
            rollbackPackage,
            DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(GetPendingPath(), JsonSerializer.Serialize(pending, JsonOptions), cancellationToken);
        foreach (var stale in Directory.EnumerateFiles(_recoveryDirectory, "healthy-*.flag")) TryDelete(stale);
        return token;
    }

    private void StartRollbackWatchdog(string healthToken)
    {
        var pending = ReadPendingAsync().GetAwaiter().GetResult();
        if (pending is null || !pending.HealthToken.Equals(healthToken, StringComparison.Ordinal)) return;
        var scriptPath = GetWatchdogPath(healthToken);
        var lines = new[]
        {
            "@echo off",
            "setlocal",
            "for /l %%I in (1,1,90) do (",
            $"  if exist \"{GetHealthyPath(healthToken)}\" exit /b 0",
            $"  if not exist \"{GetPendingPath()}\" exit /b 0",
            "  timeout /t 1 /nobreak >nul",
            ")",
            $"if not exist \"{GetPendingPath()}\" exit /b 0",
            $"\"{pending.UpdateExePath}\" --silent --rootDir \"{pending.RootAppDirectory}\" --packageDir \"{pending.PackageDirectory}\" apply --package \"{pending.RollbackPackagePath}\" -- --update-rollback",
            "exit /b 0"
        };
        File.WriteAllLines(scriptPath, lines);
        Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            ArgumentList = { "/d", "/c", scriptPath }
        });
    }

    private void CancelPendingRollback(string healthToken)
    {
        var pending = ReadPendingAsync().GetAwaiter().GetResult();
        if (pending?.HealthToken.Equals(healthToken, StringComparison.Ordinal) == true)
            TryDelete(GetPendingPath());
        TryDelete(GetWatchdogPath(healthToken));
    }
#endif

    private async Task<PendingRollback?> ReadPendingAsync()
    {
        try
        {
            if (!File.Exists(GetPendingPath())) return null;
            var json = await File.ReadAllTextAsync(GetPendingPath());
            return JsonSerializer.Deserialize<PendingRollback>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private string GetPendingPath() => Path.Combine(_recoveryDirectory, "pending.json");
    private string GetNoticePath() => Path.Combine(_recoveryDirectory, "rollback-notice.json");
    private string GetHealthyPath(string token) => Path.Combine(_recoveryDirectory, $"healthy-{token}.flag");
    private string GetWatchdogPath(string token) => Path.Combine(_recoveryDirectory, $"watchdog-{token}.cmd");
    private static bool IsSafeToken(string value) => value.Length == 32 && value.All(Uri.IsHexDigit);
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private sealed record PendingRollback(
        string HealthToken,
        string CurrentVersion,
        string TargetVersion,
        string RootAppDirectory,
        string PackageDirectory,
        string UpdateExePath,
        string RollbackPackagePath,
        DateTimeOffset PreparedUtc);

    private static string ReadCurrentVersion()
    {
        var assembly = typeof(AppUpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        return assembly.GetName().Version?.ToString(3) ?? "Unknown";
    }
}
