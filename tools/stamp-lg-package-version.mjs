import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

const projectIndex = process.argv.indexOf("--project");
const versionIndex = process.argv.indexOf("--version");
const project = projectIndex >= 0 ? process.argv[projectIndex + 1] : null;
const version = versionIndex >= 0 ? process.argv[versionIndex + 1] : null;
const validateOnly = process.argv.includes("--validate-only");

const fail = (message) => {
  throw new Error(`LG package version failed: ${message}`);
};
if (!version || (!validateOnly && !project)) {
  fail("use --version <major.minor.patch> with --validate-only or --project <directory>");
}
if (!/^(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})$/.test(version)) {
  fail("version must contain three decimal components, at most nine digits each, without leading zeroes");
}
if (validateOnly) {
  console.log(`LG package version ${version} is valid.`);
  process.exit(0);
}

const appInfoPath = resolve(project, "appinfo.json");
const source = await readFile(appInfoPath, "utf8");
const appInfo = JSON.parse(source);
if (!appInfo || typeof appInfo !== "object" || Array.isArray(appInfo)) {
  fail("appinfo.json must contain one JSON object");
}
if (typeof appInfo.version !== "string") fail("appinfo.json version is missing");
appInfo.version = version;
await writeFile(appInfoPath, `${JSON.stringify(appInfo, null, 2)}\n`, "utf8");
console.log(`Stamped LG package version ${version} in ${appInfoPath}.`);
