using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StreamVue.Player.Models;

namespace StreamVue.Player.Services;

public sealed class MediaCenterCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StreamVue.MediaCenterCredentials.v1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _credentialPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MediaCenterCredentialStore(string? credentialPath = null)
    {
        _credentialPath = credentialPath ?? StreamVueDataPaths.Resolve("media-center-credentials.v1.bin");
    }

    public async Task SaveAsync(MediaCenterCredential credential, CancellationToken cancellationToken = default)
    {
        var validated = MediaCenterSecurity.ValidateCredential(credential);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var accounts = await ReadAccountsOrEmptyAsync(cancellationToken);
            accounts.RemoveAll(account => string.Equals(
                account.Binding.CredentialId,
                validated.Binding.CredentialId,
                StringComparison.Ordinal));
            accounts.RemoveAll(account => MediaCenterSecurity.SameSource(
                validated.Binding.Provider,
                validated.Binding.BaseUrl,
                account));
            accounts.Add(validated);
            await WriteAccountsAsync(accounts, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<MediaCenterCredential?> TryLoadForSourceAsync(
        string provider,
        string baseUrl,
        CancellationToken cancellationToken = default) =>
        FindAsync(account => MediaCenterSecurity.SameSource(provider, baseUrl, account), cancellationToken);

    public Task<MediaCenterCredential?> TryLoadByServerAsync(
        string provider,
        string serverId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = MediaCenterSecurity.NormalizeProvider(provider);
        var normalizedServerId = MediaCenterSecurity.RequireIdentifier(serverId, "media-center server identifier");
        return FindAsync(account =>
            string.Equals(account.Binding.Provider, normalizedProvider, StringComparison.Ordinal) &&
            string.Equals(account.Binding.ServerId, normalizedServerId, StringComparison.Ordinal), cancellationToken);
    }

    public async Task DeleteForSourceAsync(
        string provider,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var accounts = await ReadAccountsOrEmptyAsync(cancellationToken);
            if (accounts.RemoveAll(account => MediaCenterSecurity.SameSource(provider, baseUrl, account)) == 0) return;
            if (accounts.Count == 0)
            {
                if (File.Exists(_credentialPath)) File.Delete(_credentialPath);
                return;
            }
            await WriteAccountsAsync(accounts, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MediaCenterCredential?> FindAsync(
        Func<MediaCenterCredential, bool> predicate,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var accounts = await ReadAccountsOrEmptyAsync(cancellationToken);
            return accounts.FirstOrDefault(predicate);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<MediaCenterCredential>> ReadAccountsOrEmptyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ReadAccountsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private async Task<List<MediaCenterCredential>> ReadAccountsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_credentialPath)) return [];
        var protectedBytes = await File.ReadAllBytesAsync(_credentialPath, cancellationToken);
        var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var envelope = JsonSerializer.Deserialize<CredentialEnvelope>(clearBytes, JsonOptions);
            if (envelope?.Version != 1 || envelope.Accounts is null) return [];
            var accounts = new List<MediaCenterCredential>(envelope.Accounts.Count);
            foreach (var account in envelope.Accounts)
            {
                try
                {
                    accounts.Add(MediaCenterSecurity.ValidateCredential(account));
                }
                catch
                {
                    // One damaged entry must not make every protected source unavailable.
                }
            }
            return accounts;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    private async Task WriteAccountsAsync(
        IReadOnlyCollection<MediaCenterCredential> accounts,
        CancellationToken cancellationToken)
    {
        var envelope = new CredentialEnvelope
        {
            Version = 1,
            Accounts = accounts
                .Select(MediaCenterSecurity.ValidateCredential)
                .OrderBy(account => account.Binding.Provider, StringComparer.Ordinal)
                .ThenBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
        var clearBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        try
        {
            var protectedBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
            var directory = Path.GetDirectoryName(_credentialPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _credentialPath + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
            File.Move(temporaryPath, _credentialPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    private sealed class CredentialEnvelope
    {
        public int Version { get; set; }
        public List<MediaCenterCredential> Accounts { get; set; } = [];
    }
}
