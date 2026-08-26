import type { CatalogChannel } from "@streamvue/catalog";
import type { TvPlatform } from "../platform/platform.js";

export const ASPECT_MODES = ["Auto", "Fit", "Fill", "Zoom", "16:9", "4:3", "21:9"] as const;
export type AspectMode = typeof ASPECT_MODES[number];
export type PlaybackState = "idle" | "opening" | "buffering" | "playing" | "paused" | "ended" | "error";

export interface PlaybackSignal {
  state: PlaybackState;
  message: string | null;
  warning: string | null;
}

export interface PlayerAdapter {
  readonly kind: "samsung-avplay" | "html-video";
  play(channel: CatalogChannel): Promise<void>;
  toggle(): void;
  stop(): void;
  setAspect(mode: AspectMode): void;
  resize(): void;
  destroy(): void;
}

export function selectPlayerKind(platform: TvPlatform, avPlayAvailable: boolean): PlayerAdapter["kind"] {
  return platform === "samsung-tizen" && avPlayAvailable ? "samsung-avplay" : "html-video";
}
