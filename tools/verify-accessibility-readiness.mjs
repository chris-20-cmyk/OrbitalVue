import { readFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";

const root = process.cwd();
const manifestPath = path.join(root, "store", "accessibility-readiness.json");
const releasePath = path.join(root, "store", "cross-platform-release.json");
const expectedPlatforms = ["windows", "android", "apple", "samsung", "lg"];

function fail(message) {
  throw new Error(`Accessibility readiness failed: ${message}`);
}

function exactKeys(value, expected, label) {
  if (!value || typeof value !== "object" || Array.isArray(value)) fail(`${label} must be an object`);
  const actual = Object.keys(value).sort();
  const wanted = [...expected].sort();
  if (JSON.stringify(actual) !== JSON.stringify(wanted)) {
    fail(`${label} keys must be exactly: ${wanted.join(", ")}`);
  }
}

function booleanRecord(value, label) {
  if (!value || typeof value !== "object" || Array.isArray(value) || Object.keys(value).length === 0) {
    fail(`${label} must be a non-empty object`);
  }
  for (const [key, item] of Object.entries(value)) {
    if (typeof item !== "boolean") fail(`${label}.${key} must be boolean`);
  }
}

function allTrue(value) {
  return Object.values(value).every((item) => item === true);
}

function nullableText(value, label) {
  if (value !== null && (typeof value !== "string" || value.trim().length === 0)) {
    fail(`${label} must be null or non-empty text`);
  }
}

async function source(relativePath) {
  return readFile(path.join(root, relativePath), "utf8");
}

function includesAll(text, fragments, label) {
  for (const fragment of fragments) {
    if (!text.includes(fragment)) fail(`${label} is missing required accessibility marker: ${fragment}`);
  }
}

const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
const release = JSON.parse(await readFile(releasePath, "utf8"));

exactKeys(manifest, ["schemaVersion", "status", "baseline", "sharedReview", "platforms"], "manifest");
if (manifest.schemaVersion !== 1) fail("schemaVersion must be 1");
if (manifest.status !== "manual-validation-required" && manifest.status !== "ready") {
  fail("status must be manual-validation-required or ready");
}
exactKeys(manifest.baseline, ["engineeringTarget", "conformanceClaimApproved", "scopeReviewed", "exceptionsDocumented"], "baseline");
if (!manifest.baseline.engineeringTarget.includes("WCAG 2.2 Level AA")) fail("engineering baseline must name WCAG 2.2 Level AA");
for (const key of ["conformanceClaimApproved", "scopeReviewed", "exceptionsDocumented"]) {
  if (typeof manifest.baseline[key] !== "boolean") fail(`baseline.${key} must be boolean`);
}
exactKeys(manifest.sharedReview, ["ownerApproved", "userSuppliedMediaLimitationsReviewed", "knownDefectsReviewed", "ready"], "sharedReview");
for (const key of ["ownerApproved", "userSuppliedMediaLimitationsReviewed", "knownDefectsReviewed", "ready"]) {
  if (typeof manifest.sharedReview[key] !== "boolean") fail(`sharedReview.${key} must be boolean`);
}
const sharedReady = manifest.baseline.conformanceClaimApproved
  && manifest.baseline.scopeReviewed
  && manifest.baseline.exceptionsDocumented
  && manifest.sharedReview.ownerApproved
  && manifest.sharedReview.userSuppliedMediaLimitationsReviewed
  && manifest.sharedReview.knownDefectsReviewed;
if (manifest.sharedReview.ready !== sharedReady) fail("sharedReview.ready does not match its evidence");

exactKeys(manifest.platforms, expectedPlatforms, "platforms");
const readyPlatforms = [];
for (const platform of expectedPlatforms) {
  const entry = manifest.platforms[platform];
  exactKeys(entry, ["applicationId", "sourceAudits", "manualEvidence", "testedVersion", "testedAt", "tester", "evidenceReferences", "ready"], `platforms.${platform}`);
  if (entry.applicationId !== release.platforms[platform].applicationId) {
    fail(`${platform} applicationId does not match cross-platform release identity`);
  }
  booleanRecord(entry.sourceAudits, `platforms.${platform}.sourceAudits`);
  booleanRecord(entry.manualEvidence, `platforms.${platform}.manualEvidence`);
  if (!allTrue(entry.sourceAudits)) fail(`${platform} contains an unresolved source accessibility audit`);
  nullableText(entry.testedVersion, `platforms.${platform}.testedVersion`);
  nullableText(entry.testedAt, `platforms.${platform}.testedAt`);
  nullableText(entry.tester, `platforms.${platform}.tester`);
  if (!Array.isArray(entry.evidenceReferences) || entry.evidenceReferences.some((item) => typeof item !== "string" || item.trim().length === 0)) {
    fail(`${platform} evidenceReferences must contain only non-empty paths or review references`);
  }
  if (entry.testedAt !== null && !/^\d{4}-\d{2}-\d{2}$/.test(entry.testedAt)) {
    fail(`${platform} testedAt must use YYYY-MM-DD`);
  }
  const manualReady = allTrue(entry.manualEvidence);
  const metadataReady = Boolean(entry.testedVersion && entry.testedAt && entry.tester && entry.evidenceReferences.length > 0);
  const calculatedReady = sharedReady && manualReady && metadataReady;
  if (entry.ready !== calculatedReady) fail(`${platform}.ready does not match review and test evidence`);
  if (entry.ready) readyPlatforms.push(platform);
}

const windowsApp = await source("src/StreamVue.Player/App.xaml");
const windowsMain = await source("src/StreamVue.Player/MainWindow.xaml");
includesAll(windowsApp, [
  'Property="AutomationProperties.Name"',
  'Property="IsKeyboardFocused" Value="True"',
  'Property="BorderThickness" Value="2"'
], "Windows control styles");
includesAll(windowsMain, [
  'AutomationProperties.Name="Search channels or groups"',
  'AutomationProperties.Name="Channels"',
  'AutomationProperties.Name="Volume"',
  'AutomationProperties.Name="Aspect ratio"',
  'AutomationProperties.LiveSetting="Polite"'
], "Windows main UI");
if (windowsMain.includes('FocusVisualStyle="{x:Null}"') || windowsMain.includes('Property="FocusVisualStyle" Value="{x:Null}"')) {
  fail("Windows UI must not suppress keyboard focus visuals");
}

const android = await source("platforms/android/app/src/main/java/com/streamvue/player/ui/StreamVueApp.kt");
includesAll(android, [
  "LiveRegionMode.Polite",
  ".semantics { heading() }",
  "stateDescription = if (selected)",
  'stateDescription = "Current ratio ${mode.label}"'
], "Android Compose UI");

const appleShared = await source("platforms/apple/Sources/StreamVueUI/Views/SharedComponents.swift");
const applePlayer = await source("platforms/apple/Sources/StreamVueUI/Views/PlayerPanel.swift");
const appleTv = await source("platforms/apple/Sources/StreamVueUI/Views/AppleTVRootView.swift");
includesAll(appleShared, [
  ".accessibilityLabel(channel.name)",
  "accessibilityAddTraits(isSelected ? .isSelected : [])",
  "Dismiss notification"
], "Apple shared UI");
includesAll(applePlayer, [
  "Controls \\(channel.name)",
  "Aspect ratio, \\(settings.aspectMode.label)",
  ".font(.caption2.weight(.bold))"
], "Apple player UI");
includesAll(appleTv, [
  "accessibilityReduceMotion",
  "reduceMotion ? nil",
  ".accessibilityHint(\"Starts playback\")"
], "Apple TV UI");

const televisionUi = await source("platforms/tv-web/src/ui/StreamVueTvApp.ts");
const televisionNav = await source("platforms/tv-web/src/navigation/SpatialNavigator.ts");
const televisionCss = await source("platforms/tv-web/src/styles.css");
includesAll(televisionUi, [
  'aria-live="polite"',
  'aria-pressed="${active}"',
  'aria-current="${selected}"',
  'aria-controls="source-panel-${mode}"',
  "modalReturnFocusSelector"
], "television web UI");
if (televisionUi.includes('role="listitem"')) fail("television buttons must retain native button semantics");
includesAll(televisionNav, ['event.key === "Tab"', "event.shiftKey"], "television focus manager");
includesAll(televisionCss, ["@media (prefers-reduced-motion: reduce)", "outline-offset: 5px"], "television styles");

const requestedIndex = process.argv.indexOf("--require-ready");
if (requestedIndex >= 0) {
  const platform = process.argv[requestedIndex + 1];
  if (!expectedPlatforms.includes(platform)) fail(`unknown --require-ready platform: ${platform ?? "(missing)"}`);
  if (!manifest.platforms[platform].ready) {
    fail(`${platform} accessibility is intentionally locked; complete the documented assistive-technology, scaling, contrast, remote, and playback walkthroughs first`);
  }
}

if (manifest.status === "ready" && readyPlatforms.length !== expectedPlatforms.length) {
  fail("status cannot be ready until all five platform lanes are ready");
}
if (manifest.status !== "ready" && readyPlatforms.length === expectedPlatforms.length) {
  fail("status must be ready when all five platform lanes are ready");
}

console.log(`Accessibility readiness is structurally valid; ${readyPlatforms.length} of ${expectedPlatforms.length} lanes have complete manual evidence (${readyPlatforms.join(", ") || "none"}).`);
