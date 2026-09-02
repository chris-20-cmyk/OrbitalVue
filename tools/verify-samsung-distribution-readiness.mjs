import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const manifest = JSON.parse(
  await readFile(join(repositoryRoot, "store", "samsung-distribution.json"), "utf8")
);
const configXml = await readFile(
  join(repositoryRoot, "platforms", "tv-web", "platform", "samsung", "config.xml"),
  "utf8"
);
const packagingScript = await readFile(
  join(repositoryRoot, "platforms", "tv-web", "scripts", "package-platforms.mjs"),
  "utf8"
);

const fail = (message) => {
  throw new Error(`Samsung distribution readiness failed: ${message}`);
};
const exactKeys = (value, expected, label) => {
  if (!value || typeof value !== "object" || Array.isArray(value)) fail(`${label} is missing`);
  const actual = Object.keys(value).sort();
  const wanted = [...expected].sort();
  if (JSON.stringify(actual) !== JSON.stringify(wanted)) {
    fail(`${label} must contain exactly: ${wanted.join(", ")}`);
  }
};
const xmlAttribute = (tagPattern, name) => {
  const tag = configXml.match(tagPattern)?.[0];
  return tag?.match(new RegExp(`\\b${name}="([^"]+)"`))?.[1] ?? null;
};
const cleanHttpsUrl = (value) => {
  if (typeof value !== "string") return false;
  try {
    const url = new URL(value);
    return url.protocol === "https:"
      && !url.username
      && !url.password
      && !url.search
      && !url.hash
      && url.toString() === value;
  } catch {
    return false;
  }
};

exactKeys(manifest, [
  "contractVersion",
  "widgetId",
  "packageId",
  "applicationId",
  "checkoutApplicationId",
  "verifierUrl",
  "authorCertificateSha256",
  "partnerDistributorCertificateReady",
  "sellerTermsReviewed",
  "dpiProductConfigured",
  "realTvCheckoutTested",
  "ready"
], "manifest");

if (manifest.contractVersion !== "1.0") fail("unexpected contractVersion");
if (!cleanHttpsUrl(manifest.widgetId)) fail("widgetId must be an exact clean HTTPS URL");
if (!/^[A-Za-z][A-Za-z0-9]{9}$/.test(manifest.packageId)) {
  fail("packageId must be the exact 10-character alphanumeric Tizen package identity");
}
if (!new RegExp(`^${manifest.packageId}\\.[A-Za-z0-9][A-Za-z0-9._-]{0,51}$`).test(manifest.applicationId)) {
  fail("applicationId must use the 10-byte package ID plus a 1-to-52-byte ASCII application name");
}
if (manifest.checkoutApplicationId !== null
  && !/^[A-Za-z0-9._-]{3,30}$/.test(manifest.checkoutApplicationId)) {
  fail("checkoutApplicationId is invalid");
}
if (manifest.verifierUrl !== null && !cleanHttpsUrl(manifest.verifierUrl)) {
  fail("verifierUrl must be an exact clean HTTPS URL");
}
if (manifest.authorCertificateSha256 !== null
  && !/^[A-F0-9]{64}$/.test(manifest.authorCertificateSha256)) {
  fail("authorCertificateSha256 must be an uppercase SHA-256 fingerprint without separators");
}
for (const flag of [
  "partnerDistributorCertificateReady",
  "sellerTermsReviewed",
  "dpiProductConfigured",
  "realTvCheckoutTested",
  "ready"
]) {
  if (typeof manifest[flag] !== "boolean") fail(`${flag} must be boolean`);
}

const widgetTagPattern = /<widget\b[^>]*>/;
const applicationTagPattern = /<tizen:application\b[^>]*>/;
if (xmlAttribute(widgetTagPattern, "id") !== manifest.widgetId) {
  fail("config.xml widget ID does not exactly match the distribution manifest");
}
if (xmlAttribute(applicationTagPattern, "package") !== manifest.packageId) {
  fail("config.xml package ID does not exactly match the distribution manifest");
}
if (xmlAttribute(applicationTagPattern, "id") !== manifest.applicationId) {
  fail("config.xml application ID does not exactly match the distribution manifest");
}
if (xmlAttribute(applicationTagPattern, "required_version") !== "5.0") {
  fail("config.xml must keep the Tizen 5.0 compatibility floor");
}
for (const privilege of [
  "http://tizen.org/privilege/internet",
  "http://tizen.org/privilege/tv.inputdevice",
  "http://developer.samsung.com/privilege/sso.partner",
  "http://developer.samsung.com/privilege/productinfo",
  "http://developer.samsung.com/privilege/billing"
]) {
  if (!configXml.includes(`<tizen:privilege name="${privilege}" />`)) {
    fail(`config.xml is missing required privilege ${privilege}`);
  }
}
if (!packagingScript.includes("$WEBAPIS/webapis/webapis.js")) {
  fail("Samsung package assembly does not inject the required Product API runtime");
}

if (manifest.ready) {
  if (!manifest.checkoutApplicationId) fail("ready distribution needs the Seller Office Checkout app ID");
  if (!manifest.verifierUrl) fail("ready distribution needs the HTTPS DPI verifier URL");
  if (!manifest.authorCertificateSha256) fail("ready distribution needs the original author-certificate fingerprint");
  for (const flag of [
    "partnerDistributorCertificateReady",
    "sellerTermsReviewed",
    "dpiProductConfigured",
    "realTvCheckoutTested"
  ]) {
    if (!manifest[flag]) fail(`ready distribution requires ${flag}`);
  }
}

const serialized = JSON.stringify(manifest).toLowerCase();
for (const forbidden of [
  "password",
  "private-key",
  "securitykey",
  "checkvalue",
  "certificate-base64",
  "distributor-certificate"
]) {
  if (serialized.includes(forbidden)) fail(`manifest contains forbidden secret field ${forbidden}`);
}

if (process.argv.includes("--require-ready")) {
  if (!manifest.ready) {
    fail("Samsung store distribution is intentionally locked; finish Seller Office, DPI, real-TV purchase testing, and signing continuity first");
  }
  console.log("Samsung Seller Office distribution identity and prerequisites are ready.");
} else {
  console.log(
    manifest.ready
      ? "Samsung distribution manifest is ready for a protected signed candidate."
      : "Samsung distribution manifest is valid and store candidate signing remains locked by design."
  );
}

const expectations = [
  ["--expect-checkout-application-id", "checkoutApplicationId"],
  ["--expect-verifier-url", "verifierUrl"],
  ["--expect-author-certificate-sha256", "authorCertificateSha256"]
];
for (const [argument, field] of expectations) {
  const index = process.argv.indexOf(argument);
  if (index < 0) continue;
  const expected = process.argv[index + 1];
  if (!expected || manifest[field] !== expected) {
    fail(`${field} does not exactly match the release input`);
  }
  console.log(`Samsung release ${field} exactly matches the distribution manifest.`);
}
