import { describe, expect, it } from "vitest";
import { chooseNextIndex, type RectLike } from "../src/navigation/SpatialNavigator.js";

function rect(left: number, top: number, width = 100, height = 60): RectLike {
  return { left, top, right: left + width, bottom: top + height, width, height };
}

describe("remote spatial navigation", () => {
  const grid = [
    rect(0, 0), rect(140, 0), rect(280, 0),
    rect(0, 100), rect(140, 100), rect(280, 100)
  ];

  it("moves to the nearest control in the requested direction", () => {
    expect(chooseNextIndex(grid, 4, "up")).toBe(1);
    expect(chooseNextIndex(grid, 4, "left")).toBe(3);
    expect(chooseNextIndex(grid, 4, "right")).toBe(5);
  });

  it("keeps focus at a hard boundary", () => {
    expect(chooseNextIndex(grid, 0, "up")).toBe(0);
    expect(chooseNextIndex(grid, 0, "left")).toBe(0);
  });
});
