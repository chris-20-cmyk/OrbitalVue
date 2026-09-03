import type { CatalogChannel } from "@orbitalvue/catalog";
import type { AspectMode, PlaybackSignal, PlaybackTimeline, PlayerAdapter } from "./PlayerAdapter.js";

export class HtmlVideoPlayer implements PlayerAdapter {
  readonly kind = "html-video" as const;
  private aspect: AspectMode = "Auto";

  constructor(
    private readonly video: HTMLVideoElement,
    private readonly surface: HTMLElement,
    private readonly onSignal: (signal: PlaybackSignal) => void,
    private readonly onTimeline: (timeline: PlaybackTimeline) => void
  ) {
    this.video.addEventListener("loadstart", this.onLoadStart);
    this.video.addEventListener("waiting", this.onWaiting);
    this.video.addEventListener("stalled", this.onWaiting);
    this.video.addEventListener("playing", this.onPlaying);
    this.video.addEventListener("pause", this.onPause);
    this.video.addEventListener("ended", this.onEnded);
    this.video.addEventListener("error", this.onError);
    this.video.addEventListener("timeupdate", this.onTimeUpdate);
    this.video.addEventListener("durationchange", this.onTimeUpdate);
    window.addEventListener("resize", this.resize);
  }

  async play(channel: CatalogChannel, startPositionMs = 0): Promise<void> {
    this.stop();
    const unsupportedHeaders = Object.keys(channel.stream.requestHeaders);
    this.onSignal({
      state: "opening",
      message: null,
      warning: unsupportedHeaders.length > 0
        ? "This television's native video path cannot apply custom stream headers."
        : null
    });
    this.video.src = channel.stream.uri;
    this.video.load();
    try {
      if (startPositionMs > 0) {
        await waitForMetadata(this.video);
        this.video.currentTime = startPositionMs / 1_000;
      }
      await this.video.play();
    } catch (error) {
      this.onSignal({ state: "error", message: readableError(error, "Playback could not start."), warning: null });
    }
  }

  toggle(): void {
    if (this.video.paused) void this.video.play();
    else this.video.pause();
  }

  stop(): void {
    this.video.pause();
    this.video.removeAttribute("src");
    this.video.load();
    this.onSignal({ state: "idle", message: null, warning: null });
  }

  setAspect(mode: AspectMode): void {
    this.aspect = mode;
    this.resize();
  }

  readonly resize = (): void => {
    const bounds = this.surface.getBoundingClientRect();
    const forcedRatio = forcedAspect(this.aspect);
    this.video.style.objectFit = this.aspect === "Fill" ? "fill" : this.aspect === "Zoom" ? "cover" : "contain";
    if (!forcedRatio || bounds.width <= 0 || bounds.height <= 0) {
      this.video.style.width = "100%";
      this.video.style.height = "100%";
      return;
    }
    const containerRatio = bounds.width / bounds.height;
    if (containerRatio > forcedRatio) {
      this.video.style.width = `${Math.round(bounds.height * forcedRatio)}px`;
      this.video.style.height = `${Math.round(bounds.height)}px`;
    } else {
      this.video.style.width = `${Math.round(bounds.width)}px`;
      this.video.style.height = `${Math.round(bounds.width / forcedRatio)}px`;
    }
  };

  destroy(): void {
    this.stop();
    window.removeEventListener("resize", this.resize);
    this.video.removeEventListener("loadstart", this.onLoadStart);
    this.video.removeEventListener("waiting", this.onWaiting);
    this.video.removeEventListener("stalled", this.onWaiting);
    this.video.removeEventListener("playing", this.onPlaying);
    this.video.removeEventListener("pause", this.onPause);
    this.video.removeEventListener("ended", this.onEnded);
    this.video.removeEventListener("error", this.onError);
    this.video.removeEventListener("timeupdate", this.onTimeUpdate);
    this.video.removeEventListener("durationchange", this.onTimeUpdate);
  }

  private readonly onLoadStart = (): void => this.onSignal({ state: "opening", message: null, warning: null });
  private readonly onWaiting = (): void => this.onSignal({ state: "buffering", message: null, warning: null });
  private readonly onPlaying = (): void => this.onSignal({ state: "playing", message: null, warning: null });
  private readonly onPause = (): void => {
    if (this.video.currentTime > 0 && !this.video.ended) {
      this.onSignal({ state: "paused", message: null, warning: null });
    }
  };
  private readonly onEnded = (): void => this.onSignal({ state: "ended", message: null, warning: null });
  private readonly onError = (): void => {
    const code = this.video.error?.code;
    const message = code === MediaError.MEDIA_ERR_SRC_NOT_SUPPORTED
      ? "This television does not support the stream format."
      : "The television could not play this channel.";
    this.onSignal({ state: "error", message, warning: null });
  };
  private readonly onTimeUpdate = (): void => this.onTimeline({
    positionMs: finiteMilliseconds(this.video.currentTime),
    ...(Number.isFinite(this.video.duration) && this.video.duration > 0
      ? { durationMs: finiteMilliseconds(this.video.duration) }
      : {})
  });
}

function finiteMilliseconds(seconds: number): number {
  return Number.isFinite(seconds) ? Math.max(0, Math.floor(seconds * 1_000)) : 0;
}

async function waitForMetadata(video: HTMLVideoElement): Promise<void> {
  if (video.readyState >= HTMLMediaElement.HAVE_METADATA) return;
  await new Promise<void>((resolve) => {
    const finish = (): void => {
      window.clearTimeout(timeout);
      video.removeEventListener("loadedmetadata", finish);
      video.removeEventListener("error", finish);
      resolve();
    };
    const timeout = window.setTimeout(finish, 5_000);
    video.addEventListener("loadedmetadata", finish, { once: true });
    video.addEventListener("error", finish, { once: true });
  });
}

function forcedAspect(mode: AspectMode): number | null {
  if (mode === "16:9") return 16 / 9;
  if (mode === "4:3") return 4 / 3;
  if (mode === "21:9") return 21 / 9;
  return null;
}

function readableError(error: unknown, fallback: string): string {
  return error instanceof Error && error.message ? error.message : fallback;
}
