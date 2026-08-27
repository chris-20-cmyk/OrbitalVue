const DEFAULT_MAX_RESPONSE_BYTES = 16 * 1024 * 1024;

export interface MediaCenterHttpRequest {
  method: "GET" | "POST" | "DELETE";
  url: string;
  headers: Record<string, string>;
  body?: string;
}

export interface MediaCenterHttpResponse {
  status: number;
  body: string;
}

export type MediaCenterHttpTransport = (
  request: MediaCenterHttpRequest
) => Promise<MediaCenterHttpResponse>;

export class MediaCenterHttpError extends Error {
  override readonly name = "MediaCenterHttpError";

  constructor(
    message: string,
    public readonly status: number,
    public readonly code: "http" | "invalid-json" | "response-too-large" | "invalid-response"
  ) {
    super(message);
  }
}

export function createFetchTransport(
  fetchImplementation: typeof fetch = globalThis.fetch,
  maxResponseBytes = DEFAULT_MAX_RESPONSE_BYTES
): MediaCenterHttpTransport {
  return async (request) => {
    if (typeof fetchImplementation !== "function") {
      throw new TypeError("This platform does not provide a fetch implementation.");
    }
    const init: RequestInit = {
      method: request.method,
      headers: request.headers,
      redirect: "error"
    };
    if (request.body !== undefined) init.body = request.body;
    const response = await fetchImplementation(request.url, init);
    const declaredLength = Number(response.headers.get("content-length"));
    if (Number.isFinite(declaredLength) && declaredLength > maxResponseBytes) {
      await response.body?.cancel();
      throw responseTooLarge(response.status, maxResponseBytes);
    }
    return {
      status: response.status,
      body: await readBoundedResponseBody(response, maxResponseBytes)
    };
  };
}

export async function requestJson<T>(
  transport: MediaCenterHttpTransport,
  request: MediaCenterHttpRequest,
  maxResponseBytes = DEFAULT_MAX_RESPONSE_BYTES
): Promise<T> {
  const response = await transport(request);
  if (response.status < 200 || response.status >= 300) {
    throw new MediaCenterHttpError(
      `The media server returned HTTP ${response.status}.`,
      response.status,
      "http"
    );
  }
  if (new TextEncoder().encode(response.body).byteLength > maxResponseBytes) {
    throw responseTooLarge(response.status, maxResponseBytes);
  }
  try {
    return JSON.parse(response.body) as T;
  } catch {
    throw new MediaCenterHttpError(
      "The media server returned invalid JSON.",
      response.status,
      "invalid-json"
    );
  }
}

async function readBoundedResponseBody(
  response: Response,
  maxResponseBytes: number
): Promise<string> {
  if (!response.body) return "";
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let byteCount = 0;
  let body = "";
  try {
    while (true) {
      const chunk = await reader.read();
      if (chunk.done) break;
      byteCount += chunk.value.byteLength;
      if (byteCount > maxResponseBytes) {
        await reader.cancel();
        throw responseTooLarge(response.status, maxResponseBytes);
      }
      body += decoder.decode(chunk.value, { stream: true });
    }
    body += decoder.decode();
    return body;
  } finally {
    reader.releaseLock();
  }
}

function responseTooLarge(status: number, maxResponseBytes: number): MediaCenterHttpError {
  const limitMiB = Math.max(0, maxResponseBytes) / (1024 * 1024);
  return new MediaCenterHttpError(
    `The media server response exceeded the ${limitMiB.toFixed(1)} MiB safety limit.`,
    status,
    "response-too-large"
  );
}
