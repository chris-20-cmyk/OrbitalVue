/**
 * Session identifiers are security sensitive: Plex and Emby accept playback reports keyed by the
 * play-session id, so a predictable value lets another client on the same server observe or spoof
 * a viewer's session. Every value here must come from a cryptographically secure source — never
 * from Math.random(), which is seeded predictably and is not safe in this context.
 */

function randomHex(byteLength: number): string {
  const source = globalThis.crypto;
  if (typeof source?.getRandomValues !== "function") {
    throw new Error("This device does not provide a secure random number generator.");
  }
  const bytes = source.getRandomValues(new Uint8Array(byteLength));
  let hex = "";
  for (const byte of bytes) hex += byte.toString(16).padStart(2, "0");
  return hex;
}

/**
 * Returns a unique, unpredictable playback-session identifier. Throws rather than falling back to
 * a weak generator: failing the playback report is safer than issuing a guessable session id.
 */
export function createSessionId(): string {
  const source = globalThis.crypto;
  if (typeof source?.randomUUID === "function") return source.randomUUID();
  return `orbitalvue-${randomHex(16)}`;
}
