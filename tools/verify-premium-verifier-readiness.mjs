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
const exactStringArray = (value, expected, label) => {
  if (!Array.isArray(value) || value.some((item) => typeof item !== "string")) fail(`${label} must be a string array`);
  if (JSON.stringify([...value].sort()) !== JSON.stringify([...expected].sort())) fail(`${label} entries must be exact`);
};

exactKeys(manifest, ["contractVersion", "implementation", "platforms"], "manifest");
if (manifest.contractVersion !== "1.0") fail("unexpected contractVersion");
exactKeys(manifest.implementation, [
  "packageName",
  "sourceRoot",
  "googlePlayRoute",
  "googlePlayApiMethod",
  "samsungRoute",
  "samsungDpiOrigin",
  "hosting"
], "implementation");
const expectedImplementation = {
  packageName: "@orbitalvue/entitlement-verifier",
  sourceRoot: "packages/entitlement-verifier",
  googlePlayRoute: "/google-play/verify",
  googlePlayApiMethod: "purchases.productsv2.getproductpurchasev2",
  samsungRoute: "/samsung/status",
  samsungDpiOrigin: "https://checkoutapi.samsungcheckout.com"
};
for (const [key, expected] of Object.entries(expectedImplementation)) {
  if (manifest.implementation[key] !== expected) fail(`implementation.${key} is not the reviewed value`);
}
const hosting = manifest.implementation.hosting;
exactKeys(hosting, [
  "packageName",
  "sourceRoot",
  "runtime",
  "configurationPath",
  "healthRoute",
  "workersDevEnabled",
  "previewUrlsEnabled",
  "requiredSecrets",
  "rateLimiterBindings"
], "implementation.hosting");
const expectedHosting = {
  packageName: "@orbitalvue/entitlement-verifier-worker",
  sourceRoot: "packages/entitlement-verifier-worker",
  runtime: "cloudflare-workers",
  configurationPath: "packages/entitlement-verifier-worker/wrangler.jsonc",
  healthRoute: "/healthz",
  workersDevEnabled: false,
  previewUrlsEnabled: false
};
for (const [key, expected] of Object.entries(expectedHosting)) {
  if (hosting[key] !== expected) fail(`implementation.hosting.${key} is not the reviewed value`);
}
const requiredSecrets = [
  "GOOGLE_SERVICE_ACCOUNT_EMAIL",
  "GOOGLE_SERVICE_ACCOUNT_PRIVATE_KEY",
  "SAMSUNG_DPI_SECURITY_KEY",
  "RATE_LIMIT_KEY_SECRET"
];
exactStringArray(hosting.requiredSecrets, requiredSecrets, "implementation.hosting.requiredSecrets");
const rateLimiterBindings = ["VERIFICATION_RATE_LIMITER", "PROVIDER_RATE_LIMITER"];
exactStringArray(hosting.rateLimiterBindings, rateLimiterBindings, "implementation.hosting.rateLimiterBindings");
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
  "packages/entitlement-verifier-worker/package.json",
  "packages/entitlement-verifier-worker/wrangler.jsonc",
  "packages/entitlement-verifier-worker/worker-configuration.d.ts",
  "packages/entitlement-verifier-worker/src/index.ts",
  "packages/entitlement-verifier-worker/test/worker.test.ts",
  "packages/entitlement-verifier-worker/vitest.config.ts",
  "contracts/google-play-verifier-v1.schema.json",
  "contracts/samsung-checkout-verifier-v1.schema.json"
];
await Promise.all(requiredFiles.map((path) => access(join(repositoryRoot, path))));
const [
  googleSource,
  samsungSource,
  handlerSource,
  packageSource,
  workerPackageSource,
  workerConfigSource,
  workerSource,
  workerTestSource,
  workerTypesSource
] = await Promise.all([
  readText("packages/entitlement-verifier/src/google-play.ts"),
  readText("packages/entitlement-verifier/src/samsung-dpi.ts"),
  readText("packages/entitlement-verifier/src/handler.ts"),
  readText("packages/entitlement-verifier/package.json"),
  readText("packages/entitlement-verifier-worker/package.json"),
  readText("packages/entitlement-verifier-worker/wrangler.jsonc"),
  readText("packages/entitlement-verifier-worker/src/index.ts"),
  readText("packages/entitlement-verifier-worker/test/worker.test.ts"),
  readText("packages/entitlement-verifier-worker/worker-configuration.d.ts")
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

const workerPackage = JSON.parse(workerPackageSource);
const workerConfig = JSON.parse(workerConfigSource);
if (workerPackage.name !== hosting.packageName
  || workerPackage.dependencies?.[manifest.implementation.packageName] !== "workspace:*") {
  fail("Worker package must depend on the reviewed hosting-neutral verifier");
}
for (const script of ["types:check", "typecheck", "test", "dry-run", "check"]) {
  if (typeof workerPackage.scripts?.[script] !== "string") fail(`Worker package is missing the ${script} script`);
}
if (!workerPackage.scripts["types:check"].includes("wrangler types")
  || !workerPackage.scripts["types:check"].includes("--check")
  || !workerPackage.scripts["dry-run"].includes("--dry-run")) {
  fail("Worker scripts must prove generated bindings and bundle without deploying");
}
if (workerConfig.$schema !== "./node_modules/wrangler/config-schema.json"
  || workerConfig.name !== "orbitalvue-entitlement-verifier"
  || workerConfig.main !== "src/index.ts"
  || workerConfig.workers_dev !== hosting.workersDevEnabled
  || workerConfig.preview_urls !== hosting.previewUrlsEnabled
  || !Array.isArray(workerConfig.compatibility_flags)
  || !workerConfig.compatibility_flags.includes("nodejs_compat")) {
  fail("Worker root configuration is not the reviewed fail-closed shape");
}
const compatibilityTime = Date.parse(`${workerConfig.compatibility_date}T00:00:00Z`);
const now = Date.now();
if (!Number.isFinite(compatibilityTime)
  || compatibilityTime > now + 24 * 60 * 60 * 1000
  || compatibilityTime < now - 183 * 24 * 60 * 60 * 1000) {
  fail("Worker compatibility_date must be valid and no more than six months old");
}
if (workerConfig.observability?.enabled !== true
  || workerConfig.observability?.logs?.enabled !== true
  || workerConfig.observability?.traces?.enabled !== true) {
  fail("Worker observability must keep logs and traces enabled");
}
exactKeys(workerConfig.env, ["staging", "production"], "Worker environment map");

const deploymentScopes = [
  ["root", workerConfig],
  ["staging", workerConfig.env.staging],
  ["production", workerConfig.env.production]
];
const rateLimitNamespaces = new Set();
const rateLimitPolicies = {
  VERIFICATION_RATE_LIMITER: { limit: 30, period: 60 },
  PROVIDER_RATE_LIMITER: { limit: 600, period: 60 }
};
for (const [label, scope] of deploymentScopes) {
  if (!scope || scope.workers_dev !== false || scope.preview_urls !== false) {
    fail(`Worker ${label} must disable workers.dev and preview URLs`);
  }
  exactStringArray(scope.secrets?.required, requiredSecrets, `Worker ${label} required secrets`);
  if (!Array.isArray(scope.ratelimits) || scope.ratelimits.length !== rateLimiterBindings.length) {
    fail(`Worker ${label} must define both rate limiters`);
  }
  const seenBindings = new Set();
  for (const limiter of scope.ratelimits) {
    exactKeys(limiter, ["name", "namespace_id", "simple"], `Worker ${label} rate limiter`);
    exactKeys(limiter.simple, ["limit", "period"], `Worker ${label} rate limit policy`);
    const policy = rateLimitPolicies[limiter.name];
    if (!policy
      || seenBindings.has(limiter.name)
      || !/^[1-9]\d*$/.test(limiter.namespace_id)
      || limiter.simple.limit !== policy.limit
      || limiter.simple.period !== policy.period
      || rateLimitNamespaces.has(limiter.namespace_id)) {
      fail(`Worker ${label} rate limiter is invalid or shares a binding/namespace`);
    }
    seenBindings.add(limiter.name);
    rateLimitNamespaces.add(limiter.namespace_id);
  }
  exactStringArray([...seenBindings], rateLimiterBindings, `Worker ${label} rate limiter bindings`);
  const vars = scope.vars;
  exactKeys(vars, [
    "DEPLOYMENT_ENVIRONMENT",
    "EXPECTED_HOSTNAME",
    "ALLOWED_BROWSER_ORIGINS",
    "GOOGLE_PLAY_PACKAGE_NAME",
    "GOOGLE_PLAY_PRODUCT_ID",
    "GOOGLE_PLAY_ALLOW_TEST_PURCHASES",
    "SAMSUNG_CHECKOUT_APP_ID",
    "SAMSUNG_PREMIUM_PRODUCT_ID"
  ], `Worker ${label} vars`);
  if (vars.DEPLOYMENT_ENVIRONMENT !== (label === "root" ? "local" : label)) {
    fail(`Worker ${label} deployment label is invalid`);
  }
  let expectedHost;
  try { expectedHost = new URL(`https://${vars.EXPECTED_HOSTNAME}`); } catch { fail(`Worker ${label} hostname is invalid`); }
  if (!expectedHost.hostname
    || expectedHost.hostname !== vars.EXPECTED_HOSTNAME
    || expectedHost.pathname !== "/"
    || expectedHost.port
    || expectedHost.search
    || expectedHost.hash) {
    fail(`Worker ${label} hostname must be exact`);
  }
  let origins;
  try { origins = JSON.parse(vars.ALLOWED_BROWSER_ORIGINS); } catch { fail(`Worker ${label} origin allowlist is invalid JSON`); }
  if (!Array.isArray(origins) || origins.length > 20 || new Set(origins).size !== origins.length) {
    fail(`Worker ${label} origin allowlist is invalid`);
  }
  for (const origin of origins) {
    let parsed;
    try { parsed = new URL(origin); } catch { fail(`Worker ${label} contains an invalid browser origin`); }
    if (origin === "null"
      || origin === "*"
      || !["https:", "http:"].includes(parsed.protocol)
      || (label !== "root" && parsed.protocol !== "https:")
      || parsed.origin !== origin
      || parsed.pathname !== "/") {
      fail(`Worker ${label} browser origins must be exact HTTP(S) origins`);
    }
  }
  if (!["true", "false"].includes(vars.GOOGLE_PLAY_ALLOW_TEST_PURCHASES)) {
    fail(`Worker ${label} Google Play test-purchase setting is invalid`);
  }
  if (label === "production" && vars.GOOGLE_PLAY_ALLOW_TEST_PURCHASES !== "false") {
    fail("Worker production must reject Google Play test purchases");
  }
}

const productionHostnames = Object.values(manifest.platforms)
  .flatMap((entry) => entry.productionUrl === null ? [] : [new URL(entry.productionUrl).hostname]);
if (productionHostnames.some((hostname) => hostname !== workerConfig.env.production.vars.EXPECTED_HOSTNAME)) {
  fail("Production verifier URLs must match the Worker production hostname");
}
if (manifest.platforms.android.ready
  && workerConfig.env.production.vars.GOOGLE_PLAY_PRODUCT_ID !== premium.platforms.android.productId) {
  fail("Ready Android Worker product does not match the premium product manifest");
}
if (manifest.platforms.samsung.ready
  && workerConfig.env.production.vars.SAMSUNG_PREMIUM_PRODUCT_ID !== premium.platforms.samsung.productId) {
  fail("Ready Samsung Worker product does not match the premium product manifest");
}
for (const fragment of [
  "satisfies ExportedHandler<Env>",
  "VERIFICATION_RATE_LIMITER.limit",
  "PROVIDER_RATE_LIMITER.limit",
  'crypto.subtle.sign("HMAC"',
  'request.headers.get("Origin")',
  '"Access-Control-Allow-Origin"',
  '"X-Content-Type-Options"',
  "readBoundedJson(request)",
  'console.error(JSON.stringify({'
]) {
  if (!workerSource.includes(fragment)) fail(`Worker implementation is missing ${fragment}`);
}
if (workerSource.includes("request.json()") || workerSource.includes("request.text()")) {
  fail("Worker must stream and bound the verification request body");
}
if (!workerTestSource.includes("SELF.fetch") || !workerTestSource.includes("not.toContain(\"account-user-123\")")) {
  fail("Worker tests must exercise the deployed entry point and opaque rate-limit keys");
}
if (!workerTypesSource.includes("interface Env extends __BaseEnv_Env")
  || rateLimiterBindings.some((binding) => !workerTypesSource.includes(`${binding}: RateLimit`))) {
  fail("Worker generated binding types are incomplete");
}

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
