import fs from "node:fs";

const channelItem = fs.readFileSync("src/OrbitalVue.Player/Models/ChannelItem.cs", "utf8");
const cacheStore = fs.readFileSync("src/OrbitalVue.Player/Services/PlaylistCacheStore.cs", "utf8");

const channelRequirements = [
  ["source/library display grouping", '$"{SourceName} • {MediaLibraryTitle}"'],
  ["raw persisted group", "public string PersistedGroup"],
  ["stable identity remains based on the raw group", "group:{_group.Trim().ToUpperInvariant()}"],
];

for (const [label, needle] of channelRequirements) {
  if (!channelItem.includes(needle)) {
    console.error(`Media-library grouping contract failed: missing ${label}.`);
    process.exit(1);
  }
}

const cacheRequirements = [
  ["raw group cache persistence", "Group = channel.PersistedGroup"],
  ["source-name cache persistence", "SourceName = channel.SourceName"],
  ["library-title cache persistence", "MediaLibraryTitle = channel.MediaLibraryTitle"],
  ["series-title cache persistence", "SeriesTitle = channel.SeriesTitle"],
];

for (const [label, needle] of cacheRequirements) {
  if (!cacheStore.includes(needle)) {
    console.error(`Media-library grouping contract failed: missing ${label}.`);
    process.exit(1);
  }
}

console.log("Media-library grouping contract OK: media centers browse by source/library while preserving raw identity and hierarchy in cache.");
