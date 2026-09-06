import fs from "node:fs";
import path from "node:path";

const sourcePath = path.resolve("src/OrbitalVue.Player/Services/EpgSourceService.cs");
const parserPath = path.resolve("src/OrbitalVue.Player/Services/XmlTvParser.cs");
const source = fs.readFileSync(sourcePath, "utf8");
const parser = fs.readFileSync(parserPath, "utf8");

const required = [
  ["actual gzip signature sniff", "prefix[0] == 0x1F && prefix[1] == 0x8B"],
  ["header-based stream wrapper", "WrapCompressionByHeaderAsync"],
  ["HTTP automatic decompression remains enabled", "AutomaticDecompression = DecompressionMethods.All"],
  ["gzip URL is not used as the HTTP decompression decision", "await using var responseContent = await WrapCompressionByHeaderAsync(responseStream, cancellationToken);"],
  ["resilient merged US fallback", "https://vcicio.github.io/US-EPG/merged_epg.xml.gz"],
  ["US fallback routing", "ShouldTryUsFallback"],
];

for (const [label, needle] of required) {
  if (!source.includes(needle)) {
    console.error(`EPG runtime contract failed: missing ${label}.`);
    process.exit(1);
  }
}

const parserRequired = [
  ["valid unmatched XMLTV remains usable", "Guide downloaded — no automatic channel matches yet"],
  ["channel catalog is retained for manual mapping", "channelCatalog"],
  ["stale feeds still fail closed", "The feed may be stale"],
  ["empty XMLTV still fails closed", "did not contain usable XMLTV channel/programme data"],
];
for (const [label, needle] of parserRequired) {
  if (!parser.includes(needle)) {
    console.error(`EPG runtime contract failed: missing ${label}.`);
    process.exit(1);
  }
}

const legacyHttpDecision = /uri\.AbsolutePath\.EndsWith\("\.gz"[\s\S]{0,220}new GZipStream\(responseStream/;
if (legacyHttpDecision.test(source)) {
  console.error("EPG runtime contract failed: HTTP content is still decompressed from the URL suffix instead of the actual bytes.");
  process.exit(1);
}

if (/sorted\.Count == 0\)\s*throw new InvalidDataException/.test(parser)) {
  console.error("EPG runtime contract failed: a valid XMLTV catalog still becomes a hard failure when automatic matching returns zero channels.");
  process.exit(1);
}

console.log("EPG runtime contract OK: byte-sniffed gzip, resilient US fallback, stale-feed rejection, and manual-mapping recovery are protected.");
