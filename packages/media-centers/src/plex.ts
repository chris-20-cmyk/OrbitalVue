import { requestJson, type MediaCenterHttpTransport } from "./http.js";
import {
  assertMediaCenterCredentialBinding,
  requireAllowedTransport,
  type MediaCenterCredentialBinding
} from "./credential.js";
import { asArray, asBoolean, asNumber, asRecord, asString, clampPage } from "./parse.js";
import type {
  MediaCenterConnection,
  MediaCenterItem,
  MediaCenterItemKind,
  MediaCenterLibrary,
  MediaCenterLibraryKind,
  MediaCenterMediaSource,
  MediaCenterPage,
  MediaCenterPlaybackPlan,
  MediaCenterPlaybackReport,
  MediaCenterTrack
} from "./types.js";
import {
  normalizeMediaCenterBaseUrl,
  requireIdentifier,
  resolveServerPath,
  sanitizeServerPathForStorage,
  withQuery
} from "./url.js";

export interface PlexClientConfiguration {
  connection: MediaCenterConnection;
  token: string;
  credentialBinding: MediaCenterCredentialBinding;
  clientIdentifier: string;
  product?: string;
  version?: string;
}

export interface PlexServerIdentity {
  serverId: string;
  name: string;
  version?: string;
}

export interface PlexServerProbeConfiguration {
  baseUrl: string;
  clientIdentifier: string;
  product?: string;
  version?: string;
  allowInsecureHttp?: boolean;
}

export async function probePlexServerIdentity(
  transport: MediaCenterHttpTransport,
  configuration: PlexServerProbeConfiguration
): Promise<PlexServerIdentity> {
  requireAllowedTransport(
    configuration.baseUrl,
    configuration.allowInsecureHttp ?? false
  );
  const baseUrl = normalizeMediaCenterBaseUrl(configuration.baseUrl);
  const payload = await requestJson(transport, {
    method: "GET",
    url: resolveServerPath(baseUrl, "/identity"),
    headers: plexClientHeaders(configuration)
  });
  const container = asRecord(asRecord(payload).MediaContainer);
  const rawServerId = asString(container.machineIdentifier);
  if (!rawServerId) {
    throw new TypeError("Plex did not return a server identity.");
  }
  const serverId = requireIdentifier(rawServerId, "Plex server identifier");
  const name = asString(container.friendlyName) ?? "Plex";
  const version = asString(container.version);
  return {
    serverId,
    name,
    ...(version === undefined ? {} : { version })
  };
}

export class PlexClient {
  private readonly baseUrl: string;
  private readonly clientHeaders: Record<string, string>;
  private readonly headers: Record<string, string>;
  private identityVerified = false;

  constructor(
    private readonly transport: MediaCenterHttpTransport,
    private readonly configuration: PlexClientConfiguration
  ) {
    if (configuration.connection.provider !== "plex") {
      throw new TypeError("A Plex client requires a Plex connection.");
    }
    assertMediaCenterCredentialBinding(
      configuration.connection,
      configuration.credentialBinding
    );
    const token = configuration.token.trim();
    if (!token || /[\r\n]/.test(token) || token.length > 16_384) {
      throw new TypeError("A valid Plex access token is required.");
    }
    this.baseUrl = normalizeMediaCenterBaseUrl(configuration.connection.baseUrl);
    this.clientHeaders = plexClientHeaders(configuration);
    this.headers = { ...this.clientHeaders, "X-Plex-Token": token };
  }

  async getIdentity(): Promise<PlexServerIdentity> {
    const identity = await probePlexServerIdentity(this.transport, {
      baseUrl: this.baseUrl,
      clientIdentifier: this.configuration.clientIdentifier,
      ...(this.configuration.product === undefined
        ? {}
        : { product: this.configuration.product }),
      ...(this.configuration.version === undefined
        ? {}
        : { version: this.configuration.version }),
      allowInsecureHttp: this.configuration.credentialBinding.allowInsecureHttp
    });
    if (identity.serverId !== this.configuration.connection.serverId) {
      throw new TypeError("The Plex server identity does not match this credential.");
    }
    this.identityVerified = true;
    return identity;
  }

  async getLibraries(): Promise<MediaCenterLibrary[]> {
    const payload = await this.get("/library/sections");
    const container = asRecord(asRecord(payload).MediaContainer);
    return asArray(container.Directory).flatMap((value) => {
      const directory = asRecord(value);
      const id = asString(directory.key);
      const title = asString(directory.title);
      if (!id || !title) return [];
      const itemCount = asNumber(directory.totalSize ?? directory.size);
      return [{
        id: requireIdentifier(id, "Plex library identifier"),
        title,
        kind: plexLibraryKind(asString(directory.type)),
        ...(itemCount === undefined ? {} : { itemCount })
      }];
    });
  }

  async getItems(
    library: MediaCenterLibrary,
    start = 0,
    size = 200
  ): Promise<MediaCenterPage<MediaCenterItem>> {
    const page = clampPage(start, size);
    const libraryId = requireIdentifier(library.id, "Plex library identifier");
    const payload = await this.get(`/library/sections/${encodeURIComponent(libraryId)}/all`, {
      "X-Plex-Container-Start": String(page.start),
      "X-Plex-Container-Size": String(page.size)
    });
    const container = asRecord(asRecord(payload).MediaContainer);
    const items = asArray(container.Metadata).flatMap((value) => {
      const parsed = this.parseItem(asRecord(value), library);
      return parsed === undefined ? [] : [parsed];
    });
    return {
      items,
      start: asNumber(container.offset) ?? page.start,
      size: items.length,
      total: asNumber(container.totalSize) ?? asNumber(container.size) ?? items.length
    };
  }

  getPlaybackPlan(
    item: MediaCenterItem,
    mediaSourceId?: string
  ): MediaCenterPlaybackPlan {
    this.requireVerifiedIdentity();
    if (item.provider !== "plex" || item.serverId !== this.configuration.connection.serverId) {
      throw new TypeError("The Plex item does not belong to this server connection.");
    }
    const source = mediaSourceId === undefined
      ? item.mediaSources[0]
      : item.mediaSources.find((candidate) => candidate.id === mediaSourceId);
    if (!source?.playbackPath) {
      throw new TypeError("This Plex item does not expose a direct-play media part.");
    }
    return {
      itemId: item.id,
      mediaSourceId: source.id,
      method: "direct-play",
      url: resolveServerPath(this.baseUrl, source.playbackPath),
      requestHeaders: { ...this.headers },
      sensitiveHeaderNames: ["X-Plex-Token"],
      playSessionId: createSessionId(),
      requiresPlaybackReporting: true
    };
  }

  async reportPlayback(
    plan: MediaCenterPlaybackPlan,
    report: MediaCenterPlaybackReport
  ): Promise<void> {
    if (!plan.requiresPlaybackReporting) return;
    this.requireVerifiedIdentity();
    const itemId = requireIdentifier(plan.itemId, "Plex item identifier");
    const normalized = normalizePlaybackReport(report);
    const url = withQuery(resolveServerPath(this.baseUrl, "/:/timeline"), {
      key: `/library/metadata/${itemId}`,
      ratingKey: itemId,
      state: report.kind === "stopped" ? "stopped" : normalized.state,
      time: normalized.positionMs,
      ...(normalized.durationMs === undefined ? {} : { duration: normalized.durationMs })
    });
    const response = await this.transport({
      method: "POST",
      url,
      headers: {
        ...this.headers,
        ...(plan.playSessionId === undefined
          ? {}
          : { "X-Plex-Session-Identifier": safeHeaderValue(plan.playSessionId, "") })
      }
    });
    requireSuccessfulReport(response.status, "Plex");
  }

  artworkRequest(item: MediaCenterItem, maxWidth = 640): MediaCenterPlaybackPlan | undefined {
    this.requireVerifiedIdentity();
    if (!item.artworkPath) return undefined;
    const url = withQuery(resolveServerPath(this.baseUrl, item.artworkPath), {
      width: Math.min(2_000, Math.max(64, Math.floor(maxWidth)))
    });
    return {
      itemId: item.id,
      mediaSourceId: "artwork",
      method: "direct-play",
      url,
      requestHeaders: { ...this.headers },
      sensitiveHeaderNames: ["X-Plex-Token"],
      requiresPlaybackReporting: false
    };
  }

  private async get(path: string, additionalHeaders: Record<string, string> = {}): Promise<unknown> {
    if (!this.identityVerified) await this.getIdentity();
    return requestJson(this.transport, {
      method: "GET",
      url: resolveServerPath(this.baseUrl, path),
      headers: { ...this.headers, ...additionalHeaders }
    });
  }

  private requireVerifiedIdentity(): void {
    if (!this.identityVerified) {
      throw new TypeError("Verify the Plex server identity before resolving protected media.");
    }
  }

  private parseItem(
    metadata: Record<string, unknown>,
    library: MediaCenterLibrary
  ): MediaCenterItem | undefined {
    const id = asString(metadata.ratingKey);
    const title = asString(metadata.title);
    if (!id || !title) return undefined;
    const kind = plexItemKind(asString(metadata.type));
    if (!kind) return undefined;
    const mediaSources = asArray(metadata.Media).flatMap((value) =>
      parsePlexMedia(asRecord(value), this.baseUrl)
    );
    const sortTitle = asString(metadata.titleSort);
    const seriesTitle = asString(metadata.grandparentTitle);
    const seasonNumber = asNumber(metadata.parentIndex);
    const episodeNumber = asNumber(metadata.index);
    const year = asNumber(metadata.year);
    const durationMs = asNumber(metadata.duration);
    const resumePositionMs = asNumber(metadata.viewOffset);
    const addedAt = epochSecondsISO(asNumber(metadata.addedAt));
    const lastPlayedAt = epochSecondsISO(asNumber(metadata.lastViewedAt));
    const rawArtworkPath = asString(metadata.thumb);
    const artworkPath = rawArtworkPath === undefined
      ? undefined
      : sanitizeServerPathForStorage(this.baseUrl, rawArtworkPath);
    return {
      id: requireIdentifier(id, "Plex item identifier"),
      provider: "plex",
      serverId: this.configuration.connection.serverId,
      libraryId: library.id,
      libraryTitle: library.title,
      kind,
      title,
      ...(sortTitle === undefined ? {} : { sortTitle }),
      ...(seriesTitle === undefined ? {} : { seriesTitle }),
      ...(seasonNumber === undefined ? {} : { seasonNumber }),
      ...(episodeNumber === undefined ? {} : { episodeNumber }),
      ...(year === undefined ? {} : { year }),
      ...(durationMs === undefined ? {} : { durationMs }),
      ...(resumePositionMs === undefined ? {} : { resumePositionMs }),
      played: (asNumber(metadata.viewCount) ?? 0) > 0,
      ...(addedAt === undefined ? {} : { addedAt }),
      ...(lastPlayedAt === undefined ? {} : { lastPlayedAt }),
      ...(artworkPath === undefined ? {} : { artworkPath }),
      mediaSources
    };
  }
}

function epochSecondsISO(value: number | undefined): string | undefined {
  if (value === undefined || !Number.isFinite(value) || value < 0) return undefined;
  const date = new Date(value * 1_000);
  return Number.isNaN(date.getTime()) || date.getUTCFullYear() > 3000
    ? undefined
    : date.toISOString();
}

function plexClientHeaders(configuration: {
  clientIdentifier: string;
  product?: string;
  version?: string;
}): Record<string, string> {
  return {
    Accept: "application/json",
    "X-Plex-Client-Identifier": requireIdentifier(
      configuration.clientIdentifier,
      "Plex client identifier"
    ),
    "X-Plex-Product": safeHeaderValue(configuration.product, "OrbitalVue"),
    "X-Plex-Version": safeHeaderValue(configuration.version, "5.1.0"),
    "X-Plex-Platform": "OrbitalVue",
    "X-Plex-Pms-Api-Version": "1.2.2"
  };
}

function safeHeaderValue(value: string | undefined, fallback: string): string {
  const sanitized = value?.replace(/[\r\n]/g, "").trim().slice(0, 256);
  return sanitized || fallback;
}

function plexLibraryKind(value: string | undefined): MediaCenterLibraryKind {
  switch (value?.toLowerCase()) {
  case "movie": return "movies";
  case "show": return "shows";
  case "artist": return "music";
  default: return "other";
  }
}

function plexItemKind(value: string | undefined): MediaCenterItemKind | undefined {
  switch (value?.toLowerCase()) {
  case "movie": return "movie";
  case "episode": return "episode";
  case "clip": return "video";
  case "track": return "audio";
  default: return undefined;
  }
}

function parsePlexMedia(
  media: Record<string, unknown>,
  baseUrl: string
): MediaCenterMediaSource[] {
  const mediaId = asString(media.id);
  return asArray(media.Part).flatMap((value, partIndex) => {
    const part = asRecord(value);
    const rawPlaybackPath = asString(part.key);
    const sourceId = asString(part.id) ?? mediaId ?? `part-${partIndex}`;
    if (!rawPlaybackPath) return [];
    const playbackPath = sanitizeServerPathForStorage(baseUrl, rawPlaybackPath);
    const container = asString(part.container) ?? asString(media.container);
    const videoCodec = asString(media.videoCodec);
    const audioCodec = asString(media.audioCodec);
    const width = asNumber(media.width);
    const height = asNumber(media.height);
    const bitrate = asNumber(media.bitrate);
    return [{
      id: requireIdentifier(sourceId, "Plex media source identifier"),
      playbackPath,
      ...(container === undefined ? {} : { container }),
      ...(videoCodec === undefined ? {} : { videoCodec }),
      ...(audioCodec === undefined ? {} : { audioCodec }),
      ...(width === undefined ? {} : { width }),
      ...(height === undefined ? {} : { height }),
      ...(bitrate === undefined ? {} : { bitrate }),
      supportsDirectPlay: true,
      supportsDirectStream: true,
      supportsTranscode: true,
      tracks: asArray(part.Stream).flatMap(parsePlexTrack)
    }];
  });
}

function parsePlexTrack(value: unknown): MediaCenterTrack[] {
  const stream = asRecord(value);
  const index = asNumber(stream.index ?? stream.id);
  const streamType = asNumber(stream.streamType);
  if (index === undefined || !streamType || streamType < 1 || streamType > 3) return [];
  const type = streamType === 1 ? "video" : streamType === 2 ? "audio" : "subtitle";
  const codec = asString(stream.codec);
  const language = asString(stream.languageCode ?? stream.language);
  const title = asString(stream.title ?? stream.displayTitle);
  const channels = asNumber(stream.channels);
  return [{
    index,
    type,
    ...(codec === undefined ? {} : { codec }),
    ...(language === undefined ? {} : { language }),
    ...(title === undefined ? {} : { title }),
    isDefault: asBoolean(stream.default ?? stream.selected),
    isForced: asBoolean(stream.forced),
    ...(channels === undefined ? {} : { channels })
  }];
}

function normalizePlaybackReport(report: MediaCenterPlaybackReport): {
  state: MediaCenterPlaybackReport["state"];
  positionMs: number;
  durationMs?: number;
} {
  const durationMs = Number.isFinite(report.durationMs) && (report.durationMs ?? 0) > 0
    ? Math.floor(report.durationMs!)
    : undefined;
  const rawPosition = Number.isFinite(report.positionMs) ? Math.floor(report.positionMs) : 0;
  const positionMs = Math.min(durationMs ?? Number.MAX_SAFE_INTEGER, Math.max(0, rawPosition));
  return {
    state: report.state,
    positionMs,
    ...(durationMs === undefined ? {} : { durationMs })
  };
}

function requireSuccessfulReport(status: number, provider: string): void {
  if (status < 200 || status >= 300) {
    throw new TypeError(`${provider} rejected the playback report with HTTP ${status}.`);
  }
}

function createSessionId(): string {
  if (typeof globalThis.crypto?.randomUUID === "function") return globalThis.crypto.randomUUID();
  return `streamvue-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}
