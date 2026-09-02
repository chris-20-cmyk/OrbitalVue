import {
  createCatalogFromM3u,
  sha256Hex,
  type CatalogChannel,
  type SourceType,
  type StreamVueCatalog
} from "@streamvue/catalog";
import {
  EmbyClient,
  PlexClient,
  assertMediaCenterCredentialBinding,
  authenticateEmby,
  createFetchTransport,
  createMediaCenterCatalog,
  createMediaCenterConnection,
  createMediaCenterCredentialBinding,
  normalizeMediaCenterBaseUrl,
  parseMediaCenterPlaybackUri,
  probePlexServerIdentity,
  type MediaCenterConnection,
  type MediaCenterHttpTransport,
  type MediaCenterItem,
  type MediaCenterPlaybackPlan,
  type MediaCenterPlaybackReport,
  type MediaCenterSnapshot,
  type MediaCenterProvider
} from "@streamvue/media-centers";
import {
  CatalogCache,
  type CatalogStore,
  type SavedCatalogRecord
} from "./CatalogCache.js";
import {
  createTelevisionCredentialVault,
  type ProtectedMediaCredential,
  type TelevisionCredentialVault
} from "./CredentialVault.js";
import { createDemoCatalog } from "./demoCatalog.js";
import {
  currentPremiumAccess,
  requireMediaCenterAccess,
  type PremiumAccessSnapshot
} from "../premium/PremiumAccess.js";

const MAX_PLAYLIST_BYTES = 64 * 1024 * 1024;
const DOWNLOAD_TIMEOUT_MS = 30_000;
const MEDIA_CENTER_PAGE_SIZE = 200;
const MAX_MEDIA_CENTER_ITEMS = 20_000;
const MEDIA_CENTER_VERSION = "5.7.0";

export interface CatalogLoadResult {
  catalog: StreamVueCatalog;
  notice: string | null;
  refreshed: boolean;
}

export interface PlexConnectRequest {
  serverAddress: string;
  accessToken: string;
  displayName?: string;
  allowInsecureHttp: boolean;
}

export interface EmbyConnectRequest {
  serverAddress: string;
  username: string;
  password: string;
  displayName?: string;
  allowInsecureHttp: boolean;
}

export interface ResolvedTelevisionPlayback {
  channel: CatalogChannel;
  startPositionMs: number;
  method: MediaCenterPlaybackPlan["method"] | "source";
}

type MediaCenterClient = Pick<PlexClient, "getLibraries" | "getItems">
  | Pick<EmbyClient, "getLibraries" | "getItems">;
type MediaCenterReportingClient = Pick<PlexClient, "reportPlayback">
  | Pick<EmbyClient, "reportPlayback">;

export class CatalogRepository {
  private readonly premiumAccessProvider: () => PremiumAccessSnapshot;
  private activeMediaPlayback: {
    client: MediaCenterReportingClient;
    plan: MediaCenterPlaybackPlan;
  } | null = null;

  constructor(
    private readonly cache: CatalogStore = new CatalogCache(),
    private readonly credentialVault: TelevisionCredentialVault = createTelevisionCredentialVault(),
    private readonly mediaTransport: MediaCenterHttpTransport = createFetchTransport(),
    premiumAccess: PremiumAccessSnapshot | (() => PremiumAccessSnapshot) = currentPremiumAccess()
  ) {
    this.premiumAccessProvider = typeof premiumAccess === "function"
      ? premiumAccess
      : () => premiumAccess;
  }

  get credentialSecurityLabel(): string {
    return this.credentialVault.securityLabel;
  }

  async loadSaved(): Promise<CatalogLoadResult | null> {
    const saved = await this.cache.read();
    if (!saved) return null;
    if (saved.sourceKind === "media-center" || saved.mediaCenterSnapshot) {
      this.requireMediaCenterAccess();
      return this.loadSavedMediaCenter(saved);
    }
    if (!saved.sourceUrl) return { catalog: saved.catalog, notice: null, refreshed: false };

    try {
      const playlist = await this.download(saved.sourceUrl);
      const source = saved.catalog.sources[0];
      if (!source) throw new Error("The saved source is incomplete.");
      const catalog = this.buildCatalog(playlist, {
        catalogId: saved.catalog.catalogId,
        sourceId: source.id,
        sourceName: source.name,
        sourceType: "m3u-url",
        displayLocation: safeDisplayLocation(saved.sourceUrl),
        refreshOnLaunch: true
      });
      await this.cache.write(saved.sourceUrl, catalog);
      return { catalog, notice: "Source refreshed at launch", refreshed: true };
    } catch {
      return {
        catalog: saved.catalog,
        notice: "The source is offline. OrbitalVue kept the last working channel catalog.",
        refreshed: false
      };
    }
  }

  async refreshCurrent(): Promise<CatalogLoadResult> {
    const saved = await this.cache.read();
    if (!saved) throw new Error("Connect a source before refreshing.");
    if (saved.sourceKind === "media-center" || saved.mediaCenterSnapshot) {
      this.requireMediaCenterAccess();
      const result = await this.loadSavedMediaCenter(saved, true);
      if (!result.refreshed) throw new Error(result.notice ?? "The media library could not be refreshed.");
      return result;
    }
    if (!saved.sourceUrl) {
      return {
        catalog: saved.catalog,
        notice: "Files stay private on this television and refresh when imported again.",
        refreshed: false
      };
    }
    const playlist = await this.download(saved.sourceUrl);
    const source = saved.catalog.sources[0];
    if (!source) throw new Error("The saved source is incomplete.");
    const catalog = this.buildCatalog(playlist, {
      catalogId: saved.catalog.catalogId,
      sourceId: source.id,
      sourceName: source.name,
      sourceType: "m3u-url",
      displayLocation: safeDisplayLocation(saved.sourceUrl),
      refreshOnLaunch: true
    });
    await this.cache.write(saved.sourceUrl, catalog);
    return { catalog, notice: "Source refreshed", refreshed: true };
  }

  async connectUrl(rawValue: string): Promise<CatalogLoadResult> {
    const sourceUrl = normalizePlaylistUrl(rawValue);
    const playlist = await this.download(sourceUrl);
    const host = safeDisplayLocation(sourceUrl);
    const sourceId = `url-${sha256Hex(sourceUrl).slice(0, 24).toLowerCase()}`;
    const catalog = this.buildCatalog(playlist, {
      catalogId: sourceId,
      sourceId,
      sourceName: host,
      sourceType: "m3u-url",
      displayLocation: host,
      refreshOnLaunch: true
    });
    await this.persistPlaylist(sourceUrl, catalog);
    return { catalog, notice: "Connected securely. Startup refresh is on.", refreshed: true };
  }

  async importFile(file: File): Promise<CatalogLoadResult> {
    if (file.size > MAX_PLAYLIST_BYTES) throw new Error("The playlist is larger than the 64 MB safety limit.");
    const bytes = new Uint8Array(await file.arrayBuffer());
    const playlist = decodePlaylist(bytes);
    const sourceName = file.name.replace(/\.(m3u8?|txt)$/i, "").trim() || "Imported playlist";
    const sourceId = `file-${sha256Hex(`${file.name}|${file.size}|${file.lastModified}`).slice(0, 24).toLowerCase()}`;
    const catalog = this.buildCatalog(playlist, {
      catalogId: sourceId,
      sourceId,
      sourceName,
      sourceType: "m3u-file",
      displayLocation: file.name,
      refreshOnLaunch: false
    });
    await this.persistPlaylist(null, catalog);
    return { catalog, notice: "Playlist imported and kept privately on this television.", refreshed: false };
  }

  async connectPlex(request: PlexConnectRequest): Promise<CatalogLoadResult> {
    this.requireMediaCenterAccess();
    const baseUrl = normalizeMediaCenterBaseUrl(request.serverAddress);
    const identity = await probePlexServerIdentity(this.mediaTransport, {
      baseUrl,
      clientIdentifier: televisionDeviceId(),
      product: "OrbitalVue Television",
      version: MEDIA_CENTER_VERSION,
      allowInsecureHttp: request.allowInsecureHttp
    });
    const connection = createMediaCenterConnection({
      provider: "plex",
      serverId: identity.serverId,
      displayName: request.displayName?.trim() || identity.name,
      baseUrl,
      credentialId: credentialReference("plex", identity.serverId, baseUrl)
    });
    const credential = protectedCredential(
      connection,
      request.accessToken,
      request.allowInsecureHttp
    );
    const client = this.plexClient(connection, credential);
    const snapshot = await this.buildMediaCenterSnapshot(connection, client);
    assertMediaCenterSnapshotSafe(snapshot, [request.accessToken]);
    const catalog = createMediaCenterCatalog(connection, snapshot.libraries, snapshot.items, snapshot.loadedAt);
    await this.persistMediaCenter(snapshot, catalog, credential);
    return {
      catalog,
      notice: mediaCenterConnectedNotice("Plex", this.credentialVault),
      refreshed: true
    };
  }

  async connectEmby(request: EmbyConnectRequest): Promise<CatalogLoadResult> {
    this.requireMediaCenterAccess();
    const baseUrl = normalizeMediaCenterBaseUrl(request.serverAddress);
    const device = televisionDeviceIdentity();
    const authenticated = await authenticateEmby(this.mediaTransport, {
      baseUrl,
      username: request.username,
      password: request.password,
      device,
      allowInsecureHttp: request.allowInsecureHttp
    });
    const connection = createMediaCenterConnection({
      provider: "emby",
      serverId: authenticated.serverId,
      displayName: request.displayName?.trim() || "Emby",
      baseUrl,
      credentialId: credentialReference("emby", authenticated.serverId, baseUrl, authenticated.userId),
      userId: authenticated.userId
    });
    const credential = protectedCredential(
      connection,
      authenticated.accessToken,
      request.allowInsecureHttp
    );
    const client = this.embyClient(connection, credential);
    const snapshot = await this.buildMediaCenterSnapshot(connection, client);
    assertMediaCenterSnapshotSafe(snapshot, [request.password, authenticated.accessToken]);
    const catalog = createMediaCenterCatalog(connection, snapshot.libraries, snapshot.items, snapshot.loadedAt);
    await this.persistMediaCenter(snapshot, catalog, credential);
    return {
      catalog,
      notice: mediaCenterConnectedNotice("Emby", this.credentialVault),
      refreshed: true
    };
  }

  async resolvePlayback(channel: CatalogChannel): Promise<ResolvedTelevisionPlayback> {
    if (!channel.stream.uri.startsWith("streamvue-media://")) {
      return { channel, startPositionMs: 0, method: "source" };
    }
    this.requireMediaCenterAccess();
    const saved = await this.cache.read();
    const snapshot = saved?.mediaCenterSnapshot;
    if (!saved || !snapshot) {
      throw new Error("Reconnect this media server before playback.");
    }
    assertMediaCenterSnapshotSafe(snapshot);
    const locator = parseMediaCenterPlaybackUri(channel.stream.uri);
    if (locator.provider !== snapshot.connection.provider || locator.serverId !== snapshot.connection.serverId) {
      throw new Error("This media item belongs to a different protected server.");
    }
    const item = snapshot.items.find((candidate) => candidate.id === locator.itemId);
    if (!item) throw new Error("This media item is no longer available in the saved library.");
    const credential = await this.requireCredential(snapshot.connection);
    let plan: MediaCenterPlaybackPlan;
    let client: MediaCenterReportingClient;
    if (snapshot.connection.provider === "plex") {
      const plex = this.plexClient(snapshot.connection, credential);
      await plex.getIdentity();
      plan = plex.getPlaybackPlan(item);
      client = plex;
    } else {
      const emby = this.embyClient(snapshot.connection, credential);
      plan = await emby.getPlaybackPlan(item);
      client = emby;
    }
    this.activeMediaPlayback = { client, plan };
    return {
      channel: materializeTelevisionPlayback(channel, snapshot.connection, credential, plan),
      startPositionMs: Math.max(0, item.resumePositionMs ?? 0),
      method: plan.method
    };
  }

  async reportPlayback(report: MediaCenterPlaybackReport): Promise<void> {
    const active = this.activeMediaPlayback;
    if (!active) return;
    try {
      await active.client.reportPlayback(active.plan, report);
    } finally {
      if (report.kind === "stopped" && this.activeMediaPlayback === active) {
        this.activeMediaPlayback = null;
      }
    }
  }

  async useDemo(): Promise<CatalogLoadResult> {
    const catalog = createDemoCatalog();
    return { catalog, notice: "Demonstration catalog — connect your source to watch.", refreshed: false };
  }

  async clear(): Promise<void> {
    this.activeMediaPlayback = null;
    const saved = await this.cache.read();
    await this.cache.clear();
    const credentialId = saved?.mediaCenterSnapshot?.connection.credentialId;
    if (credentialId) await this.credentialVault.remove(credentialId);
  }

  private requireMediaCenterAccess(): void {
    requireMediaCenterAccess(this.premiumAccessProvider());
  }

  private async loadSavedMediaCenter(
    saved: SavedCatalogRecord,
    strict = false
  ): Promise<CatalogLoadResult> {
    const snapshot = saved.mediaCenterSnapshot;
    if (!snapshot) throw new Error("The saved media-center snapshot is incomplete.");
    assertMediaCenterSnapshotSafe(snapshot);
    const credential = await this.credentialVault.read(snapshot.connection.credentialId);
    if (!credential) {
      const notice = "Library restored without credentials. Reconnect this server before refreshing or playing.";
      if (strict) throw new Error(notice);
      return { catalog: saved.catalog, notice, refreshed: false };
    }
    try {
      assertMediaCenterCredentialBinding(snapshot.connection, credential.binding);
    } catch (error) {
      if (strict) throw error;
      return {
        catalog: saved.catalog,
        notice: "The protected credential no longer matches this server. Reconnect it before refreshing or playing.",
        refreshed: false
      };
    }
    try {
      const refreshed = await this.refreshMediaCenter(snapshot.connection, credential);
      const catalog = createMediaCenterCatalog(
        refreshed.connection,
        refreshed.libraries,
        refreshed.items,
        refreshed.loadedAt
      );
      await this.cache.writeMediaCenter(refreshed, catalog);
      return {
        catalog,
        notice: `${providerLabel(refreshed.connection.provider)} library refreshed at launch.`,
        refreshed: true
      };
    } catch (error) {
      if (strict) throw error;
      return {
        catalog: saved.catalog,
        notice: "The media server is unavailable. OrbitalVue kept the last token-free library snapshot.",
        refreshed: false
      };
    }
  }

  private async refreshMediaCenter(
    connection: MediaCenterConnection,
    credential: ProtectedMediaCredential
  ): Promise<MediaCenterSnapshot> {
    const client = connection.provider === "plex"
      ? this.plexClient(connection, credential)
      : this.embyClient(connection, credential);
    const snapshot = await this.buildMediaCenterSnapshot(connection, client);
    assertMediaCenterSnapshotSafe(snapshot, [credential.accessToken]);
    return snapshot;
  }

  private async buildMediaCenterSnapshot(
    connection: MediaCenterConnection,
    client: MediaCenterClient
  ): Promise<MediaCenterSnapshot> {
    const libraries = await client.getLibraries();
    const items: MediaCenterItem[] = [];
    for (const library of libraries) {
      for (let start = 0; items.length < MAX_MEDIA_CENTER_ITEMS; start += MEDIA_CENTER_PAGE_SIZE) {
        const page = await client.getItems(library, start, MEDIA_CENTER_PAGE_SIZE);
        items.push(...page.items.slice(0, MAX_MEDIA_CENTER_ITEMS - items.length));
        if (start + MEDIA_CENTER_PAGE_SIZE >= page.total) break;
      }
      if (items.length >= MAX_MEDIA_CENTER_ITEMS) break;
    }
    return {
      contractVersion: connection.contractVersion,
      loadedAt: new Date().toISOString(),
      connection,
      libraries,
      items
    };
  }

  private plexClient(
    connection: MediaCenterConnection,
    credential: ProtectedMediaCredential
  ): PlexClient {
    assertMediaCenterCredentialBinding(connection, credential.binding);
    return new PlexClient(this.mediaTransport, {
      connection,
      token: credential.accessToken,
      credentialBinding: credential.binding,
      clientIdentifier: televisionDeviceId(),
      product: "OrbitalVue Television",
      version: MEDIA_CENTER_VERSION
    });
  }

  private embyClient(
    connection: MediaCenterConnection,
    credential: ProtectedMediaCredential
  ): EmbyClient {
    assertMediaCenterCredentialBinding(connection, credential.binding);
    return new EmbyClient(this.mediaTransport, {
      connection,
      token: credential.accessToken,
      credentialBinding: credential.binding,
      device: televisionDeviceIdentity()
    });
  }

  private async requireCredential(connection: MediaCenterConnection): Promise<ProtectedMediaCredential> {
    const credential = await this.credentialVault.read(connection.credentialId);
    if (!credential) {
      throw new Error("Reconnect this media server to unlock playback on this television.");
    }
    assertMediaCenterCredentialBinding(connection, credential.binding);
    return credential;
  }

  private async persistMediaCenter(
    snapshot: MediaCenterSnapshot,
    catalog: StreamVueCatalog,
    credential: ProtectedMediaCredential
  ): Promise<void> {
    const previous = await this.cache.read();
    await this.credentialVault.write(credential);
    try {
      await this.cache.writeMediaCenter(snapshot, catalog);
    } catch (error) {
      // Roll back the credential we just wrote, but never let a rollback failure replace the
      // error that caused it: the caller needs the original cause, not a cleanup symptom.
      await this.credentialVault.remove(credential.binding.credentialId).catch(() => undefined);
      throw error;
    }
    await this.removeReplacedCredential(previous, credential.binding.credentialId);
  }

  private async persistPlaylist(sourceUrl: string | null, catalog: StreamVueCatalog): Promise<void> {
    const previous = await this.cache.read();
    await this.cache.write(sourceUrl, catalog);
    await this.removeReplacedCredential(previous);
  }

  // Best-effort cleanup of a credential the viewer has already replaced. This runs while saving a
  // new source -- including a plain playlist -- so a secure-storage failure here must not fail that
  // save: refusing to load the viewer's new playlist because a superseded token could not be
  // deleted would be a worse outcome than leaving that token in place. Deletions the viewer asks
  // for directly still surface their failures through clear().
  private async removeReplacedCredential(
    previous: SavedCatalogRecord | null,
    retainedCredentialId?: string
  ): Promise<void> {
    const previousId = previous?.mediaCenterSnapshot?.connection.credentialId;
    if (previousId && previousId !== retainedCredentialId) {
      await this.credentialVault.remove(previousId).catch(() => undefined);
    }
  }

  private buildCatalog(
    playlist: string,
    source: {
      catalogId: string;
      sourceId: string;
      sourceName: string;
      sourceType: SourceType;
      displayLocation: string;
      refreshOnLaunch: boolean;
    }
  ): StreamVueCatalog {
    return createCatalogFromM3u(playlist, {
      catalogId: source.catalogId,
      displayName: source.sourceName,
      sourceId: source.sourceId,
      sourceName: source.sourceName,
      sourceType: source.sourceType,
      displayLocation: source.displayLocation,
      refreshOnLaunch: source.refreshOnLaunch
    });
  }

  private async download(sourceUrl: string): Promise<string> {
    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), DOWNLOAD_TIMEOUT_MS);
    try {
      const response = await fetch(sourceUrl, {
        cache: "no-store",
        redirect: "follow",
        signal: controller.signal
      });
      if (!response.ok) throw new Error(`Playlist server returned HTTP ${response.status}.`);
      if (sourceUrl.toLowerCase().startsWith("https://") && response.url.toLowerCase().startsWith("http://")) {
        throw new Error("The playlist server attempted an insecure redirect.");
      }
      const announcedLength = Number.parseInt(response.headers.get("Content-Length") ?? "0", 10);
      if (announcedLength > MAX_PLAYLIST_BYTES) throw new Error("The playlist is larger than the 64 MB safety limit.");
      return decodePlaylist(await readLimitedBody(response));
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") {
        throw new Error("The playlist server did not respond within 30 seconds.");
      }
      throw error;
    } finally {
      window.clearTimeout(timeout);
    }
  }
}

export function isMediaCenterCatalog(catalog: StreamVueCatalog): boolean {
  const sourceType = catalog.sources[0]?.type;
  return sourceType === "plex" || sourceType === "emby";
}

function protectedCredential(
  connection: MediaCenterConnection,
  accessToken: string,
  allowInsecureHttp: boolean
): ProtectedMediaCredential {
  return {
    schemaVersion: 1,
    binding: createMediaCenterCredentialBinding(connection, allowInsecureHttp),
    accessToken
  };
}

function credentialReference(
  provider: MediaCenterProvider,
  serverId: string,
  baseUrl: string,
  userId = "server"
): string {
  return `mc-${provider}-${sha256Hex(`${provider}|${serverId}|${baseUrl}|${userId}`)
    .slice(0, 48)
    .toLowerCase()}`;
}

function materializeTelevisionPlayback(
  channel: CatalogChannel,
  connection: MediaCenterConnection,
  credential: ProtectedMediaCredential,
  plan: MediaCenterPlaybackPlan
): CatalogChannel {
  assertMediaCenterCredentialBinding(connection, credential.binding);
  const url = new URL(plan.url);
  const server = new URL(connection.baseUrl);
  if (url.origin !== server.origin) {
    throw new Error("The media server returned playback from an untrusted origin.");
  }
  if (connection.provider === "plex") url.searchParams.set("X-Plex-Token", credential.accessToken);
  else url.searchParams.set("api_key", credential.accessToken);
  const sensitiveHeaders = new Set(plan.sensitiveHeaderNames.map((name) => name.toLowerCase()));
  const requestHeaders = Object.fromEntries(Object.entries(plan.requestHeaders).filter(([name, value]) =>
    !sensitiveHeaders.has(name.toLowerCase())
      && !["authorization", "proxy-authorization"].includes(name.toLowerCase())
      && !/[\r\n]/.test(name)
      && !/[\r\n]/.test(value)
  ));
  return {
    ...channel,
    stream: {
      uri: url.toString(),
      requestHeaders
    }
  };
}

function assertMediaCenterSnapshotSafe(
  snapshot: MediaCenterSnapshot,
  secretValues: readonly string[] = []
): void {
  const serialized = JSON.stringify(snapshot);
  for (const secret of secretValues) {
    if (secret && serialized.includes(secret)) {
      throw new Error("A media-server secret reached the portable television snapshot.");
    }
  }
  const visit = (value: unknown): void => {
    if (Array.isArray(value)) {
      value.forEach(visit);
      return;
    }
    if (!value || typeof value !== "object") return;
    for (const [key, child] of Object.entries(value)) {
      const normalized = key.toLowerCase().replace(/[^a-z0-9]/g, "");
      if (normalized !== "credentialid" && [
        "token",
        "password",
        "secret",
        "apikey",
        "accesstoken",
        "authorization",
        "requestheaders"
      ].some((name) => normalized.includes(name))) {
        throw new Error("The portable television snapshot contains a sensitive field.");
      }
      visit(child);
    }
  };
  visit(snapshot);
}

function mediaCenterConnectedNotice(
  provider: "Plex" | "Emby",
  vault: TelevisionCredentialVault
): string {
  const persistence = vault.persistence === "device-secure"
    ? "Credentials are protected by this television."
    : "Credentials stay only until this app closes.";
  return `${provider} connected. ${persistence}`;
}

function providerLabel(provider: MediaCenterProvider): string {
  return provider === "plex" ? "Plex" : "Emby";
}

function televisionDeviceIdentity(): {
  client: string;
  device: string;
  deviceId: string;
  version: string;
} {
  const televisionWindow = typeof window === "undefined" ? undefined : window;
  const platform = televisionWindow?.webapis?.avplay
    ? "Samsung TV"
    : televisionWindow?.webOS
      ? "LG webOS TV"
      : "Television browser";
  return {
    client: "OrbitalVue",
    device: platform,
    deviceId: televisionDeviceId(),
    version: MEDIA_CENTER_VERSION
  };
}

function televisionDeviceId(): string {
  const storageKey = "streamvue-tv-device-id-v1";
  try {
    const saved = localStorage.getItem(storageKey);
    if (saved && /^[A-Za-z0-9._:-]{1,256}$/.test(saved)) return saved;
    const random = new Uint8Array(24);
    crypto.getRandomValues(random);
    const generated = `streamvue-tv-${[...random].map((value) => value.toString(16).padStart(2, "0")).join("")}`;
    localStorage.setItem(storageKey, generated);
    return generated;
  } catch {
    const userAgent = typeof navigator === "undefined" ? "OrbitalVue television" : navigator.userAgent;
    return `streamvue-tv-${sha256Hex(userAgent).slice(0, 40).toLowerCase()}`;
  }
}

async function readLimitedBody(response: Response): Promise<Uint8Array> {
  if (!response.body) {
    const bytes = new Uint8Array(await response.arrayBuffer());
    if (bytes.byteLength > MAX_PLAYLIST_BYTES) throw new Error("The playlist is larger than the 64 MB safety limit.");
    return bytes;
  }

  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let length = 0;
  while (true) {
    const result = await reader.read();
    if (result.done) break;
    if (!result.value) continue;
    length += result.value.byteLength;
    if (length > MAX_PLAYLIST_BYTES) {
      await reader.cancel();
      throw new Error("The playlist is larger than the 64 MB safety limit.");
    }
    chunks.push(result.value);
  }

  const combined = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    combined.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return combined;
}

function decodePlaylist(bytes: Uint8Array): string {
  if (bytes.length >= 2 && bytes[0] === 0xff && bytes[1] === 0xfe) {
    return new TextDecoder("utf-16le").decode(bytes.slice(2));
  }
  if (bytes.length >= 2 && bytes[0] === 0xfe && bytes[1] === 0xff) {
    const swapped = bytes.slice(2);
    for (let index = 0; index + 1 < swapped.length; index += 2) {
      const first = swapped[index] ?? 0;
      swapped[index] = swapped[index + 1] ?? 0;
      swapped[index + 1] = first;
    }
    return new TextDecoder("utf-16le").decode(swapped);
  }
  return new TextDecoder("utf-8").decode(bytes).replace(/^\uFEFF/, "");
}

export function normalizePlaylistUrl(rawValue: string): string {
  const trimmed = rawValue.trim();
  if (!trimmed) throw new Error("Enter a playlist URL.");
  const candidate = trimmed.includes("://") ? trimmed : `https://${trimmed}`;
  let parsed: URL;
  try {
    parsed = new URL(candidate);
  } catch {
    throw new Error("Enter a complete HTTP or HTTPS playlist URL.");
  }
  if (!( ["http:", "https:"] as string[]).includes(parsed.protocol) || !parsed.hostname) {
    throw new Error("Enter a complete HTTP or HTTPS playlist URL.");
  }
  return parsed.toString();
}

export function safeDisplayLocation(sourceUrl: string): string {
  const parsed = new URL(sourceUrl);
  return parsed.host;
}
