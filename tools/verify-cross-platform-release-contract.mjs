import { readFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const readText = (path) => readFile(join(repositoryRoot, path), "utf8");
const readJSON = async (path) => JSON.parse(await readText(path));
const contract = await readJSON("store/cross-platform-release.json");
const premium = await readJSON("store/premium-products.json");
const apple = await readJSON("store/apple-distribution.json");
const samsung = await readJSON("store/samsung-distribution.json");
const lg = await readJSON("store/lg-distribution.json");

const fail = (message) => {
  throw new Error(`Cross-platform release contract failed: ${message}`);
};
const exactKeys = (value, expected, label) => {
  if (!value || typeof value !== "object" || Array.isArray(value)) fail(`${label} is missing`);
  const actual = Object.keys(value).sort();
  const wanted = [...expected].sort();
  if (JSON.stringify(actual) !== JSON.stringify(wanted)) {
    fail(`${label} must contain exactly: ${wanted.join(", ")}`);
  }
};
const escapePattern = (value) => value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const topLevelChildKeys = (source, section) => {
  const lines = source.split(/\r?\n/);
  const start = lines.findIndex((line) => line === `${section}:`);
  if (start < 0) return [];
  const keys = [];
  for (let index = start + 1; index < lines.length; index += 1) {
    const line = lines[index];
    if (line.length > 0 && !line.startsWith(" ")) break;
    const match = /^ {2}([A-Za-z0-9_-]+):/.exec(line);
    if (match) keys.push(match[1]);
  }
  return keys;
};

const expectedPlatforms = {
  windows: {
    applicationId: null,
    distributionTarget: "microsoft-partner-center",
    artifactKind: "unsigned-msix",
    candidateWorkflow: ".github/workflows/build-windows-store-candidate.yml",
    premiumReleasePolicy: "requires-verified-product",
    verificationProvider: "microsoft-store-license",
    requiredFragments: [
      "node tools/verify-privacy-readiness.mjs --require-ready windows",
      "node tools/verify-public-site-readiness.mjs --require-ready",
      "store/public-site-readiness.json",
      "node tools/verify-store-listing-readiness.mjs --require-ready windows",
      "store/store-listing.json",
      "node tools/verify-accessibility-readiness.mjs --require-ready windows",
      "store/accessibility-readiness.json",
      "--require-ready windows",
      "--expect-verification-provider windows microsoft-store-license",
      "-p:StreamVueDistributionMode=Store",
      "tools/build-windows-msix.ps1"
    ]
  },
  android: {
    applicationId: "com.streamvue.player",
    distributionTarget: "google-play",
    artifactKind: "upload-key-signed-aab",
    candidateWorkflow: ".github/workflows/build-google-play-candidate.yml",
    premiumReleasePolicy: "requires-verified-product",
    verificationProvider: "google-play-developer-api",
    requiredFragments: [
      "node tools/verify-privacy-readiness.mjs --require-ready android",
      "node tools/verify-public-site-readiness.mjs --require-ready",
      "store/public-site-readiness.json",
      "node tools/verify-store-listing-readiness.mjs --require-ready android",
      "store/store-listing.json",
      "node tools/verify-accessibility-readiness.mjs --require-ready android",
      "store/accessibility-readiness.json",
      "--require-ready android",
      "--expect-verification-provider android google-play-developer-api",
      "-PstreamVueDistributionMode=store",
      "-PstreamVueRequireStoreSigning=true"
    ]
  },
  apple: {
    applicationId: "com.streamvue.player",
    distributionTarget: "apple-app-store",
    artifactKind: "distribution-signed-ipa-set",
    candidateWorkflow: ".github/workflows/build-apple-store-candidate.yml",
    premiumReleasePolicy: "requires-verified-product-and-license",
    verificationProvider: "storekit2-verified-transactions",
    requiredFragments: [
      "node tools/verify-privacy-readiness.mjs --require-ready apple",
      "node tools/verify-public-site-readiness.mjs --require-ready",
      "store/public-site-readiness.json",
      "node tools/verify-store-listing-readiness.mjs --require-ready apple",
      "store/store-listing.json",
      "node tools/verify-accessibility-readiness.mjs --require-ready apple",
      "store/accessibility-readiness.json",
      "--require-ready apple",
      "--expect-verification-provider apple storekit2-verified-transactions",
      "tools/verify-apple-distribution-readiness.mjs",
      "Package.store.swift",
      "storePackageSource",
      "-configuration Store"
    ]
  },
  samsung: {
    applicationId: "SvTvPlayer.StreamVue",
    distributionTarget: "samsung-seller-office",
    artifactKind: "author-partner-signed-wgt",
    candidateWorkflow: ".github/workflows/build-samsung-store-candidate.yml",
    premiumReleasePolicy: "requires-verified-product",
    verificationProvider: "samsung-dpi-purchase-history",
    requiredFragments: [
      "node tools/verify-privacy-readiness.mjs --require-ready samsung",
      "node tools/verify-public-site-readiness.mjs --require-ready",
      "store/public-site-readiness.json",
      "node tools/verify-store-listing-readiness.mjs --require-ready samsung",
      "store/store-listing.json",
      "node tools/verify-accessibility-readiness.mjs --require-ready samsung",
      "store/accessibility-readiness.json",
      "--require-ready samsung",
      "--expect-verification-provider samsung samsung-dpi-purchase-history",
      "tools/verify-samsung-distribution-readiness.mjs",
      "VITE_STREAMVUE_DISTRIBUTION_MODE: store"
    ]
  },
  lg: {
    applicationId: "com.streamvue.player.tv",
    distributionTarget: "lg-seller-lounge",
    artifactKind: "webos-ipk",
    candidateWorkflow: ".github/workflows/build-lg-seller-lounge-candidate.yml",
    premiumReleasePolicy: "locked-free-app",
    verificationProvider: null,
    requiredFragments: [
      "node tools/verify-privacy-readiness.mjs --require-ready lg",
      "node tools/verify-public-site-readiness.mjs --require-ready",
      "store/public-site-readiness.json",
      "node tools/verify-store-listing-readiness.mjs --require-ready lg",
      "store/store-listing.json",
      "node tools/verify-accessibility-readiness.mjs --require-ready lg",
      "store/accessibility-readiness.json",
      "tools/verify-lg-distribution-readiness.mjs",
      "--expect-app-id \"$LG_APP_ID\"",
      "VITE_STREAMVUE_DISTRIBUTION_MODE: store",
      "manual-lg-seller-lounge-upload"
    ]
  }
};
const platformNames = Object.keys(expectedPlatforms);

exactKeys(contract, [
  "contractVersion",
  "featureId",
  "purchaseModel",
  "submissionPolicy",
  "platforms"
], "contract");
if (contract.contractVersion !== "1.0") fail("unexpected contractVersion");
if (contract.featureId !== premium.featureId || contract.featureId !== "personal-media-centers") {
  fail("featureId does not match the premium contract");
}
if (contract.purchaseModel !== premium.purchaseModel
  || contract.purchaseModel !== "one-time-non-consumable") {
  fail("purchaseModel must remain the shared one-time non-consumable");
}
if (contract.submissionPolicy !== "manual-only") fail("candidate submission must remain manual-only");
exactKeys(contract.platforms, platformNames, "platform map");
exactKeys(premium.platforms, platformNames, "premium platform map");

const workflowKeys = [
  "applicationId",
  "distributionTarget",
  "artifactKind",
  "candidateWorkflow",
  "premiumReleasePolicy",
  "verificationProvider"
];
const forbiddenWorkflowCommands = [
  /\bgh\s+release\s+(?:create|upload)\b/i,
  /\bfastlane\s+(?:deliver|pilot|supply)\b/i,
  /\bxcrun\s+(?:altool|notarytool)\b/i,
  /\bares-install\b/i,
  /\btizen\s+install\b/i,
  /\bgradlew?\b[^\r\n]*(?:publish|upload)\b/i
];

for (const [name, expected] of Object.entries(expectedPlatforms)) {
  const entry = contract.platforms[name];
  exactKeys(entry, workflowKeys, `${name} release entry`);
  for (const key of workflowKeys) {
    if (entry[key] !== expected[key]) fail(`${name}.${key} does not match the reviewed release lane`);
  }

  const premiumEntry = premium.platforms[name];
  exactKeys(premiumEntry, ["productId", "verificationProvider", "ready"], `${name} premium entry`);
  if (typeof premiumEntry.ready !== "boolean") fail(`${name} premium ready flag must be boolean`);
  if (premiumEntry.ready && premiumEntry.verificationProvider !== expected.verificationProvider) {
    fail(`${name} premium readiness uses an unexpected verification provider`);
  }
  if (premiumEntry.verificationProvider !== null
    && premiumEntry.verificationProvider !== expected.verificationProvider) {
    fail(`${name} declares a verification provider outside the reviewed release lane`);
  }
  if (name === "lg" && (
    premiumEntry.productId !== null
    || premiumEntry.verificationProvider !== null
    || premiumEntry.ready !== false
  )) {
    fail("LG must remain premium-locked in the free Seller Lounge lane");
  }

  const workflowPath = resolve(repositoryRoot, entry.candidateWorkflow);
  if (!workflowPath.startsWith(resolve(repositoryRoot) + "\\")
    && !workflowPath.startsWith(resolve(repositoryRoot) + "/")) {
    fail(`${name} candidate workflow resolves outside the repository`);
  }
  const workflow = await readFile(workflowPath, "utf8");
  const triggers = topLevelChildKeys(workflow, "on");
  if (JSON.stringify(triggers) !== JSON.stringify(["workflow_dispatch"])) {
    fail(`${name} candidate must have workflow_dispatch as its only trigger`);
  }
  const permissions = topLevelChildKeys(workflow, "permissions");
  if (JSON.stringify(permissions) !== JSON.stringify(["contents"])
    || !/^ {2}contents:\s*read\s*$/m.test(workflow)) {
    fail(`${name} candidate must use read-only repository contents permission`);
  }
  if (!workflow.includes("actions/upload-artifact@v7")) {
    fail(`${name} candidate must stop at a temporary workflow artifact`);
  }
  for (const forbidden of forbiddenWorkflowCommands) {
    if (forbidden.test(workflow)) fail(`${name} candidate contains an automatic install or publishing command`);
  }
  for (const fragment of expected.requiredFragments) {
    if (!workflow.includes(fragment)) fail(`${name} candidate is missing release control: ${fragment}`);
  }
}

if (apple.bundleId !== expectedPlatforms.apple.applicationId) {
  fail("Apple distribution bundle ID does not match the cross-platform release contract");
}
if (samsung.applicationId !== expectedPlatforms.samsung.applicationId) {
  fail("Samsung application ID does not match the cross-platform release contract");
}
if (lg.appId !== expectedPlatforms.lg.applicationId
  || lg.commerceMode !== "free-premium-locked"
  || lg.premiumLocked !== true) {
  fail("LG identity or locked-commerce policy does not match the cross-platform release contract");
}

const androidBuild = await readText("platforms/android/app/build.gradle.kts");
for (const field of ["namespace", "applicationId"]) {
  const identityPattern = new RegExp(`\\b${field}\\s*=\\s*\"${escapePattern(expectedPlatforms.android.applicationId)}\"`);
  if (!identityPattern.test(androidBuild)) fail(`Android ${field} does not match the release contract`);
}
const appleProject = await readText("platforms/apple/project.yml");
const appleIdentities = appleProject.match(/PRODUCT_BUNDLE_IDENTIFIER:\s*com\.streamvue\.player/g) ?? [];
if (appleIdentities.length !== 2) fail("iOS and tvOS must keep the same reviewed Apple bundle ID");
const windowsWorkflow = await readText(expectedPlatforms.windows.candidateWorkflow);
if (!windowsWorkflow.includes("vars.STREAMVUE_WINDOWS_IDENTITY_NAME")
  || !windowsWorkflow.includes("-IdentityName $env:WINDOWS_IDENTITY_NAME")) {
  fail("Windows candidate must receive its exact reserved identity from Partner Center");
}
const lgWorkflow = await readText(expectedPlatforms.lg.candidateWorkflow);
if (/verify-premium-store-readiness\.mjs[\s\\]*--require-ready lg/.test(lgWorkflow)
  || lgWorkflow.includes("VITE_STREAMVUE_LG_PRODUCT_ID")) {
  fail("LG free candidate must not claim an unimplemented premium product");
}

const releaseContractWorkflow = await readText(".github/workflows/release-contract.yml");
for (const fragment of [
  "node tools/generate-release-readiness-report.mjs",
  "artifacts/release-readiness/",
  "streamvue-release-readiness"
]) {
  if (!releaseContractWorkflow.includes(fragment)) {
    fail(`release contract workflow is missing readiness report fragment: ${fragment}`);
  }
}

const publicSiteWorkflow = await readText(".github/workflows/build-public-site-candidate.yml");
if (JSON.stringify(topLevelChildKeys(publicSiteWorkflow, "on")) !== JSON.stringify(["workflow_dispatch"])) {
  fail("public site candidate must have workflow_dispatch as its only trigger");
}
if (JSON.stringify(topLevelChildKeys(publicSiteWorkflow, "permissions")) !== JSON.stringify(["contents"])
  || !/^ {2}contents:\s*read\s*$/m.test(publicSiteWorkflow)) {
  fail("public site candidate must use read-only repository contents permission");
}
for (const fragment of [
  "node tools/verify-public-site-readiness.mjs --require-ready",
  "pnpm site:build",
  "actions/upload-artifact@v7",
  "artifacts/public-site/",
  "store/public-site-readiness.json"
]) {
  if (!publicSiteWorkflow.includes(fragment)) fail(`public site candidate is missing release control: ${fragment}`);
}
for (const forbidden of ["actions/deploy-pages", "pages: write", "id-token: write", "gh-pages"]) {
  if (publicSiteWorkflow.includes(forbidden)) fail(`public site candidate must stop before deployment: ${forbidden}`);
}

console.log(
  "Cross-platform release contract is valid: five manual candidate lanes, one one-time premium model, and LG safely free/locked."
);
