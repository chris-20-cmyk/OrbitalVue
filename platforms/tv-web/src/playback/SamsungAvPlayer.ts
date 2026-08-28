import type { CatalogChannel } from "@streamvue/catalog";
import type { AspectMode, PlaybackSignal, PlayerAdapter } from "./PlayerAdapter.js";

export class SamsungAvPlayer implements PlayerAdapter {
  readonly kind = "samsung-avplay" as const;
  private aspect: AspectMode = "Auto";

  constructor(
    private readonly objectElement: HTMLObjectElement,
    private readonly surface: HTMLElement,
    private readonly avplay: SamsungAvPlay,
    private readonly onSignal: (signal: PlaybackSignal) => void
  ) {
    this.avplay.setListener({
      onbufferingstart: () => this.onSignal({ state: "buffering", message: null, warning: null }),
      onbufferingprogress: () => undefined,
      onbufferingcomplete: () => this.onSignal({ state: "playing", message: null, warning: null }),
      oncurrentplaytime: () => undefined,
      onevent: () => undefined,
      onstreamcompleted: () => this.onSignal({ state: "ended", message: null, warning: null }),
      onerror: (eventType) => this.onSignal({ state: "error", message: `Samsung AVPlay error: ${eventType}`, warning: null }),
      onsubtitlechange: () => undefined,
      ondrmevent: () => undefined
    });
    window.addEventListener("resize", this.resize);
  }

  async play(channel: CatalogChannel, startPositionMs = 0): Promise<void> {
    this.stop();
    const warning = channel.stream.requestHeaders.Referer
      ? "Samsung AVPlay cannot apply a custom Referer header; User-Agent and Cookie remain supported."
      : null;
    this.onSignal({ state: "opening", message: null, warning });
    try {
      this.avplay.open(channel.stream.uri);
      const userAgent = channel.stream.requestHeaders["User-Agent"];
      const cookie = channel.stream.requestHeaders.Cookie;
      if (userAgent) this.avplay.setStreamingProperty("USER_AGENT", userAgent);
      if (cookie) this.avplay.setStreamingProperty("COOKIE", cookie);
      this.applyDisplayMode();
      this.resize();
      await new Promise<void>((resolve, reject) => {
        this.avplay.prepareAsync(
          () => {
            try {
              this.resize();
              if (startPositionMs > 0) this.avplay.seekTo(Math.floor(startPositionMs));
              this.avplay.play();
              this.onSignal({ state: "playing", message: null, warning });
              resolve();
            } catch (error) {
              reject(error);
            }
          },
          (error) => reject(new Error(error))
        );
      });
    } catch (error) {
      this.onSignal({
        state: "error",
        message: error instanceof Error ? error.message : "Samsung AVPlay could not open this channel.",
        warning
      });
    }
  }

  toggle(): void {
    try {
      const state = this.avplay.getState();
      if (state === "PLAYING") {
        this.avplay.pause();
        this.onSignal({ state: "paused", message: null, warning: null });
      } else if (state === "PAUSED" || state === "READY") {
        this.avplay.play();
        this.onSignal({ state: "playing", message: null, warning: null });
      }
    } catch {
      this.onSignal({ state: "error", message: "Samsung AVPlay could not change playback state.", warning: null });
    }
  }

  stop(): void {
    try {
      const state = this.avplay.getState();
      if (state === "PLAYING" || state === "PAUSED" || state === "READY") this.avplay.stop();
      if (this.avplay.getState() !== "NONE") this.avplay.close();
    } catch {
      // Closing an already-idle AVPlay instance is harmless and model-dependent.
    }
    this.onSignal({ state: "idle", message: null, warning: null });
  }

  setAspect(mode: AspectMode): void {
    this.aspect = mode;
    this.applyDisplayMode();
    this.resize();
  }

  readonly resize = (): void => {
    const bounds = this.surface.getBoundingClientRect();
    const ratio = forcedAspect(this.aspect);
    let left = Math.round(bounds.left);
    let top = Math.round(bounds.top);
    let width = Math.round(bounds.width);
    let height = Math.round(bounds.height);
    if (ratio && width > 0 && height > 0) {
      if (width / height > ratio) {
        const targetWidth = Math.round(height * ratio);
        left += Math.round((width - targetWidth) / 2);
        width = targetWidth;
      } else {
        const targetHeight = Math.round(width / ratio);
        top += Math.round((height - targetHeight) / 2);
        height = targetHeight;
      }
    }
    try {
      this.avplay.setDisplayRect(left, top, Math.max(1, width), Math.max(1, height));
    } catch {
      // AVPlay accepts the rectangle only after a stream is opened.
    }
    this.objectElement.style.left = `${left}px`;
    this.objectElement.style.top = `${top}px`;
    this.objectElement.style.width = `${Math.max(1, width)}px`;
    this.objectElement.style.height = `${Math.max(1, height)}px`;
  };

  destroy(): void {
    this.stop();
    window.removeEventListener("resize", this.resize);
  }

  private applyDisplayMode(): void {
    const mode = this.aspect === "Fill"
      ? "PLAYER_DISPLAY_MODE_FULL_SCREEN"
      : this.aspect === "Zoom"
        ? "PLAYER_DISPLAY_MODE_AUTO_ASPECT_RATIO"
        : "PLAYER_DISPLAY_MODE_LETTER_BOX";
    try {
      this.avplay.setDisplayMethod(mode);
    } catch {
      // AVPlay accepts display settings only in supported lifecycle states.
    }
  }
}

function forcedAspect(mode: AspectMode): number | null {
  if (mode === "16:9") return 16 / 9;
  if (mode === "4:3") return 4 / 3;
  if (mode === "21:9") return 21 / 9;
  return null;
}
