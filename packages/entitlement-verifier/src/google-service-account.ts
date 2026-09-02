import { isRecord } from "./contracts.js";
import { readBoundedJson } from "./google-play.js";

const ANDROID_PUBLISHER_SCOPE = "https://www.googleapis.com/auth/androidpublisher";
const DEFAULT_TOKEN_ENDPOINT = "https://oauth2.googleapis.com/token";
const TOKEN_RESPONSE_LIMIT = 32 * 1024;

export interface GoogleServiceAccountCredentials {
  clientEmail: string;
  privateKey: string;
}

export interface GoogleServiceAccountTokenProviderOptions {
  fetcher?: typeof fetch;
  crypto?: Crypto;
  now?: () => number;
  tokenEndpoint?: string;
}

export function createGoogleServiceAccountTokenProvider(
  credentials: GoogleServiceAccountCredentials,
  options: GoogleServiceAccountTokenProviderOptions = {}
): () => Promise<string> {
  const fetcher = options.fetcher ?? globalThis.fetch.bind(globalThis);
  const webCrypto = options.crypto ?? globalThis.crypto;
  const now = options.now ?? Date.now;
  const tokenEndpoint = normalizeTokenEndpoint(options.tokenEndpoint ?? DEFAULT_TOKEN_ENDPOINT);
  if (!/^[^\s@]+@[^\s@]+$/.test(credentials.clientEmail) || credentials.clientEmail.length > 320) {
    throw new Error("Google service-account client email is invalid.");
  }
  const privateKeyBytes = parsePkcs8Pem(credentials.privateKey);
  let cached: { token: string; expiresAt: number } | null = null;
  let inFlight: Promise<string> | null = null;

  return async () => {
    const current = now();
    if (cached && cached.expiresAt - 60_000 > current) return cached.token;
    if (inFlight) return inFlight;
    inFlight = acquireToken().finally(() => { inFlight = null; });
    return inFlight;
  };

  async function acquireToken(): Promise<string> {
    const issuedAt = Math.floor(now() / 1000);
    const key = await webCrypto.subtle.importKey(
      "pkcs8",
      privateKeyBytes,
      { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
      false,
      ["sign"]
    );
    const header = base64UrlJson({ alg: "RS256", typ: "JWT" });
    const claims = base64UrlJson({
      iss: credentials.clientEmail,
      scope: ANDROID_PUBLISHER_SCOPE,
      aud: tokenEndpoint,
      iat: issuedAt,
      exp: issuedAt + 3600
    });
    const unsigned = `${header}.${claims}`;
    const signature = await webCrypto.subtle.sign(
      "RSASSA-PKCS1-v1_5",
      key,
      new TextEncoder().encode(unsigned)
    );
    const assertion = `${unsigned}.${base64UrlBytes(new Uint8Array(signature))}`;
    const response = await fetcher(tokenEndpoint, {
      method: "POST",
      headers: {
        Accept: "application/json",
        "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8"
      },
      body: new URLSearchParams({
        grant_type: "urn:ietf:params:oauth:grant-type:jwt-bearer",
        assertion
      }),
      cache: "no-store",
      redirect: "error"
    });
    if (!response.ok) throw new Error(`Google OAuth token service returned HTTP ${response.status}.`);
    const raw = await readBoundedJson(response, TOKEN_RESPONSE_LIMIT, "Google OAuth token service");
    if (!isRecord(raw)
      || typeof raw.access_token !== "string"
      || raw.access_token.length === 0
      || raw.access_token.length > 8192
      || raw.token_type !== "Bearer"
      || typeof raw.expires_in !== "number"
      || !Number.isFinite(raw.expires_in)
      || raw.expires_in < 60
      || raw.expires_in > 7200) {
      throw new Error("Google OAuth token service returned an invalid token response.");
    }
    cached = { token: raw.access_token, expiresAt: now() + raw.expires_in * 1000 };
    return cached.token;
  }
}

function parsePkcs8Pem(value: string): ArrayBuffer {
  // The body class already covers whitespace, so it must not be wrapped in further \s+ quantifiers:
  // the overlap makes the match ambiguous and lets a malformed key backtrack in polynomial time.
  const match = value.trim().match(/^-----BEGIN PRIVATE KEY-----([A-Za-z0-9+/=\s]*)-----END PRIVATE KEY-----$/);
  if (!match?.[1]) throw new Error("Google service-account private key must be PKCS#8 PEM.");
  const compact = match[1].replace(/\s+/g, "");
  if (!/^[A-Za-z0-9+/]+={0,2}$/.test(compact)) throw new Error("Google service-account private key is invalid.");
  const decoded = atob(compact);
  return Uint8Array.from(decoded, (character) => character.charCodeAt(0)).buffer;
}

function base64UrlJson(value: Record<string, unknown>): string {
  return base64UrlBytes(new TextEncoder().encode(JSON.stringify(value)));
}

function base64UrlBytes(value: Uint8Array): string {
  let binary = "";
  for (const byte of value) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "");
}

function normalizeTokenEndpoint(value: string): string {
  const url = new URL(value);
  if (url.protocol !== "https:" || !url.hostname || url.username || url.password || url.search || url.hash) {
    throw new Error("Google OAuth token endpoint must be a clean HTTPS URL.");
  }
  return url.toString();
}
