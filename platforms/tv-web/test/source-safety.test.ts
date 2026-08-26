import { describe, expect, it } from "vitest";
import { normalizePlaylistUrl, safeDisplayLocation } from "../src/catalog/CatalogRepository.js";

describe("television source privacy", () => {
  it("shows only host and optional port", () => {
    const source = "https://viewer:private@provider.invalid:8443/list.m3u?username=viewer&password=private";
    expect(safeDisplayLocation(source)).toBe("provider.invalid:8443");
  });

  it("keeps private source parameters for playback without displaying them", () => {
    const normalized = normalizePlaylistUrl("provider.invalid/list.m3u?token=private");
    expect(normalized).toBe("https://provider.invalid/list.m3u?token=private");
    expect(safeDisplayLocation(normalized)).toBe("provider.invalid");
  });

  it("rejects non-network playlist schemes", () => {
    expect(() => normalizePlaylistUrl("file:///private/list.m3u")).toThrow("HTTP or HTTPS");
    expect(() => normalizePlaylistUrl("javascript:alert(1)")).toThrow("HTTP or HTTPS");
  });
});
