# Premium access and store readiness

StreamVue uses one portable entitlement decision for the optional Plex and Emby integration. This boundary is deliberately separate from app signing, a code-signing certificate, the Apple Developer Program, and the KSPlayer software license.

## Current behavior

- **Personal builds:** Plex and Emby are included. No purchase, subscription, or certificate is required to keep developing or installing the app for personal use.
- **Store builds:** media-center access fails closed until a native store adapter verifies ownership. A locked store build does not ask for Plex or Emby credentials, refresh a saved media center, or resolve protected playback.
- **M3U, M3U8, file, URL, and existing provider sources:** remain independent of this premium feature.

The intended paid product is a **one-time, non-consumable/lifetime unlock**, not a monthly subscription. No product identifier has been invented and no purchase is advertised as available yet. Each identifier must first be created in the applicable store console.

## Build modes

The repository defaults to the personal mode so current private builds remain fully functional.

| Platform | Store-mode build switch | Result today |
| --- | --- | --- |
| Windows | `-p:StreamVueDistributionMode=Store` | MSIX lane compiles locked; a package with exact Partner Center identity can verify a configured durable add-on |
| Android / Google TV | `-PstreamVueDistributionMode=store` | Compiles locked by default; a signed candidate is possible only after exact Play product/verifier readiness |
| Samsung television shell | `VITE_STREAMVUE_DISTRIBUTION_MODE=store` | Compiles locked; Samsung Checkout becomes available only with the exact seller IDs and HTTPS verifier below |
| LG television shell | `VITE_STREAMVUE_DISTRIBUTION_MODE=store` | Remains explicitly locked because LG no longer provides native TV billing |
| Apple | Xcode configuration `Store` (injects `StreamVueDistributionMode=store` into the app Info.plist) | Compiles with Plex/Emby locked |

An unknown or misspelled mode is treated as store/unavailable by every runtime policy. This is intentional: an invalid release configuration must never become an accidental unlock.

## Native purchase adapters

### Windows Microsoft Store

The Windows Store build uses `Windows.Services.Store`, not a local preference or receipt file. Create a **Durable** Partner Center add-on with product lifetime **Forever**, then pass its exact Product ID at build time:

```text
-p:StreamVueDistributionMode=Store
-p:StreamVuePremiumProductId=<exact Partner Center product ID>
```

StreamVue finds the matching `StoreProduct.InAppOfferToken`, displays the Microsoft Store title and localized price, launches the Store-owned purchase dialog on the WPF UI thread, and rebuilds access from the exact matching entry in `StoreAppLicense.AddOnLicenses`. A successful dialog result alone never unlocks the feature. `StoreContext.OfflineLicensesChanged` triggers another license query, so a removed entitlement stops protected media-center playback and locks credential actions.

The purchase surface intentionally remains unavailable for an unpackaged EXE, an elevated process, an app without an exact product ID, or an add-on that is not associated with the current package. `tools/build-windows-msix.ps1` now creates and independently inspects the x64 MSIX using the exact package identity, publisher, publisher display name, version, and add-on ID copied from Partner Center. It refuses placeholders in the real candidate workflow, removes Velopack from the Store dependency graph, and emits an unsigned `.msix` for Partner Center; Microsoft signs accepted Store submissions. Direct-download/Velopack builds remain personal and include Plex/Emby without a purchase.

Microsoft Store owns updates for the MSIX lane. The Store build does not initialize Velopack, compile its runtime package, contact GitHub Releases, expose release-channel/rollback controls, or download a parallel installer. Its in-app update panel instead explains that Store settings control signed automatic updates. The personal Windows build retains Stable/Preview checks and failed-launch rollback.

### Android and Google TV

StreamVue uses Google Play Billing Library 9.1.0 and enables pending one-time purchases plus automatic service reconnection. It queries current purchases at startup/resume, never grants access for `PENDING`, and acknowledges a non-consumable only after verification succeeds.

Google recommends verifying purchase tokens with the Google Play Developer API on a secure backend. Store builds therefore require both build properties below before the Buy or Restore actions become available:

```text
-PstreamVuePremiumProductId=<exact Play Console product ID>
-PstreamVuePremiumVerificationUrl=https://<your verifier>/google-play/verify
```

The verifier endpoint receives a versioned JSON request containing `platform`, `packageName`, `productId`, and the transient `purchaseToken`. It must return `{ "schemaVersion": 1, "verified": true, "productId": "<same ID>" }` only after checking the token with Google. The token is never copied into `PremiumAccessSnapshot`, logs, catalogs, or local settings. The endpoint must be HTTPS and cannot be configured with URL credentials, a query, or a fragment.

### Apple iOS and tvOS

The `Store` configuration reads the exact non-consumable identifier from the `STREAMVUE_PREMIUM_PRODUCT_ID` Xcode build setting. StoreKit 2 loads the localized product, processes `Transaction.updates`, and rebuilds access from cryptographically verified `Transaction.currentEntitlements`. Unverified, pending, revoked, missing, or mismatched transactions stay locked. `AppStore.sync()` is called only after the user selects **Restore purchase**, because that API can display an App Store account prompt.

### Samsung Tizen TV

Samsung store builds use Samsung Checkout on the television and the DPI purchase-history service on a protected StreamVue backend. Configure the Seller Office application for Billing/Samsung Checkout, create a **Non-Consumable** product in the DPI Portal, and supply only these non-secret build values:

```text
VITE_STREAMVUE_DISTRIBUTION_MODE=store
VITE_STREAMVUE_SAMSUNG_APP_ID=<exact Samsung Checkout application ID>
VITE_STREAMVUE_SAMSUNG_PRODUCT_ID=<exact DPI product ID>
VITE_STREAMVUE_SAMSUNG_VERIFICATION_URL=https://<your verifier>/samsung/status
```

Samsung limits a Checkout application ID to 30 characters and a DPI product ID to 20 ASCII letters, digits, `_`, or `-`; StreamVue enforces both rules before touching the TV API. The verifier URL must be HTTPS and contain no credentials, query, or fragment. `config.xml` requests the documented Billing, ProductInfo, and partner-level SSO privileges, while the packaged page loads Samsung's `webapis.js`.

At startup, restore, foreground, and source-manager entry, StreamVue sends the versioned request defined by `contracts/samsung-checkout-verifier-v1.schema.json`. Its Samsung Account `customId` is transient purchase-verification data: the verifier must minimize it, never return it, and avoid retaining it beyond the required seller transaction purpose. The backend owns the DPI security key, generates/verifies every HMAC check value, checks Checkout support for the exact service country, pages purchase history, validates the exact app/product/user/country, and treats canceled or refunded records as unowned. It returns a required secret-free `checkoutAvailable` boolean. Neither the security key nor a DPI check value belongs in this repository, a Vite variable, or a television bundle.

An unowned decision must include the exact server-validated localized product offer. When `checkoutAvailable` is false, StreamVue keeps Buy unavailable but still allows a restore/history check for an existing owner. When true, StreamVue also checks the native production Billing service immediately before `webapis.billing.buyItem(..., "PRD", ...)` on televisions that still expose Samsung's deprecated device-level probe, then discards the native result as entitlement evidence and asks the verifier for purchase history again. The server-side DPI country check remains mandatory. Only a matching response with `schemaVersion: 1`, `verified: true`, and boolean `checkoutAvailable` unlocks Plex/Emby; previously verified ownership remains valid when new purchases are unavailable in the current country. A failed/mismatched recheck revokes access and stops protected playback; ordinary M3U playback is unaffected.

### LG webOS TV

LG's current developer documentation states that the LG Billing Service for in-app purchase is no longer provided and recommends a reliable third-party billing solution such as Paymentwall; LG may require a separate contract for another provider. The store build therefore reports **unavailable**, shows no fake Buy/Restore button, and performs no placeholder billing or verification request. A production LG unlock still requires a selected/approved provider, seller terms, a StreamVue user identity and recovery model, server-side webhook/API verification, refund handling, and an LG real-TV test matrix. A client-side payment success callback alone will never be accepted as entitlement proof.

Official implementation references: [Microsoft Store purchases and trials](https://learn.microsoft.com/en-us/windows/uwp/monetize/in-app-purchases-and-trials), [Microsoft durable add-ons](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/add-on/create-app-submission), [Google Play Billing integration](https://developer.android.com/google/play/billing/integrate), [Google Play billing security](https://developer.android.com/google/play/billing/security), [StoreKit current entitlements](https://developer.apple.com/documentation/storekit/transaction/currententitlements), [AppStore.sync](https://developer.apple.com/documentation/storekit/appstore/sync()), [Samsung Checkout purchase process](https://developer.samsung.com/smarttv/develop/guides/samsung-checkout/implementing-the-purchase-process.html), [Samsung Billing API](https://developer.samsung.com/smarttv/develop/api-references/samsung-product-api-references/billing-api.html), and [LG webOS in-app purchase](https://webostv.developer.lge.com/develop/guides/in-app-purchase).

## What remains before charging for the feature

Each store adapter must:

1. Create the store-owned non-consumable product and use that exact product identifier.
2. Load purchases from the native billing API, including restored purchases and refunds/revocations.
3. Verify the transaction using the store's supported signed-transaction or server-verification path.
4. Pass only a verified boolean and the store-owned product identifier into the shared access policy. Raw receipts, account identifiers, and purchase tokens must not enter the portable catalog or entitlement snapshot.
5. Re-check ownership at launch and after the store reports a transaction change.
6. Provide **Buy once** and **Restore purchases** UI, clear pricing from the store, accessibility labels, offline behavior, and a useful locked-state explanation.
7. Test that a locked build performs zero Plex/Emby credential, refresh, artwork, or playback requests.

`store/premium-products.json` is the release-readiness manifest. It contains no receipt or account data. Its normal verifier confirms that incomplete platforms remain explicitly locked:

```text
node tools/verify-premium-store-readiness.mjs
```

Every store candidate workflow must also run `node tools/verify-premium-store-readiness.mjs --require-ready <platform>` with the exact product and approved verification provider. That command intentionally fails for every currently unconfigured platform, preventing a paid feature from being represented as purchasable.

Foundation CI exercises both modes. Direct-install test packages remain personal builds, while unsigned Google Play, Samsung, LG, and synthetic Windows MSIX artifacts are explicitly named as locked/layout-only and compile with media centers unavailable. They are verification artifacts, not sellable products.

The Windows candidate workflow cannot produce a Partner Center artifact until `--require-ready windows` passes, the exact durable-add-on ID matches, and `verificationProvider` is `microsoft-store-license`. The Google Play candidate workflow likewise requires `--require-ready android`, an exact product-ID match, `verificationProvider` equal to `google-play-developer-api`, the production HTTPS verifier, and an upload certificate whose SHA-256 fingerprint matches the registered GitHub variable. Its signing key is supplied only through protected secrets and is never part of an artifact.

The Apple candidate requires `--require-ready apple`, the exact shared StoreKit product, and `verificationProvider` equal to `storekit2-verified-transactions`. It separately requires the KSPlayer distribution decision to be ready, one bundle ID across iOS/tvOS, protected Apple Distribution signing material, and platform-specific App Store profiles. It exports reviewable IPAs but does not upload them or claim that App Store review has passed.

The Samsung candidate requires `--require-ready samsung`, the exact non-consumable product, `verificationProvider` equal to `samsung-dpi-purchase-history`, and the separate `store/samsung-distribution.json` gate. That distribution gate requires the exact Tizen/Seller identities, HTTPS verifier, original author-certificate fingerprint, Partner distributor readiness, seller-terms review, DPI product creation, and a real-TV Checkout test. The workflow exports a signed, audited `.wgt` for manual Seller Office upload and never submits it automatically.

Current adapters use StoreKit 2 on Apple, Google Play Billing on Android/Google TV, the Microsoft Store licensing API for a future MSIX release, and Samsung Checkout plus a server-side DPI verifier on Samsung TV. LG intentionally remains unavailable until a reviewed third-party billing provider and verifier are selected. No local preference or build flag may stand in for proof of purchase. Direct-download Windows builds stay personal/included.

## Portable contract

`contracts/premium-access-contract-v1.schema.json` allows only a secret-free decision for the `personal-media-centers` feature:

- `included`: personal build; no receipt required.
- `verified`: store build; a native provider verified a one-time purchase.
- `unavailable`: store or unknown build mode; no verified entitlement, no provider configured, or invalid release configuration.

Purchase tokens, receipts, passwords, media-server tokens, and user identifiers are intentionally not fields in this contract.
