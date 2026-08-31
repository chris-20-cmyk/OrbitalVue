# StreamVue 5.3.0 Windows Plex Account Discovery

StreamVue 5.3 brings secure Plex account sign-in and automatic personal-server discovery to the native Windows app. Existing M3U, Xtream, manual Plex/Emby, DVR, casting, playback-resilience, and in-place update features remain available.

## Recommended Plex sign-in

- New **Sign in with Plex** path opens the official browser approval page and polls automatically after approval
- Automatically discovers every Plex Media Server authorized for the account
- Prefers secure local connections, then secure remote/relay choices
- Lets the user choose the exact server and connection before anything is saved
- Keeps manual Plex server-token entry under an advanced option
- Requires explicit trusted-network consent before sending a server token over HTTP

## Credential and device protection

- Uses Plex's strong PIN flow with a stable Ed25519 device identity
- Sends only the public JWK to Plex; the private 32-byte signing seed is protected by Windows current-user DPAPI
- Imports the private seed into a non-exportable runtime key and zeroes temporary clear buffers
- Keeps the Plex account token in service memory only while verifying the account and fetching server-scoped resources
- Exposes only sanitized server choices and an opaque ten-minute discovery lease to WPF
- Probes the selected server without a token and requires its identity to match the selected Plex resource before storing the server token
- Invalidates discovery on cancellation, failed activation, or premium-entitlement loss

## Verification and release controls

- Protocol tests cryptographically verify the Ed25519 device JWT, claims, and five-minute lifetime
- Security tests reject unlisted addresses, cleartext connections without consent, changed server identities, cancelled leases, and leases observed after entitlement revocation
- Serialized discovery and UI models are checked for account/server-token leakage
- Both Personal and Microsoft Store build modes compile with zero warnings
- A dedicated Windows structural gate runs in foundation, Store-candidate, and preview-packaging workflows

## Updating from Windows 4.0

The personal Windows build remains on the existing Velopack update lane. StreamVue 4.0 can download and install the 5.3 preview in place; uninstalling first is not required. Microsoft Store builds remain Store-managed and do not contact the GitHub updater.

This is a prerelease intended for personal testing. Store submission remains locked until the real product, privacy, listing, accessibility, public-site, and Partner Center owner-review gates are complete.
