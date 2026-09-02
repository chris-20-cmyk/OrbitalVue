import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const read = (path) => readFile(join(repositoryRoot, path), "utf8");
const fail = (message) => {
  throw new Error(`Windows media-center artwork contract failed: ${message}`);
};
const requireFragments = (source, fragments, label) => {
  for (const fragment of fragments) {
    if (!source.includes(fragment)) fail(`${label} is missing ${fragment}`);
  }
};

const [security, catalog, artwork, channel, windowArtwork, xaml, probe, privacyInventory, privacyDisclosure] = await Promise.all([
  read("src/OrbitalVue.Player/Services/MediaCenterSecurity.cs"),
  read("src/OrbitalVue.Player/Services/MediaCenterSourceService.cs"),
  read("src/OrbitalVue.Player/Services/MediaCenterSourceService.Artwork.cs"),
  read("src/OrbitalVue.Player/Models/ChannelItem.cs"),
  read("src/OrbitalVue.Player/MainWindow.Artwork.cs"),
  read("src/OrbitalVue.Player/MainWindow.xaml"),
  read("tools/OrbitalVue.FeatureProbe/Program.cs"),
  read("store/privacy-data-inventory.json"),
  read("docs/privacy-and-store-disclosures.md")
]);

requireFragments(security, [
  '"orbitalvue-artwork://',
  "BuildArtworkLocator",
  "ParseArtworkLocator",
  "IsArtworkLocator",
  "The media-center artwork address is not canonical.",
  "RequireIdentifier(parts[2], \"media-center artwork version\")"
], "canonical token-free artwork locator");

requireFragments(catalog, [
  'ReadString(element, "thumb")',
  'ReadObject(element, "ImageTags")',
  'ReadString(imageTags.Value, "Primary")',
  "TryReadPlexArtworkVersion",
  "MediaCenterSecurity.BuildArtworkLocator",
  "LogoUrl = item.HasArtwork"
], "Plex and Emby catalog artwork metadata");

requireFragments(artwork, [
  "MaximumArtworkResponseBytes = 8 * 1024 * 1024",
  "ArtworkIdentityLifetime = TimeSpan.FromMinutes(5)",
  "new(4, 4)",
  "ParseArtworkLocator",
  "TryLoadByServerAsync(locator.Provider, locator.ServerId",
  "AssertCredentialBinding",
  "EnsureArtworkServerIdentityAsync",
  "ProbePlexIdentityAsync",
  "ProbeEmbyIdentityAsync",
  '"/photo/:/transcode"',
  '"/Items/{Uri.EscapeDataString(locator.ItemId)}/Images/Primary"',
  'headers["Accept"] = "image/*"',
  'embyHeaders["Accept"] = "image/*"',
  "PlexHeaders(credential.AccessToken)",
  "EmbyHeaders(credential.AccessToken, credential.Binding.UserId)",
  'contentType.StartsWith("image/"',
  "response.Content.Headers.ContentLength > MaximumArtworkResponseBytes",
  "CancelAllArtworkRequests",
  "CancelArtworkRequestsForSource"
], "provider-bound artwork transport");
if (artwork.includes("AddCredentialQuery") || /[?&](X-Plex-Token|api_key)=/i.test(artwork)) {
  fail("artwork transport materializes a provider token in a URL");
}

requireFragments(channel, [
  "[JsonIgnore]",
  "public ImageSource? ArtworkSource",
  'IsProtectedMedia && IsPlayed',
  '? "WATCHED"',
  '? "RESUME"'
], "token-free channel presentation model");

requireFragments(windowArtwork, [
  "MaximumRetainedArtworkItems = 160",
  "new(4, 4)",
  "LoadArtworkAsync(locator, 320",
  "BitmapCacheOption.OnLoad",
  "DecodePixelWidth = 320",
  "image.Freeze()",
  "Artwork is optional; initials remain visible",
  "ResetArtworkLoading",
  "CancelAllArtworkRequests"
], "bounded fail-open WPF artwork pipeline");
requireFragments(xaml, [
  'Source="{Binding ArtworkSource}"',
  'Loaded="ChannelArtwork_Loaded"',
  'x:Name="InspectorArtwork"'
], "virtualized library and inspector artwork UI");

requireFragments(probe, [
  "A locked premium artwork request reached the network.",
  "Plex protected artwork bytes did not round-trip.",
  "Emby protected artwork bytes did not round-trip.",
  "Plex artwork was not fetched with bounded, header-only authentication.",
  "Emby artwork was not fetched with bounded, header-only authentication.",
  "A non-image media-center artwork response was accepted.",
  "An oversized media-center artwork response was accepted.",
  "A forged media-center artwork locator reached the network.",
  "Deleting a media-center credential left its artwork request active.",
  "Protected media-center cache metadata did not round-trip."
], "feature-probe artwork security coverage");

const privacy = JSON.parse(privacyInventory);
const providerFlow = privacy.dataFlows?.find(({ id }) => id === "provider-connections");
if (!providerFlow?.data?.includes("media-library-metadata-and-artwork") ||
    !providerFlow.transmittedTo?.includes("user-selected-iptv-plex-or-emby-provider") ||
    providerFlow.developerAccess !== "none") {
  fail("the privacy inventory does not cover provider library metadata and artwork");
}
requireFragments(privacyDisclosure, [
  "library metadata and artwork thumbnails",
  "go only to that selected server"
], "plain-language artwork privacy disclosure");

console.log("Windows Plex/Emby artwork is structurally valid, identity-bound, header-authenticated, bounded, cancellable, and fail-open.");
