# StreamVue media-center adapters

This private workspace package is the portable reference layer for Plex and Emby. It maps authenticated library metadata into StreamVue's catalog contract while keeping passwords, account tokens, server tokens, request headers, and playback sessions out of cached JSON.

## Plex

`PlexAccountClient` implements Plex's current signed PIN flow:

1. A platform creates an Ed25519 key in its secure key store and supplies a `PlexDeviceSigner`.
2. StreamVue creates a strong PIN and opens the returned Plex authorization URL.
3. StreamVue exchanges a device-signed JWT for the short-lived Plex account token.
4. The account token discovers available servers and their server-scoped tokens.
5. Only an opaque `credentialId` is stored in the portable connection. The key and tokens remain in Keychain, Windows protected storage, Android Keystore-backed storage, or the television's protected adapter.

The same signer refreshes the Plex account JWT from a server nonce before it expires. A server-scoped token is placed in `X-Plex-Token` only when resolving a request.

## Emby

`authenticateEmby` first verifies the server's public identity without sending a password or token, then exchanges a user name and password directly with the user-provided Emby server. The password is never returned by the adapter. The resulting token is stored by the platform vault, while `EmbyClient` exposes safe browse metadata and ephemeral playback plans.

## Credential and transport boundary

Every protected token must be stored atomically with the `MediaCenterCredentialBinding` returned by `createMediaCenterCredentialBinding`. The binding locks the secret to one provider, server identifier, normalized origin and path, credential reference, and—when applicable—user. Retrieve that record from the protected vault and pass it to the Plex or Emby client; never recreate it from mutable catalog data when opening a connection.

Both clients verify the server's public identity before sending a protected request. Public identity probes never carry Plex or Emby credentials, redirects are rejected by the reference transport, and secure Plex connections are preferred during discovery. HTTPS is the default. Plain HTTP is accepted only after the platform presents an explicit insecure-local-network warning and saves that consent with the protected binding.

Catalog snapshots contain only opaque internal playback and artwork locators. Real server URLs, authorization headers, passwords, tokens, and credential bindings stay out of cached and synced catalog JSON.

## Verification

```text
pnpm media-centers:test
pnpm media-centers:build
node contracts/validate-contract.mjs
```
