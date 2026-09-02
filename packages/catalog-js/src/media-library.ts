import type { CatalogChannel } from "./types.js";

export type MediaLibraryBrowseMode =
  | "all"
  | "continue-watching"
  | "recently-added"
  | "live"
  | "movies"
  | "series";

export interface MediaLibraryBrowseSummary {
  isMediaCenterLibrary: boolean;
  continueWatchingCount: number;
  recentlyAddedCount: number;
  movieCount: number;
  seriesCount: number;
}

export const RECENTLY_ADDED_WINDOW_MS = 30 * 24 * 60 * 60 * 1_000;
const CLOCK_SKEW_ALLOWANCE_MS = 24 * 60 * 60 * 1_000;
const RESUME_EDGE_MS = 30_000;

export function isMediaCenterChannel(channel: CatalogChannel): boolean {
  return channel.tags?.includes("media-center") === true;
}

export function canResumeMedia(channel: CatalogChannel): boolean {
  if (!isMediaCenterChannel(channel)) return false;
  const resume = channel.media?.resumePositionMs ?? 0;
  const duration = channel.media?.durationMs ?? 0;
  return Number.isFinite(resume)
    && resume >= RESUME_EDGE_MS
    && (duration <= 0 || resume < duration - RESUME_EDGE_MS);
}

export function matchesMediaLibraryBrowseMode(
  channel: CatalogChannel,
  mode: MediaLibraryBrowseMode,
  now = new Date()
): boolean {
  switch (mode) {
  case "all": return true;
  case "continue-watching": return canResumeMedia(channel);
  case "recently-added": {
    if (!isMediaCenterChannel(channel)) return false;
    const addedAt = timestamp(channel.media?.addedAt);
    return addedAt !== undefined
      && addedAt <= now.getTime() + CLOCK_SKEW_ALLOWANCE_MS
      && addedAt >= now.getTime() - RECENTLY_ADDED_WINDOW_MS;
  }
  case "live": return channel.kind === "live";
  case "movies": return channel.kind === "movie";
  case "series": return channel.kind === "series";
  }
}

export function mediaLibraryBrowseSummary(
  channels: readonly CatalogChannel[],
  now = new Date()
): MediaLibraryBrowseSummary {
  return {
    isMediaCenterLibrary: channels.some(isMediaCenterChannel),
    continueWatchingCount: channels.filter((channel) =>
      matchesMediaLibraryBrowseMode(channel, "continue-watching", now)).length,
    recentlyAddedCount: channels.filter((channel) =>
      matchesMediaLibraryBrowseMode(channel, "recently-added", now)).length,
    movieCount: channels.filter((channel) => channel.kind === "movie").length,
    seriesCount: channels.filter((channel) => channel.kind === "series").length
  };
}

export function orderMediaLibraryChannels(
  channels: readonly CatalogChannel[],
  mode: MediaLibraryBrowseMode
): CatalogChannel[] {
  const values = [...channels];
  if (mode === "continue-watching") {
    values.sort((left, right) =>
      compareDescending(timestamp(left.media?.lastPlayedAt), timestamp(right.media?.lastPlayedAt))
      || compareDescending(left.media?.resumePositionMs, right.media?.resumePositionMs)
      || left.number - right.number);
  } else if (mode === "recently-added") {
    values.sort((left, right) =>
      compareDescending(timestamp(left.media?.addedAt), timestamp(right.media?.addedAt))
      || left.number - right.number);
  }
  return values;
}

function timestamp(value: string | undefined): number | undefined {
  if (!value) return undefined;
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function compareDescending(left: number | undefined, right: number | undefined): number {
  return (right ?? Number.NEGATIVE_INFINITY) - (left ?? Number.NEGATIVE_INFINITY);
}
