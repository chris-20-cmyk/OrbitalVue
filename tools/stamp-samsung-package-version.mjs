import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

const projectIndex = process.argv.indexOf("--project");
const versionIndex = process.argv.indexOf("--version");
const project = projectIndex >= 0 ? process.argv[projectIndex + 1] : null;
const version = versionIndex >= 0 ? process.argv[versionIndex + 1] : null;
const validateOnly = process.argv.includes("--validate-only");

const fail = (message) => {
  throw new Error(`Samsung package version failed: ${message}`);
};
if (!version || (!validateOnly && !project)) {
  fail("use --version <major.minor.patch> with --validate-only or --project <directory>");
}

const match = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.exec(version);
if (!match) fail("version must contain three decimal components without leading zeroes");
const components = match.slice(1).map(Number);
if (components[0] > 255 || components[1] > 255 || components[2] > 65535) {
  fail("version exceeds Tizen's 255.255.65535 component limits");
}
if (validateOnly) {
  console.log(`Samsung package version ${version} is valid.`);
  process.exit(0);
}

const configPath = resolve(project, "config.xml");
const source = await readFile(configPath, "utf8");
const widgetTags = source.match(/<widget\b[^>]*>/g) ?? [];
if (widgetTags.length !== 1) fail("config.xml must contain exactly one widget tag");
const currentVersion = widgetTags[0].match(/\bversion="([^"]+)"/)?.[1];
if (!currentVersion) fail("config.xml widget version is missing");
const updatedTag = widgetTags[0].replace(`version="${currentVersion}"`, `version="${version}"`);
const updated = source.replace(widgetTags[0], updatedTag);
// Counted literally rather than through a constructed RegExp: escaping only "." left every other
// metacharacter live, so the pattern depended on --version staying well-formed to stay a literal.
const stamped = `version="${version}"`;
if (updated.split(stamped).length - 1 !== 1) {
  fail("could not stamp the package version exactly once");
}
await writeFile(configPath, updated, "utf8");
console.log(`Stamped Samsung package version ${version} in ${configPath}.`);
