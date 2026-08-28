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
  seekTo(milliseconds: number): void;
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
  keymanager?: {
    saveData(
      name: string,
      data: string,
      password?: string | null,
      successCallback?: () => void,
      errorCallback?: (error: { name?: string; message?: string }) => void
    ): void;
    getData(alias: { name: string }, password?: string | null): string;
    removeData(alias: { name: string }): void;
  };
}

interface WebOsServiceResponse {
  returnValue?: boolean;
  errorCode?: number;
  errorText?: string;
  handle?: string;
  iv?: string;
  output?: string;
}

interface WebOsService {
  request(
    uri: string,
    options: {
      method: string;
      parameters: Record<string, unknown>;
      onSuccess(response: WebOsServiceResponse): void;
      onFailure(error: WebOsServiceResponse): void;
    }
  ): unknown;
}

interface WebOsApi {
  service?: WebOsService;
}

interface Window {
  webapis?: SamsungWebApis;
  tizen?: SamsungTizen;
  webOS?: WebOsApi;
}
