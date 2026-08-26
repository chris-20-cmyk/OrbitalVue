export type TvPlatform = "samsung-tizen" | "lg-webos" | "browser";

export function detectPlatform(): TvPlatform {
  if (window.webapis?.avplay) return "samsung-tizen";
  if (window.webOS || /(?:web0s|webos)/i.test(navigator.userAgent)) return "lg-webos";
  return "browser";
}

export function registerPlatformRemoteKeys(): void {
  const input = window.tizen?.tvinputdevice;
  if (!input) return;
  for (const key of ["MediaPlay", "MediaPause", "MediaStop", "MediaPlayPause", "ChannelUp", "ChannelDown"]) {
    try {
      input.registerKey(key);
    } catch {
      // Model-specific keys are optional. Arrow, OK, and Back remain available.
    }
  }
}

export function exitTelevisionApp(): boolean {
  try {
    window.tizen?.application.getCurrentApplication().exit();
    return Boolean(window.tizen);
  } catch {
    return false;
  }
}
