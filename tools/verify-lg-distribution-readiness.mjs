import { readFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const readJSON = async (path) => JSON.parse(await readFile(path, "utf8"));
const manifest = await readJSON(join(repositoryRoot, "store", "lg-distribution.json"));
const appInfo = await readJSON(join(
  repositoryRoot,
  "platforms",
  "tv-web",
  "platform",
  "webos",
  "appinfo.json"
));
const premium = await readJSON(join(repositoryRoot, "store", "premium-products.json"));
const packagingScript = await readFile(join(
  repositoryRoot,
  "platforms",
  "tv-web",
  "scripts",
  "package-platforms.mjs"
), "utf8");
const premiumService = await readFile(join(
  repositoryRoot,
  "platforms",
  "tv-web",
  "src",
  "premium",
  "TelevisionPremiumService.ts"
), "utf8");

const fail = (message) => {
  throw new Error(`LG distribution readiness failed: ${message}`);
};
const exactKeys = (value, expected, label) => {
  if (!value || typeof value !== "object" || Array.isArray(value)) fail(`${label} is missing`);
  const actual = Object.keys(value).sort();
  const wanted = [...expected].sort();
  if (JSON.stringify(actual) !== JSON.stringify(wanted)) {
    fail(`${label} must contain exactly: ${wanted.join(", ")}`);
  }
};
const validAppId = (value) => typeof value === "string"
  && /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)+$/.test(value)
  && !["com.palm", "com.webos", "com.lge", "com.palmdts"].some(
    (prefix) => value === prefix || value.startsWith(`${prefix}.`)
  );
const relativeAsset = (value) => typeof value === "string"
  && value.length > 0
  && !value.startsWith("/")
  && !value.startsWith("\\")
  && !/^[A-Za-z]:/.test(value)
  && !value.split(/[\\/]/).includes("..");
const validatePng = async (path, width, height, label) => {
  const bytes = await readFile(path);
  if (bytes.length < 33 || bytes.subarray(0, 8).toString("hex") !== "89504e470d0a1a0a") {
    fail(`${label} is not a valid PNG`);
  }
  if (bytes.subarray(12, 16).toString("ascii") !== "IHDR") {
    fail(`${label} has no leading PNG IHDR chunk`);
  }
  if (bytes.readUInt32BE(16) !== width || bytes.readUInt32BE(20) !== height) {
    fail(`${label} must be exactly ${width}x${height}`);
  }
  if (bytes[24] !== 8 || ![2, 6].includes(bytes[25]) || bytes[26] !== 0 || bytes[27] !== 0) {
    fail(`${label} must be an 8-bit RGB or RGBA PNG using standard compression and filtering`);
  }
};

exactKeys(manifest, [
  "contractVersion",
  "appId",
  "sellerIcon",
  "sellerIconReviewed",
  "listingAssetsPrepared",
  "sellerAccountType",
  "sellerTermsReviewed",
  "uxScenarioPrepared",
  "selfChecklistCompleted",
  "privacyDisclosureReviewed",
  "realTvMatrixCompleted",
  "commerceMode",
  "premiumLocked",
  "ready"
], "manifest");
if (manifest.contractVersion !== "1.0") fail("unexpected contractVersion");
if (!validAppId(manifest.appId)) fail("appId must be an unreserved lowercase reverse-DNS webOS ID");
if (!relativeAsset(manifest.sellerIcon)) fail("sellerIcon must be a repository-relative asset path");
if (manifest.sellerAccountType !== null
  && !["individual", "corporate"].includes(manifest.sellerAccountType)) {
  fail("sellerAccountType must be null, individual, or corporate");
}
for (const flag of [
  "sellerTermsReviewed",
  "sellerIconReviewed",
  "listingAssetsPrepared",
  "uxScenarioPrepared",
  "selfChecklistCompleted",
  "privacyDisclosureReviewed",
  "realTvMatrixCompleted",
  "premiumLocked",
  "ready"
]) {
  if (typeof manifest[flag] !== "boolean") fail(`${flag} must be boolean`);
}
if (manifest.commerceMode !== "free-premium-locked" || !manifest.premiumLocked) {
  fail("LG distribution must remain free with premium media centers locked until a reviewed third-party billing integration exists");
}

exactKeys(appInfo, [
  "id",
  "version",
  "vendor",
  "type",
  "main",
  "title",
  "appDescription",
  "icon",
  "largeIcon",
  "iconColor",
  "resolution",
  "splashBackground",
  "transparent",
  "disableBackHistoryAPI"
], "appinfo.json");
if (appInfo.id !== manifest.appId) fail("appinfo.json id does not exactly match the distribution manifest");
if (!/^(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})$/.test(appInfo.version)) {
  fail("appinfo.json version must be three decimal components of at most nine digits each");
}
if (appInfo.vendor !== "StreamVue" || appInfo.type !== "web" || appInfo.main !== "index.html") {
  fail("appinfo.json must keep the reviewed StreamVue web-app identity and entry point");
}
if (typeof appInfo.title !== "string" || appInfo.title.length === 0 || appInfo.title.length > 20) {
  fail("appinfo.json title must contain 1 to 20 characters");
}
if (typeof appInfo.appDescription !== "string"
  || appInfo.appDescription.length === 0
  || appInfo.appDescription.length > 60) {
  fail("appinfo.json appDescription must contain 1 to 60 characters");
}
if (appInfo.icon !== "icon.png"
  || appInfo.largeIcon !== "largeIcon.png"
  || appInfo.splashBackground !== "splash.png") {
  fail("appinfo.json must keep the reviewed relative package asset paths");
}
for (const asset of [appInfo.main, appInfo.icon, appInfo.largeIcon, appInfo.splashBackground]) {
  if (!relativeAsset(asset)) fail(`appinfo.json contains unsafe path ${asset}`);
}
if (!/^#[0-9A-F]{6}$/.test(appInfo.iconColor)) fail("appinfo.json iconColor must be an uppercase six-digit hex color");
if (appInfo.resolution !== "1920x1080") fail("appinfo.json must target the reviewed 1920x1080 television surface");
if (appInfo.transparent !== false || appInfo.disableBackHistoryAPI !== false) {
  fail("appinfo.json window and Back-button behavior changed from the reviewed baseline");
}

const televisionAssets = join(repositoryRoot, "platforms", "tv-web", "assets");
await validatePng(join(televisionAssets, "icon-80.png"), 80, 80, "small app icon");
await validatePng(join(televisionAssets, "icon-130.png"), 130, 130, "large app icon");
await validatePng(join(televisionAssets, "splash-1920x1080.png"), 1920, 1080, "splash background");
const sellerIconPath = resolve(repositoryRoot, manifest.sellerIcon);
if (!sellerIconPath.startsWith(resolve(repositoryRoot) + "\\")
  && !sellerIconPath.startsWith(resolve(repositoryRoot) + "/")) {
  fail("sellerIcon resolves outside the repository");
}
await validatePng(sellerIconPath, 400, 400, "Seller Lounge icon");

for (const required of [
  'cp(join(assetsRoot, "icon-80.png"), join(output, "icon.png"))',
  'cp(join(assetsRoot, "icon-130.png"), join(output, "largeIcon.png"))',
  'cp(join(assetsRoot, "splash-1920x1080.png"), join(output, "splash.png"))',
  "removeSourceMaps(output)"
]) {
  if (!packagingScript.includes(required)) fail("webOS package assembly no longer matches the reviewed asset policy");
}
const lgPremium = premium.platforms?.lg;
if (!lgPremium
  || lgPremium.productId !== null
  || lgPremium.verificationProvider !== null
  || lgPremium.ready !== false) {
  fail("premium-products.json must keep LG premium commerce unavailable in this free candidate lane");
}
if (!premiumService.includes('platform === "lg-webos"')
  || !premiumService.includes("no longer provides a native TV billing service")
  || premiumService.includes("VITE_STREAMVUE_LG_PRODUCT_ID")) {
  fail("the LG Store adapter must remain explicitly locked without placeholder product wiring");
}

if (manifest.ready) {
  if (!manifest.sellerAccountType) fail("ready distribution needs the confirmed Seller Lounge account type");
  for (const flag of [
    "sellerTermsReviewed",
    "sellerIconReviewed",
    "listingAssetsPrepared",
    "uxScenarioPrepared",
    "selfChecklistCompleted",
    "privacyDisclosureReviewed",
    "realTvMatrixCompleted"
  ]) {
    if (!manifest[flag]) fail(`ready distribution requires ${flag}`);
  }
}

const serialized = JSON.stringify(manifest).toLowerCase();
for (const forbidden of ["password", "private-key", "access-token", "api-key", "project-key", "billing-key"]) {
  if (serialized.includes(forbidden)) fail(`manifest contains forbidden secret field ${forbidden}`);
}

const appIdExpectation = process.argv.indexOf("--expect-app-id");
if (appIdExpectation >= 0) {
  const expected = process.argv[appIdExpectation + 1];
  if (!expected || manifest.appId !== expected) fail("appId does not exactly match the release input");
  console.log("LG release appId exactly matches the distribution manifest.");
}

if (process.argv.includes("--require-ready")) {
  if (!manifest.ready) {
    fail("LG Seller Lounge distribution is intentionally locked; finish account, terms, reviewed listing assets, UX scenario, self-checklist, privacy, and real-TV testing first");
  }
  console.log("LG Seller Lounge identity and manual submission prerequisites are ready.");
} else {
  console.log(
    manifest.ready
      ? "LG distribution manifest is ready for an audited Seller Lounge IPK candidate."
      : "LG distribution manifest is valid and Seller Lounge candidate creation remains locked by design."
  );
}
