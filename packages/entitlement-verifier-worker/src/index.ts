import {
  GOOGLE_PLAY_ROUTE,
  SAMSUNG_ROUTE,
  GooglePlayPublisherHttpClient,
  SamsungDpiHttpClient,
  VerifierContractError,
  createEntitlementVerifierHandler,
  createGoogleServiceAccountTokenProvider,
  parseGooglePlayRequest,
  parseSamsungStatusRequest
} from "@streamvue/entitlement-verifier";

const HEALTH_ROUTE = "/healthz";
const MAX_REQUEST_BYTES = 32 * 1024;
const RATE_LIMIT_WINDOW_SECONDS = 60;
const VERIFICATION_ROUTES = new Set([GOOGLE_PLAY_ROUTE, SAMSUNG_ROUTE]);

class WorkerBoundaryError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string
  ) {
    super(message);
    this.name = "WorkerBoundaryError";
  }
}

export async function handleEntitlementVerifierWorkerRequest(
  request: Request,
  env: Env
): Promise<Response> {
  const url = new URL(request.url);
  let allowedOrigin: string | null = null;

  try {
    assertExpectedHost(url, env.EXPECTED_HOSTNAME);
    allowedOrigin = authorizeBrowserOrigin(
      request.headers.get("Origin"),
      env.ALLOWED_BROWSER_ORIGINS,
      env.DEPLOYMENT_ENVIRONMENT === "local"
    );

    if (url.search || url.hash) return secure(errorResponse(404, "not-found"), allowedOrigin);
    if (url.pathname === HEALTH_ROUTE) {
      if (request.method !== "GET") {
        return secure(errorResponse(405, "method-not-allowed", { Allow: "GET" }), allowedOrigin);
      }
      return secure(jsonResponse(200, {
        schemaVersion: 1,
        service: "streamvue-entitlement-verifier",
        status: "available"
      }), allowedOrigin);
    }
    if (!VERIFICATION_ROUTES.has(url.pathname)) {
      return secure(errorResponse(404, "not-found"), allowedOrigin);
    }
    if (request.method === "OPTIONS") {
      if (allowedOrigin === null) {
        return secure(errorResponse(403, "origin-required"), null);
      }
      return secure(new Response(null, {
        status: 204,
        headers: {
          "Access-Control-Allow-Headers": "accept, content-type",
          "Access-Control-Allow-Methods": "POST, OPTIONS",
          "Access-Control-Max-Age": "600"
        }
      }), allowedOrigin);
    }
    if (request.method !== "POST") {
      return secure(errorResponse(405, "method-not-allowed", { Allow: "POST, OPTIONS" }), allowedOrigin);
    }
    if (!request.headers.get("content-type")?.toLowerCase().startsWith("application/json")) {
      return secure(errorResponse(415, "unsupported-media-type"), allowedOrigin);
    }

    assertDeploymentConfiguration(url.pathname, env);
    const providerRateLimit = await env.PROVIDER_RATE_LIMITER.limit({ key: `provider:${url.pathname}` });
    if (!providerRateLimit.success) {
      return rateLimitedResponse(url.pathname, "provider", env.DEPLOYMENT_ENVIRONMENT, allowedOrigin);
    }
    const rateLimitKey = await createRateLimitKey(request.clone(), url.pathname, env.RATE_LIMIT_KEY_SECRET);
    const rateLimit = await env.VERIFICATION_RATE_LIMITER.limit({ key: rateLimitKey });
    if (!rateLimit.success) {
      return rateLimitedResponse(url.pathname, "purchaser", env.DEPLOYMENT_ENVIRONMENT, allowedOrigin);
    }

    const handler = createRouteHandler(url.pathname, env);
    const response = await handler(request);
    if (response.status >= 500) {
      console.error(JSON.stringify({
        event: "verification-unavailable",
        route: url.pathname,
        status: response.status,
        environment: safeEnvironmentLabel(env.DEPLOYMENT_ENVIRONMENT)
      }));
    }
    return secure(response, allowedOrigin);
  } catch (error) {
    if (error instanceof WorkerBoundaryError) {
      return secure(errorResponse(error.status, error.code), allowedOrigin);
    }
    console.error(JSON.stringify({
      event: "worker-boundary-failure",
      route: safeRoute(url.pathname),
      status: 503,
      environment: safeEnvironmentLabel(env.DEPLOYMENT_ENVIRONMENT)
    }));
    return secure(errorResponse(503, "verification-unavailable"), allowedOrigin);
  }
}

function rateLimitedResponse(
  route: string,
  scope: "provider" | "purchaser",
  environment: string,
  allowedOrigin: string | null
): Response {
  console.warn(JSON.stringify({
    event: "verification-rate-limited",
    route,
    scope,
    environment: safeEnvironmentLabel(environment)
  }));
  return secure(errorResponse(429, "rate-limit-exceeded", {
    "Retry-After": String(RATE_LIMIT_WINDOW_SECONDS)
  }), allowedOrigin);
}

function createRouteHandler(pathname: string, env: Env) {
  if (pathname === GOOGLE_PLAY_ROUTE) {
    const getAccessToken = createGoogleServiceAccountTokenProvider({
      clientEmail: env.GOOGLE_SERVICE_ACCOUNT_EMAIL,
      privateKey: env.GOOGLE_SERVICE_ACCOUNT_PRIVATE_KEY
    });
    return createEntitlementVerifierHandler({
      googlePlay: {
        config: {
          packageName: env.GOOGLE_PLAY_PACKAGE_NAME,
          productId: env.GOOGLE_PLAY_PRODUCT_ID,
          allowTestPurchases: parseBooleanSetting(env.GOOGLE_PLAY_ALLOW_TEST_PURCHASES)
        },
        publisher: new GooglePlayPublisherHttpClient({ getAccessToken })
      }
    });
  }

  return createEntitlementVerifierHandler({
    samsung: {
      config: {
        appId: env.SAMSUNG_CHECKOUT_APP_ID,
        productId: env.SAMSUNG_PREMIUM_PRODUCT_ID
      },
      dpi: new SamsungDpiHttpClient({
        appId: env.SAMSUNG_CHECKOUT_APP_ID,
        productId: env.SAMSUNG_PREMIUM_PRODUCT_ID,
        securityKey: env.SAMSUNG_DPI_SECURITY_KEY
      })
    }
  });
}

function assertExpectedHost(url: URL, configuredHostname: string): void {
  const expected = normalizeHostname(configuredHostname);
  if (url.hostname.toLowerCase() !== expected) {
    throw new WorkerBoundaryError(421, "misdirected-request", "Request host does not match this deployment.");
  }
}

function normalizeHostname(value: string): string {
  if (!value || value !== value.trim() || value !== value.toLowerCase() || value.length > 253) {
    throw new Error("EXPECTED_HOSTNAME is invalid.");
  }
  const parsed = new URL(`https://${value}`);
  if (parsed.hostname !== value || parsed.port || parsed.pathname !== "/" || parsed.search || parsed.hash) {
    throw new Error("EXPECTED_HOSTNAME is invalid.");
  }
  return value;
}

function authorizeBrowserOrigin(
  origin: string | null,
  serializedAllowlist: string,
  allowHttp: boolean
): string | null {
  const allowlist = parseBrowserOriginAllowlist(serializedAllowlist, allowHttp);
  if (origin === null) return null;
  let normalized: string;
  try {
    normalized = normalizeBrowserOrigin(origin, allowHttp);
  } catch {
    throw new WorkerBoundaryError(403, "origin-not-allowed", "Browser origin is not allowed.");
  }
  if (!allowlist.has(normalized)) {
    throw new WorkerBoundaryError(403, "origin-not-allowed", "Browser origin is not allowed.");
  }
  return normalized;
}

function parseBrowserOriginAllowlist(value: string, allowHttp: boolean): Set<string> {
  let parsed: unknown;
  try {
    parsed = JSON.parse(value) as unknown;
  } catch {
    throw new Error("ALLOWED_BROWSER_ORIGINS must be a JSON array.");
  }
  if (!Array.isArray(parsed) || parsed.length > 20 || !parsed.every((entry) => typeof entry === "string")) {
    throw new Error("ALLOWED_BROWSER_ORIGINS must contain at most 20 origins.");
  }
  const normalized = parsed.map((origin) => normalizeBrowserOrigin(origin, allowHttp));
  if (new Set(normalized).size !== normalized.length) {
    throw new Error("ALLOWED_BROWSER_ORIGINS contains duplicates.");
  }
  return new Set(normalized);
}

function normalizeBrowserOrigin(value: string, allowHttp: boolean): string {
  if (!value || value !== value.trim() || value === "null" || value.length > 512) {
    throw new Error("Browser origin is invalid.");
  }
  const url = new URL(value);
  if (url.protocol !== "https:" && !(allowHttp && url.protocol === "http:")
    || !url.hostname
    || url.username
    || url.password
    || url.pathname !== "/"
    || url.search
    || url.hash
    || url.origin !== value) {
    throw new Error("Browser origin must be an exact HTTP(S) origin.");
  }
  return url.origin;
}

function assertDeploymentConfiguration(pathname: string, env: Env): void {
  if (!["local", "staging", "production"].includes(env.DEPLOYMENT_ENVIRONMENT)) {
    throw new Error("DEPLOYMENT_ENVIRONMENT is invalid.");
  }
  if (env.RATE_LIMIT_KEY_SECRET.length < 32
    || env.RATE_LIMIT_KEY_SECRET.length > 4096
    || /[\u0000-\u001F\u007F]/.test(env.RATE_LIMIT_KEY_SECRET)) {
    throw new Error("RATE_LIMIT_KEY_SECRET is invalid.");
  }
  if (pathname === GOOGLE_PLAY_ROUTE) {
    assertNotPlaceholder(env.GOOGLE_PLAY_PRODUCT_ID, "GOOGLE_PLAY_PRODUCT_ID");
    const allowTestPurchases = parseBooleanSetting(env.GOOGLE_PLAY_ALLOW_TEST_PURCHASES);
    if (env.DEPLOYMENT_ENVIRONMENT === "production" && allowTestPurchases) {
      throw new Error("Production cannot allow Google Play test purchases.");
    }
    return;
  }
  assertNotPlaceholder(env.SAMSUNG_CHECKOUT_APP_ID, "SAMSUNG_CHECKOUT_APP_ID");
  assertNotPlaceholder(env.SAMSUNG_PREMIUM_PRODUCT_ID, "SAMSUNG_PREMIUM_PRODUCT_ID");
}

function assertNotPlaceholder(value: string, name: string): void {
  if (!value || value.startsWith("REPLACE_")) throw new Error(`${name} is not configured.`);
}

function parseBooleanSetting(value: string): boolean {
  if (value === "true") return true;
  if (value === "false") return false;
  throw new Error("Boolean Worker setting is invalid.");
}

async function createRateLimitKey(request: Request, pathname: string, secret: string): Promise<string> {
  const value = await readBoundedJson(request);
  let identity: string;
  let platform: string;
  try {
    if (pathname === GOOGLE_PLAY_ROUTE) {
      const parsed = parseGooglePlayRequest(value);
      identity = `${parsed.productId}\u0000${parsed.purchaseToken}`;
      platform = "google-play";
    } else {
      const parsed = parseSamsungStatusRequest(value);
      identity = `${parsed.productId}\u0000${parsed.customId}`;
      platform = "samsung";
    }
  } catch (error) {
    if (error instanceof VerifierContractError) {
      throw new WorkerBoundaryError(400, "invalid-request", "Verification request is invalid.");
    }
    throw error;
  }

  const encoder = new TextEncoder();
  const key = await crypto.subtle.importKey(
    "raw",
    encoder.encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const signature = await crypto.subtle.sign("HMAC", key, encoder.encode(identity));
  return `${platform}:${bytesToHex(new Uint8Array(signature))}`;
}

async function readBoundedJson(request: Request): Promise<unknown> {
  const announced = request.headers.get("content-length");
  if (announced !== null) {
    if (!/^\d+$/.test(announced) || Number(announced) > MAX_REQUEST_BYTES) {
      throw new WorkerBoundaryError(413, "request-too-large", "Request body is too large.");
    }
  }
  if (request.body === null) {
    throw new WorkerBoundaryError(400, "invalid-request", "Request body is missing.");
  }

  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let totalBytes = 0;
  while (true) {
    const result = await reader.read();
    if (result.done) break;
    totalBytes += result.value.byteLength;
    if (totalBytes > MAX_REQUEST_BYTES) {
      await reader.cancel();
      throw new WorkerBoundaryError(413, "request-too-large", "Request body is too large.");
    }
    chunks.push(result.value);
  }
  if (totalBytes === 0) {
    throw new WorkerBoundaryError(400, "invalid-request", "Request body is missing.");
  }
  const bytes = new Uint8Array(totalBytes);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  try {
    const text = new TextDecoder("utf-8", { fatal: true, ignoreBOM: false }).decode(bytes);
    return JSON.parse(text) as unknown;
  } catch {
    throw new WorkerBoundaryError(400, "invalid-request", "Request body is not valid UTF-8 JSON.");
  }
}

function bytesToHex(bytes: Uint8Array): string {
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
}

function errorResponse(status: number, code: string, headers?: HeadersInit): Response {
  return jsonResponse(status, { schemaVersion: 1, error: code }, headers);
}

function jsonResponse(status: number, body: unknown, extraHeaders?: HeadersInit): Response {
  const headers = new Headers(extraHeaders);
  headers.set("Content-Type", "application/json; charset=utf-8");
  return new Response(JSON.stringify(body), { status, headers });
}

function secure(response: Response, allowedOrigin: string | null): Response {
  const secured = new Response(response.body, response);
  secured.headers.set("Cache-Control", "no-store, max-age=0");
  secured.headers.set("Content-Security-Policy", "default-src 'none'; base-uri 'none'; frame-ancestors 'none'");
  secured.headers.set("Permissions-Policy", "camera=(), geolocation=(), microphone=(), payment=()");
  secured.headers.set("Referrer-Policy", "no-referrer");
  secured.headers.set("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
  secured.headers.set("X-Content-Type-Options", "nosniff");
  secured.headers.set("X-Frame-Options", "DENY");
  secured.headers.set("X-Robots-Tag", "noindex, nofollow");
  appendVary(secured.headers, "Origin");
  if (allowedOrigin !== null) secured.headers.set("Access-Control-Allow-Origin", allowedOrigin);
  return secured;
}

function appendVary(headers: Headers, value: string): void {
  const existing = headers.get("Vary")?.split(",").map((entry) => entry.trim()).filter(Boolean) ?? [];
  if (!existing.some((entry) => entry.toLowerCase() === value.toLowerCase())) existing.push(value);
  headers.set("Vary", existing.join(", "));
}

function safeRoute(pathname: string): string {
  return VERIFICATION_ROUTES.has(pathname) || pathname === HEALTH_ROUTE ? pathname : "unknown";
}

function safeEnvironmentLabel(value: string): string {
  return ["local", "staging", "production"].includes(value) ? value : "unknown";
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    return handleEntitlementVerifierWorkerRequest(request, env);
  }
} satisfies ExportedHandler<Env>;
