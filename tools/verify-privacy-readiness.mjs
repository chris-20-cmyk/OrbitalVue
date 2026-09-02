import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const readText = (path) => readFile(join(repositoryRoot, path), "utf8");
const readJSON = async (path) => JSON.parse(await readText(path));
const inventory = await readJSON("store/privacy-data-inventory.json");
const release = await readJSON("store/cross-platform-release.json");
const appleDistribution = await readJSON("store/apple-distribution.json");
const lgDistribution = await readJSON("store/lg-distribution.json");

const fail = (message) => {
  throw new Error(`Privacy readiness failed: ${message}`);
};
const exactKeys = (value, expected, label) => {
  if (!value || typeof value !== "object" || Array.isArray(value)) fail(`${label} is missing`);
  const actual = Object.keys(value).sort();
  const wanted = [...expected].sort();
  if (JSON.stringify(actual) !== JSON.stringify(wanted)) {
    fail(`${label} must contain exactly: ${wanted.join(", ")}`);
  }
};
const requireStringArray = (value, label) => {
  if (!Array.isArray(value) || value.some((item) => typeof item !== "string" || !item)) {
    fail(`${label} must be an array of non-empty strings`);
  }
};
const validPublicUrl = (value) => {
  if (typeof value !== "string") return false;
  try {
    const parsed = new URL(value);
    return parsed.protocol === "https:" && !parsed.username && !parsed.password && !parsed.hash;
  } catch {
    return false;
  }
};

const args = process.argv.slice(2);
let requiredPlatform = null;
for (let index = 0; index < args.length; index += 1) {
  if (args[index] !== "--require-ready" || requiredPlatform !== null || !args[index + 1]) {
    fail("usage: node tools/verify-privacy-readiness.mjs [--require-ready windows|android|apple|samsung|lg]");
  }
  requiredPlatform = args[index + 1];
  index += 1;
}

const platformDeclarations = {
  windows: [
    "microsoftStorePrivacyDisclosureReviewed",
    "localCrashAndDiagnosticsDisclosureReviewed",
    "thirdPartyDataPracticesReviewed",
    "retentionAndDeletionReviewed"
  ],
  android: [
    "googlePlayDataSafetyFormReviewed",
    "sdkDataPracticesReviewed",
    "purchaseVerifierRetentionDocumented",
    "retentionAndDeletionReviewed"
  ],
  apple: [
    "appStorePrivacyAnswersReviewed",
    "tvOSPrivacyPolicyTextReviewed",
    "requiredReasonApiReviewCompleted",
    "ksPlayerDataPracticesReviewed",
    "retentionAndDeletionReviewed"
  ],
  samsung: [
    "sellerOfficePrivacyDisclosureReviewed",
    "samsungCheckoutDataPracticesReviewed",
    "purchaseVerifierRetentionDocumented",
    "retentionAndDeletionReviewed"
  ],
  lg: [
    "sellerLoungePrivacyDisclosureReviewed",
    "webOsSecureStorageReviewed",
    "premiumLockDisclosureReviewed",
    "retentionAndDeletionReviewed"
  ]
};
const platformNames = Object.keys(platformDeclarations);

exactKeys(inventory, [
  "contractVersion",
  "status",
  "technicalReviewDate",
  "privacyContact",
  "policyEffectiveDate",
  "policyOwnerApproved",
  "globalPractices",
  "dataFlows",
  "platforms"
], "privacy inventory");
if (inventory.contractVersion !== "1.0") fail("unexpected contractVersion");
if (inventory.status !== "draft-unpublished" && inventory.status !== "published-reviewed") {
  fail("status must describe whether the policy is still a draft or published and reviewed");
}
if (!/^\d{4}-\d{2}-\d{2}$/.test(inventory.technicalReviewDate)) {
  fail("technicalReviewDate must use YYYY-MM-DD");
}
if (inventory.privacyContact !== null && (
  typeof inventory.privacyContact !== "string"
  || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(inventory.privacyContact)
)) fail("privacyContact must be null or an email address");
if (inventory.policyEffectiveDate !== null
  && !/^\d{4}-\d{2}-\d{2}$/.test(inventory.policyEffectiveDate)) {
  fail("policyEffectiveDate must be null or use YYYY-MM-DD");
}
if (typeof inventory.policyOwnerApproved !== "boolean") fail("policyOwnerApproved must be boolean");

exactKeys(inventory.globalPractices, [
  "automaticTelemetryEnabled",
  "advertisingEnabled",
  "crossAppTrackingEnabled",
  "dataSaleEnabled",
  "developerAccountSystemEnabled"
], "global practices");
for (const [name, value] of Object.entries(inventory.globalPractices)) {
  if (value !== false) fail(`${name} must remain false until the inventory, policy, and Store forms are updated`);
}

const expectedFlows = [
  "provider-connections",
  "catalog-and-preferences",
  "provider-client-identity",
  "premium-purchase-verification",
  "diagnostics"
];
if (!Array.isArray(inventory.dataFlows) || inventory.dataFlows.length !== expectedFlows.length) {
  fail(`dataFlows must contain exactly ${expectedFlows.length} reviewed flows`);
}
for (const [index, flow] of inventory.dataFlows.entries()) {
  exactKeys(flow, [
    "id",
    "data",
    "purpose",
    "storage",
    "transmittedTo",
    "developerAccess",
    "availability"
  ], `data flow ${index + 1}`);
  if (flow.id !== expectedFlows[index]) fail(`data flow ${index + 1} must remain ${expectedFlows[index]}`);
  requireStringArray(flow.data, `${flow.id}.data`);
  requireStringArray(flow.transmittedTo, `${flow.id}.transmittedTo`);
  for (const field of ["purpose", "storage", "developerAccess", "availability"]) {
    if (typeof flow[field] !== "string" || !flow[field]) fail(`${flow.id}.${field} is required`);
  }
}

exactKeys(inventory.platforms, platformNames, "privacy platform map");
for (const name of platformNames) {
  const entry = inventory.platforms[name];
  exactKeys(entry, ["applicationId", "privacyPolicyUrl", "supportUrl", "declarations", "ready"], `${name} privacy entry`);
  if (entry.applicationId !== release.platforms[name].applicationId) {
    fail(`${name} application ID does not match the release contract`);
  }
  exactKeys(entry.declarations, platformDeclarations[name], `${name} privacy declarations`);
  for (const [declaration, value] of Object.entries(entry.declarations)) {
    if (typeof value !== "boolean") fail(`${name}.${declaration} must be boolean`);
  }
  if (entry.privacyPolicyUrl !== null && !validPublicUrl(entry.privacyPolicyUrl)) {
    fail(`${name}.privacyPolicyUrl must be null or a public HTTPS URL`);
  }
  if (entry.supportUrl !== null && !validPublicUrl(entry.supportUrl)) {
    fail(`${name}.supportUrl must be null or a public HTTPS URL`);
  }
  if (typeof entry.ready !== "boolean") fail(`${name}.ready must be boolean`);
  const calculatedReady = inventory.status === "published-reviewed"
    && inventory.policyOwnerApproved
    && inventory.privacyContact !== null
    && inventory.policyEffectiveDate !== null
    && validPublicUrl(entry.privacyPolicyUrl)
    && validPublicUrl(entry.supportUrl)
    && Object.values(entry.declarations).every((value) => value === true);
  if (entry.ready !== calculatedReady) {
    fail(`${name}.ready must exactly match the published policy and completed declaration gates`);
  }
}

if (inventory.platforms.lg.declarations.sellerLoungePrivacyDisclosureReviewed
  !== lgDistribution.privacyDisclosureReviewed) {
  fail("LG privacy review must agree with the Seller Lounge distribution manifest");
}
if (appleDistribution.ksPlayer.storePackageSource !== "absent"
  && inventory.platforms.apple.ready
  && !inventory.platforms.apple.declarations.ksPlayerDataPracticesReviewed) {
  fail("Apple cannot be privacy-ready until the selected KSPlayer distribution is reviewed");
}

const privacyManifest = await readText("platforms/apple/Resources/PrivacyInfo.xcprivacy");
for (const fragment of [
  "<key>NSPrivacyTracking</key>",
  "<false/>",
  "<key>NSPrivacyCollectedDataTypes</key>",
  "<key>NSPrivacyAccessedAPIType</key>",
  "<string>NSPrivacyAccessedAPICategoryUserDefaults</string>",
  "<key>NSPrivacyAccessedAPITypeReasons</key>",
  "<string>CA92.1</string>"
]) {
  if (!privacyManifest.includes(fragment)) fail(`Apple privacy manifest is missing ${fragment}`);
}
if (!/<key>NSPrivacyTracking<\/key>\s*<false\/>/.test(privacyManifest)
  || !/<key>NSPrivacyTrackingDomains<\/key>\s*<array\s*\/>/.test(privacyManifest)
  || !/<key>NSPrivacyCollectedDataTypes<\/key>\s*<array\s*\/>/.test(privacyManifest)) {
  fail("Apple privacy manifest must keep tracking off, tracking domains empty, and the app-level collected-data list empty until reviewed data practices change");
}
if ((privacyManifest.match(/NSPrivacyAccessedAPICategoryUserDefaults/g) ?? []).length !== 1
  || (privacyManifest.match(/<string>CA92\.1<\/string>/g) ?? []).length !== 1
  || (privacyManifest.match(/NSPrivacyAccessedAPICategory(?!UserDefaults)/g) ?? []).length !== 0) {
  fail("Apple required-reason API declaration must contain exactly the reviewed app-only UserDefaults reason");
}
const appleProject = await readText("platforms/apple/project.yml");
if ((appleProject.match(/- path: Resources\s*\r?\n\s*buildPhase: resources/g) ?? []).length !== 2) {
  fail("iOS and tvOS must both package the shared PrivacyInfo.xcprivacy resource");
}

const dependencySources = await Promise.all([
  readText("package.json"),
  readText("platforms/tv-web/package.json"),
  readText("platforms/android/app/build.gradle.kts"),
  readText("platforms/apple/Package.swift"),
  readText("src/OrbitalVue.Player/OrbitalVue.Player.csproj")
]);
const forbiddenAutomaticDataSdks = [
  "firebase-analytics",
  "google-mobile-ads",
  "appcenter-analytics",
  "sentry-android",
  "sentry-cocoa",
  "sentry-dotnet",
  "mixpanel",
  "segment-analytics",
  "appsflyer",
  "adjust-android"
];
const dependencyText = dependencySources.join("\n").toLowerCase();
for (const sdk of forbiddenAutomaticDataSdks) {
  if (dependencyText.includes(sdk)) {
    fail(`automatic data SDK ${sdk} was added without updating the privacy contract`);
  }
}

if (requiredPlatform !== null) {
  if (!platformNames.includes(requiredPlatform)) fail(`unknown platform ${requiredPlatform}`);
  if (!inventory.platforms[requiredPlatform].ready) {
    fail(`${requiredPlatform} privacy readiness is intentionally locked; publish and approve the policy, support page, Store declarations, retention, and third-party review first`);
  }
}

const readyPlatforms = platformNames.filter((name) => inventory.platforms[name].ready);
console.log(
  `Privacy inventory is structurally valid; ${readyPlatforms.length} of ${platformNames.length} Store lanes are approved (${readyPlatforms.join(", ") || "none"}).`
);
