import type { MediaCenterConnection, MediaCenterProvider } from "./types.js";
import { MEDIA_CENTER_CONTRACT_VERSION } from "./types.js";
import { normalizeMediaCenterBaseUrl, requireIdentifier } from "./url.js";

/**
 * Non-secret metadata stored atomically with a token in the platform vault.
 * Callers must retrieve this binding from that protected record, never rebuild
 * it from a mutable catalog connection when opening a client.
 */
export interface MediaCenterCredentialBinding {
  contractVersion: typeof MEDIA_CENTER_CONTRACT_VERSION;
  provider: MediaCenterProvider;
  serverId: string;
  baseUrl: string;
  credentialId: string;
  userId?: string;
  allowInsecureHttp: boolean;
}

export function createMediaCenterCredentialBinding(
  connection: MediaCenterConnection,
  allowInsecureHttp = false
): MediaCenterCredentialBinding {
  const baseUrl = normalizeMediaCenterBaseUrl(connection.baseUrl);
  requireAllowedTransport(baseUrl, allowInsecureHttp);
  const serverId = requireIdentifier(connection.serverId, "media-center server identifier");
  const credentialId = requireIdentifier(
    connection.credentialId,
    "secure credential reference"
  );
  const userId = connection.userId === undefined
    ? undefined
    : requireIdentifier(connection.userId, "media-center user identifier");
  return {
    contractVersion: MEDIA_CENTER_CONTRACT_VERSION,
    provider: connection.provider,
    serverId,
    baseUrl,
    credentialId,
    ...(userId === undefined ? {} : { userId }),
    allowInsecureHttp
  };
}

export function assertMediaCenterCredentialBinding(
  connection: MediaCenterConnection,
  binding: MediaCenterCredentialBinding
): void {
  const expected = createMediaCenterCredentialBinding(
    connection,
    binding.allowInsecureHttp
  );
  if (binding.contractVersion !== expected.contractVersion
    || binding.provider !== expected.provider
    || binding.serverId !== expected.serverId
    || binding.baseUrl !== expected.baseUrl
    || binding.credentialId !== expected.credentialId
    || binding.userId !== expected.userId) {
    throw new TypeError(
      "The protected media-center credential does not belong to this server connection."
    );
  }
}

export function requireAllowedTransport(baseUrl: string, allowInsecureHttp: boolean): void {
  const url = new URL(normalizeMediaCenterBaseUrl(baseUrl));
  if (url.protocol === "http:" && !allowInsecureHttp) {
    throw new TypeError(
      "This media server uses unencrypted HTTP. Confirm the insecure local connection before saving credentials."
    );
  }
}
