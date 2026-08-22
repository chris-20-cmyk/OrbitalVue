using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace StreamVue.Player.Services;

public enum AppUpdateState
{
    Available,
    Current,
    DeveloperBuild
}

public sealed record AppUpdateCheckResult(AppUpdateState State, string CurrentVersion, string? AvailableVersion = null);

public sealed class AppUpdateService
{
    // Public Velopack releases are published here. An environment override keeps
    // local feed testing possible without changing the production application.
    public const string RepositoryUrl = "https://github.com/chris-20-cmyk/StreamVue";

    private UpdateManager? _manager;
    private UpdateInfo? _availableUpdate;
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    public string CurrentVersion { get; } = ReadCurrentVersion();

    public bool HasAvailableUpdate => _availableUpdate is not null;

    public async Task<AppUpdateCheckResult> CheckAsync()
    {
        await _checkGate.WaitAsync();
        try
        {
            var repositoryUrl = Environment.GetEnvironmentVariable("STREAMVUE_UPDATE_REPOSITORY") ?? RepositoryUrl;
            var source = new GithubSource(repositoryUrl, null, prerelease: true);
            _manager = new UpdateManager(source);
            _availableUpdate = null;

            if (!_manager.IsInstalled)
                return new AppUpdateCheckResult(AppUpdateState.DeveloperBuild, CurrentVersion);

            _availableUpdate = await _manager.CheckForUpdatesAsync();
            if (_availableUpdate is null)
                return new AppUpdateCheckResult(AppUpdateState.Current, CurrentVersion);

            return new AppUpdateCheckResult(
                AppUpdateState.Available,
                CurrentVersion,
                _availableUpdate.TargetFullRelease.Version.ToString());
        }
        finally
        {
            _checkGate.Release();
        }
    }

    public async Task DownloadAndRestartAsync(Action<int> progress, CancellationToken cancellationToken = default)
    {
        if (_manager is null || _availableUpdate is null)
            throw new InvalidOperationException("Check for an update before downloading it.");

        await _manager.DownloadUpdatesAsync(_availableUpdate, progress, cancellationToken);
        _manager.ApplyUpdatesAndRestart(_availableUpdate.TargetFullRelease, []);
    }

    private static string ReadCurrentVersion()
    {
        var assembly = typeof(AppUpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        return assembly.GetName().Version?.ToString(3) ?? "Unknown";
    }
}
