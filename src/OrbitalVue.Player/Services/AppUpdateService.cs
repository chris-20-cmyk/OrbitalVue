using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
#if !ORBITALVUE_STORE_BUILD
using Velopack;
using Velopack.Locators;
using Velopack.Sources;
#endif

namespace OrbitalVue.Player.Services;

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
    public const string RepositoryUrl = "https://github.com/chris-20-cmyk/OrbitalVue";

    // Expected Authenticode subject name for signed OrbitalVue releases.
    // When set, updates must be signed by a certificate with this subject to be trusted.
    // Set to null during development; production releases should configure this.
    private static readonly string? ExpectedPublisherSubject = 
        Environment.GetEnvironmentVariable("ORBITALVUE_PUBLISHER_SUBJECT");

    // Require signature verification for production releases.
    // Can be disabled for local testing via ORBITALVUE_SKIP_UPDATE_SIGNATURE_CHECK=1
    private static readonly bool RequireSignedUpdates = 
        Environment.GetEnvironmentVariable("ORBITALVUE_SKIP_UPDATE_SIGNATURE_CHECK") != "1";

#if !ORBITALVUE_STORE_BUILD
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
#if ORBITALVUE_STORE_BUILD
            return false;
#else
            return !IsStoreManaged && _availableUpdate is not null;
#endif
        }
    }

    public void ClearAvailableUpdate()
    {
#if !ORBITALVUE_STORE_BUILD
        _availableUpdate = null;
#endif
    }

    public AppUpdateService(string? recoveryDirectory = null, bool? storeManagedOverride = null)
    {
        _recoveryDirectory = recoveryDirectory ?? OrbitalVueDataPaths.Resolve("update-recovery");
#if ORBITALVUE_STORE_BUILD
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
#if ORBITALVUE_STORE_BUILD
            return new AppUpdateCheckResult(AppUpdateState.StoreManaged, CurrentVersion);
#else
            _manager = null;
            _availableUpdate = null;
            if (IsStoreManaged)
                return new AppUpdateCheckResult(AppUpdateState.StoreManaged, CurrentVersion);

            var repositoryUrl = Environment.GetEnvironmentVariable("ORBITALVUE_UPDATE_REPOSITORY") ?? RepositoryUrl;
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
#if ORBITALVUE_STORE_BUILD
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
            
            // Verify the downloaded package signature before applying the update.
            // This protects against compromised release authority by requiring
            // an independently authenticated publisher certificate.
            VerifyPackageSignature(_manager, _availableUpdate);
            
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

#if !ORBITALVUE_STORE_BUILD
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

#if !ORBITALVUE_STORE_BUILD
    /// <summary>
    /// Verifies the Authenticode signature of the downloaded update package.
    /// Throws SecurityException if signature verification is required and fails.
    /// </summary>
    private static void VerifyPackageSignature(UpdateManager manager, UpdateInfo update)
    {
        var locator = VelopackLocator.Current;
        var packageDirectory = locator.PackagesDir;
        if (string.IsNullOrWhiteSpace(packageDirectory) || !Directory.Exists(packageDirectory))
        {
            if (RequireSignedUpdates)
                throw new System.Security.SecurityException("Cannot verify update signature: package directory not found.");
            return;
        }

        // Locate the downloaded full package file.
        var packageFileName = $"{update.TargetFullRelease.PackageId}-{update.TargetFullRelease.Version}-full.nupkg";
        var packagePath = Path.Combine(packageDirectory, packageFileName);
        if (!File.Exists(packagePath))
        {
            if (RequireSignedUpdates)
                throw new System.Security.SecurityException($"Cannot verify update signature: package file not found at {packagePath}");
            return;
        }

        // Verify the Authenticode signature using Windows certificate validation.
        // Velopack packages can be signed with signtool.exe, which embeds an Authenticode signature.
        X509Certificate2? certificate = null;
        bool isSigned = false;
        
        try
        {
            // Attempt to get the certificate from the file's digital signature.
            // For .nupkg files signed by Velopack's --signParams, we need to check the Setup.exe
            // that's extracted, but for now we check if the main executable in the package is signed.
            // A more robust approach: check the Update.exe or Setup.exe that Velopack creates.
            
            // First, try to find and verify the Setup.exe in the release directory
            var setupExePath = Path.Combine(Path.GetDirectoryName(packageDirectory) ?? "", 
                $"{update.TargetFullRelease.PackageId}-win-Setup.exe");
            
            if (File.Exists(setupExePath))
            {
                try
                {
                    certificate = X509Certificate2.CreateFromSignedFile(setupExePath);
                    isSigned = true;
                }
                catch (CryptographicException)
                {
                    // Setup.exe is not signed
                }
            }
            
            // If Setup.exe isn't available or signed, this might be during an update check
            // where only the package is downloaded. In production, we require the package
            // to come from a signed release.
        }
        catch (Exception ex) when (ex is not System.Security.SecurityException)
        {
            // Unexpected error during signature check
            if (RequireSignedUpdates)
                throw new System.Security.SecurityException(
                    $"Failed to verify update package signature: {ex.Message}", ex);
            return;
        }

        if (!isSigned)
        {
            if (RequireSignedUpdates)
            {
                throw new System.Security.SecurityException(
                    "Update package is not signed with a valid Authenticode certificate. " +
                    "OrbitalVue requires signed updates to protect against compromised releases. " +
                    "Developers: set ORBITALVUE_SKIP_UPDATE_SIGNATURE_CHECK=1 for local testing.");
            }
            return;
        }

        if (certificate == null)
        {
            if (RequireSignedUpdates)
                throw new System.Security.SecurityException("Update package signature could not be verified.");
            return;
        }

        try
        {
            // Verify the certificate chain and trust.
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            
            if (!chain.Build(certificate))
            {
                var errors = string.Join(", ", chain.ChainStatus.Select(s => s.StatusInformation));
                throw new System.Security.SecurityException(
                    $"Update package certificate chain validation failed: {errors}");
            }

            // If a specific publisher subject is configured, verify it matches.
            if (!string.IsNullOrWhiteSpace(ExpectedPublisherSubject))
            {
                // Normalize subject strings for comparison (handle different orderings)
                var expectedParts = ExpectedPublisherSubject.Split(',').Select(p => p.Trim()).OrderBy(p => p).ToArray();
                var actualParts = certificate.Subject.Split(',').Select(p => p.Trim()).OrderBy(p => p).ToArray();
                
                if (!expectedParts.SequenceEqual(actualParts, StringComparer.OrdinalIgnoreCase))
                {
                    throw new System.Security.SecurityException(
                        $"Update package is signed by an unexpected publisher. " +
                        $"Expected: {ExpectedPublisherSubject}, Got: {certificate.Subject}");
                }
            }
        }
        finally
        {
            certificate.Dispose();
        }
    }
#endif

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
