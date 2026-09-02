import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const read = (path) => readFile(join(repositoryRoot, path), "utf8");
const fail = (message) => {
  throw new Error(`Windows Plex account foundation failed: ${message}`);
};
const requireFragments = (source, fragments, label) => {
  for (const fragment of fragments) {
    if (!source.includes(fragment)) fail(`${label} is missing ${fragment}`);
  }
};

const [project, account, models, identity, windowXaml, windowCode, probe, privacyText] =
  await Promise.all([
    read("src/OrbitalVue.Player/OrbitalVue.Player.csproj"),
    read("src/OrbitalVue.Player/Services/MediaCenterSourceService.PlexAccount.cs"),
    read("src/OrbitalVue.Player/Models/PlexAccountModels.cs"),
    read("src/OrbitalVue.Player/Services/PlexDeviceIdentityStore.cs"),
    read("src/OrbitalVue.Player/MainWindow.xaml"),
    read("src/OrbitalVue.Player/MainWindow.xaml.cs"),
    read("tools/OrbitalVue.FeatureProbe/Program.cs"),
    read("store/privacy-data-inventory.json")
  ]);

requireFragments(project, [
  'PackageReference Include="NSec.Cryptography" Version="26.4.0"'
], "cryptographic dependency");

requireFragments(identity, [
  "SignatureAlgorithm.Ed25519",
  "DataProtectionScope.CurrentUser",
  "ProtectedData.Protect",
  "ProtectedData.Unprotect",
  "CryptographicOperations.ZeroMemory(privateSeed)",
  '["kty"] = "OKP"',
  '["crv"] = "Ed25519"',
  '["alg"] = "EdDSA"',
  "KeyExportPolicies.None",
  "SignJwt"
], "DPAPI-only Ed25519 device identity");
if (identity.includes('["d"]') || identity.includes("AllowPlaintextExport")) {
  fail("the device identity exposes private JWK material or an exportable runtime key");
}

requireFragments(account, [
  '"https://clients.plex.tv/api/v2"',
  '"https://plex.tv/api/v2"',
  '"clients.plex.tv", "plex.tv"',
  "strong = true",
  '("deviceJWT", proof)',
  "PlexAccountMaximumResponseBytes = 2 * 1024 * 1024",
  "PlexDiscoveryLifetime = TimeSpan.FromMinutes(10)",
  "PlexAccountDiscoverySecret",
  "servers.Select(secret => secret.Server).ToList()",
  "RequireAllowedTransport(selectedConnection.Url, allowInsecureHttp)",
  "ProbePlexIdentityAsync(selectedConnection.Url",
  "identity.ServerId, selectedServer.Server.ServerId",
  "CancelPlexAccountDiscovery",
  "if (!access.CanUseMediaCenters) CancelPlexAccountDiscovery()"
], "signed account and revocable discovery service");

if (/public[^\n]*(accountToken|accessToken)/i.test(account)) {
  fail("the public service boundary exposes token-shaped account data");
}
if (/Token\s*[,)]/i.test(models) || /AccountToken|AccessToken/i.test(models)) {
  fail("the public Plex discovery models expose a token");
}
requireFragments(models, [
  "public sealed record PlexPinChallenge",
  "public sealed record PlexServerDiscovery",
  "string SessionId",
  "IReadOnlyList<PlexDiscoveredServer> Servers"
], "token-free public discovery models");

requireFragments(windowXaml, [
  'Content="Sign in with Plex"',
  'Content="Open approval"',
  "Allow this unencrypted HTTP connection on my trusted local network",
  "Advanced: connect with a server token"
], "recommended WPF account sign-in surface");
requireFragments(windowCode, [
  "StartPlexAccountSignInAsync",
  "WaitForPlexAccountServersAsync",
  "ConnectDiscoveredPlexServerAsync",
  "CancelPlexAccountSignInCore",
  "PremiumStore_StateChanged",
  "PlexAccountAllowHttpBox.IsChecked == true"
], "WPF lifecycle and entitlement boundary");
const uiState = windowCode.match(/PlexPinChallenge\?[^;]+;[\s\S]*?PlexServerDiscovery\?[^;]+;/)?.[0] ?? "";
if (/token/i.test(uiState)) fail("the WPF Plex account state contains token-shaped data");

requireFragments(probe, [
  "create a strong public-key PIN challenge",
  "PlexDeviceProofVerified",
  "The DPAPI Plex device identity exposed clear signing material",
  "Plex account discovery exposed an account or server token",
  "accepted an unlisted server address",
  "accepted HTTP without explicit consent",
  "A changed Plex server identity was accepted",
  "A cancelled Plex discovery lease remained usable",
  "A Plex discovery lease survived entitlement revocation",
  "SignatureAlgorithm.Ed25519.Verify"
], "Windows feature-probe security coverage");

const privacy = JSON.parse(privacyText);
const providerFlow = privacy.dataFlows?.find(({ id }) => id === "provider-connections");
const identityFlow = privacy.dataFlows?.find(({ id }) => id === "provider-client-identity");
if (!providerFlow?.data?.includes("provider-account-token-during-authentication") ||
    !providerFlow.data.includes("provider-server-access-token")) {
  fail("the privacy inventory omits Plex account/server token handling");
}
if (!identityFlow?.data?.includes("plex-device-public-signing-key") ||
    !identityFlow.storage?.includes("Windows current-user DPAPI")) {
  fail("the privacy inventory omits Windows Plex signing-key protection");
}

console.log("Windows signed Plex account discovery is structurally valid, token-minimized, DPAPI-protected, and revocable.");
