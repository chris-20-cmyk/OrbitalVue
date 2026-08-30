# StreamVue entitlement verifier

This private workspace package is the fail-closed server core for the one-time Plex and Emby unlock. It implements the two backend routes already used by StreamVue clients:

- `POST /google-play/verify` checks a transient purchase token with Google Play Developer API `purchases.productsv2.getproductpurchasev2`.
- `POST /samsung/status` checks the exact DPI product offer, HMAC-verifies every DPI product and purchase-history response, pages purchase history, rejects canceled purchases, and verifies the matching invoice.

The package is deliberately hosting-neutral. A thin HTTPS adapter can pass a standards-based `Request` to `createEntitlementVerifierHandler()` and return its `Response`. The adapter must add an origin policy suitable for the signed TV package, rate limiting, request-size enforcement at the edge, and secret-manager bindings. Do not enable wildcard browser origins for these routes.

## Runtime secrets

Google Play needs a service account that has the minimum Play Console app permission required to view purchases. Pass its email and PKCS#8 private key from the host secret manager to `createGoogleServiceAccountTokenProvider()`. The private key and resulting OAuth token must never be put in a build variable, client bundle, repository file, artifact, log, or error response.

Samsung needs the DPI security key issued for the exact Checkout application ID. Pass it only from the host secret manager to `SamsungDpiHttpClient`. The key and HMAC check values must never enter the Tizen bundle, response contract, analytics, or logs.

The service intentionally does not log request bodies. The Android purchase token and Samsung Account custom ID exist only for the provider request being processed. A production host still needs an explicit retention policy, abuse controls, monitoring that contains no identifiers, and real Store/test-buyer evidence before its readiness manifest can be marked complete.

## Verification behavior

Google Play unlocks only a completed `PURCHASED` result with one matching, non-consumed product line. Fully refunded line items and test purchases are rejected by default. Test purchases can be allowed only in an isolated test deployment.

Samsung returns a buyable offer only when DPI returns the exact non-consumable product for the television service country. An unavailable country returns no invented price. Ownership requires a non-canceled matching purchase-history record plus a successful exact invoice verification. Native Checkout callback data is never entitlement proof.

Run:

```text
pnpm entitlements:build
pnpm entitlements:test
pnpm verifier:check
```

`store/premium-verifier-readiness.json` remains blocked until the production HTTPS adapter, secrets, rate limits, seller console permissions, and real-device refund/restore tests have evidence.
