import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const read = (path) => readFile(join(repositoryRoot, path), "utf8");
const fail = (message) => {
  throw new Error(`Apple Plex account foundation failed: ${message}`);
};
const requireFragments = (source, fragments, label) => {
  for (const fragment of fragments) {
    if (!source.includes(fragment)) fail(`${label} is missing ${fragment}`);
  }
};

const [
  account,
  service,
  repository,
  store,
  model,
  view,
  tests,
  premiumTests,
  privacyText
] = await Promise.all([
  read("platforms/apple/Sources/OrbitalVueCore/PlexAccount.swift"),
  read("platforms/apple/Sources/OrbitalVueCore/MediaCenterService.swift"),
  read("platforms/apple/Sources/OrbitalVueCore/MediaCenterRepository.swift"),
  read("platforms/apple/Sources/OrbitalVueUI/OrbitalVueStore.swift"),
  read("platforms/apple/Sources/OrbitalVueUI/PlexAccountConnectModel.swift"),
  read("platforms/apple/Sources/OrbitalVueUI/Views/PlexAccountConnectSection.swift"),
  read("platforms/apple/Tests/OrbitalVueCoreTests/MediaCenterTests.swift"),
  read("platforms/apple/Tests/OrbitalVueCoreTests/PremiumAccessTests.swift"),
  read("store/privacy-data-inventory.json")
]);

requireFragments(account, [
  "Curve25519.Signing.PrivateKey",
  '"kty": "OKP"',
  '"crv": "Ed25519"',
  '"alg": "EdDSA"',
  '"strong": true',
  '"deviceJWT"',
  '"https://clients.plex.tv/api/v2"',
  '"https://plex.tv/api/v2"',
  '"X-Plex-Token"',
  "maximumResponseBytes = 2 * 1_024 * 1_024",
  "compactMap { parseServer($0, excluding: token) }",
  "excluding: [accountToken, accessToken]"
], "signed account client");

const publicServerStart = account.indexOf("public struct PlexDiscoveredServer");
const publicServerEnd = account.indexOf("public struct PlexServerDiscovery", publicServerStart);
if (publicServerStart < 0 || publicServerEnd <= publicServerStart) {
  fail("the sanitized public server boundary is missing");
}
if (account.slice(publicServerStart, publicServerEnd).includes("accessToken")) {
  fail("the public server choice exposes an access token");
}
if (/public struct PlexAccountToken/.test(account) || /public let accessToken/.test(account)) {
  fail("an account or server token became public API");
}

requireFragments(service, [
  "plexDiscoverySessions",
  "Date().addingTimeInterval(10 * 60)",
  "servers: servers.map(\\.server)",
  "purgeExpiredPlexDiscoverySessions",
  "secretStore.save(",
  "plex-account-client-identifier-v1",
  "plex-account-ed25519-v1-",
  "connectDiscoveredPlexServer",
  "expectedServerID",
  "plexDiscoveryConnectionsInFlight",
  "cancelledPlexDiscoverySessions",
  "Task.checkCancellation()",
  "throw CancellationError()",
  "cancelAllPlexDiscovery",
  "plexDiscoverySessions.removeValue(forKey: sessionID)"
], "service isolation boundary");
requireFragments(repository, [
  "try await requirePremiumAccess()",
  "createPlexSignInChallenge",
  "completePlexSignIn",
  "connectDiscoveredPlexServer",
  "await service.cancelPlexDiscovery(sessionID: discovery.sessionID)",
  "ensurePremiumAccess"
], "premium repository boundary");
requireFragments(store, [
  "cancelAllPlexDiscovery",
  "try await mediaCenterRepository.ensurePremiumAccess()",
  "catch is CancellationError"
], "premium UI application boundary");
requireFragments(model, [
  "Task.sleep(for: .seconds(2))",
  "while !Task.isCancelled",
  "consecutiveFailures >= 3",
  "cancelPlexDiscovery",
  "allowInsecureHTTP"
], "lifecycle model");
requireFragments(view, [
  "PlexSignInQRCode",
  "actionTask?.cancel()",
  "Link(destination: challenge.authorizationURL)",
  ".task(id: model.challengeID)",
  "Allow unencrypted local connection",
  "QR code for Plex sign-in"
], "SwiftUI sign-in surface");
requireFragments(tests, [
  "Completes signed Plex account discovery without exposing account tokens",
  "Moves a discovered Plex server token directly into an origin-bound credential",
  "Rejects a Plex connection when the selected server identity changes",
  "Cancelling a Plex discovery connection rolls back its credential",
  "deviceJWT",
  "discoverySessionExpired",
  "allValues"
], "core tests");
requireFragments(premiumTests, [
  "Plex account discovery fails closed when premium access is revoked in flight",
  "await runtime.update(locked)",
  "catch is PremiumAccessError"
], "premium race tests");

const privacy = JSON.parse(privacyText);
const providerFlow = privacy.dataFlows?.find(({ id }) => id === "provider-connections");
const identityFlow = privacy.dataFlows?.find(({ id }) => id === "provider-client-identity");
if (!providerFlow?.data?.includes("provider-account-token-during-authentication")
  || !providerFlow.data.includes("provider-server-access-token")) {
  fail("the privacy inventory omits Plex account/server token handling");
}
if (!identityFlow?.data?.includes("plex-device-public-signing-key")
  || !identityFlow.storage?.toLowerCase().includes("private signing key in keychain")) {
  fail("the privacy inventory omits the Plex signing identity boundary");
}

console.log("Apple signed Plex account discovery foundation is structurally valid and token-minimized.");
