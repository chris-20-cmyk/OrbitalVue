# OrbitalVue portable contracts

Contract `1.0` is the stable boundary between OrbitalVue platform clients. It preserves the playlist behavior that already exists on Windows without requiring the Windows UI or playback engine on Android, TV, or Apple devices.

The contract covers source identity, channel groups, guide matching, request headers, catch-up metadata, and stable channel IDs. Source display locations must be safe labels and must never expose credentials or playlist tokens.

Files in `fixtures/` are synthetic and use the reserved `.invalid` domain. They are parser conformance inputs, not real channels or distributable content.

Run the dependency-free validation from the repository root:

```text
node contracts/validate-contract.mjs
```

The Samsung/LG implementation lives in `packages/catalog-js` and runs the same fixture through its TypeScript parser:

```text
pnpm catalog:test
```

Breaking changes require a new major contract version. New optional fields can be added in a minor revision after all shipping clients ignore unknown fields safely.

## Authenticated media centers

`media-center-contract-v1.schema.json` defines the portable, cache-safe Plex and Emby snapshot. It stores server and library metadata plus a `credentialId` that points to a platform secure store. Passwords, access tokens, resolved media URLs, request headers, and playback sessions are intentionally absent.

Media-center items enter the browse catalog through opaque `orbitalvue-media://` locators. A platform adapter resolves a locator immediately before playback and injects the current token from Keychain, Windows Credential Manager/DPAPI, Android Keystore-backed storage, or the TV platform's secure storage. A resolved playback plan is ephemeral and must never be written to the portable catalog.

The TypeScript reference implementation and mocked Plex/Emby conformance tests live in `packages/media-centers`. Plex account onboarding uses its current Ed25519-signed PIN and refresh flow; platforms own the private signing key and every returned token:

```text
pnpm media-centers:test
```

## Premium access

`premium-access-contract-v1.schema.json` defines the secret-free access decision for optional personal media-center integration. Personal builds include the feature. Store builds require a verified one-time, non-consumable purchase and otherwise fail closed; unknown build modes also fail closed. Receipts, purchase tokens, store account identifiers, and media-server credentials are never part of this portable decision.

See [Premium access and store readiness](../docs/premium-entitlements.md) for build switches and the native store-adapter checklist.
