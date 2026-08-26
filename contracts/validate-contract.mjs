import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const schema = JSON.parse(await readFile(join(here, "streamvue-catalog.schema.json"), "utf8"));
const catalog = JSON.parse(await readFile(join(here, "fixtures", "catalog.expected.json"), "utf8"));
const playlist = await readFile(join(here, "fixtures", "iptv-features.m3u"), "utf8");

const fail = (message) => {
  throw new Error(`Contract validation failed: ${message}`);
};

if (schema.$schema !== "https://json-schema.org/draft/2020-12/schema") {
  fail("the schema must remain on JSON Schema Draft 2020-12");
}
if (catalog.contractVersion !== "1.0") fail("unexpected contractVersion");
if (!catalog.catalogId || !catalog.displayName) fail("catalog identity is incomplete");
if (Number.isNaN(Date.parse(catalog.loadedAt))) fail("loadedAt is not an ISO date-time");
if (!Array.isArray(catalog.sources) || catalog.sources.length === 0) fail("at least one source is required");
if (!Array.isArray(catalog.channels) || catalog.channels.length === 0) fail("at least one channel is required");

const sourceIds = new Set(catalog.sources.map((source) => source.id));
const channelIds = new Set();
const allowedKinds = new Set(["live", "movie", "series", "recording", "replay"]);
const allowedSchemes = new Set(["http:", "https:", "rtsp:", "rtmp:", "udp:", "file:"]);

for (const source of catalog.sources) {
  if (!source.id || !source.name || !source.type) fail("source identity is incomplete");
  if (/[?&](?:token|password|username|auth)=/i.test(source.displayLocation ?? "")) {
    fail(`source ${source.id} leaks a credential in displayLocation`);
  }
}

for (const channel of catalog.channels) {
  if (!/^[A-F0-9]{64}$/.test(channel.id)) fail(`channel ${channel.number} has an invalid stable id`);
  if (channelIds.has(channel.id)) fail(`duplicate channel id ${channel.id}`);
  channelIds.add(channel.id);
  if (!sourceIds.has(channel.sourceId)) fail(`channel ${channel.number} refers to an unknown source`);
  if (!allowedKinds.has(channel.kind)) fail(`channel ${channel.number} has an unknown kind`);
  if (!channel.name?.trim() || !channel.group?.trim()) fail(`channel ${channel.number} is missing browse metadata`);

  let uri;
  try {
    uri = new URL(channel.stream?.uri);
  } catch {
    fail(`channel ${channel.number} has an invalid stream URI`);
  }
  if (!allowedSchemes.has(uri.protocol)) fail(`channel ${channel.number} uses unsupported scheme ${uri.protocol}`);
  if (!playlist.includes(channel.name) || !playlist.includes(channel.stream.uri)) {
    fail(`channel ${channel.number} does not match the M3U conformance fixture`);
  }
}

console.log(`StreamVue catalog contract 1.0 is valid (${catalog.channels.length} fixture channels).`);
