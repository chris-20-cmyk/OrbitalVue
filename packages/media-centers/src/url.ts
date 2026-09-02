const IDENTIFIER_PATTERN = /^[A-Za-z0-9._:-]{1,256}$/;

export function normalizeMediaCenterBaseUrl(input: string): string {
  const trimmed = input.trim();
  if (!trimmed) throw new TypeError("Enter a media-center server address.");
  const candidate = /^[A-Za-z][A-Za-z0-9+.-]*:\/\//.test(trimmed)
    ? trimmed
    : `https://${trimmed}`;
  const url = new URL(candidate);
  if (url.protocol !== "http:" && url.protocol !== "https:") {
    throw new TypeError("Media-center servers must use HTTP or HTTPS.");
  }
  if (url.username || url.password) {
    throw new TypeError("Do not put credentials in the media-center server address.");
  }
  if (url.search || url.hash) {
    throw new TypeError("The media-center server address cannot contain a query or fragment.");
  }
  url.pathname = url.pathname.replace(/\/+$/, "");
  return url.toString().replace(/\/$/, "");
}

export function safeServerDisplayLocation(baseUrl: string): string {
  const url = new URL(normalizeMediaCenterBaseUrl(baseUrl));
  const path = url.pathname === "/" ? "" : url.pathname;
  return `${url.host}${path}`;
}

export function resolveServerPath(baseUrl: string, path: string): string {
  const base = new URL(`${normalizeMediaCenterBaseUrl(baseUrl)}/`);
  const resolved = new URL(path.replace(/^\/+/, ""), base);
  if (resolved.origin !== base.origin) {
    throw new TypeError("The media server returned a cross-origin playback address.");
  }
  resolved.username = "";
  resolved.password = "";
  resolved.hash = "";
  for (const key of [...resolved.searchParams.keys()]) {
    if (isSensitiveQueryParameter(key)) resolved.searchParams.delete(key);
  }
  return resolved.toString();
}

export function sanitizeServerPathForStorage(baseUrl: string, path: string): string {
  const normalizedBase = normalizeMediaCenterBaseUrl(baseUrl);
  const base = new URL(`${normalizedBase}/`);
  const resolved = new URL(resolveServerPath(normalizedBase, path));
  if (!resolved.pathname.startsWith(base.pathname)) {
    throw new TypeError("The media server returned a path outside its configured API root.");
  }
  const relativePath = `/${resolved.pathname.slice(base.pathname.length)}`;
  const storedPath = `${relativePath}${resolved.search}`;
  if (storedPath.length > 2_048) {
    throw new TypeError("The media server returned an excessively long resource path.");
  }
  return storedPath;
}

export function requireIdentifier(value: string, label: string): string {
  const trimmed = value.trim();
  if (!IDENTIFIER_PATTERN.test(trimmed)) {
    throw new TypeError(`The ${label} is not a safe identifier.`);
  }
  return trimmed;
}

export function withQuery(url: string, values: Record<string, string | number | boolean>): string {
  const result = new URL(url);
  for (const [key, value] of Object.entries(values)) {
    result.searchParams.set(key, String(value));
  }
  return result.toString();
}

function isSensitiveQueryParameter(key: string): boolean {
  return [
    "api_key",
    "api-key",
    "apikey",
    "access_token",
    "auth",
    "password",
    "pw",
    "token",
    "username",
    "x-emby-authorization",
    "x-emby-token",
    "x-plex-token"
  ].includes(key.toLowerCase());
}
