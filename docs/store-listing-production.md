# Store listing production

`store/store-listing.json` is the single English (United States) source for OrbitalVue's public product name, descriptions, claims, content disclaimer, categories, screenshot story, and platform artwork paths. It is deliberately a reviewed draft: missing owner identity, rights/trademark decisions, rating questionnaires, privacy approval, and real-device assets keep every Store lane locked.

## Approved positioning draft

OrbitalVue is a player for user-supplied IPTV and personal media sources. It is not a channel service, media seller, playlist directory, or content-discovery catalog. Every listing must keep the authorization disclaimer visible and describe Plex/Emby as an optional one-time Premium feature only where that platform has a verified purchase route.

The four-screen story is consistent across platforms:

1. Connect a playlist source and see the private source library.
2. Browse source-defined groups with search and favorites.
3. Show actual native playback with aspect controls and signal state.
4. Show playback/buffering settings available in that exact platform build.

Use real captures from the current target build. Never commit a personal provider name, playlist URL, account detail, notification, or stream the tester is not authorized to display. Concept art and generated interface images are not valid product screenshots.

## Platform production matrix

| Platform | Final assets required by OrbitalVue's quality gate |
| --- | --- |
| Windows | 300×300 PNG Store icon and four PNG desktop screenshots at 1366×768, 1920×1080, or 3840×2160. Microsoft requires at least one desktop screenshot and recommends four or more. |
| Google Play | 512×512 32-bit PNG icon, 1024×500 opaque feature graphic, 1280×720 opaque Android TV banner, and four current screenshots each for phone, tablet, and TV. Google requires at least two screenshots overall, plus a TV screenshot and banner for Android TV; OrbitalVue keeps the stronger four-per-surface bar. |
| Apple | Four opaque screenshots each for iPhone 6.9-inch, iPad 13-inch, and Apple TV. OrbitalVue accepts current official sizes encoded in the validator; App Store Connect requires one to ten for every supported device class. The app icon continues to come from the reviewed Xcode asset catalog. |
| Samsung TV | Separate 1920×1080 RGBA logo and opaque background under 300 KB each, opaque 512×423 PNG logo under 300 KB, and four 1920×1080 JPG screenshots under 500 KB each. |
| LG webOS | The existing 400×400 Seller Lounge icon plus four clean 1920×1080 captures. The Seller Lounge account owner must recheck its portal-specific format/size rules and mark the listing review; the public webOS documentation only establishes the separately uploaded 400×400 Store icon. |

Paths go under `store/listing-assets/<platform>/` and are entered in `store/store-listing.json`. `pnpm listing:check` validates real files rather than filenames alone: signature, dimensions, alpha requirements, and file-size limits. A configured file still does not become approved until its human review flag is true.

## Owner decisions still required

- Developer display name and copyright holder.
- Confirmation that every public screenshot, brand name, and connected-service reference may be used.
- App/content rating questionnaires in each seller console; do not guess an age rating in the repository.
- Terms/license review and the matching public privacy/support pages.
- English copy review plus a decision on additional localizations.
- Real-device checks for text legibility, overscan/safe areas, remote/touch focus, and feature accuracy.

After those decisions and assets are complete, set only the matching reviewed fields. `node tools/verify-store-listing-readiness.mjs --require-ready <platform>` must pass before a candidate workflow may access signing material.

## Current official references

- [Microsoft MSIX screenshots and Store images](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)
- [Google Play preview assets](https://support.google.com/googleplay/android-developer/answer/9866151?hl=en)
- [Apple required App Store properties](https://developer.apple.com/help/app-store-connect/reference/app-information/required-localizable-and-editable-properties)
- [Apple screenshot specifications](https://developer.apple.com/help/app-store-connect/reference/app-information/screenshot-specifications/)
- [Samsung TV app icons and screenshots](https://developer.samsung.com/smarttv/design/app-icons-and-screenshots.html)
- [LG webOS app resources](https://webostv.developer.lge.com/develop/getting-started/app-resources)
