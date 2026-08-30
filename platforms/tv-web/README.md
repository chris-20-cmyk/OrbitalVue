# StreamVue for Samsung Tizen and LG webOS

This project is the shared television UI for StreamVue 5.1. It deliberately uses framework-free TypeScript and a small Vite bundle so remote navigation and startup remain responsive on embedded television browsers.

## What works

- M3U/M3U8 URL and file import with a 64 MB safety limit
- Direct connection to personal Plex and Emby servers, with paged movie, episode, and live-TV catalogs
- On-demand protected playback resolution and resume-position handoff without writing access tokens into the catalog cache
- Automatic URL refresh at launch with an IndexedDB last-working catalog
- Exact playlist groups and grouped section labels in All Channels
- Large-library windowing, search, and private favorites
- Directional-pad, OK, Back, media-key, channel-key, and Magic Remote interaction
- A Samsung AVPlay adapter with User-Agent and Cookie support
- Native HTML5/HLS playback for LG webOS and browser development
- Auto, Fit, Fill, Zoom, 16:9, 4:3, and 21:9 framing
- Buffering shown only while the native player reports buffering
- Samsung Checkout one-time Buy/Restore UI with localized verifier-owned product metadata, server-side DPI purchase-history and country-availability verification, a native service check immediately before purchase where the deprecated probe is still exposed, and live revocation handling
- An explicit LG billing-unavailable state with no placeholder payment flow, because LG has discontinued its native television billing service

Raw playlist locations never appear in normal browsing; only the provider host and optional port are displayed. The full source URL remains in app-private television storage so it can refresh at launch.

Plex and Emby credentials are kept outside the catalog database. Samsung builds use Tizen KeyManager. LG webOS 24 and newer use Key Manager 3 backed by the television's trusted execution environment; older or unsupported televisions intentionally keep the credential for the current app session only and ask the user to reconnect after a restart. Public server identity is verified before a protected request is sent, and cached media snapshots contain only opaque StreamVue locators.

Personal builds include media-center access. Setting `VITE_STREAMVUE_DISTRIBUTION_MODE=store` produces the fail-closed store surface: it shows the locked premium state, renders no credential inputs, and blocks refresh and playback before the credential vault or media-server network is touched. Samsung packages can unlock after an exact Seller Office/DPI non-consumable and HTTPS entitlement verifier are configured; the DPI security key must remain on that backend. LG packages stay locked until a reviewed third-party billing provider is contracted and server-side verification is implemented. See [premium access and store readiness](../../docs/premium-entitlements.md).

For a Samsung store-candidate build, provide the three non-secret seller/verifier values with the store mode:

```powershell
$env:VITE_STREAMVUE_DISTRIBUTION_MODE = "store"
$env:VITE_STREAMVUE_SAMSUNG_APP_ID = "<Seller Office Checkout app ID>"
$env:VITE_STREAMVUE_SAMSUNG_PRODUCT_ID = "<DPI non-consumable product ID>"
$env:VITE_STREAMVUE_SAMSUNG_VERIFICATION_URL = "https://<verifier>/samsung/status"
pnpm tv:build
```

The exact request/response boundary is committed in [`contracts/samsung-checkout-verifier-v1.schema.json`](../../contracts/samsung-checkout-verifier-v1.schema.json). The backend response includes `checkoutAvailable`, computed with Samsung DPI's signed country-availability request; no DPI security key or check value is sent to the TV. The normal unsigned CI artifact does not set these values and remains visibly store-locked.

The television's native player cannot attach arbitrary authorization headers. StreamVue therefore materializes the provider token into the native playback URL only for the active playback request, clears that URL when playback stops, and never stores it in source metadata or offline snapshots.

Samsung AVPlay exposes User-Agent and Cookie streaming properties but not an arbitrary Referer property. LG's portable HTML5 video path cannot guarantee custom request headers. StreamVue reports that limitation instead of sending a private source through a proxy.

## Build and verify

From the repository root:

```powershell
pnpm install
pnpm tv:check
pnpm tv:test
pnpm tv:build
```

The build produces:

- `platforms/tv-web/dist/web` — browser QA build
- `platforms/tv-web/dist/samsung` — Tizen project contents
- `platforms/tv-web/dist/webos` — webOS package contents

## Personal television installation

Samsung requires Tizen Studio, its TV extensions, a device-author certificate profile, and the TV's developer mode. After `pnpm tv:build`:

```text
tizen build-web -- platforms/tv-web/dist/samsung
tizen package -t wgt -s YOUR_CERTIFICATE_PROFILE -- platforms/tv-web/dist/samsung/.buildResult
```

LG uses the free Developer Mode app and the current webOS CLI. Register the TV, package, and install with the pinned repository copy:

```text
node node_modules/@webos-tools/cli/bin/ares-setup-device.js
node node_modules/@webos-tools/cli/bin/ares-package.js platforms/tv-web/dist/webos
node node_modules/@webos-tools/cli/bin/ares-install.js --device YOUR_TV com.streamvue.player.tv_5.1.0_all.ipk
```

The repository pins `@webos-tools/cli` 3.2.5 in `pnpm-lock.yaml`. These direct Node entry points also avoid a Windows command-shim issue observed with `pnpm exec`. LG Developer Mode is time limited, so extend the session before it expires or the TV will remove developer-installed apps.

The generated Samsung application/package ID is provisional until the first Tizen Studio device project is paired. Keep the same IDs after store submission begins. LG and Samsung packages must be tested on real televisions because codecs, HLS variants, remote keys, and provider header requirements vary by model.

## Samsung Seller Office candidate

The manual **Build Samsung TV Store candidate** workflow first proves every public readiness field in a separate job before requesting access to the signing environment. It then installs the SHA-256-pinned Tizen Web CLI, builds in Store mode, stamps only the generated `config.xml`, verifies the exact package/widget/Checkout identities, and signs one `.wgt` with protected author and partner-distributor certificates. It checks the author certificate against the committed continuity fingerprint, removes all signing material, and uploads a temporary audit artifact. It never submits to Seller Office.

The workflow remains locked until `store/premium-products.json` and `store/samsung-distribution.json` contain the real reviewed values. Add these non-secret repository variables:

```text
STREAMVUE_SAMSUNG_APP_ID
STREAMVUE_SAMSUNG_PREMIUM_PRODUCT_ID
STREAMVUE_SAMSUNG_VERIFICATION_URL
STREAMVUE_SAMSUNG_AUTHOR_CERT_SHA256
```

Create a protected GitHub environment named `samsung-store-signing` (ideally with a required reviewer), then add these environment secrets:

```text
STREAMVUE_SAMSUNG_AUTHOR_CERTIFICATE_BASE64
STREAMVUE_SAMSUNG_AUTHOR_CERTIFICATE_PASSWORD
STREAMVUE_SAMSUNG_DISTRIBUTOR_CERTIFICATE_BASE64
STREAMVUE_SAMSUNG_DISTRIBUTOR_CERTIFICATE_PASSWORD
```

The distributor certificate must have the Partner privilege level because `sso.partner` is used. For personal sideloading it must also contain the target television's DUID. Back up the original author `.p12` and password outside GitHub; every future update must preserve that author identity.

## LG Seller Lounge candidate

The manual **Build LG webOS Seller Lounge candidate** workflow produces a free Store-mode IPK with Plex and Emby visibly locked. This is deliberate: LG no longer provides its native television billing service, and StreamVue will not simulate ownership with a local flag or a client-side payment callback. Ordinary authorized M3U sources remain available.

Before the workflow can run, complete `store/lg-distribution.json` with the real Seller Lounge account type and mark each human prerequisite true only after it is actually finished:

- Seller terms reviewed
- 400×400 store icon reviewed and remaining listing images prepared
- UX scenario prepared
- mandatory self-checklist completed with actual results
- privacy disclosures reviewed
- real-TV model matrix completed

The verifier also locks the permanent `com.streamvue.player.tv` identity, validates the package icons, splash, and separate 400×400 Seller Lounge icon, and proves that LG remains null/unready in `store/premium-products.json`. The workflow stamps only generated `appinfo.json`, invokes the pinned official CLI, independently opens the resulting IPK, and emits the IPK, store icon, package analysis, audit record, and checksums. It does not use or invent an author certificate, accept Seller Lounge terms, upload the app, or claim LG approval.

LG requires a UX scenario and a fully completed self-checklist for submission, and every later update receives its own QA review. See LG's [app approval process](https://webostv.developer.lge.com/distribute/app-approval-process), [app self-checklist](https://webostv.developer.lge.com/distribute/app-self-checklist), and [app resource requirements](https://webostv.developer.lge.com/develop/getting-started/app-resources).
