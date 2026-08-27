import { requestJson, type MediaCenterHttpTransport } from "./http.js";
import { asArray, asBoolean, asNumber, asRecord, asString, clampPage } from "./parse.js";
import type {
  MediaCenterConnection,
  MediaCenterDeviceIdentity,
  MediaCenterItem,
  MediaCenterItemKind,
  MediaCenterLibrary,
  MediaCenterLibraryKind,
  MediaCenterMediaSource,
  MediaCenterPage,
  MediaCenterPlaybackPlan,
  MediaCenterTrack,
  PlaybackMethod
} from "./types.js";
import {
  normalizeMediaCenterBaseUrl,
  requireIdentifier,
  resolveServerPath,
  sanitizeServerPathForStorage,
  withQuery
} from "./url.js";

export interface EmbyAuthenticationRequest {
  baseUrl: string;
  username: string;
  password: string;
  device: MediaCenterDeviceIdentity;
}

export interface EmbyAuthenticationResult {
  accessToken: string;
  userId: string;
  serverId: string;
  userName: string;
}

export interface EmbyClientConfiguration {
  connection: MediaCenterConnection;
  token: string;
  device: MediaCenterDeviceIdentity;
}

export async function authenticateEmby(
  transport: MediaCenterHttpTransport,
  request: EmbyAuthenticationRequest
): Promise<EmbyAuthenticationResult> {
  const username = request.username.trim();
  if (!username) throw new TypeError("Enter an Emby user name.");
  if (!request.password) throw new TypeError("Enter the Emby password.");
  const apiBaseUrl = embyApiBaseUrl(request.baseUrl);
  const payload = await requestJson<unknown>(transport, {
    method: "POST",
    url: resolveServerPath(apiBaseUrl, "/Users/AuthenticateByName"),
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      "X-Emby-Authorization": embyAuthorization(request.device)
    },
    body: JSON.stringify({ Username: username, Pw: request.password })
  });
  const result = asRecord(payload);
  const token = asString(result.AccessToken);
  const serverId = asString(result.ServerId);
  const rawUser = Array.isArray(result.User) ? result.User[0] : result.User;
  const user = asRecord(rawUser);
  const userId = asString(user.Id);
  const userName = asString(user.Name) ?? username;
  if (!token || !serverId || !userId) {
    throw new TypeError("Emby authenticated but returned an incomplete session.");
  }
  return {
    accessToken: token,
    userId: requireIdentifier(userId, "Emby user identifier"),
    serverId: requireIdentifier(serverId, "Emby server identifier"),
    userName
  };
}

export class EmbyClient {
  private readonly apiBaseUrl: string;
  private readonly headers: Record<string, string>;
  private readonly userId: string;

  constructor(
    private readonly transport: MediaCenterHttpTransport,
    private readonly configuration: EmbyClientConfiguration
  ) {
    if (configuration.connection.provider !== "emby") {
      throw new TypeError("An Emby client requires an Emby connection.");
    }
    const token = configuration.token.trim();
    if (!token) throw new TypeError("An Emby access token is required.");
    this.userId = requireIdentifier(
      configuration.connection.userId ?? "",
      "Emby user identifier"
    );
    this.apiBaseUrl = embyApiBaseUrl(configuration.connection.baseUrl);
    this.headers = {
      Accept: "application/json",
      "X-Emby-Token": token,
      "X-Emby-Authorization": embyAuthorization(configuration.device, this.userId, token)
    };
  }

  async getLibraries(): Promise<MediaCenterLibrary[]> {
    const payload = await this.get(`/Users/${encodeURIComponent(this.userId)}/Views`);
    return asArray(asRecord(payload).Items).flatMap((value) => {
      const item = asRecord(value);
      const id = asString(item.Id);
      const title = asString(item.Name);
      if (!id || !title) return [];
      const itemCount = asNumber(item.ChildCount);
      return [{
        id: requireIdentifier(id, "Emby library identifier"),
        title,
        kind: embyLibraryKind(asString(item.CollectionType ?? item.Type)),
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
    const libraryId = requireIdentifier(library.id, "Emby library identifier");
    const url = withQuery(
      resolveServerPath(
        this.apiBaseUrl,
        `/Users/${encodeURIComponent(this.userId)}/Items`
      ),
      {
        ParentId: libraryId,
        Recursive: true,
        IncludeItemTypes: "Movie,Episode,Video,MusicVideo,Recording,LiveTvChannel,Audio",
        Fields: "MediaSources,MediaStreams,Path,PrimaryImageAspectRatio,SortName,Overview",
        EnableImages: true,
        EnableUserData: true,
        StartIndex: page.start,
        Limit: page.size
      }
    );
    const payload = await this.getAbsolute(url);
    const result = asRecord(payload);
    const items = asArray(result.Items).flatMap((value) => {
      const parsed = this.parseItem(asRecord(value), library);
      return parsed === undefined ? [] : [parsed];
    });
    return {
      items,
      start: page.start,
      size: items.length,
      total: asNumber(result.TotalRecordCount) ?? items.length
    };
  }

  async getPlaybackPlan(
    item: MediaCenterItem,
    mediaSourceId?: string,
    startPositionMs = item.resumePositionMs ?? 0
  ): Promise<MediaCenterPlaybackPlan> {
    if (item.provider !== "emby" || item.serverId !== this.configuration.connection.serverId) {
      throw new TypeError("The Emby item does not belong to this server connection.");
    }
    const itemId = requireIdentifier(item.id, "Emby item identifier");
    const infoUrl = withQuery(
      resolveServerPath(this.apiBaseUrl, `/Items/${encodeURIComponent(itemId)}/PlaybackInfo`),
      {
        UserId: this.userId,
        StartTimeTicks: Math.max(0, Math.floor(startPositionMs * 10_000))
      }
    );
    const payload = asRecord(await this.getAbsolute(infoUrl));
    const playSessionId = asString(payload.PlaySessionId) ?? createSessionId();
    const candidates = asArray(payload.MediaSources).map(asRecord);
    const source = mediaSourceId === undefined
      ? candidates[0]
      : candidates.find((candidate) => asString(candidate.Id) === mediaSourceId);
    if (!source) throw new TypeError("Emby returned no playable media source.");

    const sourceId = requireIdentifier(
      asString(source.Id) ?? item.mediaSources[0]?.id ?? "default",
      "Emby media source identifier"
    );
    const directStreamPath = asString(source.DirectStreamUrl);
    const transcodePath = asString(source.TranscodingUrl);
    const supportsDirectPlay = asBoolean(source.SupportsDirectPlay);
    const supportsDirectStream = asBoolean(source.SupportsDirectStream);
    const supportsTranscode = asBoolean(source.SupportsTranscoding);
    let method: PlaybackMethod;
    let url: string;
    if (supportsDirectPlay || supportsDirectStream) {
      method = supportsDirectPlay ? "direct-play" : "direct-stream";
      if (directStreamPath) {
        url = resolveServerPath(this.apiBaseUrl, directStreamPath);
      } else {
        const container = safeContainer(asString(source.Container));
        url = withQuery(
          resolveServerPath(
            this.apiBaseUrl,
            `/Videos/${encodeURIComponent(itemId)}/stream.${container}`
          ),
          {
            MediaSourceId: sourceId,
            PlaySessionId: playSessionId,
            Static: true
          }
        );
      }
    } else if (supportsTranscode && transcodePath) {
      method = "transcode";
      url = resolveServerPath(this.apiBaseUrl, transcodePath);
    } else {
      throw new TypeError("Emby did not provide a supported direct-play or transcode path.");
    }

    const requiredHeaders = safeHeaderRecord(asRecord(source.RequiredHttpHeaders));
    const liveStreamId = asString(source.LiveStreamId);
    return {
      itemId,
      mediaSourceId: sourceId,
      method,
      url,
      requestHeaders: { ...requiredHeaders, ...this.headers },
      sensitiveHeaderNames: ["X-Emby-Token", "X-Emby-Authorization"],
      playSessionId,
      ...(liveStreamId === undefined ? {} : { liveStreamId }),
      requiresPlaybackReporting: true
    };
  }

  artworkRequest(item: MediaCenterItem, maxWidth = 640): MediaCenterPlaybackPlan | undefined {
    if (!item.artworkPath) return undefined;
    const url = withQuery(resolveServerPath(this.apiBaseUrl, item.artworkPath), {
      MaxWidth: Math.min(2_000, Math.max(64, Math.floor(maxWidth)))
    });
    return {
      itemId: item.id,
      mediaSourceId: "artwork",
      method: "direct-play",
      url,
      requestHeaders: { ...this.headers },
      sensitiveHeaderNames: ["X-Emby-Token", "X-Emby-Authorization"],
      requiresPlaybackReporting: false
    };
  }

  private async get(path: string): Promise<unknown> {
    return this.getAbsolute(resolveServerPath(this.apiBaseUrl, path));
  }

  private async getAbsolute(url: string): Promise<unknown> {
    return requestJson(this.transport, { method: "GET", url, headers: this.headers });
  }

  private parseItem(
    raw: Record<string, unknown>,
    library: MediaCenterLibrary
  ): MediaCenterItem | undefined {
    const id = asString(raw.Id);
    const title = asString(raw.Name);
    const kind = embyItemKind(asString(raw.Type));
    if (!id || !title || !kind) return undefined;
    const userData = asRecord(raw.UserData);
    const sortTitle = asString(raw.SortName);
    const seriesTitle = asString(raw.SeriesName);
    const seasonNumber = asNumber(raw.ParentIndexNumber);
    const episodeNumber = asNumber(raw.IndexNumber);
    const year = asNumber(raw.ProductionYear);
    const durationTicks = asNumber(raw.RunTimeTicks);
    const resumeTicks = asNumber(userData.PlaybackPositionTicks);
    const imageTags = asRecord(raw.ImageTags);
    const primaryTag = asString(imageTags.Primary ?? raw.PrimaryImageTag);
    const artworkPath = primaryTag
      ? `/Items/${encodeURIComponent(id)}/Images/Primary?Tag=${encodeURIComponent(primaryTag)}`
      : undefined;
    return {
      id: requireIdentifier(id, "Emby item identifier"),
      provider: "emby",
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
      ...(durationTicks === undefined ? {} : { durationMs: Math.floor(durationTicks / 10_000) }),
      ...(resumeTicks === undefined ? {} : { resumePositionMs: Math.floor(resumeTicks / 10_000) }),
      played: asBoolean(userData.Played),
      ...(artworkPath === undefined ? {} : { artworkPath }),
      mediaSources: asArray(raw.MediaSources).map((value, index) =>
        parseEmbyMediaSource(asRecord(value), index, this.apiBaseUrl)
      )
    };
  }
}

function embyApiBaseUrl(input: string): string {
  const base = normalizeMediaCenterBaseUrl(input);
  return new URL(base).pathname.toLowerCase().endsWith("/emby") ? base : `${base}/emby`;
}

function embyAuthorization(
  device: MediaCenterDeviceIdentity,
  userId?: string,
  token?: string
): string {
  const fields = [
    ["Client", device.client],
    ["Device", device.device],
    ["DeviceId", device.deviceId],
    ["Version", device.version],
    ...(userId ? [["UserId", userId]] : []),
    ...(token ? [["Token", token]] : [])
  ];
  return `Emby ${fields.map(([key, value]) => `${key}="${safeHeaderValue(value ?? "")}"`).join(", ")}`;
}

function safeHeaderValue(value: string): string {
  return value.replace(/["\r\n]/g, "").slice(0, 512);
}

function safeHeaderRecord(value: Record<string, unknown>): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [key, rawValue] of Object.entries(value)) {
    const text = asString(rawValue);
    if (/^[A-Za-z0-9-]{1,64}$/.test(key) && text && !isReservedPlaybackHeader(key)) {
      result[key] = safeHeaderValue(text);
    }
  }
  return result;
}

function isReservedPlaybackHeader(key: string): boolean {
  return new Set([
    "authorization",
    "connection",
    "content-length",
    "cookie",
    "host",
    "proxy-authorization",
    "proxy-connection",
    "set-cookie",
    "te",
    "trailer",
    "transfer-encoding",
    "upgrade",
    "x-emby-authorization",
    "x-emby-token",
    "x-plex-token"
  ]).has(key.toLowerCase());
}

function embyLibraryKind(value: string | undefined): MediaCenterLibraryKind {
  switch (value?.toLowerCase()) {
  case "movies": return "movies";
  case "tvshows": return "shows";
  case "livetv": return "live-tv";
  case "music": return "music";
  case "recordings": return "recordings";
  default: return "other";
  }
}

function embyItemKind(value: string | undefined): MediaCenterItemKind | undefined {
  switch (value?.toLowerCase()) {
  case "movie": return "movie";
  case "episode": return "episode";
  case "video":
  case "musicvideo": return "video";
  case "recording": return "recording";
  case "livetvchannel": return "live-tv";
  case "audio": return "audio";
  default: return undefined;
  }
}

function parseEmbyMediaSource(
  source: Record<string, unknown>,
  index: number,
  apiBaseUrl: string
): MediaCenterMediaSource {
  const id = asString(source.Id) ?? `source-${index}`;
  const rawPlaybackPath = asString(source.DirectStreamUrl);
  const playbackPath = rawPlaybackPath === undefined
    ? undefined
    : sanitizeServerPathForStorage(apiBaseUrl, rawPlaybackPath);
  const container = asString(source.Container);
  const bitrate = asNumber(source.Bitrate);
  const tracks = asArray(source.MediaStreams).flatMap(parseEmbyTrack);
  const video = tracks.find((track) => track.type === "video");
  const audio = tracks.find((track) => track.type === "audio");
  const rawVideo = asArray(source.MediaStreams)
    .map(asRecord)
    .find((track) => asString(track.Type)?.toLowerCase() === "video");
  const width = rawVideo === undefined ? undefined : asNumber(rawVideo.Width);
  const height = rawVideo === undefined ? undefined : asNumber(rawVideo.Height);
  return {
    id: requireIdentifier(id, "Emby media source identifier"),
    ...(playbackPath === undefined ? {} : { playbackPath }),
    ...(container === undefined ? {} : { container }),
    ...(video?.codec === undefined ? {} : { videoCodec: video.codec }),
    ...(audio?.codec === undefined ? {} : { audioCodec: audio.codec }),
    ...(width === undefined ? {} : { width }),
    ...(height === undefined ? {} : { height }),
    ...(bitrate === undefined ? {} : { bitrate }),
    supportsDirectPlay: asBoolean(source.SupportsDirectPlay),
    supportsDirectStream: asBoolean(source.SupportsDirectStream),
    supportsTranscode: asBoolean(source.SupportsTranscoding),
    tracks
  };
}

function parseEmbyTrack(value: unknown): MediaCenterTrack[] {
  const stream = asRecord(value);
  const index = asNumber(stream.Index);
  const rawType = asString(stream.Type)?.toLowerCase();
  if (index === undefined || !rawType || !["video", "audio", "subtitle"].includes(rawType)) return [];
  const type = rawType as MediaCenterTrack["type"];
  const codec = asString(stream.Codec);
  const language = asString(stream.Language);
  const title = asString(stream.DisplayTitle ?? stream.Title);
  const channels = asNumber(stream.Channels);
  return [{
    index,
    type,
    ...(codec === undefined ? {} : { codec }),
    ...(language === undefined ? {} : { language }),
    ...(title === undefined ? {} : { title }),
    isDefault: asBoolean(stream.IsDefault),
    isForced: asBoolean(stream.IsForced),
    ...(channels === undefined ? {} : { channels })
  }];
}

function safeContainer(value: string | undefined): string {
  const candidate = value?.split(",")[0]?.trim().toLowerCase();
  return candidate && /^[a-z0-9]{1,12}$/.test(candidate) ? candidate : "mkv";
}

function createSessionId(): string {
  if (typeof globalThis.crypto?.randomUUID === "function") return globalThis.crypto.randomUUID();
  return `streamvue-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}
