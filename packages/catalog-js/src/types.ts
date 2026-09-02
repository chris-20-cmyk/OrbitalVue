export const CATALOG_CONTRACT_VERSION = "1.0" as const;

export type ChannelKind = "live" | "movie" | "series" | "recording" | "replay" | "music";
export type SourceType = "m3u-file" | "m3u-url" | "xtream" | "plex" | "emby" | "generated";

export interface CatalogSource {
  id: string;
  name: string;
  type: SourceType;
  displayLocation: string;
  refreshOnLaunch: boolean;
}

export interface StreamDescriptor {
  uri: string;
  requestHeaders: Record<string, string>;
}

export interface GuideMetadata {
  tvgId?: string;
  tvgName?: string;
  logoUri?: string;
}

export interface CatchupMetadata {
  mode: string;
  source: string;
  days: number;
  correctionMinutes: number;
}

export interface CatalogMediaMetadata {
  libraryId?: string;
  libraryTitle?: string;
  seriesTitle?: string;
  seasonNumber?: number;
  episodeNumber?: number;
  year?: number;
  durationMs?: number;
  resumePositionMs?: number;
  played?: boolean;
  addedAt?: string;
  lastPlayedAt?: string;
}

export interface CatalogChannel {
  id: string;
  number: number;
  name: string;
  group: string;
  kind: ChannelKind;
  sourceId: string;
  stream: StreamDescriptor;
  guide?: GuideMetadata;
  catchup?: CatchupMetadata;
  media?: CatalogMediaMetadata;
  tags?: string[];
}

export interface OrbitalVueCatalog {
  contractVersion: typeof CATALOG_CONTRACT_VERSION;
  catalogId: string;
  displayName: string;
  loadedAt: string;
  sources: CatalogSource[];
  guideSources: string[];
  channels: CatalogChannel[];
}

export interface ParsedPlaylist {
  channels: CatalogChannel[];
  guideSources: string[];
}

export interface ParseM3uOptions {
  sourceId: string;
  sourceName: string;
  maxChannels?: number;
}

export interface CreateCatalogOptions extends ParseM3uOptions {
  catalogId: string;
  displayName: string;
  sourceType: SourceType;
  displayLocation: string;
  refreshOnLaunch: boolean;
  loadedAt?: string;
}
