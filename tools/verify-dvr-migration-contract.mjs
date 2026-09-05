import fs from "node:fs";

const source = fs.readFileSync("src/OrbitalVue.Player/Services/DvrRecordingService.cs", "utf8");

const required = [
  ["OrbitalVue default recordings folder", '"OrbitalVue Recordings"'],
  ["StreamVue legacy recordings folder", '"StreamVue Recordings"'],
  ["legacy fallback condition", "Directory.Exists(LegacyDefaultRecordingsFolder) && !Directory.Exists(DefaultRecordingsFolder)"],
];

for (const [label, needle] of required) {
  if (!source.includes(needle)) {
    console.error(`DVR migration contract failed: missing ${label}.`);
    process.exit(1);
  }
}

if ((source.match(/"OrbitalVue Recordings"/g) ?? []).length !== 1) {
  console.error("DVR migration contract failed: the legacy folder appears to have regressed to the OrbitalVue folder.");
  process.exit(1);
}

console.log("DVR migration contract OK: new recordings use OrbitalVue while an existing StreamVue folder remains discoverable.");
