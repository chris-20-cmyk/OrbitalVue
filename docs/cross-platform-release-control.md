# Cross-platform release control

StreamVue has one optional premium feature, `personal-media-centers`, sold as a one-time non-consumable where a platform has a verified native purchase route. `store/cross-platform-release.json` is the machine-readable map from that product contract to each vendor's application identity, verification provider, candidate artifact, and manual submission workflow.

Run the complete release-control check from the repository root:

```text
pnpm release:check
```

This check is deliberately independent of signing credentials. It reads only committed public configuration and source metadata, including the fail-closed technical privacy inventory.

The lightweight **Cross-platform Store release contract** CI workflow runs the same boundary whenever a candidate workflow, readiness manifest, application identity, billing adapter, or reviewed store asset changes.

## Current release state

| Platform | Candidate output | Premium state today | Remaining external work |
| --- | --- | --- | --- |
| Windows | unsigned MSIX for Partner Center | locked | reserve the package identity, create the durable add-on, fill the public manifest/variables, and test Store license changes |
| Android / Google TV | upload-key-signed AAB | locked | finish Play registration/testing, create the one-time product and verifier, register the upload certificate, and complete Play review data |
| iPhone / iPad / Apple TV | Apple-distribution-signed IPA set | locked | choose a legitimate KSPlayer/AVKit distribution path, enroll, create the shared non-consumable, supply profiles/certificate, and complete App Store review data |
| Samsung TV | author/Partner-distributor-signed WGT | locked | finish Seller Office/DPI setup, preserve the author identity, deploy the verifier, and test Checkout on real TVs |
| LG webOS TV | webOS IPK plus 400×400 store icon | free app; Plex/Emby locked | finish Seller Lounge account/terms, listing assets, UX scenario, mandatory checklist, privacy review, and real-TV testing |

No row is marked ready merely because its code compiles. `store/premium-products.json` stays authoritative for purchasable premium products; the Apple, Samsung, and LG distribution manifests add their platform-specific legal, identity, signing, or review gates.

## What the verifier proves

- All five candidates use the same feature ID and one-time purchase model.
- Android and Apple retain `com.streamvue.player`; Samsung and LG retain their reviewed television identities; Windows accepts only the identity reserved in Partner Center.
- A ready paid lane uses its exact native verification provider: Microsoft Store license, Google Play Developer API, StoreKit 2 verified transactions, or Samsung DPI purchase history.
- LG remains a free Store build with premium media centers locked until a reviewed third-party commerce integration is implemented.
- Every platform remains privacy-locked until a public policy and support page, owner approval, Store disclosures, retention/deletion decisions, and third-party review agree with the technical inventory.
- Every candidate workflow is `workflow_dispatch` only, has read-only repository contents permission, builds in Store mode, and uploads a temporary audit artifact.
- Candidate workflows contain no vendor install command or automatic App Store, Play Console, Seller Office, Seller Lounge, or GitHub Release publication command.

The verifier does not create seller accounts or products, accept terms, upload packages, perform vendor certification, or replace required real-device tests. Those actions need the account owner and must be recorded truthfully in the relevant readiness manifest.

## Safe release sequence

1. Create the permanent vendor application record before changing a committed application identity.
2. Create the one-time product and server verification path where that vendor supports the premium lane.
3. Publish and owner-approve the privacy/support pages, complete each Store disclosure and retention review in `store/privacy-data-inventory.json`, then finish listing, accessibility, and real-device checks with actual results.
4. Update only the matching public readiness fields; never commit receipts, tokens, passwords, certificates, or private keys.
5. Run `pnpm release:check` plus the platform foundation tests.
6. Run the matching manual candidate workflow and inspect its audit/checksum artifact.
7. Upload manually in the vendor console and let that store own customer delivery and subsequent store updates.

See [privacy and Store disclosures](privacy-and-store-disclosures.md), [premium entitlements](premium-entitlements.md), [distribution and signing choices](distribution-and-signing.md), and each platform README for the detailed implementation boundary.
