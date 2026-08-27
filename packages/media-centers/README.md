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

`authenticateEmby` exchanges a user name and password directly with the user-provided Emby server. The password is never returned by the adapter. The resulting token is stored by the platform vault, while `EmbyClient` exposes safe browse metadata and ephemeral playback plans.

## Verification

```text
pnpm media-centers:test
pnpm media-centers:build
node contracts/validate-contract.mjs
```
