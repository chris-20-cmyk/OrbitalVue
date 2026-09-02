import { readFile } from "node:fs/promises";
import { dirname, extname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const readText = (path) => readFile(join(repositoryRoot, path), "utf8");
const readJSON = async (path) => JSON.parse(await readText(path));
const listing = await readJSON("store/store-listing.json");
const release = await readJSON("store/cross-platform-release.json");
const privacy = await readJSON("store/privacy-data-inventory.json");
const lgDistribution = await readJSON("store/lg-distribution.json");

const fail = (message) => {
  throw new Error(`Store listing readiness failed: ${message}`);
};
const exactKeys = (value, expected, label) => {
  if (!value || typeof value !== "object" || Array.isArray(value)) fail(`${label} is missing`);
  const actual = Object.keys(value).sort();
  const wanted = [...expected].sort();
  if (JSON.stringify(actual) !== JSON.stringify(wanted)) {
    fail(`${label} must contain exactly: ${wanted.join(", ")}`);
  }
};
const validText = (value, minimum, maximum, label) => {
  if (typeof value !== "string" || value.trim() !== value || value.length < minimum || value.length > maximum) {
    fail(`${label} must contain ${minimum}-${maximum} trimmed characters`);
  }
};
const relativeAssetPath = (value, platform) => {
  if (typeof value !== "string" || !value || value.includes("\\")) {
    fail(`${platform} asset paths must use non-empty repository-relative forward-slash paths`);
  }
  const absolute = resolve(repositoryRoot, value);
  const root = resolve(repositoryRoot);
  const fromRoot = relative(root, absolute);
  if (!fromRoot || fromRoot.startsWith("..") || fromRoot.includes(":") || fromRoot.startsWith("/")) {
    fail(`${platform} asset path resolves outside the repository`);
  }
  const expectedPrefix = `store/listing-assets/${platform}/`;
  if (!value.startsWith(expectedPrefix)
    && !(platform === "lg" && value === "platforms/tv-web/assets/seller-icon-400.png")) {
    fail(`${platform} listing assets must live below ${expectedPrefix}`);
  }
  return absolute;
};

const args = process.argv.slice(2);
let requiredPlatform = null;
for (let index = 0; index < args.length; index += 1) {
  if (args[index] !== "--require-ready" || requiredPlatform !== null || !args[index + 1]) {
    fail("usage: node tools/verify-store-listing-readiness.mjs [--require-ready windows|android|apple|samsung|lg]");
  }
  requiredPlatform = args[index + 1];
  index += 1;
}

const platformNames = ["windows", "android", "apple", "samsung", "lg"];
const reviewKeys = [
  "copyReviewed",
  "assetsReviewed",
  "contentRatingReviewed",
  "storeQuestionnaireReviewed",
  "realDeviceScreenshotsReviewed"
];
const copyKeys = {
  windows: ["shortDescription", "descriptionSource", "featureSource", "premiumAvailabilityNote"],
  android: ["appName", "shortDescription", "fullDescriptionSource", "premiumAvailabilityNote"],
  apple: ["name", "subtitle", "descriptionSource", "keywords", "premiumAvailabilityNote"],
  samsung: ["name", "description", "fullDescriptionSource", "premiumAvailabilityNote"],
  lg: ["title", "appDescription", "fullDescriptionSource", "premiumAvailabilityNote"]
};
const assetKeys = {
  windows: ["storeIcon300", "desktopScreenshots"],
  android: [
    "playIcon512",
    "featureGraphic1024x500",
    "tvBanner1280x720",
    "phoneScreenshots",
    "tabletScreenshots",
    "tvScreenshots"
  ],
  apple: ["iPhoneScreenshots", "iPadScreenshots", "tvOSScreenshots"],
  samsung: ["heroLogo1920x1080", "heroBackground1920x1080", "tileLogo512x423", "screenshots"],
  lg: ["sellerIcon400", "screenshots"]
};

exactKeys(listing, [
  "contractVersion",
  "status",
  "primaryLocale",
  "technicalReviewDate",
  "owner",
  "shared",
  "platforms"
], "Store listing contract");
if (listing.contractVersion !== "1.0") fail("unexpected contractVersion");
if (listing.status !== "draft-assets-missing" && listing.status !== "reviewed-complete") {
  fail("status must be draft-assets-missing or reviewed-complete");
}
if (listing.primaryLocale !== "en-US") fail("the reviewed primary locale must remain en-US");
if (!/^\d{4}-\d{2}-\d{2}$/.test(listing.technicalReviewDate)) {
  fail("technicalReviewDate must use YYYY-MM-DD");
}

const ownerKeys = [
  "developerDisplayName",
  "copyrightHolder",
  "contentRightsConfirmed",
  "trademarkUseReviewed",
  "ageRatingQuestionnairesCompleted",
  "termsReviewed",
  "listingClaimsReviewed",
  "localizationReviewed"
];
exactKeys(listing.owner, ownerKeys, "listing owner approvals");
for (const field of ["developerDisplayName", "copyrightHolder"]) {
  if (listing.owner[field] !== null) validText(listing.owner[field], 2, 200, `owner.${field}`);
}
for (const field of ownerKeys.slice(2)) {
  if (typeof listing.owner[field] !== "boolean") fail(`owner.${field} must be boolean`);
}

exactKeys(listing.shared, [
  "productName",
  "tagline",
  "shortDescription",
  "fullDescription",
  "features",
  "contentDisclaimer",
  "screenshotPlan"
], "shared listing copy");
if (listing.shared.productName !== "OrbitalVue") fail("the product name must remain OrbitalVue");
validText(listing.shared.tagline, 2, 30, "shared tagline");
validText(listing.shared.shortDescription, 10, 80, "shared short description");
validText(listing.shared.fullDescription, 200, 10_000, "shared full description");
validText(listing.shared.contentDisclaimer, 40, 500, "content disclaimer");
if (!/does not provide/i.test(listing.shared.contentDisclaimer)
  || !/authorized to access/i.test(listing.shared.contentDisclaimer)) {
  fail("content disclaimer must say OrbitalVue provides no content and requires authorized sources");
}
if (!Array.isArray(listing.shared.features) || listing.shared.features.length !== 6) {
  fail("shared features must contain the six reviewed product capabilities");
}
for (const [index, feature] of listing.shared.features.entries()) {
  validText(feature, 10, 200, `shared feature ${index + 1}`);
}
const sceneIds = ["source-library", "group-browser", "native-playback", "playback-settings"];
if (!Array.isArray(listing.shared.screenshotPlan) || listing.shared.screenshotPlan.length !== sceneIds.length) {
  fail("screenshotPlan must contain the four reviewed scenes");
}
for (const [index, scene] of listing.shared.screenshotPlan.entries()) {
  exactKeys(scene, ["id", "caption"], `screenshot scene ${index + 1}`);
  if (scene.id !== sceneIds[index]) fail(`screenshot scene ${index + 1} must be ${sceneIds[index]}`);
  validText(scene.caption, 20, 140, `${scene.id} caption`);
}

const allPublicCopy = JSON.stringify({ shared: listing.shared, platforms: listing.platforms });
const forbiddenClaims = /(?:\b(?:best|official|sale|discount)\b|#1|download now|install now|try now)/i;
if (forbiddenClaims.test(allPublicCopy)) {
  fail("listing copy contains an unreviewed ranking, affiliation, promotion, or call-to-action claim");
}
for (const required of ["M3U", "M3U8", "Plex", "Emby", "does not provide"]) {
  if (!listing.shared.fullDescription.includes(required)) fail(`shared description is missing ${required}`);
}

exactKeys(listing.platforms, platformNames, "listing platform map");
for (const platform of platformNames) {
  const entry = listing.platforms[platform];
  exactKeys(entry, ["applicationId", "category", "copy", "assets", "reviews", "ready"], `${platform} listing`);
  if (entry.applicationId !== release.platforms[platform].applicationId) {
    fail(`${platform} application ID does not match the release contract`);
  }
  validText(entry.category, 3, 100, `${platform} category`);
  exactKeys(entry.copy, copyKeys[platform], `${platform} listing copy`);
  exactKeys(entry.assets, assetKeys[platform], `${platform} listing assets`);
  exactKeys(entry.reviews, reviewKeys, `${platform} listing reviews`);
  for (const [review, value] of Object.entries(entry.reviews)) {
    if (typeof value !== "boolean") fail(`${platform}.${review} must be boolean`);
  }
  if (typeof entry.ready !== "boolean") fail(`${platform}.ready must be boolean`);
  validText(entry.copy.premiumAvailabilityNote, 20, 200, `${platform} premium availability note`);
}

if (listing.platforms.windows.copy.descriptionSource !== "shared.fullDescription"
  || listing.platforms.windows.copy.featureSource !== "shared.features") {
  fail("Windows must use the reviewed shared description and feature list");
}
validText(listing.platforms.windows.copy.shortDescription, 10, 1_500, "Windows short description");
if (listing.platforms.android.copy.appName !== "OrbitalVue") fail("Google Play app name must remain OrbitalVue");
validText(listing.platforms.android.copy.appName, 2, 30, "Google Play app name");
validText(listing.platforms.android.copy.shortDescription, 10, 80, "Google Play short description");
if (/[\r\n]/.test(listing.platforms.android.copy.shortDescription)
  || listing.platforms.android.copy.fullDescriptionSource !== "shared.fullDescription") {
  fail("Google Play short description must be one line and use the reviewed full description");
}
validText(listing.platforms.apple.copy.name, 2, 30, "Apple app name");
validText(listing.platforms.apple.copy.subtitle, 2, 30, "Apple subtitle");
validText(listing.platforms.apple.copy.keywords, 3, 100, "Apple keywords");
if (listing.platforms.apple.copy.descriptionSource !== "shared.fullDescription") {
  fail("Apple must use the reviewed shared description");
}
if (listing.platforms.samsung.copy.name !== "OrbitalVue") fail("Samsung app name must remain OrbitalVue");
validText(listing.platforms.samsung.copy.description, 20, 500, "Samsung description");
if (listing.platforms.samsung.copy.fullDescriptionSource !== "shared.fullDescription") {
  fail("Samsung must use the reviewed shared full description");
}
validText(listing.platforms.lg.copy.title, 1, 20, "LG app title");
validText(listing.platforms.lg.copy.appDescription, 1, 60, "LG app description");
if (listing.platforms.lg.copy.fullDescriptionSource !== "shared.fullDescription") {
  fail("LG must use the reviewed shared full description");
}
if (!/not available in this LG release/i.test(listing.platforms.lg.copy.premiumAvailabilityNote)) {
  fail("LG listing must explicitly disclose that Plex and Emby are unavailable");
}
for (const platform of ["windows", "android", "apple", "samsung"]) {
  if (!/one-time Premium unlock/i.test(listing.platforms[platform].copy.premiumAvailabilityNote)) {
    fail(`${platform} listing must describe premium as an optional one-time unlock`);
  }
}

const pngSignature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
const imageMetadata = async (path, platform) => {
  const absolute = relativeAssetPath(path, platform);
  const bytes = await readFile(absolute).catch(() => fail(`${platform} listing asset is missing: ${path}`));
  const extension = extname(path).toLowerCase();
  if (bytes.length >= 26 && bytes.subarray(0, 8).equals(pngSignature)) {
    return {
      format: "png",
      width: bytes.readUInt32BE(16),
      height: bytes.readUInt32BE(20),
      colorType: bytes[25],
      hasAlpha: bytes[25] === 4 || bytes[25] === 6,
      bytes: bytes.length
    };
  }
  if (bytes.length >= 4 && bytes[0] === 0xff && bytes[1] === 0xd8) {
    let offset = 2;
    const startOfFrame = new Set([0xc0, 0xc1, 0xc2, 0xc3, 0xc5, 0xc6, 0xc7, 0xc9, 0xca, 0xcb, 0xcd, 0xce, 0xcf]);
    while (offset + 8 < bytes.length) {
      while (offset < bytes.length && bytes[offset] === 0xff) offset += 1;
      const marker = bytes[offset];
      offset += 1;
      if (marker === 0xd9 || marker === 0xda) break;
      if (marker === 0x01 || (marker >= 0xd0 && marker <= 0xd7)) continue;
      if (offset + 2 > bytes.length) break;
      const length = bytes.readUInt16BE(offset);
      if (length < 2 || offset + length > bytes.length) break;
      if (startOfFrame.has(marker)) {
        return {
          format: "jpg",
          width: bytes.readUInt16BE(offset + 5),
          height: bytes.readUInt16BE(offset + 3),
          colorType: null,
          hasAlpha: false,
          bytes: bytes.length
        };
      }
      offset += length;
    }
  }
  fail(`${path} is not a readable PNG or JPEG listing asset (extension ${extension || "none"})`);
};
const validateImage = async (path, platform, specification) => {
  const image = await imageMetadata(path, platform);
  if (!specification.formats.includes(image.format)) {
    fail(`${path} must be ${specification.formats.join(" or ")}`);
  }
  const accepted = specification.sizes.some(([width, height]) => image.width === width && image.height === height);
  if (!accepted) {
    fail(`${path} has ${image.width}x${image.height}; expected ${specification.sizes.map((size) => size.join("x")).join(" or ")}`);
  }
  if (specification.alpha !== null && image.hasAlpha !== specification.alpha) {
    fail(`${path} must ${specification.alpha ? "include" : "not include"} an alpha channel`);
  }
  if (specification.colorType !== null && image.colorType !== specification.colorType) {
    fail(`${path} must use PNG color type ${specification.colorType}`);
  }
  if (image.bytes > specification.maximumBytes || (specification.strictMaximum && image.bytes === specification.maximumBytes)) {
    fail(`${path} exceeds its Store file-size limit`);
  }
};
const refComplete = async (ref, platform, specification, label) => {
  exactKeys(ref, ["path", "reviewed"], label);
  if (ref.path !== null) await validateImage(ref.path, platform, specification);
  if (typeof ref.reviewed !== "boolean") fail(`${label}.reviewed must be boolean`);
  return ref.path !== null && ref.reviewed;
};
const shotsComplete = async (paths, platform, specification, label) => {
  if (!Array.isArray(paths) || paths.length !== sceneIds.length) {
    fail(`${label} must contain exactly four ordered screenshot paths`);
  }
  for (const [index, path] of paths.entries()) {
    if (path !== null) {
      if (typeof path !== "string") fail(`${label}[${index}] must be null or a path`);
      await validateImage(path, platform, specification);
    }
  }
  return paths.every((path) => typeof path === "string");
};

const MiB = 1024 * 1024;
const KiB = 1024;
const spec = (sizes, formats, alpha, maximumBytes, colorType = null, strictMaximum = false) => ({
  sizes, formats, alpha, maximumBytes, colorType, strictMaximum
});
const imageSpecs = {
  windowsIcon: spec([[300, 300]], ["png"], null, 50 * MiB),
  windowsShot: spec([[1366, 768], [1920, 1080], [3840, 2160]], ["png"], null, 50 * MiB),
  androidIcon: spec([[512, 512]], ["png"], true, 1 * MiB, 6),
  androidFeature: spec([[1024, 500]], ["png", "jpg"], false, 15 * MiB),
  androidBanner: spec([[1280, 720]], ["png", "jpg"], false, 15 * MiB),
  androidPhone: spec([[1080, 1920], [1920, 1080]], ["png", "jpg"], false, 8 * MiB),
  androidLandscape: spec([[1920, 1080]], ["png", "jpg"], false, 8 * MiB),
  applePhone: spec([[1260, 2736], [2736, 1260], [1290, 2796], [2796, 1290], [1320, 2868], [2868, 1320]], ["png", "jpg"], false, 10 * MiB),
  applePad: spec([[2064, 2752], [2752, 2064], [2048, 2732], [2732, 2048]], ["png", "jpg"], false, 10 * MiB),
  appleTV: spec([[1920, 1080], [3840, 2160]], ["png", "jpg"], false, 10 * MiB),
  samsungHeroLogo: spec([[1920, 1080]], ["png"], true, 300 * KiB, 6, true),
  samsungHeroBackground: spec([[1920, 1080]], ["png", "jpg"], false, 300 * KiB, null, true),
  samsungTile: spec([[512, 423]], ["png"], false, 300 * KiB, null, true),
  samsungShot: spec([[1920, 1080]], ["jpg"], false, 500 * KiB, null, true),
  lgIcon: spec([[400, 400]], ["png"], null, 5 * MiB),
  lgShot: spec([[1920, 1080]], ["png", "jpg"], false, 10 * MiB)
};

const assetReadiness = {};
const windowsAssets = await Promise.all([
  refComplete(
    listing.platforms.windows.assets.storeIcon300,
    "windows",
    imageSpecs.windowsIcon,
    "windows.storeIcon300"
  ),
  shotsComplete(
    listing.platforms.windows.assets.desktopScreenshots,
    "windows",
    imageSpecs.windowsShot,
    "windows.desktopScreenshots"
  )
]);
assetReadiness.windows = windowsAssets.every(Boolean);
const androidAssets = await Promise.all([
  refComplete(
    listing.platforms.android.assets.playIcon512,
    "android",
    imageSpecs.androidIcon,
    "android.playIcon512"
  ),
  refComplete(
    listing.platforms.android.assets.featureGraphic1024x500,
    "android",
    imageSpecs.androidFeature,
    "android.featureGraphic1024x500"
  ),
  refComplete(
    listing.platforms.android.assets.tvBanner1280x720,
    "android",
    imageSpecs.androidBanner,
    "android.tvBanner1280x720"
  ),
  shotsComplete(
    listing.platforms.android.assets.phoneScreenshots,
    "android",
    imageSpecs.androidPhone,
    "android.phoneScreenshots"
  ),
  shotsComplete(
    listing.platforms.android.assets.tabletScreenshots,
    "android",
    imageSpecs.androidLandscape,
    "android.tabletScreenshots"
  ),
  shotsComplete(
    listing.platforms.android.assets.tvScreenshots,
    "android",
    imageSpecs.androidLandscape,
    "android.tvScreenshots"
  )
]);
assetReadiness.android = androidAssets.every(Boolean);
const appleAssets = await Promise.all([
  shotsComplete(
    listing.platforms.apple.assets.iPhoneScreenshots,
    "apple",
    imageSpecs.applePhone,
    "apple.iPhoneScreenshots"
  ),
  shotsComplete(
    listing.platforms.apple.assets.iPadScreenshots,
    "apple",
    imageSpecs.applePad,
    "apple.iPadScreenshots"
  ),
  shotsComplete(
    listing.platforms.apple.assets.tvOSScreenshots,
    "apple",
    imageSpecs.appleTV,
    "apple.tvOSScreenshots"
  )
]);
assetReadiness.apple = appleAssets.every(Boolean);
const samsungAssets = await Promise.all([
  refComplete(
    listing.platforms.samsung.assets.heroLogo1920x1080,
    "samsung",
    imageSpecs.samsungHeroLogo,
    "samsung.heroLogo1920x1080"
  ),
  refComplete(
    listing.platforms.samsung.assets.heroBackground1920x1080,
    "samsung",
    imageSpecs.samsungHeroBackground,
    "samsung.heroBackground1920x1080"
  ),
  refComplete(
    listing.platforms.samsung.assets.tileLogo512x423,
    "samsung",
    imageSpecs.samsungTile,
    "samsung.tileLogo512x423"
  ),
  shotsComplete(
    listing.platforms.samsung.assets.screenshots,
    "samsung",
    imageSpecs.samsungShot,
    "samsung.screenshots"
  )
]);
assetReadiness.samsung = samsungAssets.every(Boolean);
const lgAssets = await Promise.all([
  refComplete(
    listing.platforms.lg.assets.sellerIcon400,
    "lg",
    imageSpecs.lgIcon,
    "lg.sellerIcon400"
  ),
  shotsComplete(
    listing.platforms.lg.assets.screenshots,
    "lg",
    imageSpecs.lgShot,
    "lg.screenshots"
  )
]);
assetReadiness.lg = lgAssets.every(Boolean);

const webOsInfo = await readJSON("platforms/tv-web/platform/webos/appinfo.json");
if (webOsInfo.title !== listing.platforms.lg.copy.title
  || webOsInfo.appDescription !== listing.platforms.lg.copy.appDescription) {
  fail("LG packaged title and description do not match the Store listing contract");
}
const samsungConfig = await readText("platforms/tv-web/platform/samsung/config.xml");
for (const fragment of [
  `<name>${listing.platforms.samsung.copy.name}</name>`,
  `<description>${listing.platforms.samsung.copy.description}</description>`
]) {
  if (!samsungConfig.includes(fragment)) fail(`Samsung package metadata is missing ${fragment}`);
}
const androidStrings = await readText("platforms/android/app/src/main/res/values/strings.xml");
if (!androidStrings.includes(`<string name="app_name">${listing.platforms.android.copy.appName}</string>`)) {
  fail("Android packaged app name does not match the Google Play listing");
}
const appleProject = await readText("platforms/apple/project.yml");
if ((appleProject.match(/CFBundleDisplayName: OrbitalVue/g) ?? []).length !== 2
  || (appleProject.match(/LSApplicationCategoryType: public\.app-category\.entertainment/g) ?? []).length !== 2) {
  fail("Apple packaged names/categories do not match the App Store listing");
}
const windowsManifest = await readText("packaging/windows-msix/AppxManifest.template.xml");
if ((windowsManifest.match(/DisplayName="?OrbitalVue"?/g) ?? []).length < 1
  && !windowsManifest.includes("<DisplayName>OrbitalVue</DisplayName>")) {
  fail("Windows package display name does not match the Partner Center listing");
}
if (listing.platforms.lg.assets.sellerIcon400.reviewed !== lgDistribution.sellerIconReviewed
  || listing.platforms.lg.reviews.assetsReviewed !== lgDistribution.listingAssetsPrepared) {
  fail("LG listing artwork review must agree with the Seller Lounge distribution manifest");
}

const ownerReady = listing.status === "reviewed-complete"
  && typeof listing.owner.developerDisplayName === "string"
  && typeof listing.owner.copyrightHolder === "string"
  && ownerKeys.slice(2).every((field) => listing.owner[field] === true);
for (const platform of platformNames) {
  const entry = listing.platforms[platform];
  const calculatedReady = ownerReady
    && privacy.platforms[platform].ready
    && Object.values(entry.reviews).every((value) => value === true)
    && assetReadiness[platform];
  if (entry.ready !== calculatedReady) {
    fail(`${platform}.ready must exactly match owner, privacy, review, and final-asset gates`);
  }
}

if (requiredPlatform !== null) {
  if (!platformNames.includes(requiredPlatform)) fail(`unknown platform ${requiredPlatform}`);
  if (!listing.platforms[requiredPlatform].ready) {
    fail(`${requiredPlatform} Store listing is intentionally locked; finish owner identity, content/trademark/age/terms review, privacy readiness, final assets, and real-device screenshots first`);
  }
}

const readyPlatforms = platformNames.filter((platform) => listing.platforms[platform].ready);
console.log(
  `Store listing contract is valid; ${readyPlatforms.length} of ${platformNames.length} lanes have approved copy and final assets (${readyPlatforms.join(", ") || "none"}).`
);
