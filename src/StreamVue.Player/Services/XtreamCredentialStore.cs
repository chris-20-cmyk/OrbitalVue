using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StreamVue.Player.Services;

public sealed record XtreamCredentials(string Server, string Username, string Password);

public sealed class XtreamCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StreamVue.XtreamCredentials.v1");
    private readonly string _credentialPath;

    public XtreamCredentialStore(string? credentialPath = null)
    {
        _credentialPath = credentialPath ?? StreamVueDataPaths.Resolve("xtream-credentials.v1.bin");
    }

    public async Task SaveAsync(XtreamCredentials credentials, CancellationToken cancellationToken = default)
    {
        var clearBytes = JsonSerializer.SerializeToUtf8Bytes(credentials);
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

    public async Task<XtreamCredentials?> TryLoadAsync(string server, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_credentialPath)) return null;
            var protectedBytes = await File.ReadAllBytesAsync(_credentialPath, cancellationToken);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var credentials = JsonSerializer.Deserialize<XtreamCredentials>(clearBytes);
                return credentials is not null && SameServer(credentials.Server, server) ? credentials : null;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearBytes);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static bool SameServer(string left, string right) =>
        string.Equals(NormalizeServer(left), NormalizeServer(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeServer(string value)
    {
        var normalized = value.Trim();
        if (!normalized.Contains("://", StringComparison.Ordinal)) normalized = $"http://{normalized}";
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            ? new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/'), Query = string.Empty, Fragment = string.Empty }.Uri.ToString().TrimEnd('/')
            : normalized.TrimEnd('/');
    }
}
