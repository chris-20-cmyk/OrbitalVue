using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;

namespace OrbitalVue.Player.Services;

/// <summary>
/// Persists only a DPAPI-protected Ed25519 seed. The matching public key and
/// client identifier are safe to send to Plex; the seed never leaves this PC.
/// </summary>
public sealed class PlexDeviceIdentityStore
{
    private const int EnvelopeVersion = 1;
    private const int PrivateSeedSize = 32;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OrbitalVue.PlexDeviceIdentity.v1");
    private readonly string _identityPath;
    private readonly object _gate = new();

    public PlexDeviceIdentityStore(string? identityPath = null)
    {
        _identityPath = Path.GetFullPath(identityPath ?? OrbitalVueDataPaths.Resolve("plex-device-identity.v1.bin"));
    }

    internal WindowsPlexDeviceSigner OpenSigner(string clientIdentifier)
    {
        var safeClientIdentifier = MediaCenterSecurity.RequireIdentifier(
            clientIdentifier,
            "Plex client identifier");
        lock (_gate)
        {
            return new WindowsPlexDeviceSigner(safeClientIdentifier, LoadOrCreateKey());
        }
    }

    private Key LoadOrCreateKey()
    {
        if (File.Exists(_identityPath)) return ImportProtectedKey();

        var creationParameters = new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextArchiving
        };
        using var generated = Key.Create(SignatureAlgorithm.Ed25519, creationParameters);
        var privateSeed = generated.Export(KeyBlobFormat.RawPrivateKey);
        try
        {
            if (privateSeed.Length != PrivateSeedSize)
                throw new CryptographicException("Windows generated an invalid Plex device key.");
            PersistProtectedSeed(privateSeed);
            var importParameters = new KeyCreationParameters
            {
                ExportPolicy = KeyExportPolicies.None
            };
            return Key.Import(
                SignatureAlgorithm.Ed25519,
                privateSeed,
                KeyBlobFormat.RawPrivateKey,
                importParameters);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateSeed);
        }
    }

    private Key ImportProtectedKey()
    {
        byte[]? clearEnvelope = null;
        byte[]? privateSeed = null;
        try
        {
            var protectedEnvelope = File.ReadAllBytes(_identityPath);
            if (protectedEnvelope.Length == 0)
                throw new CryptographicException("The protected Plex device identity is empty.");
            clearEnvelope = ProtectedData.Unprotect(
                protectedEnvelope,
                Entropy,
                DataProtectionScope.CurrentUser);
            if (clearEnvelope.Length != sizeof(int) + PrivateSeedSize ||
                BinaryPrimitives.ReadInt32LittleEndian(clearEnvelope) != EnvelopeVersion)
                throw new CryptographicException("The protected Plex device identity is invalid.");
            privateSeed = clearEnvelope.AsSpan(sizeof(int), PrivateSeedSize).ToArray();
            var importParameters = new KeyCreationParameters
            {
                ExportPolicy = KeyExportPolicies.None
            };
            return Key.Import(
                SignatureAlgorithm.Ed25519,
                privateSeed,
                KeyBlobFormat.RawPrivateKey,
                importParameters);
        }
        catch (CryptographicException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CryptographicException(
                "Windows could not open the protected Plex device identity.",
                exception);
        }
        finally
        {
            if (privateSeed is not null) CryptographicOperations.ZeroMemory(privateSeed);
            if (clearEnvelope is not null) CryptographicOperations.ZeroMemory(clearEnvelope);
        }
    }

    private void PersistProtectedSeed(ReadOnlySpan<byte> privateSeed)
    {
        var clearEnvelope = new byte[sizeof(int) + PrivateSeedSize];
        byte[]? protectedEnvelope = null;
        try
        {
            BinaryPrimitives.WriteInt32LittleEndian(clearEnvelope, EnvelopeVersion);
            privateSeed.CopyTo(clearEnvelope.AsSpan(sizeof(int)));
            protectedEnvelope = ProtectedData.Protect(
                clearEnvelope,
                Entropy,
                DataProtectionScope.CurrentUser);
            var directory = Path.GetDirectoryName(_identityPath)
                ?? throw new IOException("The Plex device identity path is invalid.");
            Directory.CreateDirectory(directory);
            var temporaryPath = _identityPath + ".tmp";
            File.WriteAllBytes(temporaryPath, protectedEnvelope);
            File.Move(temporaryPath, _identityPath, overwrite: false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearEnvelope);
            if (protectedEnvelope is not null) CryptographicOperations.ZeroMemory(protectedEnvelope);
        }
    }
}

internal sealed class WindowsPlexDeviceSigner : IDisposable
{
    private readonly Key _key;

    public WindowsPlexDeviceSigner(string clientIdentifier, Key key)
    {
        ClientIdentifier = clientIdentifier;
        _key = key;
        var publicBytes = _key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        try
        {
            if (publicBytes.Length != 32)
                throw new CryptographicException("Windows exported an invalid Plex device public key.");
            var encodedPublicKey = Base64Url(publicBytes);
            PublicJwk = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["alg"] = "EdDSA",
                ["crv"] = "Ed25519",
                ["kid"] = Base64Url(SHA256.HashData(publicBytes)),
                ["kty"] = "OKP",
                ["x"] = encodedPublicKey
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicBytes);
        }
    }

    public string ClientIdentifier { get; }
    public IReadOnlyDictionary<string, string> PublicJwk { get; }

    public string SignJwt(IReadOnlyDictionary<string, object> claims)
    {
        var header = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["alg"] = "EdDSA",
            ["kid"] = PublicJwk["kid"],
            ["typ"] = "JWT"
        };
        var orderedClaims = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in claims) orderedClaims[name] = value;
        var encodedHeader = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var encodedClaims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(orderedClaims));
        var signingInput = $"{encodedHeader}.{encodedClaims}";
        var signature = SignatureAlgorithm.Ed25519.Sign(_key, Encoding.UTF8.GetBytes(signingInput));
        try
        {
            if (signature.Length != 64)
                throw new CryptographicException("Windows generated an invalid Plex device proof.");
            return $"{signingInput}.{Base64Url(signature)}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    public void Dispose() => _key.Dispose();

    private static string Base64Url(ReadOnlySpan<byte> value) => Convert
        .ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
