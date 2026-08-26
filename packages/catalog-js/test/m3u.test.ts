import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { createCatalogFromM3u, parseM3u, sha256Hex, stableChannelId } from "../src/index.js";

const fixturePath = resolve(process.cwd(), "../../contracts/fixtures/iptv-features.m3u");
const expectedPath = resolve(process.cwd(), "../../contracts/fixtures/catalog.expected.json");

describe("portable M3U parser", () => {
  it("matches the shared catalog fixture exactly", async () => {
    const [playlist, expectedText] = await Promise.all([
      readFile(fixturePath, "utf8"),
      readFile(expectedPath, "utf8")
    ]);
    const expected = JSON.parse(expectedText) as { channels: unknown; guideSources: unknown };
    const parsed = parseM3u(playlist, { sourceId: "fixture-source", sourceName: "IPTV feature fixture" });

    expect(parsed.channels).toEqual(expected.channels);
    expect(parsed.guideSources).toEqual(expected.guideSources);
  });

  it("creates contract 1.0 catalogs without exposing source secrets", async () => {
    const playlist = await readFile(fixturePath, "utf8");
    const catalog = createCatalogFromM3u(playlist, {
      catalogId: "fixture-catalog",
      displayName: "IPTV feature fixture",
      sourceId: "fixture-source",
      sourceName: "IPTV feature fixture",
      sourceType: "m3u-url",
      displayLocation: "stream.example.invalid",
      refreshOnLaunch: true,
      loadedAt: "2026-08-25T12:00:00Z"
    });

    expect(catalog.contractVersion).toBe("1.0");
    expect(catalog.sources[0]?.displayLocation).toBe("stream.example.invalid");
    expect(JSON.stringify(catalog.sources)).not.toContain("token=fixture");
  });

  it("keeps stable identities when only a stream token changes", () => {
    const first = stableChannelId("news.one", "News One", "News", "https://example.invalid/live.m3u8?token=one");
    const second = stableChannelId("news.one", "News One", "News", "https://example.invalid/live.m3u8?token=two");
    expect(first).toBe(second);
  });

  it("rejects empty and oversized catalogs", () => {
    expect(() => parseM3u("#EXTM3U", { sourceId: "source", sourceName: "Empty" })).toThrow("No playable entries");
    expect(() => parseM3u(
      "#EXTM3U\n#EXTINF:-1,One\nhttps://one.invalid/live\n#EXTINF:-1,Two\nhttps://two.invalid/live",
      { sourceId: "source", sourceName: "Too many", maxChannels: 1 }
    )).toThrow("safety limit");
  });

  it("implements the standard SHA-256 vectors without a runtime dependency", () => {
    expect(sha256Hex("")).toBe("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");
    expect(sha256Hex("abc")).toBe("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");
  });
});
