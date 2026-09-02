import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const schema = JSON.parse(await readFile(join(here, "streamvue-catalog.schema.json"), "utf8"));
const catalog = JSON.parse(await readFile(join(here, "fixtures", "catalog.expected.json"), "utf8"));
const playlist = await readFile(join(here, "fixtures", "iptv-features.m3u"), "utf8");
const mediaCenterSchema = JSON.parse(
  await readFile(join(here, "media-center-contract-v1.schema.json"), "utf8")
);
const mediaCenter = JSON.parse(
  await readFile(join(here, "fixtures", "media-center.expected.json"), "utf8")
);
const premiumAccessSchema = JSON.parse(
  await readFile(join(here, "premium-access-contract-v1.schema.json"), "utf8")
);
const personalPremiumAccess = JSON.parse(
  await readFile(join(here, "fixtures", "premium-access.personal.expected.json"), "utf8")
);
const lockedStorePremiumAccess = JSON.parse(
  await readFile(join(here, "fixtures", "premium-access.store-locked.expected.json"), "utf8")
);
const lockedUnknownPremiumAccess = JSON.parse(
  await readFile(join(here, "fixtures", "premium-access.unknown-locked.expected.json"), "utf8")
);
const googlePlayVerifierSchema = JSON.parse(
  await readFile(join(here, "google-play-verifier-v1.schema.json"), "utf8")
);
const googlePlayVerificationRequest = JSON.parse(
  await readFile(join(here, "fixtures", "google-play.verify.request.json"), "utf8")
);
const googlePlayVerifiedResponse = JSON.parse(
  await readFile(join(here, "fixtures", "google-play.verify.verified.json"), "utf8")
);
const googlePlayUnverifiedResponse = JSON.parse(
  await readFile(join(here, "fixtures", "google-play.verify.unverified.json"), "utf8")
);
const samsungVerifierSchema = JSON.parse(
  await readFile(join(here, "samsung-checkout-verifier-v1.schema.json"), "utf8")
);
const samsungStatusRequest = JSON.parse(
  await readFile(join(here, "fixtures", "samsung-checkout.status.request.json"), "utf8")
);
const samsungAvailableResponse = JSON.parse(
  await readFile(join(here, "fixtures", "samsung-checkout.status.available.json"), "utf8")
);
const samsungVerifiedResponse = JSON.parse(
  await readFile(join(here, "fixtures", "samsung-checkout.status.verified.json"), "utf8")
);
const samsungUnavailableResponse = JSON.parse(
  await readFile(join(here, "fixtures", "samsung-checkout.status.unavailable.json"), "utf8")
);

const fail = (message) => {
  throw new Error(`Contract validation failed: ${message}`);
};

if (schema.$schema !== "https://json-schema.org/draft/2020-12/schema") {
  fail("the schema must remain on JSON Schema Draft 2020-12");
}
if (catalog.contractVersion !== "1.0") fail("unexpected contractVersion");
if (!catalog.catalogId || !catalog.displayName) fail("catalog identity is incomplete");
if (Number.isNaN(Date.parse(catalog.loadedAt))) fail("loadedAt is not an ISO date-time");
if (!Array.isArray(catalog.sources) || catalog.sources.length === 0) fail("at least one source is required");
if (!Array.isArray(catalog.channels) || catalog.channels.length === 0) fail("at least one channel is required");

const sourceIds = new Set(catalog.sources.map((source) => source.id));
const channelIds = new Set();
const allowedKinds = new Set(["live", "movie", "series", "recording", "replay", "music"]);
const allowedSchemes = new Set([
  "http:",
  "https:",
  "rtsp:",
  "rtmp:",
  "udp:",
  "file:",
  "streamvue-media:"
]);

for (const source of catalog.sources) {
  if (!source.id || !source.name || !source.type) fail("source identity is incomplete");
  if (/[?&](?:token|password|username|auth)=/i.test(source.displayLocation ?? "")) {
    fail(`source ${source.id} leaks a credential in displayLocation`);
  }
}

for (const channel of catalog.channels) {
  if (!/^[A-F0-9]{64}$/.test(channel.id)) fail(`channel ${channel.number} has an invalid stable id`);
  if (channelIds.has(channel.id)) fail(`duplicate channel id ${channel.id}`);
  channelIds.add(channel.id);
  if (!sourceIds.has(channel.sourceId)) fail(`channel ${channel.number} refers to an unknown source`);
  if (!allowedKinds.has(channel.kind)) fail(`channel ${channel.number} has an unknown kind`);
  if (!channel.name?.trim() || !channel.group?.trim()) fail(`channel ${channel.number} is missing browse metadata`);

  let uri;
  try {
    uri = new URL(channel.stream?.uri);
  } catch {
    fail(`channel ${channel.number} has an invalid stream URI`);
  }
  if (!allowedSchemes.has(uri.protocol)) fail(`channel ${channel.number} uses unsupported scheme ${uri.protocol}`);
  if (!playlist.includes(channel.name) || !playlist.includes(channel.stream.uri)) {
    fail(`channel ${channel.number} does not match the M3U conformance fixture`);
  }
}

if (mediaCenterSchema.$schema !== "https://json-schema.org/draft/2020-12/schema") {
  fail("the media-center schema must remain on JSON Schema Draft 2020-12");
}
if (mediaCenter.contractVersion !== "1.0") fail("unexpected media-center contractVersion");
if (Number.isNaN(Date.parse(mediaCenter.loadedAt))) fail("media-center loadedAt is not an ISO date-time");
if (!mediaCenter.connection || !["plex", "emby"].includes(mediaCenter.connection.provider)) {
  fail("media-center connection provider is invalid");
}
if (!mediaCenter.connection.credentialId) fail("media-center connection has no credential reference");
if (!/^https?:$/.test(new URL(mediaCenter.connection.baseUrl).protocol)) {
  fail("media-center connection must use HTTP or HTTPS");
}
if (new URL(mediaCenter.connection.baseUrl).username || new URL(mediaCenter.connection.baseUrl).password) {
  fail("media-center baseUrl contains credentials");
}
if (!Array.isArray(mediaCenter.libraries) || mediaCenter.libraries.length === 0) {
  fail("media-center fixture has no libraries");
}
if (!Array.isArray(mediaCenter.items) || mediaCenter.items.length === 0) {
  fail("media-center fixture has no items");
}

const forbiddenSecretKeys = new Set([
  "password",
  "username",
  "pw",
  "token",
  "accesstoken",
  "x-plex-token",
  "x-emby-token",
  "requestheaders",
  "playbackplan",
  "playsessionid",
  "livestreamid"
]);
const inspectForSecrets = (value, path = "mediaCenter") => {
  if (Array.isArray(value)) {
    value.forEach((entry, index) => inspectForSecrets(entry, `${path}[${index}]`));
    return;
  }
  if (!value || typeof value !== "object") return;
  for (const [key, child] of Object.entries(value)) {
    if (forbiddenSecretKeys.has(key.toLowerCase())) fail(`${path}.${key} is not cache-safe`);
    if (typeof child === "string" && /[?&](?:api_key|x-plex-token|token|access_token|password|pw)=/i.test(child)) {
      fail(`${path}.${key} contains a credential-bearing query`);
    }
    inspectForSecrets(child, `${path}.${key}`);
  }
};
inspectForSecrets(mediaCenter);

for (const item of mediaCenter.items) {
  if (item.provider !== mediaCenter.connection.provider) fail(`media-center item ${item.id} has a different provider`);
  if (item.serverId !== mediaCenter.connection.serverId) fail(`media-center item ${item.id} has a different server`);
  if (!mediaCenter.libraries.some((library) => library.id === item.libraryId)) {
    fail(`media-center item ${item.id} refers to an unknown library`);
  }
}

if (premiumAccessSchema.$schema !== "https://json-schema.org/draft/2020-12/schema") {
  fail("the premium-access schema must remain on JSON Schema Draft 2020-12");
}
const validatePremiumDecision = (decision, expected) => {
  const allowedKeys = new Set([
    "contractVersion",
    "featureId",
    "distributionMode",
    "accessState",
    "acquisition",
    "receiptVerification",
    "productId"
  ]);
  for (const key of Object.keys(decision)) {
    if (!allowedKeys.has(key)) fail(`premium-access decision contains unexpected field ${key}`);
  }
  if (decision.contractVersion !== "1.0") fail("unexpected premium-access contractVersion");
  if (decision.featureId !== "personal-media-centers") fail("unexpected premium feature identifier");
  for (const [key, value] of Object.entries(expected)) {
    if (decision[key] !== value) fail(`premium-access ${decision.distributionMode}.${key} is invalid`);
  }
  const serialized = JSON.stringify(decision).toLowerCase();
  for (const secretName of ["purchase-token", "account-id", "password", "access-token"]) {
    if (serialized.includes(secretName)) fail(`premium-access decision contains ${secretName}`);
  }
};
validatePremiumDecision(personalPremiumAccess, {
  distributionMode: "personal",
  accessState: "included",
  acquisition: "included",
  receiptVerification: "not-required"
});
validatePremiumDecision(lockedStorePremiumAccess, {
  distributionMode: "store",
  accessState: "unavailable",
  acquisition: "one-time",
  receiptVerification: "unavailable"
});
validatePremiumDecision(lockedUnknownPremiumAccess, {
  distributionMode: "unknown",
  accessState: "unavailable",
  acquisition: "one-time",
  receiptVerification: "unavailable"
});

if (googlePlayVerifierSchema.$schema !== "https://json-schema.org/draft/2020-12/schema") {
  fail("the Google Play verifier schema must remain on JSON Schema Draft 2020-12");
}
assertExactKeys(
  googlePlayVerificationRequest,
  ["schemaVersion", "platform", "packageName", "productId", "purchaseToken"],
  "Google Play verification request"
);
if (googlePlayVerificationRequest.schemaVersion !== 1
  || googlePlayVerificationRequest.platform !== "google-play"
  || !/^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+$/.test(googlePlayVerificationRequest.packageName)
  || !/^[A-Za-z0-9._-]{3,256}$/.test(googlePlayVerificationRequest.productId)
  || typeof googlePlayVerificationRequest.purchaseToken !== "string"
  || googlePlayVerificationRequest.purchaseToken.length === 0) {
  fail("Google Play verification request fixture is invalid");
}
const validateGooglePlayResponse = (response, expectedVerified) => {
  assertExactKeys(response, ["schemaVersion", "verified", "productId"], "Google Play verification response");
  if (response.schemaVersion !== 1
    || response.verified !== expectedVerified
    || response.productId !== googlePlayVerificationRequest.productId) {
    fail("Google Play verification response fixture is invalid");
  }
  const serialized = JSON.stringify(response).toLowerCase();
  for (const secretName of ["purchasetoken", "receipt", "password", "access-token", "accountid"]) {
    if (serialized.includes(secretName)) fail(`Google Play response contains forbidden ${secretName}`);
  }
};
validateGooglePlayResponse(googlePlayVerifiedResponse, true);
validateGooglePlayResponse(googlePlayUnverifiedResponse, false);

if (samsungVerifierSchema.$schema !== "https://json-schema.org/draft/2020-12/schema") {
  fail("the Samsung Checkout verifier schema must remain on JSON Schema Draft 2020-12");
}
const samsungProductPattern = /^[A-Za-z0-9_-]{1,20}$/;
function assertExactKeys(value, expectedKeys, label) {
  const keys = Object.keys(value).sort();
  const expected = [...expectedKeys].sort();
  if (JSON.stringify(keys) !== JSON.stringify(expected)) {
    fail(`${label} fields are not exact`);
  }
}
assertExactKeys(
  samsungStatusRequest,
  ["schemaVersion", "platform", "action", "appId", "productId", "customId", "countryCode"],
  "Samsung status request"
);
if (samsungStatusRequest.schemaVersion !== 1
  || samsungStatusRequest.platform !== "samsung"
  || samsungStatusRequest.action !== "status"
  || !/^[A-Za-z0-9._-]{3,30}$/.test(samsungStatusRequest.appId)
  || !samsungProductPattern.test(samsungStatusRequest.productId)
  || !/^[A-Z]{2}$/.test(samsungStatusRequest.countryCode)
  || !samsungStatusRequest.customId) {
  fail("Samsung status request fixture is invalid");
}
const validateSamsungResponse = (response, expectedVerified) => {
  const expectedKeys = expectedVerified || response.checkoutAvailable === false
    ? ["schemaVersion", "verified", "checkoutAvailable", "productId"]
    : ["schemaVersion", "verified", "checkoutAvailable", "productId", "product"];
  assertExactKeys(response, expectedKeys, "Samsung status response");
  if (response.schemaVersion !== 1
    || response.verified !== expectedVerified
    || typeof response.checkoutAvailable !== "boolean"
    || response.productId !== samsungStatusRequest.productId) {
    fail("Samsung status response fixture is invalid");
  }
  if (!expectedVerified && response.checkoutAvailable) {
    assertExactKeys(
      response.product,
      ["productId", "title", "localizedPrice", "orderTotal", "currencyId"],
      "Samsung product offer"
    );
    if (response.product.productId !== response.productId
      || !/^[A-Z]{3}$/.test(response.product.currencyId)
      || !/^(?:0|[1-9]\d{0,11})(?:\.\d{1,2})?$/.test(response.product.orderTotal)) {
      fail("Samsung product offer fixture is invalid");
    }
  }
  const serialized = JSON.stringify(response).toLowerCase();
  for (const secretName of ["checkvalue", "securitykey", "receipt", "password", "token", "customid"]) {
    if (serialized.includes(secretName)) fail(`Samsung response contains forbidden ${secretName}`);
  }
};
validateSamsungResponse(samsungAvailableResponse, false);
validateSamsungResponse(samsungVerifiedResponse, true);
validateSamsungResponse(samsungUnavailableResponse, false);

console.log(
  `OrbitalVue contracts 1.0 are valid (${catalog.channels.length} playlist channels, ${mediaCenter.items.length} media-center items, personal/store/unknown premium access, Google Play and Samsung verifier exchanges).`
);
