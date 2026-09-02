import type { PlaybackSignal, PlaybackTimeline, PlayerAdapter } from "./PlayerAdapter.js";
import { selectPlayerKind } from "./PlayerAdapter.js";
import { HtmlVideoPlayer } from "./HtmlVideoPlayer.js";
import { SamsungAvPlayer } from "./SamsungAvPlayer.js";
import { detectPlatform } from "../platform/platform.js";

export function createPlayerAdapter(
  video: HTMLVideoElement,
  samsungObject: HTMLObjectElement,
  surface: HTMLElement,
  onSignal: (signal: PlaybackSignal) => void,
  onTimeline: (timeline: PlaybackTimeline) => void
): PlayerAdapter {
  const platform = detectPlatform();
  const avplay = window.webapis?.avplay;
  if (selectPlayerKind(platform, Boolean(avplay)) === "samsung-avplay" && avplay) {
    video.hidden = true;
    samsungObject.hidden = false;
    return new SamsungAvPlayer(samsungObject, surface, avplay, onSignal, onTimeline);
  }
  samsungObject.hidden = true;
  video.hidden = false;
  return new HtmlVideoPlayer(video, surface, onSignal, onTimeline);
}
