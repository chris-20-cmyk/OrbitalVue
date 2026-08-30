# Cross-platform release control

StreamVue has one optional premium feature, `personal-media-centers`, sold as a one-time non-consumable where a platform has a verified native purchase route. `store/cross-platform-release.json` is the machine-readable map from that product contract to each vendor's application identity, verification provider, candidate artifact, and manual submission workflow.

Run the complete release-control check from the repository root:

```text
pnpm release:check
```

Generate the consolidated platform blocker dossier with `pnpm release:report`; see [release readiness reporting](release-readiness-report.md).

This check is deliberately independent of signing credentials. It reads only committed public configuration and source metadata, including the fail-closed technical privacy inventory, Store-listing/asset contract, and accessibility evidence matrix.

The lightweight **Cross-platform Store release contract** CI workflow runs the same boundary whenever a candidate workflow, readiness manifest, application identity, billing adapter, or reviewed store asset changes.

## Current release state

| Platform | Candidate output | Premium state today | Remaining external work |
| --- | --- | --- | --- |
| Windows | unsigned MSIX for Partner Center | locked | reserve the package identity, create the durable add-on, fill the public manifest/variables, and test Store license changes |
| Android / Google TV | upload-key-signed AAB | locked | finish Play registration/testing, create the one-time product, deploy the implemented verifier with protected service-account access, register the upload certificate, and complete Play review data |
| iPhone / iPad / Apple TV | Apple-distribution-signed AVKit-only IPA set | locked | complete App Store terms/privacy review, enroll, create the shared non-consumable, supply profiles/certificate, and finish device/review evidence; personal builds retain KSPlayer |
| Samsung TV | author/Partner-distributor-signed WGT | locked | finish Seller Office/DPI setup, preserve the author identity, deploy the verifier, and test Checkout on real TVs |
| LG webOS TV | webOS IPK plus 400×400 store icon | free app; Plex/Emby locked | finish Seller Lounge account/terms, listing assets, UX scenario, mandatory checklist, privacy review, and real-TV testing |

No row is marked ready merely because its code compiles. `store/premium-products.json` stays authoritative for purchasable premium products; `store/premium-verifier-readiness.json` records the route-disabled Cloudflare Worker adapter and separately gates Android and Samsung production hosting, secrets, controls, and provider/device evidence; the Apple, Samsung, and LG distribution manifests add their platform-specific legal, identity, signing, or review gates.

## What the verifier proves

- All five candidates use the same feature ID and one-time purchase model.
- Android and Apple retain `com.streamvue.player`; Samsung and LG retain their reviewed television identities; Windows accepts only the identity reserved in Partner Center.
- A ready paid lane uses its exact native verification provider: Microsoft Store license, Google Play Developer API, StoreKit 2 verified transactions, or Samsung DPI purchase history.
- Android and Samsung candidate URLs exactly match a production verifier deployment whose credentials are secret-manager bound, rate-limited, privacy-reviewed, and proven with real purchase and refund tests.
- The committed verifier Worker has no public route, disables `workers.dev` and preview URLs, requires four secret bindings, rejects unapproved browser origins, accepts Samsung's documented origin-less TV requests, caps provider-wide bursts, and uses only an HMAC-derived key for per-purchaser limits. A successful dry-run bundle is implementation evidence, not deployment evidence.
- LG remains a free Store build with premium media centers locked until a reviewed third-party commerce integration is implemented.
- Every platform remains privacy-locked until a public policy and support page, owner approval, Store disclosures, retention/deletion decisions, and third-party review agree with the technical inventory.
- Every candidate carries the same reviewed listing manifest and matching platform assets; owner identity, rights/trademark checks, rating questionnaires, and real-device captures must all be complete.
- Every candidate remains accessibility-locked until source checks pass and the exact build has documented keyboard/remote, assistive-technology, scaling/contrast, playback, and error-recovery evidence.
- Every candidate workflow is `workflow_dispatch` only, has read-only repository contents permission, builds in Store mode, and uploads a temporary audit artifact.
- Candidate workflows contain no vendor install command or automatic App Store, Play Console, Seller Office, Seller Lounge, or GitHub Release publication command.

The verifier does not create seller accounts or products, accept terms, upload packages, perform vendor certification, or replace required real-device tests. Those actions need the account owner and must be recorded truthfully in the relevant readiness manifest.

## Safe release sequence

1. Create the permanent vendor application record before changing a committed application identity.
2. Create the one-time product and server verification path where that vendor supports the premium lane.
3. Publish and owner-approve the privacy/support pages, complete each Store disclosure and retention review, then finish the copy, rights, ratings, and real-device artwork in `store/store-listing.json`.
4. Complete the platform matrix in `store/accessibility-readiness.json` against the exact release build and retain non-sensitive evidence.
5. Update only the matching public readiness fields; never commit receipts, tokens, passwords, certificates, private keys, or private assistive-technology logs.
6. Run `pnpm release:check` plus the platform foundation tests.
7. Run the matching manual candidate workflow and inspect its audit/checksum artifact.
8. Upload manually in the vendor console and let that store own customer delivery and subsequent store updates.

See [release readiness reporting](release-readiness-report.md), [privacy and Store disclosures](privacy-and-store-disclosures.md), [Store listing production](store-listing-production.md), [accessibility validation](accessibility-validation.md), [premium entitlements](premium-entitlements.md), [distribution and signing choices](distribution-and-signing.md), and each platform README for the detailed implementation boundary.
