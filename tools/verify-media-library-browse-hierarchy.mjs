import fs from "node:fs";
import path from "node:path";

const channelPath = path.resolve("src/OrbitalVue.Player/Models/ChannelItem.cs");
const policyPath = path.resolve("src/OrbitalVue.Player/Services/MediaLibraryBrowsePolicy.cs");
const windowPath = path.resolve("src/OrbitalVue.Player/MainWindow.LibraryBrowse.cs");

const channel = fs.readFileSync(channelPath, "utf8");
const policy = fs.readFileSync(policyPath, "utf8");
const window = fs.readFileSync(windowPath, "utf8");

const required = [
  ["music browse mode", policy, "MediaLibraryBrowseMode.Music => item.Kind == ChannelKind.Music"],
  ["series browse group", channel, "public string SeriesBrowseGroup"],
  ["episode label", channel, "public string? SeriesEpisodeLabel"],
  ["series grouping", window, "nameof(ChannelItem.SeriesBrowseGroup)"],
  ["season ordering", window, "nameof(ChannelItem.SeasonNumber)"],
  ["episode ordering", window, "nameof(ChannelItem.EpisodeNumber)"],
  ["music filter", window, "Content = \"Music\""],
];

for (const [label, source, needle] of required) {
  if (!source.includes(needle)) {
    console.error(`Media-library browse contract failed: missing ${label}.`);
    process.exit(1);
  }
}

if (!channel.includes("group:{_group.Trim().ToUpperInvariant()}")) {
  console.error("Media-library browse contract failed: stable identity no longer uses the persisted raw group.");
  process.exit(1);
}

console.log("Media-library browse contract OK: libraries stay stable while Series drills down by show/episode and Music has a dedicated filter.");
