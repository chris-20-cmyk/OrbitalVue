import { describe, expect, it } from "vitest";
import {
  canResumeMedia,
  matchesMediaLibraryBrowseMode,
  mediaLibraryBrowseSummary,
  orderMediaLibraryChannels,
  type CatalogChannel
} from "../src/index.js";

const now = new Date("2026-09-01T12:00:00.000Z");

function channel(
  number: number,
  kind: CatalogChannel["kind"],
  media: CatalogChannel["media"],
  mediaCenter = true
): CatalogChannel {
  return {
    id: String(number).padStart(64, "A"),
    number,
    name: `Title ${number}`,
    group: "Library",
    kind,
    sourceId: "fixture",
    stream: { uri: `https://example.invalid/${number}`, requestHeaders: {} },
    media,
    tags: mediaCenter ? ["media-center"] : []
  };
}

describe("portable media-library browsing", () => {
  it("matches the Windows resume and 30-day recency boundaries", () => {
    const resumable = channel(1, "movie", {
      durationMs: 3_600_000,
      resumePositionMs: 600_000,
      addedAt: "2026-08-20T12:00:00.000Z"
    });
    const almostFinished = channel(2, "movie", {
      durationMs: 3_600_000,
      resumePositionMs: 3_580_000,
      addedAt: "2026-07-01T12:00:00.000Z"
    });

    expect(canResumeMedia(resumable)).toBe(true);
    expect(canResumeMedia(almostFinished)).toBe(false);
    expect(matchesMediaLibraryBrowseMode(resumable, "recently-added", now)).toBe(true);
    expect(matchesMediaLibraryBrowseMode(almostFinished, "recently-added", now)).toBe(false);
    expect(canResumeMedia(channel(3, "movie", { resumePositionMs: 600_000 }, false))).toBe(false);
  });

  it("summarizes and orders editorial shelves by provider activity", () => {
    const older = channel(1, "movie", {
      durationMs: 3_600_000,
      resumePositionMs: 500_000,
      lastPlayedAt: "2026-08-25T12:00:00.000Z",
      addedAt: "2026-08-30T12:00:00.000Z"
    });
    const newer = channel(2, "series", {
      durationMs: 2_400_000,
      resumePositionMs: 700_000,
      lastPlayedAt: "2026-08-31T12:00:00.000Z",
      addedAt: "2026-08-15T12:00:00.000Z"
    });
    const values = [older, newer];

    expect(mediaLibraryBrowseSummary(values, now)).toEqual({
      isMediaCenterLibrary: true,
      continueWatchingCount: 2,
      recentlyAddedCount: 2,
      movieCount: 1,
      seriesCount: 1
    });
    expect(orderMediaLibraryChannels(values, "continue-watching").map((item) => item.number))
      .toEqual([2, 1]);
    expect(orderMediaLibraryChannels(values, "recently-added").map((item) => item.number))
      .toEqual([1, 2]);
  });
});
