# KSPlayer licensing gate

StreamVue's Apple source pins `kingslay/KSPlayer` 2.3.4 and uses KSMEPlayer as its default Metal/FFmpeg playback engine. The upstream public package is GPL-3.0. The upstream project also advertises separately licensed LGPL and commercial options; terms and pricing must be confirmed directly with its maintainer before relying on them.

This repository intentionally has no root software `LICENSE`, so merely publishing the source on GitHub does not satisfy GPL redistribution requirements. Until the owner makes an explicit license choice or obtains a separate KSPlayer license:

- local source builds for personal evaluation may include KSPlayer;
- GitHub CI may compile and analyze the integration but must not upload the combined app binary;
- StreamVue releases, TestFlight, and App Store submissions must not include the public KSPlayer package;
- AVKit remains implemented as the selectable native engine and fallback, but removing KSPlayer from a release requires a deliberate build profile rather than deleting the integration.

There are three legitimate release paths:

1. License the complete distributable StreamVue Apple work under GPL-3.0-compatible terms, publish the corresponding source and required notices, and separately confirm that the chosen store channel's terms are compatible.
2. Obtain KSPlayer's LGPL/commercial package and comply with its written terms. The project README directs commercial users to `kingslay@icloud.com` and its licensing discussion.
3. Ship an AVKit-only Apple build that does not link or embed KSPlayer.

Code signing and software licensing are separate. A free Xcode Personal Team can sign a personal device build, while Apple Developer Program membership governs TestFlight/App Store signing. Neither one grants a KSPlayer distribution license.

`store/apple-distribution.json` is the machine-readable decision record. `node tools/verify-apple-distribution-readiness.mjs` validates both the pinned personal package and the selected Store route in normal CI; its `--require-ready` mode is mandatory in the signed candidate workflow. The selected Store route is now `avkit-only`: `Package.store.swift` omits KSPlayer and the Apple UI compiles through an optional `canImport(KSPlayer)` boundary. Personal source builds still include the pinned public GPL package. The Store gate remains locked until the owner completes the App Store terms review and the other privacy, listing, premium, signing, and device evidence.
