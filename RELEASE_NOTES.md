# StreamVue 5.0.0 Cross-platform Foundation

StreamVue 5.0 begins the move from a Windows-only player to one shared product family. This preview adds native Android/Google TV foundations plus dedicated Samsung Tizen and LG webOS television clients while preserving the proven Windows 4.0 application.

- One machine-checked release contract now locks all five application identities, native entitlement providers, manual candidate triggers, Store modes, and artifact types
- Foundation and candidate workflows reject cross-platform release drift before signing material or vendor submission becomes relevant
- A dedicated lightweight CI gate reruns the full release contract whenever a seller manifest, candidate workflow, app identity, billing boundary, or reviewed store asset changes

## Android and Google TV

- Native Kotlin and Jetpack Compose application for touch, D-pad, and ten-foot television use
- Media3 playback with hardware decode when the device and stream support it
- M3U URL and file import, exact source groups, search, favorites, and auto-refresh at launch
- Last-known-good catalog recovery if a provider refresh fails
- Safe handling for playlist headers and credentials without displaying private source URLs
- Personal-test APK plus an unsigned Play-ready AAB; no paid certificate is required for development

## Samsung and LG televisions

- Remote-first shared television shell with native Samsung AVPlay and LG/browser video adapters
- Exact playlist groups, categorized All Channels sections, search, favorites, and five-row Full HD/four-row HD browsing
- Auto-refresh for URL sources, IndexedDB last-good recovery, and clear first-run URL/file setup
- Full-screen playback with real buffering state and Auto, Fit, Fill, Zoom, 16:9, 4:3, and 21:9 aspect modes
- Samsung and LG package directories, store metadata, icons, splash artwork, and repeatable build scripts
- An unsigned Samsung platform project plus LG package contents for free Developer Mode testing
- Samsung Checkout country-availability enforcement with existing-owner restore behavior and fail-closed server verification
- A separate readiness-gated, author/Partner-distributor-signed Samsung `.wgt` candidate for manual Seller Office upload
- A permanent LG app identity, complete package/store artwork validation, and explicit Seller Lounge readiness manifest
- A pinned webOS CLI 3.2.5 lane that creates, analyzes, and independently reopens one premium-locked IPK for manual LG review
- No fabricated LG certificate or payment path: Seller terms, UX scenario, mandatory self-checklist, privacy review, and real-TV testing remain human gates

## Shared catalog contract

- Matching M3U behavior across native Android and JavaScript television clients
- Stable channel identities, source-order grouping, stream kind detection, guide metadata, catch-up attributes, and request headers
- Limits for oversized or malformed playlists and private, host-only source labels
- Portable fixture validation to keep later iOS and tvOS implementations compatible

## Verification

- Android unit tests, lint, debug APK, release AAB, Leanback metadata, and 16 KB package-alignment checks
- Shared catalog and television unit tests, TypeScript checks, production builds, and dependency audit
- Full HD and HD browser QA for D-pad navigation, first-run setup, search, favorites, playback errors, and every aspect mode
- No real playlist, provider credential, token, or private source address is included in the repository or packages

This is a prerelease foundation. The Android APK can be installed for personal testing. Samsung sideloading still needs a free TV certificate profile; LG uses its free Developer Mode app and an IPK created by the pinned webOS CLI, with no Samsung-style certificate. The existing Windows 4.0 release remains the current Windows installer and continues updating in place.
