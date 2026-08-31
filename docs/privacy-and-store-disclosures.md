# Privacy and Store disclosure control

StreamVue has a technical privacy inventory, but it does not yet have a published privacy policy or approved Store disclosure set. The release workflows therefore fail closed before signing or packaging access. This document is an engineering record and policy-writing aid, not a claim of legal approval.

## What the current app does

- StreamVue has no advertising, cross-app tracking, automatic analytics, or developer-hosted user account system.
- Playlist files, channel and guide metadata, favorites, recents, and playback preferences stay on the device. When a user plays their own Plex or Emby library, StreamVue sends the media item identifier, position, and play/pause/stop state directly to that selected server so its account resume and watched state can stay current.
- A playlist URL, Xtream request, or Plex/Emby request goes only to the server selected by the user. StreamVue does not proxy those requests through a StreamVue service.
- Plex/Emby tokens and Windows Xtream credentials are protected locally when the platform offers an appropriate secure store. Windows uses current-user data protection, Android uses an Android Keystore AES-GCM key, Apple uses Keychain, Samsung uses Tizen KeyManager, and LG uses its trusted-execution key manager with a session-only fallback when secure persistence is unavailable. Playlist locations and catalogs stay in app-private device storage; the added encryption/protection level varies by platform, so a signed playlist URL must be treated as sensitive data.
- A random app-installation identifier is sent to a connected Plex or Emby server as a player/client identifier. It is not a hardware identifier and StreamVue does not receive it.
- Apple and Android Plex account discovery send a stable public Ed25519 device-registration key to Plex's signed-PIN endpoint. On Apple, the matching private key and random client identifier stay in Keychain and may remain after app deletion when retained by the operating system. On Android, the matching private key stays in a Tink encrypted keyset wrapped by a verified Android Keystore AES-GCM key; StreamVue does not permit Tink's cleartext fallback, and app backup/transfer is disabled. The returned Plex account token exists only in core memory while StreamVue verifies the account and fetches available servers; SwiftUI and Compose receive no token, and only the selected server-scoped token is retained in an origin-bound platform-secure record after the server identity matches the selected resource.
- Windows writes crash details locally. A diagnostics archive is created only when the user requests one, excludes provider addresses and credentials, hashes the current-channel identity, and is not uploaded automatically.
- Apple purchase verification remains within StoreKit. The implemented but undeployed Android verifier receives package name, product ID, and a transient purchase token. The implemented but undeployed Samsung verifier receives the Checkout customer ID, service country, app ID, and product ID. The shared service and route-disabled Worker do not log request bodies or return those identifiers; the rate limiter receives only a keyed HMAC, not the raw token/customer ID. The production host, secret-manager records, retention policy, Store disclosures, and real-device evidence remain unconfigured and Store-locked.

The canonical machine-readable record is [`store/privacy-data-inventory.json`](../store/privacy-data-inventory.json). Any new analytics, advertising, account, cloud-sync, crash-upload, purchase-verification, or third-party SDK behavior must update that inventory and the Store forms before release.

## Required owner inputs

Before any public Store candidate can be built, the owner must provide:

1. A monitored privacy contact email.
2. A public HTTPS privacy-policy page and public HTTPS support page.
3. An effective date and explicit owner approval of the published text.
4. A retention/deletion statement covering local app data, user-requested diagnostics, and any future Android or Samsung verification records.
5. A completed third-party review, including the exact KSPlayer distribution selected for Apple.

Do not put private keys, provider credentials, purchase tokens, or personal account identifiers into this repository or the published policy.

## Store-specific checklist

| Platform | Required before `ready` can become true |
| --- | --- |
| Microsoft Store | Review the Store privacy disclosure, disclose the local crash log and user-initiated diagnostics export, review third-party libraries, and confirm retention/deletion wording. |
| Google Play | Complete Data safety for the exact app and every SDK, document the purchase-verifier retention/deletion behavior, and make the privacy policy public. The form is required even when an app says it does not collect data. |
| Apple App Store | Review the app-level App Privacy answers across iOS and tvOS, provide the tvOS privacy-policy text, review required-reason APIs and the selected KSPlayer build, and retain the bundled `PrivacyInfo.xcprivacy`. |
| Samsung Seller Office | Review the privacy disclosure plus Samsung Checkout identity and the implemented verifier behavior, then document retention/deletion. |
| LG Seller Lounge | Review the Seller Lounge privacy disclosure, webOS protected-storage behavior, the free/premium-locked product state, and retention/deletion. |

Run `pnpm privacy:check` for the non-claiming structural check. A Store workflow runs `node tools/verify-privacy-readiness.mjs --require-ready <platform>` and remains blocked until every public/human gate for that platform is true.

## Policy drafting outline

The published policy should identify the owner, contact, effective date, covered StreamVue platforms, local data categories, user-selected provider communications, protected credential storage, diagnostics behavior, purchase processing, retention/deletion controls, third-party services, children/audience position, security limitations, and change-notice process. Replace every unresolved fact with an owner decision; do not publish placeholders as a finished policy.
