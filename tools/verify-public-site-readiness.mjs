import { readFile } from "node:fs/promises";
import { dirname, join, normalize, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const readText = (path) => readFile(join(repositoryRoot, path), "utf8");
const readJSON = async (path) => JSON.parse(await readText(path));
const fail = (message) => {
  throw new Error(`Public site readiness failed: ${message}`);
};
const exactKeys = (value, expected, label) => {
  if (!value || typeof value !== "object" || Array.isArray(value)) fail(`${label} is missing`);
  const actual = Object.keys(value).sort();
  const wanted = [...expected].sort();
  if (JSON.stringify(actual) !== JSON.stringify(wanted)) {
    fail(`${label} must contain exactly: ${wanted.join(", ")}`);
  }
};
const validPublicUrl = (value) => {
  if (typeof value !== "string") return false;
  try {
    const url = new URL(value);
    return url.protocol === "https:"
      && !url.username
      && !url.password
      && !url.hash
      && url.hostname !== "localhost"
      && url.hostname !== "127.0.0.1";
  } catch {
    return false;
  }
};
const validEmail = (value) => typeof value === "string"
  && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);

const args = process.argv.slice(2);
if (args.some((arg) => arg !== "--require-ready") || args.filter((arg) => arg === "--require-ready").length > 1) {
  fail("usage: node tools/verify-public-site-readiness.mjs [--require-ready]");
}
const requireReady = args.includes("--require-ready");

const [site, privacy] = await Promise.all([
  readJSON("store/public-site-readiness.json"),
  readJSON("store/privacy-data-inventory.json")
]);

exactKeys(site, [
  "contractVersion",
  "status",
  "canonicalBaseUrl",
  "urls",
  "projectUrl",
  "supportRequestUrl",
  "owner",
  "pages",
  "deployment",
  "ready"
], "public site manifest");
if (site.contractVersion !== "1.0") fail("unexpected contractVersion");
if (!["draft-owner-input-required", "published-owner-approved"].includes(site.status)) {
  fail("status must identify a draft or an owner-approved published site");
}
exactKeys(site.urls, ["overview", "privacy", "support"], "public site URLs");
exactKeys(site.owner, ["legalName", "privacyContact", "copyrightHolder", "approved"], "public site owner");
exactKeys(site.pages, ["overview", "privacy", "support", "notFound", "contentReviewed"], "public site pages");
exactKeys(site.deployment, ["provider", "customDomain", "liveUrlsVerified", "manualWorkflowOnly", "ready"], "public site deployment");

if (!validPublicUrl(site.projectUrl) || !validPublicUrl(site.supportRequestUrl)) {
  fail("projectUrl and supportRequestUrl must be public HTTPS URLs");
}
if (site.projectUrl !== "https://github.com/chris-20-cmyk/StreamVue"
  || site.supportRequestUrl !== `${site.projectUrl}/issues/new`) {
  fail("project and support-request URLs must remain on the reviewed repository until its separate rename is approved");
}
if (site.canonicalBaseUrl !== null && !validPublicUrl(site.canonicalBaseUrl)) {
  fail("canonicalBaseUrl must be null or a public HTTPS URL");
}
for (const [name, value] of Object.entries(site.urls)) {
  if (value !== null && !validPublicUrl(value)) fail(`urls.${name} must be null or a public HTTPS URL`);
}
for (const field of ["legalName", "copyrightHolder"]) {
  if (site.owner[field] !== null && (typeof site.owner[field] !== "string" || !site.owner[field].trim())) {
    fail(`owner.${field} must be null or a non-empty string`);
  }
}
if (site.owner.privacyContact !== null && !validEmail(site.owner.privacyContact)) {
  fail("owner.privacyContact must be null or a valid email address");
}
if (typeof site.owner.approved !== "boolean" || typeof site.pages.contentReviewed !== "boolean") {
  fail("owner approval and page content review must be boolean");
}
if (site.deployment.provider !== "github-pages") fail("the reviewed deployment provider must remain github-pages");
if (site.deployment.customDomain !== null && !validPublicUrl(`https://${site.deployment.customDomain}`)) {
  fail("deployment.customDomain must be null or a valid public host name");
}
for (const field of ["liveUrlsVerified", "manualWorkflowOnly", "ready"]) {
  if (typeof site.deployment[field] !== "boolean") fail(`deployment.${field} must be boolean`);
}
if (!site.deployment.manualWorkflowOnly) fail("public site deployment must remain manual-only");
if (typeof site.ready !== "boolean") fail("ready must be boolean");

const expectedPages = {
  overview: "site/index.html",
  privacy: "site/privacy.html",
  support: "site/support.html",
  notFound: "site/404.html"
};
const pageSources = {};
for (const [name, expectedPath] of Object.entries(expectedPages)) {
  if (site.pages[name] !== expectedPath) fail(`pages.${name} must remain ${expectedPath}`);
  const absolutePath = resolve(repositoryRoot, site.pages[name]);
  if (!absolutePath.startsWith(`${resolve(repositoryRoot)}\\`) && !absolutePath.startsWith(`${resolve(repositoryRoot)}/`)) {
    fail(`pages.${name} resolves outside the repository`);
  }
  pageSources[name] = await readFile(absolutePath, "utf8");
}
const css = await readText("site/assets/site.css");
const javascript = await readText("site/assets/site.js");
const allSiteText = Object.values(pageSources).join("\n") + css + javascript;

for (const [name, html] of Object.entries(pageSources)) {
  for (const fragment of ["<!doctype html>", "<meta name=\"viewport\"", "assets/site.css"]) {
    if (!html.toLowerCase().includes(fragment.toLowerCase())) fail(`${name} is missing ${fragment}`);
  }
  if (name !== "notFound") {
    for (const fragment of ["class=\"skip-link\"", "<main id=\"main\"", "aria-label=\"Main navigation\""]) {
      if (!html.includes(fragment)) fail(`${name} is missing accessibility control ${fragment}`);
    }
  }
  for (const match of html.matchAll(/\bhref="([^"]+)"/g)) {
    const target = match[1];
    if (/^(?:javascript|data):/i.test(target)) fail(`${name} contains an unsafe link protocol`);
    if (/^https?:/i.test(target) && !validPublicUrl(target)) fail(`${name} contains a non-public or non-HTTPS external link`);
    if (!/^(?:https:|#)/i.test(target)) {
      const localTarget = normalize(join(dirname(site.pages[name]), target.split("#")[0]));
      if (target && !localTarget.startsWith("site")) fail(`${name} contains a link outside site/`);
    }
  }
}

for (const forbidden of [
  /\blorem ipsum\b/i,
  /\bTODO\b/,
  /support@streamvue\.app/i,
  /privacy@streamvue\.app/i,
  /\bAIza[0-9A-Za-z_-]{30,}\b/,
  /\b(?:password|access[_-]?token)\s*[=:]\s*["'][^"']+["']/i
]) {
  if (forbidden.test(allSiteText)) fail(`site contains forbidden placeholder or secret-like text: ${forbidden}`);
}

for (const fragment of [
  "No OrbitalVue account",
  "advertising",
  "cross-app tracking",
  "automatic telemetry",
  "The OrbitalVue developer does not receive those provider credentials",
  "The developer receives diagnostic information only if you choose to share that export",
  "OrbitalVue does not provide channels or media"
]) {
  if (!pageSources.privacy.includes(fragment) && !pageSources.overview.includes(fragment)) {
    fail(`reviewed privacy copy is missing: ${fragment}`);
  }
}
for (const fragment of [
  "Never post playlist URLs",
  "Open a public issue",
  "OrbitalVue does not provide channels or media"
]) {
  if (!pageSources.support.includes(fragment)) fail(`reviewed support copy is missing: ${fragment}`);
}

const draftNotice = "Draft for owner review — not yet a published Store policy.";
const calculatedDeploymentReady = site.deployment.liveUrlsVerified
  && validPublicUrl(site.canonicalBaseUrl)
  && Object.values(site.urls).every(validPublicUrl);
if (site.deployment.ready !== calculatedDeploymentReady) {
  fail("deployment.ready must exactly match the canonical and live-URL verification gates");
}
const calculatedReady = site.status === "published-owner-approved"
  && site.owner.approved
  && site.pages.contentReviewed
  && typeof site.owner.legalName === "string"
  && validEmail(site.owner.privacyContact)
  && typeof site.owner.copyrightHolder === "string"
  && site.deployment.ready;
if (site.ready !== calculatedReady) fail("ready must exactly match owner, content, and deployment gates");

if (!site.ready) {
  if (!pageSources.privacy.includes(draftNotice)) fail("unapproved privacy content must keep the visible draft notice");
  if (!pageSources.support.includes("Draft support center")) fail("unapproved support content must keep the visible draft notice");
} else {
  if (pageSources.privacy.includes(draftNotice) || pageSources.support.includes("Draft support center")) {
    fail("an approved public site cannot still present itself as a draft");
  }
  const base = site.canonicalBaseUrl.replace(/\/$/, "");
  const expectedUrls = {
    overview: `${base}/`,
    privacy: `${base}/privacy.html`,
    support: `${base}/support.html`
  };
  for (const [name, expectedUrl] of Object.entries(expectedUrls)) {
    if (site.urls[name] !== expectedUrl) fail(`urls.${name} must be ${expectedUrl}`);
  }
  if (privacy.privacyContact !== site.owner.privacyContact) fail("published privacy contacts must match");
  for (const [platform, entry] of Object.entries(privacy.platforms)) {
    if (entry.privacyPolicyUrl !== site.urls.privacy || entry.supportUrl !== site.urls.support) {
      fail(`${platform} privacy and support URLs must match the verified public site`);
    }
  }
}

if (requireReady && !site.ready) {
  fail("the public site is intentionally locked; add verified owner identity/contact, approve the content, publish by a manual workflow, verify every live URL, and update the Store privacy manifest first");
}

console.log(`Public site is structurally valid and ${site.ready ? "approved for Store use" : "safely retained as an unpublished owner-review draft"}.`);
