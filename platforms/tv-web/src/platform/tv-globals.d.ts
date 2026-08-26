interface SamsungAvPlayListener {
  onbufferingstart(): void;
  onbufferingprogress(percent: number): void;
  onbufferingcomplete(): void;
  oncurrentplaytime(milliseconds: number): void;
  onevent(eventType: number, eventData: string): void;
  onstreamcompleted(): void;
  onerror(eventType: string): void;
  onsubtitlechange(duration: number, text: string, type: number, attributes: Record<string, unknown>): void;
  ondrmevent(drmEvent: string, drmData: string): void;
}

interface SamsungAvPlay {
  open(uri: string): void;
  close(): void;
  prepareAsync(success: () => void, failure: (error: string) => void): void;
  play(): void;
  pause(): void;
  stop(): void;
  getState(): "NONE" | "IDLE" | "READY" | "PLAYING" | "PAUSED";
  setListener(listener: SamsungAvPlayListener): void;
  setDisplayRect(x: number, y: number, width: number, height: number): void;
  setDisplayMethod(method: string): void;
  setStreamingProperty(property: string, value: string): void;
}

interface SamsungWebApis {
  avplay: SamsungAvPlay;
}

interface SamsungTizen {
  application: {
    getCurrentApplication(): { exit(): void };
  };
  tvinputdevice: {
    registerKey(keyName: string): void;
  };
}

interface Window {
  webapis?: SamsungWebApis;
  tizen?: SamsungTizen;
  webOS?: unknown;
}
