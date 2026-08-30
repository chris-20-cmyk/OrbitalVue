import {
  GOOGLE_PLAY_ROUTE,
  SAMSUNG_ROUTE,
  VerifierContractError,
  parseGooglePlayRequest,
  parseSamsungStatusRequest
} from "./contracts.js";
import {
  verifyGooglePlayPurchase,
  type GooglePlayPublisher,
  type GooglePlayVerifierConfig
} from "./google-play.js";
import {
  verifySamsungStatus,
  type SamsungDpiProvider,
  type SamsungDpiVerifierConfig
} from "./samsung-dpi.js";

const MAX_REQUEST_BYTES = 32 * 1024;

export interface EntitlementVerifierConfiguration {
  googlePlay?: {
    config: GooglePlayVerifierConfig;
    publisher: GooglePlayPublisher;
  };
  samsung?: {
    config: SamsungDpiVerifierConfig;
    dpi: SamsungDpiProvider;
  };
}

export type EntitlementVerifierHandler = (request: Request) => Promise<Response>;

export function createEntitlementVerifierHandler(
  configuration: EntitlementVerifierConfiguration
): EntitlementVerifierHandler {
  return async (request) => {
    const url = new URL(request.url);
    if (url.search || url.hash) return errorResponse(404, "not-found");
    if (request.method !== "POST") return errorResponse(405, "method-not-allowed", { Allow: "POST" });
    if (!request.headers.get("content-type")?.toLowerCase().startsWith("application/json")) {
      return errorResponse(415, "unsupported-media-type");
    }
    try {
      const body = await readBoundedRequestJson(request);
      if (url.pathname === GOOGLE_PLAY_ROUTE) {
        if (!configuration.googlePlay) return errorResponse(503, "provider-unavailable");
        const parsed = parseGooglePlayRequest(body);
        const result = await verifyGooglePlayPurchase(
          parsed,
          configuration.googlePlay.config,
          configuration.googlePlay.publisher
        );
        return jsonResponse(200, result);
      }
      if (url.pathname === SAMSUNG_ROUTE) {
        if (!configuration.samsung) return errorResponse(503, "provider-unavailable");
        const parsed = parseSamsungStatusRequest(body);
        const result = await verifySamsungStatus(
          parsed,
          configuration.samsung.config,
          configuration.samsung.dpi
        );
        return jsonResponse(200, result);
      }
      return errorResponse(404, "not-found");
    } catch (error) {
      if (error instanceof VerifierContractError) return errorResponse(400, "invalid-request");
      return errorResponse(503, "verification-unavailable");
    }
  };
}

async function readBoundedRequestJson(request: Request): Promise<unknown> {
  const announced = Number(request.headers.get("content-length"));
  if (Number.isFinite(announced) && announced > MAX_REQUEST_BYTES) {
    throw new VerifierContractError("Request body is too large.");
  }
  const body = await request.text();
  if (body.length === 0 || new TextEncoder().encode(body).length > MAX_REQUEST_BYTES) {
    throw new VerifierContractError("Request body size is invalid.");
  }
  try {
    return JSON.parse(body) as unknown;
  } catch {
    throw new VerifierContractError("Request body is not valid JSON.");
  }
}

function errorResponse(status: number, code: string, headers?: HeadersInit): Response {
  return jsonResponse(status, { schemaVersion: 1, error: code }, headers);
}

function jsonResponse(status: number, body: unknown, extraHeaders?: HeadersInit): Response {
  const headers = new Headers(extraHeaders);
  headers.set("Cache-Control", "no-store, max-age=0");
  headers.set("Content-Type", "application/json; charset=utf-8");
  headers.set("Referrer-Policy", "no-referrer");
  headers.set("X-Content-Type-Options", "nosniff");
  return new Response(JSON.stringify(body), { status, headers });
}
