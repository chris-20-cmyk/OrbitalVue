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
| Windows | `-p:StreamVueDistributionMode=Store` | Compiles locked; an MSIX Store build can verify a configured durable add-on |
| Android / Google TV | `-PstreamVueDistributionMode=store` | Compiles with Plex/Emby locked |
| Samsung / LG television shell | `VITE_STREAMVUE_DISTRIBUTION_MODE=store` | Bundles with Plex/Emby locked |
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

The purchase surface intentionally remains unavailable for an unpackaged EXE, an elevated process, an app without an exact product ID, or an add-on that is not associated with the current package. The public Store route therefore still needs a Partner Center product plus the planned MSIX packaging lane. Direct-download/Velopack builds remain personal and include Plex/Emby without a purchase.

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

Official implementation references: [Microsoft Store purchases and trials](https://learn.microsoft.com/en-us/windows/uwp/monetize/in-app-purchases-and-trials), [Microsoft durable add-ons](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/add-on/create-app-submission), [Google Play Billing integration](https://developer.android.com/google/play/billing/integrate), [Google Play billing security](https://developer.android.com/google/play/billing/security), [StoreKit current entitlements](https://developer.apple.com/documentation/storekit/transaction/currententitlements), and [AppStore.sync](https://developer.apple.com/documentation/storekit/appstore/sync()).

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

A future store workflow must also run `node tools/verify-premium-store-readiness.mjs --require-ready <platform>`. That command intentionally fails today for every platform, preventing an unconfigured paid feature from being represented as purchasable.

Foundation CI exercises both modes. Direct-install test packages remain personal builds, while unsigned Google Play, Samsung, and LG artifacts are explicitly named `store-locked` and compile with media centers unavailable. They are verification artifacts, not sellable products; a future store-publish job must pass the platform-specific `--require-ready` gate first.

Current adapters use StoreKit 2 on Apple, Google Play Billing on Android/Google TV, and the Microsoft Store licensing API for a future MSIX release. Samsung/LG will use only an applicable seller API that can verify the television account's entitlement; no local preference or build flag may stand in for proof of purchase. Direct-download Windows builds stay personal/included.

## Portable contract

`contracts/premium-access-contract-v1.schema.json` allows only a secret-free decision for the `personal-media-centers` feature:

- `included`: personal build; no receipt required.
- `verified`: store build; a native provider verified a one-time purchase.
- `unavailable`: store or unknown build mode; no verified entitlement, no provider configured, or invalid release configuration.

Purchase tokens, receipts, passwords, media-server tokens, and user identifiers are intentionally not fields in this contract.
