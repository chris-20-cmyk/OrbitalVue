import { describe, expect, it } from "vitest";
import { ASPECT_MODES, selectPlayerKind } from "../src/playback/PlayerAdapter.js";

describe("television playback adapter selection", () => {
  it("uses AVPlay only when the Samsung API is actually available", () => {
    expect(selectPlayerKind("samsung-tizen", true)).toBe("samsung-avplay");
    expect(selectPlayerKind("samsung-tizen", false)).toBe("html-video");
    expect(selectPlayerKind("lg-webos", true)).toBe("html-video");
  });

  it("keeps every framing mode in the portable television control", () => {
    expect(ASPECT_MODES).toEqual(["Auto", "Fit", "Fill", "Zoom", "16:9", "4:3", "21:9"]);
  });
});
