using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StreamVue.Player.Services;

public sealed record XtreamCredentials(string Server, string Username, string Password);

public sealed class XtreamCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StreamVue.XtreamCredentials.v1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _credentialPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public XtreamCredentialStore(string? credentialPath = null)
    {
        _credentialPath = credentialPath ?? StreamVueDataPaths.Resolve("xtream-credentials.v1.bin");
    }

    public async Task SaveAsync(XtreamCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.Server);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.Username);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<XtreamCredentials> accounts;
            try
            {
                (accounts, _) = await ReadAccountsAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                accounts = [];
            }
            accounts.RemoveAll(account => SameServer(account.Server, credentials.Server));
            accounts.Add(credentials with { Server = NormalizeServer(credentials.Server) });
            await WriteAccountsAsync(accounts, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<XtreamCredentials?> TryLoadAsync(string server, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var (accounts, wasLegacy) = await ReadAccountsAsync(cancellationToken);
            var match = accounts.FirstOrDefault(account => SameServer(account.Server, server));
            if (wasLegacy)
            {
                try
                {
                    await WriteAccountsAsync(accounts, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // A migration write failure must not hide credentials that were decrypted successfully.
                }
            }
            return match;
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

    public async Task DeleteAsync(string server, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var (accounts, _) = await ReadAccountsAsync(cancellationToken);
            if (accounts.RemoveAll(account => SameServer(account.Server, server)) == 0) return;
            if (accounts.Count == 0)
            {
                File.Delete(_credentialPath);
                return;
            }
            await WriteAccountsAsync(accounts, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(List<XtreamCredentials> Accounts, bool WasLegacy)> ReadAccountsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_credentialPath)) return ([], false);
        var protectedBytes = await File.ReadAllBytesAsync(_credentialPath, cancellationToken);
        var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var envelope = JsonSerializer.Deserialize<XtreamCredentialEnvelope>(clearBytes, JsonOptions);
            if (envelope?.Version >= 2 && envelope.Accounts is not null)
                return (NormalizeAccounts(envelope.Accounts), false);

            var legacy = JsonSerializer.Deserialize<XtreamCredentials>(clearBytes, JsonOptions);
            return legacy is null ? ([], false) : (NormalizeAccounts([legacy]), true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    private async Task WriteAccountsAsync(IReadOnlyCollection<XtreamCredentials> accounts, CancellationToken cancellationToken)
    {
        var envelope = new XtreamCredentialEnvelope
        {
            Version = 2,
            Accounts = NormalizeAccounts(accounts)
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

    private static List<XtreamCredentials> NormalizeAccounts(IEnumerable<XtreamCredentials> accounts)
    {
        var normalized = new Dictionary<string, XtreamCredentials>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in accounts)
        {
            if (string.IsNullOrWhiteSpace(account.Server) || string.IsNullOrWhiteSpace(account.Username)) continue;
            var server = NormalizeServer(account.Server);
            normalized[BuildServerKey(server)] = account with { Server = server };
        }
        return normalized.Values.OrderBy(account => account.Server, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool SameServer(string left, string right) =>
        string.Equals(BuildServerKey(left), BuildServerKey(right), StringComparison.OrdinalIgnoreCase);

    private static string BuildServerKey(string value)
    {
        var normalized = NormalizeServer(value);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)) return normalized;
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        return $"{uri.IdnHost}{port}{uri.AbsolutePath.TrimEnd('/')}";
    }

    private static string NormalizeServer(string value)
    {
        var normalized = value.Trim();
        if (!normalized.Contains("://", StringComparison.Ordinal)) normalized = $"http://{normalized}";
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            ? new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/'), Query = string.Empty, Fragment = string.Empty }.Uri.ToString().TrimEnd('/')
            : normalized.TrimEnd('/');
    }

    private sealed class XtreamCredentialEnvelope
    {
        public int Version { get; set; }
        public List<XtreamCredentials> Accounts { get; set; } = [];
    }
}
