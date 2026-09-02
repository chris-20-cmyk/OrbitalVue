import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const read = (path) => readFile(join(repositoryRoot, path), "utf8");
const fail = (message) => {
  throw new Error(`Windows media-center progress contract failed: ${message}`);
};
const requireFragments = (source, fragments, label) => {
  for (const fragment of fragments) {
    if (!source.includes(fragment)) fail(`${label} is missing ${fragment}`);
  }
};

const [models, reporting, resolver, windowCode, probe, privacyInventory, privacyDisclosure] = await Promise.all([
  read("src/OrbitalVue.Player/Models/MediaCenterModels.cs"),
  read("src/OrbitalVue.Player/Services/MediaCenterSourceService.PlaybackReporting.cs"),
  read("src/OrbitalVue.Player/Services/MediaCenterSourceService.cs"),
  read("src/OrbitalVue.Player/MainWindow.xaml.cs"),
  read("tools/OrbitalVue.FeatureProbe/Program.cs"),
  read("store/privacy-data-inventory.json"),
  read("docs/privacy-and-store-disclosures.md")
]);

requireFragments(models, [
  "string? ReportingSessionId = null",
  "public enum MediaCenterPlaybackState",
  "Playing,",
  "Paused,",
  "Stopped"
], "token-free public playback state");
if (/ReportingSessionId[^;\n]*(Token|Credential|Password)/i.test(models)) {
  fail("the public reporting handle carries credential-shaped state");
}

requireFragments(resolver, [
  "RegisterPlaybackReportingSession(locator, credential, \"direct-play\")",
  "playSessionId,",
  "sourceId)",
  "ReportingSessionId: reportingSessionId"
], "just-in-time reporting-session creation");

requireFragments(reporting, [
  "MaximumPlaybackReportingSessions = 8",
  "PlaybackReportingSessionLifetime = TimeSpan.FromHours(18)",
  "Guid.NewGuid().ToString(\"N\")",
  "Interlocked.Increment(ref session.NextSequence)",
  "Enum.IsDefined(state)",
  "session.SerialGate.WaitAsync",
  "sequence <= session.LastAppliedSequence",
  "MediaCenterSecurity.AssertCredentialBinding",
  "X-Plex-Session-Identifier",
  "HttpMethod.Post",
  '"/:/timeline"',
  '("ratingKey", session.Locator.ItemId)',
  '("state", state.ToString().ToLowerInvariant())',
  '"/Sessions/Playing"',
  '"/Sessions/Playing/Progress"',
  '"/Sessions/Playing/Stopped"',
  "PlaySessionId = playSessionId",
  "MediaSourceId = mediaSourceId",
  "PositionTicks = checked(positionMilliseconds * 10_000)",
  '"TimeUpdate"',
  '"Pause"',
  '"Unpause"',
  "CancelAllPlaybackReportingSessions",
  "CancelPlaybackReportingSessionsForSource",
  "session.LifetimeCancellation.Cancel()",
  "CreateLinkedTokenSource"
], "provider-bound reporting service");

requireFragments(windowCode, [
  "TimeSpan.FromSeconds(10)",
  "QueueMediaPlaybackReport(MediaCenterPlaybackState.Paused, force: true)",
  "QueueMediaPlaybackReport(CurrentMediaCenterPlaybackState()",
  "EndCurrentMediaPlaybackReporting",
  "StopPlaybackReportingAsync",
  "Reporting is best-effort and must never interrupt local playback.",
  "Plex/Emby progress synchronization fails open so video keeps playing.",
  "_mediaCenterSource.CancelAllPlaybackReportingSessions()"
], "WPF play, pause, seek, stop, shutdown, and revocation lifecycle");
const publicUiState = windowCode.match(/private string\? _mediaPlaybackReportingSessionId;[\s\S]*?private DateTimeOffset _lastMediaPlaybackReportUtc[^;]+;/)?.[0] ?? "";
if (/token|credential|password/i.test(publicUiState)) {
  fail("the WPF reporting state contains credential-shaped data");
}

requireFragments(probe, [
  "An unknown reporting session reached the media-server network.",
  "An invalid playback state reached the media-server network.",
  "Premium revocation emitted a protected playback report.",
  "Plex timeline reporting was not bound to the protected playback session.",
  "Plex timeline reporting did not preserve state, item, and secret-isolation semantics.",
  "Emby playback check-ins did not follow the start/progress/stop lifecycle.",
  "An Emby secret leaked into playback progress metadata.",
  "Emby progress check-ins did not identify time, pause, and unpause events.",
  "A completed playback session emitted duplicate stop check-ins.",
  "A deleted media-center source emitted a playback report."
], "feature-probe lifecycle and credential-isolation coverage");

const privacy = JSON.parse(privacyInventory);
const providerFlow = privacy.dataFlows?.find(({ id }) => id === "provider-connections");
if (!providerFlow?.data?.includes("media-item-identifier-and-playback-progress") ||
    !providerFlow.transmittedTo?.includes("user-selected-iptv-plex-or-emby-provider") ||
    providerFlow.developerAccess !== "none") {
  fail("the privacy inventory does not disclose direct provider playback-progress synchronization");
}
requireFragments(privacyDisclosure, [
  "media item identifier, position, and play/pause/stop state",
  "directly to that selected server"
], "plain-language privacy disclosure");

console.log("Windows Plex/Emby watch-progress synchronization is structurally valid, provider-bound, serialized, revocable, and fail-open.");
