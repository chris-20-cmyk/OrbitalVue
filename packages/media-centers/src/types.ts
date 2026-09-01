export const MEDIA_CENTER_CONTRACT_VERSION = "1.0" as const;

export type MediaCenterProvider = "plex" | "emby";
export type MediaCenterLibraryKind = "movies" | "shows" | "recordings" | "live-tv" | "music" | "other";
export type MediaCenterItemKind = "movie" | "episode" | "video" | "recording" | "live-tv" | "audio";
export type PlaybackMethod = "direct-play" | "direct-stream" | "transcode";

export interface MediaCenterConnection {
  contractVersion: typeof MEDIA_CENTER_CONTRACT_VERSION;
  provider: MediaCenterProvider;
  serverId: string;
  displayName: string;
  baseUrl: string;
  displayLocation: string;
  credentialId: string;
  userId?: string;
}

/**
 * Safe, portable data that may be cached on disk. Credentials and resolved
 * playback plans deliberately have no place in this contract.
 */
export interface MediaCenterSnapshot {
  contractVersion: typeof MEDIA_CENTER_CONTRACT_VERSION;
  loadedAt: string;
  connection: MediaCenterConnection;
  libraries: MediaCenterLibrary[];
  items: MediaCenterItem[];
}

export interface MediaCenterLibrary {
  id: string;
  title: string;
  kind: MediaCenterLibraryKind;
  itemCount?: number;
}

export interface MediaCenterTrack {
  index: number;
  type: "video" | "audio" | "subtitle";
  codec?: string;
  language?: string;
  title?: string;
  isDefault: boolean;
  isForced: boolean;
  channels?: number;
}

export interface MediaCenterMediaSource {
  id: string;
  playbackPath?: string;
  container?: string;
  videoCodec?: string;
  audioCodec?: string;
  width?: number;
  height?: number;
  bitrate?: number;
  supportsDirectPlay: boolean;
  supportsDirectStream: boolean;
  supportsTranscode: boolean;
  tracks: MediaCenterTrack[];
}

export interface MediaCenterItem {
  id: string;
  provider: MediaCenterProvider;
  serverId: string;
  libraryId: string;
  libraryTitle: string;
  kind: MediaCenterItemKind;
  title: string;
  sortTitle?: string;
  seriesTitle?: string;
  seasonNumber?: number;
  episodeNumber?: number;
  year?: number;
  durationMs?: number;
  resumePositionMs?: number;
  played: boolean;
  addedAt?: string;
  lastPlayedAt?: string;
  artworkPath?: string;
  mediaSources: MediaCenterMediaSource[];
}

export interface MediaCenterPage<T> {
  items: T[];
  start: number;
  size: number;
  total: number;
}

export interface MediaCenterPlaybackPlan {
  itemId: string;
  mediaSourceId: string;
  method: PlaybackMethod;
  url: string;
  requestHeaders: Record<string, string>;
  sensitiveHeaderNames: string[];
  playSessionId?: string;
  liveStreamId?: string;
  requiresPlaybackReporting: boolean;
}

export interface MediaCenterDeviceIdentity {
  client: string;
  device: string;
  deviceId: string;
  version: string;
}

export interface MediaCenterCapabilities {
  canDirectPlay: boolean;
  canDirectStream: boolean;
  canTranscode: boolean;
  canInjectRequestHeaders: boolean;
  supportsHls: boolean;
  maxVideoBitrate?: number;
  maxWidth?: number;
  maxHeight?: number;
}
