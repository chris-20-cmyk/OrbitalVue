import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";

const root = process.cwd();
const platforms = ["windows", "android", "apple", "samsung", "lg"];
const platformLabels = {
  windows: "Windows / Microsoft Store",
  android: "Android / Google Play",
  apple: "iPhone, iPad, and Apple TV",
  samsung: "Samsung TV",
  lg: "LG webOS TV"
};

async function json(relativePath) {
  return JSON.parse(await readFile(path.join(root, relativePath), "utf8"));
}

function fail(message) {
  throw new Error(`Release readiness report failed: ${message}`);
}

function incomplete(value, prefix) {
  if (value === null || value === false) return [prefix];
  if (Array.isArray(value)) {
    if (value.length === 0) return [`${prefix} (none recorded)`];
    const missing = value.filter((item) => item === null || item === false || item === "").length;
    return missing > 0 ? [`${prefix} (${missing} of ${value.length} missing)`] : [];
  }
  if (!value || typeof value !== "object") return [];
  return Object.entries(value).flatMap(([key, item]) => {
    if (key === "ready" || key === "status") return [];
    return incomplete(item, prefix ? `${prefix}.${key}` : key);
  });
}

function gate(ready, blockers, note = null) {
  if (ready && blockers.length > 0) fail(`ready gate still has blockers: ${blockers.join(", ")}`);
  if (!ready && blockers.length === 0) fail("blocked gate must explain at least one blocker");
  return { ready, blockers: [...new Set(blockers)].sort(), note };
}

function titleCasePath(value) {
  return value
    .replaceAll(".", " › ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

const [pkg, release, premium, premiumVerifier, privacy, listing, accessibility, publicSite, apple, samsung, lg] = await Promise.all([
  json("package.json"),
  json("store/cross-platform-release.json"),
  json("store/premium-products.json"),
  json("store/premium-verifier-readiness.json"),
  json("store/privacy-data-inventory.json"),
  json("store/store-listing.json"),
  json("store/accessibility-readiness.json"),
  json("store/public-site-readiness.json"),
  json("store/apple-distribution.json"),
  json("store/samsung-distribution.json"),
  json("store/lg-distribution.json")
]);

for (const manifest of [release, premium, privacy, listing, accessibility]) {
  const keys = Object.keys(manifest.platforms ?? {}).sort();
  if (JSON.stringify(keys) !== JSON.stringify([...platforms].sort())) fail("a shared manifest does not contain exactly five platforms");
}
if (JSON.stringify(Object.keys(premiumVerifier.platforms ?? {}).sort()) !== JSON.stringify(["android", "samsung"])) {
  fail("premium verifier manifest must contain exactly Android and Samsung");
}

const privacyShared = incomplete({
  privacyContact: privacy.privacyContact,
  policyEffectiveDate: privacy.policyEffectiveDate,
  policyOwnerApproved: privacy.policyOwnerApproved
}, "privacy.shared");
const listingShared = incomplete(listing.owner, "listing.owner");
const accessibilityShared = incomplete({
  baseline: accessibility.baseline,
  sharedReview: accessibility.sharedReview
}, "accessibility.shared");
const publicSiteShared = incomplete({
  canonicalBaseUrl: publicSite.canonicalBaseUrl,
  urls: publicSite.urls,
  owner: publicSite.owner,
  pages: publicSite.pages,
  deployment: publicSite.deployment
}, "publicSite");

const reportPlatforms = {};
for (const platform of platforms) {
  const paidPremium = release.platforms[platform].premiumReleasePolicy !== "locked-free-app";
  const premiumGate = paidPremium
    ? gate(premium.platforms[platform].ready, incomplete(premium.platforms[platform], "premium"))
    : { ready: true, blockers: [], note: "Free Store lane; personal media centers remain intentionally locked." };
  const verifierGate = platform === "android" || platform === "samsung"
    ? gate(
      premiumVerifier.platforms[platform].ready,
      incomplete(premiumVerifier.platforms[platform], "premiumVerifier")
    )
    : {
      ready: true,
      blockers: [],
      note: platform === "lg"
        ? "No verifier is enabled in the free premium-locked lane."
        : "Entitlement verification is native to this Store platform."
    };

  const privacyGate = gate(
    privacy.platforms[platform].ready,
    [...privacyShared, ...incomplete(privacy.platforms[platform], "privacy.platform")]
  );
  const listingGate = gate(
    listing.platforms[platform].ready,
    [...listingShared, ...incomplete(listing.platforms[platform], "listing.platform")]
  );
  const accessibilityGate = gate(
    accessibility.platforms[platform].ready,
    [...accessibilityShared, ...incomplete(accessibility.platforms[platform], "accessibility.platform")]
  );
  const publicSiteGate = gate(publicSite.ready, publicSiteShared);

  let distributionGate = { ready: true, blockers: [], note: "Candidate workflow and public application identity are structurally configured." };
  if (platform === "windows" && release.platforms.windows.applicationId === null) {
    distributionGate = gate(false, ["distribution.partnerCenterApplicationIdentity"]);
  } else if (platform === "apple") {
    const appleBlockers = incomplete(apple.ksPlayer, "distribution.ksPlayer");
    if (apple.ksPlayer.distributionPath === "unresolved") appleBlockers.push("distribution.ksPlayer.distributionPath");
    distributionGate = gate(apple.ksPlayer.ready, appleBlockers);
  } else if (platform === "samsung") {
    distributionGate = gate(samsung.ready, incomplete(samsung, "distribution.samsung"));
  } else if (platform === "lg") {
    distributionGate = gate(lg.ready, incomplete(lg, "distribution.lg"));
  }

  const gates = {
    premium: premiumGate,
    verifier: verifierGate,
    privacy: privacyGate,
    listing: listingGate,
    accessibility: accessibilityGate,
    publicSite: publicSiteGate,
    distribution: distributionGate
  };
  reportPlatforms[platform] = {
    label: platformLabels[platform],
    applicationId: release.platforms[platform].applicationId,
    candidateWorkflow: release.platforms[platform].candidateWorkflow,
    artifactKind: release.platforms[platform].artifactKind,
    ready: Object.values(gates).every((item) => item.ready),
    gates
  };
}

const readyPlatforms = platforms.filter((platform) => reportPlatforms[platform].ready);
const blockerCount = platforms.reduce(
  (total, platform) => total + Object.values(reportPlatforms[platform].gates).reduce((sum, item) => sum + item.blockers.length, 0),
  0
);
const report = {
  reportVersion: 1,
  appVersion: pkg.version,
  featureId: release.featureId,
  purchaseModel: release.purchaseModel,
  submissionPolicy: release.submissionPolicy,
  summary: {
    readyPlatformCount: readyPlatforms.length,
    platformCount: platforms.length,
    platformGateBlockerCount: blockerCount,
    readyPlatforms
  },
  platforms: reportPlatforms
};

const lines = [
  "# StreamVue release readiness",
  "",
  `App version: **${pkg.version}**  `,
  `Ready Store lanes: **${readyPlatforms.length}/${platforms.length}**  `,
  `Open platform-gate items: **${blockerCount}**`,
  "",
  "This report is generated from committed fail-closed manifests. It does not create seller accounts, approve legal text, perform device testing, sign packages, or upload a release.",
  ""
];

for (const platform of platforms) {
  const entry = reportPlatforms[platform];
  lines.push(`## ${entry.label}`, "");
  lines.push(`Overall: **${entry.ready ? "READY" : "BLOCKED"}**  `);
  lines.push(`Identity: \`${entry.applicationId ?? "not reserved"}\`  `);
  lines.push(`Candidate: \`${entry.artifactKind}\` via \`${entry.candidateWorkflow}\``, "");
  lines.push("| Gate | State | Remaining evidence |", "| --- | --- | --- |");
  for (const [name, item] of Object.entries(entry.gates)) {
    const detail = item.blockers.length > 0
      ? item.blockers.map(titleCasePath).join("<br>")
      : item.note ?? "Complete";
    lines.push(`| ${titleCasePath(name)} | ${item.ready ? "Ready" : "Blocked"} | ${detail} |`);
  }
  lines.push("");
}

lines.push(
  "## Safe next action",
  "",
  "Resolve only fields backed by completed owner, vendor, or device evidence. Re-run `pnpm release:check` and `pnpm release:report`; do not set a readiness flag merely to make a workflow pass.",
  ""
);

const outputIndex = process.argv.indexOf("--output-dir");
const requestedOutput = outputIndex >= 0 ? process.argv[outputIndex + 1] : "artifacts/release-readiness";
if (!requestedOutput) fail("--output-dir requires a path");
const outputDirectory = path.resolve(root, requestedOutput);
const allowedRoot = path.resolve(root, "artifacts");
const relativeOutput = path.relative(allowedRoot, outputDirectory);
if (relativeOutput.startsWith("..") || path.isAbsolute(relativeOutput)) fail("output directory must stay under artifacts/");

await mkdir(outputDirectory, { recursive: true });
await writeFile(path.join(outputDirectory, "release-readiness.json"), `${JSON.stringify(report, null, 2)}\n`, "utf8");
await writeFile(path.join(outputDirectory, "release-readiness.md"), `${lines.join("\n")}\n`, "utf8");
console.log(`Release readiness report written to ${path.relative(root, outputDirectory)}: ${readyPlatforms.length}/${platforms.length} lanes ready, ${blockerCount} open gate items.`);
