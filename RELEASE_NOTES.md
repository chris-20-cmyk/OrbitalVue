# StreamVue 5.2.0 Android Plex Account & Resilience

StreamVue 5.2 adds secure Plex account sign-in and automatic personal-server discovery to the native Android and Google TV app. It retains the Apple 5.1 foundation, Samsung/LG television clients, and the proven Windows 4.0 application and updater.

## Android and Google TV Plex sign-in

- Recommended **Sign in with Plex** flow with QR approval and an external-browser action
- Strong signed PIN requests using a stable Ed25519 device identity
- Automatic discovery of every authorized Plex Media Server with secure/local connection preference
- Server and connection pickers designed for touch, D-pad, and ten-foot television use
- Manual server-token entry retained under an advanced option
- Explicit consent before any unencrypted local HTTP connection

## Credential and identity protection

- Plex account tokens remain session-only and never enter Compose state, the portable catalog, or saved preferences
- Compose receives sanitized server choices plus an opaque discovery lease that expires in no more than ten minutes
- The chosen server token is stored only after the responding server identity matches the selected Plex resource
- In-flight cancellation and premium-entitlement loss invalidate discovery and remove any newly created credential
- The device private key is held only in a Tink encrypted keyset wrapped by a verified Android Keystore AES-GCM key
- Tink's opportunistic cleartext keyset fallback is not used; unsupported or failing Keystore devices fail closed
- Android cloud backup and device-to-device transfer remain disabled for credentials and private source state

## Verification and release controls

- Protocol tests cover public-key-only strong PIN creation, signed proof claims, provider allowlists, response limits, and secure/local sorting
- Lifecycle tests cover token-free discovery state, unlisted URL rejection, substituted server identity, HTTP denial/retry, and cancellation rollback
- A dedicated structural gate rejects token-bearing UI models and any cleartext/opportunistic signing-key storage path
- Android CI validates unit tests, lint, debug APK, minified Store AAB, Leanback metadata, and 16 KB package alignment
- The personal APK remains available for testing without a paid certificate
- The Google Play AAB remains premium-locked and unsigned until the real Play product, verifier, upload key, privacy, listing, and accessibility gates are complete

## Existing platform foundation retained

- Native Apple SwiftUI clients with KSPlayer/Metal personal builds and AVKit-only Store candidates
- Plex and Emby token/account connections across the platform family using token-free `streamvue-media://` catalogs
- Samsung AVPlay and LG/webOS television clients with remote-first browsing and native playback paths
- One cross-platform premium contract based on a one-time lifetime unlock rather than a subscription

This is a prerelease foundation. The Android APK can be installed for personal testing. The release does not contain a new Windows installer; Windows 4.0 remains the current Windows build and continues updating in place.
