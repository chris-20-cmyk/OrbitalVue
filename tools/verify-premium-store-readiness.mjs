import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const manifest = JSON.parse(
  await readFile(join(repositoryRoot, "store", "premium-products.json"), "utf8")
);
const expectedPlatforms = ["windows", "android", "apple", "samsung", "lg"];
const productPattern = /^[A-Za-z0-9._-]{3,256}$/;
const providerPattern = /^[A-Za-z0-9._-]{3,128}$/;

const fail = (message) => {
  throw new Error(`Premium store readiness failed: ${message}`);
};

if (manifest.contractVersion !== "1.0") fail("unexpected contractVersion");
if (manifest.featureId !== "personal-media-centers") fail("unexpected featureId");
if (manifest.purchaseModel !== "one-time-non-consumable") {
  fail("the media-center product must remain a one-time non-consumable");
}
if (!manifest.platforms || typeof manifest.platforms !== "object") fail("platform map is missing");

for (const name of expectedPlatforms) {
  const entry = manifest.platforms[name];
  if (!entry || typeof entry !== "object") fail(`${name} readiness is missing`);
  const allowedKeys = new Set(["productId", "verificationProvider", "ready"]);
  for (const key of Object.keys(entry)) {
    if (!allowedKeys.has(key)) fail(`${name} contains unexpected field ${key}`);
  }
  if (typeof entry.ready !== "boolean") fail(`${name}.ready must be boolean`);
  if (entry.productId !== null && !productPattern.test(entry.productId)) {
    fail(`${name}.productId is invalid`);
  }
  if (entry.verificationProvider !== null && !providerPattern.test(entry.verificationProvider)) {
    fail(`${name}.verificationProvider is invalid`);
  }
  if (entry.ready && (!entry.productId || !entry.verificationProvider)) {
    fail(`${name} cannot be ready without a product and native verification provider`);
  }
}

for (const name of Object.keys(manifest.platforms)) {
  if (!expectedPlatforms.includes(name)) fail(`unknown platform ${name}`);
}

const serialized = JSON.stringify(manifest).toLowerCase();
for (const forbidden of ["receipt", "purchase-token", "account-id", "password", "access-token"]) {
  if (serialized.includes(forbidden)) fail(`manifest contains forbidden secret field ${forbidden}`);
}

const requireReadyIndex = process.argv.indexOf("--require-ready");
if (requireReadyIndex >= 0) {
  const requested = process.argv[requireReadyIndex + 1];
  if (!expectedPlatforms.includes(requested)) {
    fail(`--require-ready expects one of: ${expectedPlatforms.join(", ")}`);
  }
  const entry = manifest.platforms[requested];
  if (!entry.ready) {
    fail(`${requested} is intentionally locked; configure and verify its native one-time product before store publishing`);
  }
  console.log(`${requested} premium store product is configured and marked ready.`);
} else {
  const ready = expectedPlatforms.filter((name) => manifest.platforms[name].ready);
  const locked = expectedPlatforms.filter((name) => !manifest.platforms[name].ready);
  console.log(
    `Premium readiness manifest is valid: ${ready.length} ready; locked by design: ${locked.join(", ")}.`
  );
}
