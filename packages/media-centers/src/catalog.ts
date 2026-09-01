import {
  CATALOG_CONTRACT_VERSION,
  sha256Hex,
  type CatalogChannel,
  type ChannelKind,
  type StreamVueCatalog
} from "@streamvue/catalog";
import type {
  MediaCenterConnection,
  MediaCenterItem,
  MediaCenterLibrary,
  MediaCenterProvider
} from "./types.js";
import { MEDIA_CENTER_CONTRACT_VERSION } from "./types.js";
import {
  normalizeMediaCenterBaseUrl,
  requireIdentifier,
  safeServerDisplayLocation
} from "./url.js";

export interface MediaCenterPlaybackLocator {
  provider: MediaCenterProvider;
  serverId: string;
  itemId: string;
}

export interface MediaCenterConnectionInput {
  provider: MediaCenterProvider;
  serverId: string;
  displayName: string;
  baseUrl: string;
  credentialId: string;
  userId?: string;
}

export function createMediaCenterConnection(
  input: MediaCenterConnectionInput
): MediaCenterConnection {
  const displayName = input.displayName.trim();
  if (!displayName || displayName.length > 256) {
    throw new TypeError("Enter a media-center name with no more than 256 characters.");
  }
  const baseUrl = normalizeMediaCenterBaseUrl(input.baseUrl);
  const serverId = requireIdentifier(input.serverId, "media-center server identifier");
  const credentialId = requireIdentifier(input.credentialId, "secure credential reference");
  const userId = input.userId === undefined
    ? undefined
    : requireIdentifier(input.userId, "media-center user identifier");
  return {
    contractVersion: MEDIA_CENTER_CONTRACT_VERSION,
    provider: input.provider,
    serverId,
    displayName,
    baseUrl,
    displayLocation: safeServerDisplayLocation(baseUrl),
    credentialId,
    ...(userId === undefined ? {} : { userId })
  };
}

export function createMediaCenterCatalog(
  connection: MediaCenterConnection,
  libraries: readonly MediaCenterLibrary[],
  items: readonly MediaCenterItem[],
  loadedAt = new Date().toISOString()
): StreamVueCatalog {
  if (connection.contractVersion !== MEDIA_CENTER_CONTRACT_VERSION) {
    throw new TypeError("The media-center connection contract is not supported.");
  }
  const serverId = requireIdentifier(connection.serverId, "media-center server identifier");
  const sourceId = `MC-${sha256Hex(`${connection.provider}|${serverId}`).slice(0, 48)}`;
  const libraryNames = new Map(libraries.map((library) => [library.id, library.title]));
  const channels = items.flatMap((item, index) => {
    const channel = toCatalogChannel(connection, sourceId, libraryNames, item, index + 1);
    return channel === undefined ? [] : [channel];
  });
  return {
    contractVersion: CATALOG_CONTRACT_VERSION,
    catalogId: `MC-${sha256Hex(`${connection.provider}|${serverId}|catalog`).slice(0, 48)}`,
    displayName: `${connection.displayName} • ${providerLabel(connection.provider)}`,
    loadedAt,
    sources: [{
      id: sourceId,
      name: connection.displayName,
      type: connection.provider,
      displayLocation: safeServerDisplayLocation(connection.baseUrl),
      refreshOnLaunch: true
    }],
    guideSources: [],
    channels
  };
}

export function mediaCenterPlaybackUri(locator: MediaCenterPlaybackLocator): string {
  return mediaCenterLocatorUri("streamvue-media", locator);
}

export function mediaCenterArtworkUri(locator: MediaCenterPlaybackLocator): string {
  return mediaCenterLocatorUri("streamvue-artwork", locator);
}

export function parseMediaCenterPlaybackUri(value: string): MediaCenterPlaybackLocator {
  if (value !== value.trim()) {
    throw new TypeError("The media-center playback address is not canonical.");
  }
  const url = new URL(value);
  if (url.protocol !== "streamvue-media:"
    || url.username !== ""
    || url.password !== ""
    || url.port !== ""
    || url.search !== ""
    || url.hash !== "") {
    throw new TypeError("This is not an OrbitalVue media-center playback address.");
  }
  const provider = url.hostname.toLowerCase();
  if (provider !== "plex" && provider !== "emby") {
    throw new TypeError("The media-center playback provider is not supported.");
  }
  const parts = url.pathname.split("/").filter(Boolean).map(decodeURIComponent);
  if (parts.length !== 2) {
    throw new TypeError("The media-center playback address is incomplete.");
  }
  const locator: MediaCenterPlaybackLocator = {
    provider,
    serverId: requireIdentifier(parts[0] ?? "", "media-center server identifier"),
    itemId: requireIdentifier(parts[1] ?? "", "media-center item identifier")
  };
  if (url.href !== mediaCenterPlaybackUri(locator)) {
    throw new TypeError("The media-center playback address is not canonical.");
  }
  return locator;
}

function mediaCenterLocatorUri(
  scheme: "streamvue-media" | "streamvue-artwork",
  locator: MediaCenterPlaybackLocator
): string {
  if (locator.provider !== "plex" && locator.provider !== "emby") {
    throw new TypeError("The media-center playback provider is not supported.");
  }
  const serverId = requireIdentifier(locator.serverId, "media-center server identifier");
  const itemId = requireIdentifier(locator.itemId, "media-center item identifier");
  return `${scheme}://${locator.provider}/${encodeURIComponent(serverId)}/${encodeURIComponent(itemId)}`;
}

function toCatalogChannel(
  connection: MediaCenterConnection,
  sourceId: string,
  libraryNames: ReadonlyMap<string, string>,
  item: MediaCenterItem,
  number: number
): CatalogChannel | undefined {
  if (item.provider !== connection.provider || item.serverId !== connection.serverId) {
    throw new TypeError("A media-center item belongs to a different connection.");
  }
  const kind = catalogKind(item.kind);
  if (!kind) return undefined;
  const locator = {
    provider: item.provider,
    serverId: item.serverId,
    itemId: item.id
  };
  const group = item.seriesTitle
    ?? libraryNames.get(item.libraryId)
    ?? item.libraryTitle
    ?? providerLabel(item.provider);
  const logoUri = item.artworkPath ? mediaCenterArtworkUri(locator) : undefined;
  const tags = [
    "media-center",
    item.provider,
    item.kind,
    ...(item.played ? ["played"] : []),
    ...((item.resumePositionMs ?? 0) > 0 ? ["resume"] : [])
  ];
  return {
    id: sha256Hex(`media-center|${item.provider}|${item.serverId}|${item.id}`),
    number,
    name: displayTitle(item),
    group,
    kind,
    sourceId,
    stream: {
      uri: mediaCenterPlaybackUri(locator),
      requestHeaders: {}
    },
    media: {
      libraryId: item.libraryId,
      libraryTitle: item.libraryTitle,
      ...(item.seriesTitle === undefined ? {} : { seriesTitle: item.seriesTitle }),
      ...(item.seasonNumber === undefined ? {} : { seasonNumber: item.seasonNumber }),
      ...(item.episodeNumber === undefined ? {} : { episodeNumber: item.episodeNumber }),
      ...(item.year === undefined ? {} : { year: item.year }),
      ...(item.durationMs === undefined ? {} : { durationMs: item.durationMs }),
      ...(item.resumePositionMs === undefined ? {} : { resumePositionMs: item.resumePositionMs }),
      played: item.played,
      ...(item.addedAt === undefined ? {} : { addedAt: item.addedAt }),
      ...(item.lastPlayedAt === undefined ? {} : { lastPlayedAt: item.lastPlayedAt })
    },
    ...(logoUri === undefined ? {} : { guide: { logoUri } }),
    tags
  };
}

function catalogKind(kind: MediaCenterItem["kind"]): ChannelKind | undefined {
  switch (kind) {
  case "movie":
  case "video": return "movie";
  case "episode": return "series";
  case "recording": return "recording";
  case "live-tv": return "live";
  case "audio": return undefined;
  }
}

function displayTitle(item: MediaCenterItem): string {
  if (item.kind !== "episode") return item.title;
  const season = item.seasonNumber === undefined
    ? ""
    : `S${String(item.seasonNumber).padStart(2, "0")}`;
  const episode = item.episodeNumber === undefined
    ? ""
    : `E${String(item.episodeNumber).padStart(2, "0")}`;
  const prefix = `${season}${episode}`;
  return prefix ? `${prefix} • ${item.title}` : item.title;
}

function providerLabel(provider: MediaCenterProvider): string {
  return provider === "plex" ? "Plex" : "Emby";
}
