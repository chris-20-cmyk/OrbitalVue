import { access, readFile } from "node:fs/promises";
import { constants } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const manifest = JSON.parse(
  await readFile(join(repositoryRoot, "store", "apple-distribution.json"), "utf8")
);
const packageManifest = await readFile(
  join(repositoryRoot, "platforms", "apple", "Package.swift"),
  "utf8"
);
const appleSourceFiles = [
  join(repositoryRoot, "platforms", "apple", "Sources", "StreamVueUI", "Playback", "KSPlayerSurface.swift"),
  join(repositoryRoot, "platforms", "apple", "Sources", "StreamVueUI", "Playback", "StreamPlayerController.swift")
];

const fail = (message) => {
  throw new Error(`Apple distribution readiness failed: ${message}`);
};
const exactKeys = (value, expected, label) => {
  if (!value || typeof value !== "object" || Array.isArray(value)) fail(`${label} is missing`);
  const actual = Object.keys(value).sort();
  const wanted = [...expected].sort();
  if (JSON.stringify(actual) !== JSON.stringify(wanted)) {
    fail(`${label} must contain exactly: ${wanted.join(", ")}`);
  }
};

exactKeys(manifest, ["contractVersion", "bundleId", "ksPlayer"], "manifest");
exactKeys(
  manifest.ksPlayer,
  [
    "dependency",
    "version",
    "packageSource",
    "distributionPath",
    "appStoreTermsReviewed",
    "ready"
  ],
  "ksPlayer"
);

if (manifest.contractVersion !== "1.0") fail("unexpected contractVersion");
if (!/^[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+$/.test(manifest.bundleId) || manifest.bundleId.length > 255) {
  fail("bundleId is invalid");
}
if (manifest.ksPlayer.dependency !== "kingslay/KSPlayer") fail("unexpected KSPlayer dependency");
if (!/^\d+\.\d+\.\d+$/.test(manifest.ksPlayer.version)) fail("KSPlayer version is invalid");
if (!["public-gpl", "licensed", "absent"].includes(manifest.ksPlayer.packageSource)) {
  fail("unknown KSPlayer packageSource");
}
if (!["unresolved", "gpl-source", "separately-licensed", "avkit-only"].includes(manifest.ksPlayer.distributionPath)) {
  fail("unknown KSPlayer distributionPath");
}
if (typeof manifest.ksPlayer.appStoreTermsReviewed !== "boolean" || typeof manifest.ksPlayer.ready !== "boolean") {
  fail("KSPlayer readiness flags must be boolean");
}

const publicPackageURL = "https://github.com/kingslay/KSPlayer.git";
const usesPublicPackage = packageManifest.includes(publicPackageURL);
const exactVersion = packageManifest.includes(`exact: "${manifest.ksPlayer.version}"`);
if (manifest.ksPlayer.packageSource === "public-gpl" && (!usesPublicPackage || !exactVersion)) {
  fail("the public GPL package source/version does not match Package.swift");
}
if (manifest.ksPlayer.packageSource !== "public-gpl" && usesPublicPackage) {
  fail("Package.swift still references the public GPL package");
}
if (manifest.ksPlayer.distributionPath === "unresolved" && manifest.ksPlayer.ready) {
  fail("an unresolved license path cannot be ready");
}
if (manifest.ksPlayer.distributionPath === "gpl-source" && manifest.ksPlayer.packageSource !== "public-gpl") {
  fail("GPL source distribution must use the declared public package");
}
if (manifest.ksPlayer.distributionPath === "separately-licensed" && manifest.ksPlayer.packageSource !== "licensed") {
  fail("separately licensed distribution must use the licensed package source");
}
if (manifest.ksPlayer.distributionPath === "avkit-only" && manifest.ksPlayer.packageSource !== "absent") {
  fail("AVKit-only distribution must remove the KSPlayer package");
}
if (manifest.ksPlayer.ready && !manifest.ksPlayer.appStoreTermsReviewed) {
  fail("a ready Apple binary requires an explicit App Store compatibility review");
}

if (manifest.ksPlayer.ready && manifest.ksPlayer.distributionPath === "gpl-source") {
  const licensePath = join(repositoryRoot, "LICENSE");
  try {
    await access(licensePath, constants.R_OK);
  } catch {
    fail("GPL source release requires a readable root LICENSE");
  }
  const license = await readFile(licensePath, "utf8");
  if (!/GNU GENERAL PUBLIC LICENSE/i.test(license) || !/Version 3/i.test(license)) {
    fail("root LICENSE is not recognizable as GPL version 3");
  }
}

if (manifest.ksPlayer.ready && manifest.ksPlayer.distributionPath === "separately-licensed") {
  if (!exactVersion) fail("separately licensed package must remain pinned to the declared version");
}

if (manifest.ksPlayer.distributionPath === "avkit-only") {
  if (/\bKSPlayer\b/.test(packageManifest)) fail("AVKit-only Package.swift still declares KSPlayer");
  for (const sourceFile of appleSourceFiles) {
    const source = await readFile(sourceFile, "utf8");
    if (/\bimport\s+KSPlayer\b/.test(source)) fail("AVKit-only source still imports KSPlayer");
  }
}

const serialized = JSON.stringify(manifest).toLowerCase();
for (const forbidden of ["password", "private-key", "certificate", "provisioning-profile"]) {
  if (serialized.includes(forbidden)) fail(`manifest contains forbidden signing field ${forbidden}`);
}

const expectedBundleIndex = process.argv.indexOf("--expect-bundle-id");
if (expectedBundleIndex >= 0) {
  const expected = process.argv[expectedBundleIndex + 1];
  if (!expected || manifest.bundleId !== expected) fail("bundleId does not exactly match the release input");
  console.log("Apple release bundle ID exactly matches the distribution manifest.");
}

if (process.argv.includes("--require-ready")) {
  if (!manifest.ksPlayer.ready) {
    fail("KSPlayer distribution is intentionally locked; choose and verify a legitimate release path first");
  }
  console.log(`Apple distribution is ready through ${manifest.ksPlayer.distributionPath}.`);
} else {
  console.log(
    manifest.ksPlayer.ready
      ? `Apple distribution manifest is ready through ${manifest.ksPlayer.distributionPath}.`
      : "Apple distribution manifest is valid and KSPlayer binary distribution remains locked by design."
  );
}
