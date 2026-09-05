import fs from "node:fs";
import path from "node:path";

const sourcePath = path.resolve("src/OrbitalVue.Player/Services/EpgSourceService.cs");
const source = fs.readFileSync(sourcePath, "utf8");

const required = [
  ["actual gzip signature sniff", "prefix[0] == 0x1F && prefix[1] == 0x8B"],
  ["header-based stream wrapper", "WrapCompressionByHeaderAsync"],
  ["HTTP automatic decompression remains enabled", "AutomaticDecompression = DecompressionMethods.All"],
  ["gzip URL is not used as the HTTP decompression decision", "await using var responseContent = await WrapCompressionByHeaderAsync(responseStream, cancellationToken);"],
];

for (const [label, needle] of required) {
  if (!source.includes(needle)) {
    console.error(`EPG gzip contract failed: missing ${label}.`);
    process.exit(1);
  }
}

const legacyHttpDecision = /uri\.AbsolutePath\.EndsWith\("\.gz"[\s\S]{0,220}new GZipStream\(responseStream/;
if (legacyHttpDecision.test(source)) {
  console.error("EPG gzip contract failed: HTTP content is still decompressed from the URL suffix instead of the actual bytes.");
  process.exit(1);
}

console.log("EPG gzip contract OK: HTTP XMLTV content is decompressed only when the received stream is actually gzip encoded.");
