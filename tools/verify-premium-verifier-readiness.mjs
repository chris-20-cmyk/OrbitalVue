import { access, readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const readText = (path) => readFile(join(repositoryRoot, path), "utf8");
const [manifest, premium, samsungDistribution] = await Promise.all([
  readText("store/premium-verifier-readiness.json").then(JSON.parse),
  readText("store/premium-products.json").then(JSON.parse),
  readText("store/samsung-distribution.json").then(JSON.parse)
]);

const fail = (message) => {
  throw new Error(`Premium verifier readiness failed: ${message}`);
};
const exactKeys = (value, keys, label) => {
  if (!value || typeof value !== "object" || Array.isArray(value)) fail(`${label} is missing`);
  if (JSON.stringify(Object.keys(value).sort()) !== JSON.stringify([...keys].sort())) {
    fail(`${label} fields must be exact`);
  }
};

exactKeys(manifest, ["contractVersion", "implementation", "platforms"], "manifest");
if (manifest.contractVersion !== "1.0") fail("unexpected contractVersion");
exactKeys(manifest.implementation, [
  "packageName",
  "sourceRoot",
  "googlePlayRoute",
  "googlePlayApiMethod",
  "samsungRoute",
  "samsungDpiOrigin"
], "implementation");
const expectedImplementation = {
  packageName: "@streamvue/entitlement-verifier",
  sourceRoot: "packages/entitlement-verifier",
  googlePlayRoute: "/google-play/verify",
  googlePlayApiMethod: "purchases.productsv2.getproductpurchasev2",
  samsungRoute: "/samsung/status",
  samsungDpiOrigin: "https://checkoutapi.samsungcheckout.com"
};
for (const [key, expected] of Object.entries(expectedImplementation)) {
  if (manifest.implementation[key] !== expected) fail(`implementation.${key} is not the reviewed value`);
}
exactKeys(manifest.platforms, ["android", "samsung"], "platform map");

const platformKeys = {
  android: [
    "verificationProvider",
    "productionUrl",
    "secretManagerConfigured",
    "playApiAccessGranted",
    "edgeRateLimitConfigured",
    "privacyRetentionReviewed",
    "realPurchaseVerified",
    "pendingPurchaseTested",
    "refundRevocationTested",
    "ready"
  ],
  samsung: [
    "verificationProvider",
    "productionUrl",
    "secretManagerConfigured",
    "dpiApiAccessGranted",
    "edgeRateLimitConfigured",
    "privacyRetentionReviewed",
    "realPurchaseVerified",
    "unavailableCountryTested",
    "refundRevocationTested",
    "ready"
  ]
};
const providers = {
  android: "google-play-developer-api",
  samsung: "samsung-dpi-purchase-history"
};
const routes = { android: "/google-play/verify", samsung: "/samsung/status" };

for (const platform of ["android", "samsung"]) {
  const entry = manifest.platforms[platform];
  exactKeys(entry, platformKeys[platform], `${platform} verifier entry`);
  if (entry.verificationProvider !== providers[platform]) fail(`${platform} verifier provider is invalid`);
  for (const [key, value] of Object.entries(entry)) {
    if (!["verificationProvider", "productionUrl"].includes(key) && typeof value !== "boolean") {
      fail(`${platform}.${key} must be boolean`);
    }
  }
  if (entry.productionUrl !== null) {
    let url;
    try { url = new URL(entry.productionUrl); } catch { fail(`${platform}.productionUrl is invalid`); }
    if (url.protocol !== "https:"
      || !url.hostname
      || url.username
      || url.password
      || url.search
      || url.hash
      || url.pathname !== routes[platform]) {
      fail(`${platform}.productionUrl must be a clean HTTPS URL ending at ${routes[platform]}`);
    }
  }
  const evidence = Object.entries(entry)
    .filter(([key]) => !["verificationProvider", "productionUrl", "ready"].includes(key))
    .every(([, value]) => value === true);
  const expectedReady = entry.productionUrl !== null && evidence;
  if (entry.ready !== expectedReady) fail(`${platform}.ready does not match its deployment and test evidence`);
  if (entry.ready) {
    if (!premium.platforms[platform].ready
      || premium.platforms[platform].verificationProvider !== providers[platform]) {
      fail(`${platform} verifier cannot be ready before the matching premium product is ready`);
    }
    if (platform === "samsung" && samsungDistribution.verifierUrl !== entry.productionUrl) {
      fail("Samsung verifier URL does not match the Samsung distribution manifest");
    }
  }
}

const requiredFiles = [
  "packages/entitlement-verifier/package.json",
  "packages/entitlement-verifier/src/contracts.ts",
  "packages/entitlement-verifier/src/google-play.ts",
  "packages/entitlement-verifier/src/google-service-account.ts",
  "packages/entitlement-verifier/src/handler.ts",
  "packages/entitlement-verifier/src/samsung-dpi.ts",
  "packages/entitlement-verifier/test/google-play.test.ts",
  "packages/entitlement-verifier/test/samsung-dpi.test.ts",
  "contracts/google-play-verifier-v1.schema.json",
  "contracts/samsung-checkout-verifier-v1.schema.json"
];
await Promise.all(requiredFiles.map((path) => access(join(repositoryRoot, path))));
const [googleSource, samsungSource, handlerSource, packageSource] = await Promise.all([
  readText("packages/entitlement-verifier/src/google-play.ts"),
  readText("packages/entitlement-verifier/src/samsung-dpi.ts"),
  readText("packages/entitlement-verifier/src/handler.ts"),
  readText("packages/entitlement-verifier/package.json")
]);
for (const fragment of [
  "purchases/productsv2/tokens",
  'purchaseState !== "PURCHASED"',
  "refundableQuantity",
  'consumptionState !== "YET_TO_BE_CONSUMED"'
]) {
  if (!googleSource.includes(fragment)) fail(`Google verifier implementation is missing ${fragment}`);
}
for (const fragment of ["/openapi/cont/list", "/openapi/invoice/list", "/openapi/invoice/verify", "constantTimeEqual", "CancelStatus"]) {
  if (!samsungSource.includes(fragment)) fail(`Samsung verifier implementation is missing ${fragment}`);
}
if (!handlerSource.includes("no-store")
  || !handlerSource.includes('errorResponse(503, "verification-unavailable")')) {
  fail("HTTP verifier boundary must stay non-cacheable and secret-free on provider errors");
}
if (JSON.parse(packageSource).name !== manifest.implementation.packageName) fail("verifier package name mismatch");

const requireReadyIndex = process.argv.indexOf("--require-ready");
if (requireReadyIndex >= 0) {
  const platform = process.argv[requireReadyIndex + 1];
  if (!["android", "samsung"].includes(platform)) fail("--require-ready expects android or samsung");
  if (!manifest.platforms[platform].ready) {
    fail(`${platform} verifier is intentionally blocked until production hosting, secrets, controls, and real-device evidence are complete`);
  }
  console.log(`${platform} premium verifier is production-ready.`);
}

const expectUrlIndex = process.argv.indexOf("--expect-url");
if (expectUrlIndex >= 0) {
  const platform = process.argv[expectUrlIndex + 1];
  const expectedUrl = process.argv[expectUrlIndex + 2];
  if (!["android", "samsung"].includes(platform) || !expectedUrl) {
    fail("--expect-url expects android or samsung and an exact URL");
  }
  if (manifest.platforms[platform].productionUrl !== expectedUrl) {
    fail(`${platform} production verifier URL does not match the release build input`);
  }
  console.log(`${platform} release URL exactly matches the verifier readiness manifest.`);
}

if (requireReadyIndex < 0) {
  const ready = ["android", "samsung"].filter((platform) => manifest.platforms[platform].ready);
  console.log(`Premium verifier implementation is valid: ${ready.length}/2 production deployments ready; incomplete deployments remain locked.`);
}
