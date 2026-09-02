import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const read = (path) => readFile(join(repositoryRoot, path), "utf8");
const fail = (message) => {
  throw new Error(`Android Plex account foundation failed: ${message}`);
};
const requireFragments = (source, fragments, label) => {
  for (const fragment of fragments) {
    if (!source.includes(fragment)) fail(`${label} is missing ${fragment}`);
  }
};

const [
  account,
  signer,
  service,
  repository,
  viewModel,
  panel,
  app,
  tests,
  discoveryTests,
  manifest,
  privacyText
] = await Promise.all([
  read("platforms/android/app/src/main/java/com/streamvue/player/data/PlexAccount.kt"),
  read("platforms/android/app/src/main/java/com/streamvue/player/data/AndroidPlexDeviceSigner.kt"),
  read("platforms/android/app/src/main/java/com/streamvue/player/data/MediaCenterService.kt"),
  read("platforms/android/app/src/main/java/com/streamvue/player/data/MediaCenterRepository.kt"),
  read("platforms/android/app/src/main/java/com/streamvue/player/MainViewModel.kt"),
  read("platforms/android/app/src/main/java/com/streamvue/player/ui/PlexAccountConnectPanel.kt"),
  read("platforms/android/app/src/main/java/com/streamvue/player/ui/StreamVueApp.kt"),
  read("platforms/android/app/src/test/java/com/streamvue/player/data/PlexAccountClientTest.kt"),
  read("platforms/android/app/src/test/java/com/streamvue/player/data/PlexDiscoverySecurityTest.kt"),
  read("platforms/android/app/src/main/AndroidManifest.xml"),
  read("store/privacy-data-inventory.json")
]);

requireFragments(account, [
  'addProperty("strong", true)',
  'mapOf("deviceJWT" to proof)',
  'const val CLIENTS_BASE_URL = "https://clients.plex.tv/api/v2"',
  'const val ACCOUNT_BASE_URL = "https://plex.tv/api/v2"',
  '"X-Plex-Token"',
  "MAX_RESPONSE_BYTES = 2 * 1_024 * 1_024",
  "parseServer(it, accountToken)",
  "listOf(accountToken, accessToken)"
], "signed account client");

const publicServerStart = account.indexOf("data class PlexDiscoveredServer");
const internalSecretStart = account.indexOf("internal data class PlexAccountToken", publicServerStart);
if (publicServerStart < 0 || internalSecretStart <= publicServerStart) {
  fail("the sanitized public discovery boundary is missing");
}
const publicDiscovery = account.slice(publicServerStart, internalSecretStart);
if (publicDiscovery.includes("accessToken") || publicDiscovery.includes("accountToken")) {
  fail("the public discovery models expose a Plex token");
}

requireFragments(signer, [
  'addProperty("kty", "OKP")',
  'addProperty("crv", "Ed25519")',
  'addProperty("alg", "EdDSA")',
  "AndroidKeystore.hasKey(MASTER_KEY_ALIAS)",
  "AndroidKeystore.generateNewAes256GcmKey(MASTER_KEY_ALIAS)",
  "AndroidKeystore.getAead(MASTER_KEY_ALIAS)",
  "verifyMasterKey(masterAead)",
  "TinkProtoKeysetFormat.serializeEncryptedKeyset",
  "TinkProtoKeysetFormat.parseEncryptedKeyset",
  "PredefinedSignatureParameters.ED25519WithRawOutput",
  "RegistryConfiguration.get()",
  "MessageDigest.isEqual",
  "putString(KEYSET_NAME, Hex.encode(encrypted)).commit()"
], "Keystore-only device signer");
if (signer.includes("AndroidKeysetManager") || signer.includes("CleartextKeysetHandle")
  || signer.includes("doNotUseKeystore")) {
  fail("the device signer permits an opportunistic or cleartext keyset path");
}

requireFragments(service, [
  "plexDiscoverySessions",
  "PLEX_DISCOVERY_LIFETIME_SECONDS = 10 * 60L",
  "servers = servers.map(PlexAccountServerSecret::server)",
  "connectDiscoveredPlexServer",
  "expectedServerId = secret.server.serverId",
  "plexDiscoveryConnectionsInFlight",
  "cancelledPlexDiscoverySessions",
  "throw CancellationException",
  "credentialVault.remove(connection.credentialId)"
], "service isolation boundary");
requireFragments(repository, [
  "currentPremiumAccess().requireMediaCenters()",
  "createPlexSignInChallenge",
  "completePlexSignIn",
  "connectDiscoveredPlexServer",
  "service.cancelPlexDiscovery(it.sessionId)",
  "service.disconnect(connection)"
], "premium repository boundary");
requireFragments(viewModel, [
  "data class PlexSignInUiState",
  "delay(PLEX_PIN_POLL_INTERVAL_MS)",
  "catch (cancelled: CancellationException)",
  "cancelPlexDiscovery",
  "allowInsecureHttp"
], "lifecycle model");
const uiStateStart = viewModel.indexOf("data class PlexSignInUiState");
const uiStateEnd = viewModel.indexOf("data class AppUiState", uiStateStart);
if (uiStateStart < 0 || uiStateEnd <= uiStateStart) fail("the Plex UI-state boundary is missing");
if (/token/i.test(viewModel.slice(uiStateStart, uiStateEnd))) {
  fail("Plex UI state exposes token-shaped data");
}
requireFragments(panel, [
  "PlexQrCode(authorizationUrl)",
  "Open Plex sign-in",
  "PlexServerMenu",
  "PlexConnectionMenu",
  "Allow unencrypted local connection",
  "HTTP can expose the server token and viewing activity"
], "Compose sign-in surface");
requireFragments(app, [
  "PlexAccountConnectPanel",
  "Advanced: connect with server token"
], "recommended and advanced connection paths");
requireFragments(tests, [
  "creates a strong pin with public key material only",
  "claims the pin with a compact signed device proof",
  "discovers servers and prefers secure local connections",
  "rejects private key material before contacting Plex"
], "protocol unit tests");
requireFragments(discoveryTests, [
  "signed discovery exposes no token and persists only the selected server token",
  "unlisted connection is rejected before a server request or credential write",
  "changed server identity is rejected before its token is stored",
  "http requires consent and the same discovery can recover after denial",
  "cancelling an in-flight selection removes its newly stored credential",
  "assertFalse(encodedDiscovery.contains(\"account-token\"))",
  "assertFalse(encodedDiscovery.contains(\"server-token\"))",
  "assertTrue(vault.values.isEmpty())"
], "discovery security tests");
requireFragments(manifest, [
  'android:allowBackup="false"',
  'android:dataExtractionRules="@xml/data_extraction_rules"',
  'android:fullBackupContent="@xml/backup_rules"'
], "Android backup boundary");

const privacy = JSON.parse(privacyText);
const providerFlow = privacy.dataFlows?.find(({ id }) => id === "provider-connections");
const identityFlow = privacy.dataFlows?.find(({ id }) => id === "provider-client-identity");
if (!providerFlow?.data?.includes("provider-account-token-during-authentication")
  || !providerFlow.data.includes("provider-server-access-token")) {
  fail("the privacy inventory omits Plex account/server token handling");
}
if (!identityFlow?.data?.includes("plex-device-public-signing-key")
  || !identityFlow.storage?.includes("Android Keystore")) {
  fail("the privacy inventory omits Android Plex signing-key protection");
}

console.log("Android signed Plex account discovery is structurally valid, token-minimized, and Keystore-only.");
