import { requestJson, type MediaCenterHttpTransport } from "./http.js";
import { asArray, asBoolean, asNumber, asRecord, asString } from "./parse.js";
import { normalizeMediaCenterBaseUrl, requireIdentifier, withQuery } from "./url.js";

const PLEX_CLIENTS_BASE_URL = "https://clients.plex.tv/api/v2";
const PLEX_ACCOUNT_BASE_URL = "https://plex.tv/api/v2";

export interface PlexDevicePublicKey {
  kty: "OKP";
  crv: "Ed25519";
  x: string;
  kid: string;
  alg: "EdDSA";
}

/**
 * Platform implementations keep the Ed25519 private key in their secure key
 * store. Only the public JWK and compact signed JWT cross this boundary.
 */
export interface PlexDeviceSigner {
  publicKey: PlexDevicePublicKey;
  sign(payload: Readonly<Record<string, string | number>>): Promise<string>;
}

export interface PlexAccountClientConfiguration {
  clientIdentifier: string;
  product?: string;
  version?: string;
  now?: () => Date;
}

export interface PlexPinChallenge {
  id: number;
  code: string;
  authorizationUrl: string;
  expiresAt?: string;
}

export interface PlexAccountToken {
  token: string;
  expiresAt?: string;
}

export interface PlexServerConnection {
  uri: string;
  local: boolean;
  relay: boolean;
  secure: boolean;
  ipv6: boolean;
}

/** Contains a server-scoped access token and must never be cached in a catalog. */
export interface PlexDiscoveredServer {
  serverId: string;
  name: string;
  owned: boolean;
  accessToken: string;
  connections: PlexServerConnection[];
}

export class PlexAccountClient {
  private readonly headers: Record<string, string>;
  private readonly clientIdentifier: string;
  private readonly product: string;
  private readonly now: () => Date;

  constructor(
    private readonly transport: MediaCenterHttpTransport,
    configuration: PlexAccountClientConfiguration
  ) {
    this.clientIdentifier = requireIdentifier(
      configuration.clientIdentifier,
      "Plex client identifier"
    );
    this.product = safeHeaderValue(configuration.product, "StreamVue");
    this.now = configuration.now ?? (() => new Date());
    this.headers = {
      Accept: "application/json",
      "X-Plex-Client-Identifier": this.clientIdentifier,
      "X-Plex-Product": this.product,
      "X-Plex-Version": safeHeaderValue(configuration.version, "5.1.0")
    };
  }

  async createPin(signer: PlexDeviceSigner): Promise<PlexPinChallenge> {
    const publicKey = validatePublicKey(signer.publicKey);
    const payload = asRecord(await requestJson(this.transport, {
      method: "POST",
      url: `${PLEX_CLIENTS_BASE_URL}/pins`,
      headers: { ...this.headers, "Content-Type": "application/json" },
      body: JSON.stringify({ jwk: publicKey, strong: true })
    }));
    const id = asNumber(payload.id);
    const code = asString(payload.code);
    if (id === undefined || id < 1 || !Number.isInteger(id) || !code) {
      throw new TypeError("Plex returned an incomplete sign-in challenge.");
    }
    const expiresAt = parsePlexExpiry(payload, this.now());
    return {
      id,
      code,
      authorizationUrl: plexAuthorizationUrl(this.clientIdentifier, code, this.product),
      ...(expiresAt === undefined ? {} : { expiresAt })
    };
  }

  async claimPin(
    challenge: PlexPinChallenge,
    signer: PlexDeviceSigner
  ): Promise<PlexAccountToken | undefined> {
    validatePublicKey(signer.publicKey);
    if (!Number.isInteger(challenge.id) || challenge.id < 1) {
      throw new TypeError("The Plex sign-in challenge is invalid.");
    }
    const issuedAt = unixSeconds(this.now());
    const proof = validateCompactJwt(await signer.sign({
      aud: "plex.tv",
      iss: this.clientIdentifier,
      iat: issuedAt,
      exp: issuedAt + 300
    }));
    const url = withQuery(`${PLEX_CLIENTS_BASE_URL}/pins/${challenge.id}`, {
      deviceJWT: proof
    });
    const payload = asRecord(await requestJson(this.transport, {
      method: "GET",
      url,
      headers: this.headers
    }));
    return parseAccountToken(payload);
  }

  async refreshToken(signer: PlexDeviceSigner): Promise<PlexAccountToken> {
    validatePublicKey(signer.publicKey);
    const noncePayload = asRecord(await requestJson(this.transport, {
      method: "GET",
      url: `${PLEX_CLIENTS_BASE_URL}/auth/nonce`,
      headers: this.headers
    }));
    const nonce = asString(noncePayload.nonce);
    if (!nonce) throw new TypeError("Plex did not return a token-refresh nonce.");
    const issuedAt = unixSeconds(this.now());
    const proof = validateCompactJwt(await signer.sign({
      nonce,
      scope: "username,email,friendly_name",
      aud: "plex.tv",
      iss: this.clientIdentifier,
      iat: issuedAt,
      exp: issuedAt + 300
    }));
    const payload = asRecord(await requestJson(this.transport, {
      method: "POST",
      url: `${PLEX_CLIENTS_BASE_URL}/auth/token`,
      headers: { ...this.headers, "Content-Type": "application/json" },
      body: JSON.stringify({ jwt: proof })
    }));
    const token = parseAccountToken(payload);
    if (!token) throw new TypeError("Plex did not return a refreshed account token.");
    return token;
  }

  async verifyToken(token: string): Promise<void> {
    await requestJson(this.transport, {
      method: "GET",
      url: `${PLEX_ACCOUNT_BASE_URL}/user`,
      headers: this.authenticatedHeaders(token)
    });
  }

  async getServers(token: string): Promise<PlexDiscoveredServer[]> {
    const url = withQuery(`${PLEX_CLIENTS_BASE_URL}/resources`, {
      includeHttps: 1,
      includeRelay: 1,
      includeIPv6: 1
    });
    const payload = await requestJson<unknown>(this.transport, {
      method: "GET",
      url,
      headers: this.authenticatedHeaders(token)
    });
    return asArray(payload).flatMap(parsePlexServerResource);
  }

  private authenticatedHeaders(token: string): Record<string, string> {
    const value = token.trim();
    if (!value || /[\r\n]/.test(value) || value.length > 16_384) {
      throw new TypeError("The Plex account token is invalid.");
    }
    return { ...this.headers, "X-Plex-Token": value };
  }
}

export function selectPreferredPlexConnection(
  connections: readonly PlexServerConnection[]
): PlexServerConnection | undefined {
  return [...connections].sort((left, right) =>
    connectionPriority(left) - connectionPriority(right)
  )[0];
}

function parsePlexServerResource(value: unknown): PlexDiscoveredServer[] {
  const resource = asRecord(value);
  const provides = new Set(
    (asString(resource.provides) ?? "")
      .split(",")
      .map((entry) => entry.trim().toLowerCase())
      .filter(Boolean)
  );
  if (!provides.has("server")) return [];
  const serverId = asString(resource.clientIdentifier);
  const name = asString(resource.name);
  const accessToken = asString(resource.accessToken);
  if (!serverId || !name || !accessToken || /[\r\n]/.test(accessToken)) return [];
  const connections = asArray(resource.connections).flatMap(parsePlexConnection);
  if (connections.length === 0) return [];
  return [{
    serverId: requireIdentifier(serverId, "Plex server identifier"),
    name,
    owned: asBoolean(resource.owned),
    accessToken,
    connections: connections.sort((left, right) =>
      connectionPriority(left) - connectionPriority(right)
    )
  }];
}

function parsePlexConnection(value: unknown): PlexServerConnection[] {
  const connection = asRecord(value);
  const rawUri = asString(connection.uri);
  const rawProtocol = asString(connection.protocol)?.toLowerCase();
  const address = asString(connection.address);
  const port = asNumber(connection.port);
  let candidate = rawUri;
  if (!candidate && (rawProtocol === "http" || rawProtocol === "https") && address && port) {
    const host = address.includes(":") && !address.startsWith("[") ? `[${address}]` : address;
    candidate = `${rawProtocol}://${host}:${Math.floor(port)}`;
  }
  if (!candidate) return [];
  try {
    const uri = normalizeMediaCenterBaseUrl(candidate);
    const parsed = new URL(uri);
    return [{
      uri,
      local: asBoolean(connection.local),
      relay: asBoolean(connection.relay),
      secure: parsed.protocol === "https:",
      ipv6: asBoolean(connection.IPv6) || parsed.hostname.includes(":")
    }];
  } catch {
    return [];
  }
}

function validatePublicKey(value: PlexDevicePublicKey): PlexDevicePublicKey {
  if (value.kty !== "OKP" || value.crv !== "Ed25519" || value.alg !== "EdDSA") {
    throw new TypeError("Plex requires an Ed25519 device signing key.");
  }
  if (!/^[A-Za-z0-9_-]{40,64}$/.test(value.x)) {
    throw new TypeError("The Plex device public key is invalid.");
  }
  requireIdentifier(value.kid, "Plex device key identifier");
  return { ...value };
}

function validateCompactJwt(value: string): string {
  const token = value.trim();
  if (token.length > 16_384 || !/^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(token)) {
    throw new TypeError("The Plex device proof is not a compact signed JWT.");
  }
  return token;
}

function parseAccountToken(payload: Record<string, unknown>): PlexAccountToken | undefined {
  const token = asString(payload.authToken ?? payload.auth_token);
  if (!token || /[\r\n]/.test(token) || token.length > 16_384) return undefined;
  const expiresAt = parsePlexExpiry(payload);
  return { token, ...(expiresAt === undefined ? {} : { expiresAt }) };
}

function parsePlexExpiry(
  payload: Record<string, unknown>,
  now = new Date()
): string | undefined {
  const explicit = asString(payload.expiresAt ?? payload.expires_at);
  if (explicit && !Number.isNaN(Date.parse(explicit))) return new Date(explicit).toISOString();
  const seconds = asNumber(payload.expiresIn ?? payload.expires_in);
  if (seconds === undefined || seconds <= 0) return undefined;
  return new Date(now.getTime() + Math.floor(seconds) * 1_000).toISOString();
}

function plexAuthorizationUrl(clientIdentifier: string, code: string, product: string): string {
  const query = new URLSearchParams({
    clientID: clientIdentifier,
    code,
    "context[device][product]": product
  });
  return `https://app.plex.tv/auth#?${query.toString()}`;
}

function connectionPriority(connection: PlexServerConnection): number {
  return (connection.local ? 0 : 100)
    + (connection.relay ? 50 : 0)
    + (connection.secure ? 0 : 10)
    + (connection.ipv6 ? 1 : 0);
}

function safeHeaderValue(value: string | undefined, fallback: string): string {
  const sanitized = value?.replace(/[\r\n]/g, "").trim().slice(0, 256);
  return sanitized || fallback;
}

function unixSeconds(value: Date): number {
  const milliseconds = value.getTime();
  if (!Number.isFinite(milliseconds)) throw new TypeError("The current time is invalid.");
  return Math.floor(milliseconds / 1_000);
}
