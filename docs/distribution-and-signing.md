# StreamVue distribution and signing choices

Verified against the platform owners' documentation on August 25, 2026. Store fees and eligibility rules can change, so recheck them immediately before enrollment.

## The short answer

StreamVue can be built and tested on Windows, Android, Android TV, Google TV, Samsung TV, LG TV, and personal Apple devices without buying a commercial code-signing certificate. Public store distribution is a separate gate:

| Platform | Personal testing | Lowest-cost public distribution |
| --- | --- | --- |
| Windows | Free unsigned or self-signed build | Microsoft Store MSIX: free developer onboarding and Microsoft-provided signing |
| Android / Google TV | Free debug APK or self-generated release key | Google Play: US$25 one-time developer registration; Play App Signing is included |
| Samsung TV | Free Samsung/Tizen author and distributor certificate for registered test TVs | TV Seller Office enrollment and review; Samsung's public documentation does not state a recurring certificate fee |
| LG webOS TV | Free LG developer account and Developer Mode testing | LG Seller Lounge enrollment and review; confirm any seller terms during enrollment |
| iPhone / iPad / Apple TV | Free Apple Account with short-lived personal provisioning | Apple Developer Program: US$99 per membership year; no one-time App Store option |

No paid certificate is needed to continue building StreamVue 5.1. The generated Android debug APK installs on personal devices after Android's normal sideload confirmation. Foundation CI deliberately emits an unsigned, locked AAB; the separate Google Play candidate workflow signs a publishable AAB with a protected, self-generated upload key after the real product and verifier pass readiness. Google then applies the Play-managed app-signing key to delivered APKs. Apple CI compiles unsigned simulator products but does not publish KSPlayer-enabled binaries until the separate software-license gate is resolved; physical Apple devices require Xcode signing with either a free Personal Team or a paid team.

## Windows choices

### 1. Microsoft Store MSIX — recommended free path

Microsoft's current onboarding flow at [storedeveloper.microsoft.com](https://storedeveloper.microsoft.com/) has no registration fee. An MSIX submitted through the Store does not need a CA-trusted certificate: after certification, Microsoft signs it and provides trusted installation and Store-managed updates. StreamVue's Velopack EXE remains the personal/direct-download lane; the repository now includes a separate, reproducible MSIX packaging lane for Partner Center. See Microsoft's [publishing guide](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app), [Store overview](https://learn.microsoft.com/en-us/windows/apps/publish/get-started), and [MSIX upload formats](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages).

The source includes the Microsoft Store durable-add-on adapter and package builder. A Store package must use the exact package Identity Name, Publisher, Publisher display name, and durable add-on Product ID copied from Partner Center. Purchase UI remains fail-closed until the executable is running with that final MSIX package identity; the existing direct-download/Velopack build does not impersonate Store ownership.

The Store candidate is built with:

```powershell
.\tools\build-windows-msix.ps1 `
  -IdentityName '<Partner Center package identity name>' `
  -Publisher '<Partner Center CN= publisher>' `
  -PublisherDisplayName '<Partner Center publisher display name>' `
  -Version '5.1.0.0' `
  -PremiumProductId '<durable add-on product ID>'
```

The script targets `Windows.Desktop` 10.0.19041 or later as a medium-integrity packaged classic app with `runFullTrust`, creates exact 50/44/150-pixel Store assets, strips unused x86 VLC files and symbols, and reopens the finished package to verify its identity, payload, architecture, Store audit, and lack of Velopack files. Its output is unsigned by design: Partner Center accepts `.msix` and Microsoft signs the certified submission. The synthetic CI package uses unmistakable test identity values and is not a Store candidate.

After the real package identity and durable add-on exist, set these public GitHub repository variables and update the Windows entry in `store/premium-products.json` before running **Build Windows Store candidate**:

- `STREAMVUE_WINDOWS_IDENTITY_NAME`
- `STREAMVUE_WINDOWS_PUBLISHER`
- `STREAMVUE_WINDOWS_PUBLISHER_DISPLAY_NAME`
- `STREAMVUE_WINDOWS_PREMIUM_PRODUCT_ID`

The workflow fails unless Windows is explicitly marked ready and the build input product ID exactly matches the readiness manifest. It produces an unsigned `.msix` plus SHA-256 checksum for manual Partner Center upload; it does not publish, enroll, or make a purchase claim automatically.

### 2. SignPath Foundation — free if StreamVue becomes open source

[SignPath Foundation](https://signpath.org/) offers free signing for eligible open-source projects. Its published conditions require an OSI-approved license, no proprietary components, an actively maintained and already released project, and public documentation. StreamVue currently has no repository `LICENSE` file, so choosing this route would require an explicit licensing decision; making a repository public by itself does not grant an open-source license. See the [SignPath eligibility conditions](https://signpath.org/terms.html).

### 3. Self-signed certificate — free, personal devices only

A locally created certificate can sign MSIX packages or binaries for computers where that certificate is manually trusted. It is appropriate for personal or managed-family devices, but it does not provide public trust and does not remove SmartScreen friction for general downloads.

### 4. Unsigned personal build — free

The existing Velopack/EXE preview can remain unsigned for personal use. Windows may show an unknown-publisher or SmartScreen warning. This is not suitable for a polished public release, but it does not block development.

### 5. Commercial direct-download certificate — recurring cost

For a public EXE/MSI downloaded outside the Store, Microsoft currently lists CA-issued OV certificates at roughly US$150–300 per year. Azure Artifact Signing is approximately US$9.99 per month where eligible. Neither is a true one-time purchase. Microsoft's current comparison is in [Code signing options for Windows app developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options).

## Android and Google Play choices

### Personal and direct installation — free

Android Studio/Gradle creates a free debug signing key automatically. A long-lived release keystore can also be generated locally at no charge for direct APK distribution. That key must be backed up permanently because future updates must use the same application ID and signing identity.

### Google Play — US$25 one time

Google documents a [US$25 one-time Play Console registration fee](https://support.google.com/googleplay/android-developer/answer/6112435). Play App Signing then protects the app-signing key and delivers optimized packages. New personal accounts must also complete Google's identity, device, and testing requirements. There is no monthly developer-account fee in the documented registration path.

The committed StreamVue pipeline produces three deliberately separate outputs:

- `app-debug.apk` for personal installation and device testing.
- An unsigned `app-release.aab` from foundation CI with Store mode locked and no product/verifier identity. This artifact cannot be mistaken for a signed Play candidate.
- A readiness-gated, upload-key-signed AAB from **Build Google Play candidate** for manual Play Console upload.

The candidate workflow accepts an explicit positive version code (which must never be reused) and release version name. It requires an exact Play Console product ID, HTTPS Google Play Developer API verifier, protected upload keystore/password secrets, and the registered upload-certificate SHA-256 fingerprint. It verifies tests, lint, Store BuildConfig values, JAR/AAB signature, RSA key strength, certificate fingerprint, and absence of private key files before publishing only a temporary workflow artifact. It never commits a key and does not publish to a Play track.

The upload key can be generated locally with `keytool` at no charge. It is not a CA-issued certificate and has no monthly fee. Google Play developer registration remains the separate US$25 one-time account fee described above. See the [Android build and upload-key instructions](../platforms/android/README.md), Google's [Play App Signing overview](https://support.google.com/googleplay/android-developer/answer/9842756), and [Android app-signing guide](https://developer.android.com/studio/publish/app-signing).

## Samsung TV

Samsung requires every TV application to be signed. Tizen Studio's Samsung Certificate Extension creates an author certificate and one or more distributor certificates after signing in with a Samsung Developer account. A target television's DUID is added to the distributor certificate for sideload testing. Certificate creation itself is part of the SDK workflow; Samsung's [certificate guide](https://developer.samsung.com/smarttv/develop/getting-started/setting-up-sdk/creating-certificates.html) does not list a monthly certificate charge.

Back up the author `.p12` and its password securely. Samsung states that an update signed with a different author certificate can be treated as a different application. Public distribution uses [TV Seller Office](https://seller.samsungapps.com/tv/login) and Samsung review. The package format is a signed `.wgt`.

## LG webOS TV

Personal testing uses an LG Developer account, the television's free Developer Mode app, and the webOS CLI. Developer Mode is time limited and must be extended while the TV is online; when it expires, developer-installed apps are removed. LG documents the process in [App Testing with Developer Mode](https://webostv.developer.lge.com/develop/getting-started/developer-mode-app).

The store path uses LG Seller Lounge and a reviewed `.ipk` package. LG's public developer pages describe the account and submission workflow but do not publish a simple registration-price statement, so any seller fee or commercial term must be confirmed in the enrollment screen before accepting it. Local development can proceed without that enrollment.

## Apple devices

An Apple Account can use Xcode's Personal Team for free on-device testing. Apple limits free personal provisioning to 10 App IDs and 3 devices per platform, and the App IDs, device registrations, and profiles expire after 7 days. This is enough to develop and test StreamVue on personally owned devices, but it requires periodic rebuild/reinstallation.

The generated Xcode project currently defines `com.streamvue.player` for iPhone/iPad and `com.streamvue.player.tv` for Apple TV. Select the Personal Team for both targets in Xcode; if either identifier is unavailable to that team, change the bundle ID to a unique value before signing. A compatible Mac and local Xcode installation are required for Personal Team device signing; the GitHub macOS runner only verifies unsigned simulator builds and does not publish them.

App Store, TestFlight, ad-hoc, and normal long-lived distribution require the [Apple Developer Program](https://developer.apple.com/support/compare-memberships/), currently US$99 per membership year. Eligible nonprofit, educational, or government organizations can request a fee waiver. Apple does not provide a one-time-fee App Store membership, and no separate commercial certificate purchase is needed beyond that membership. Final distribution also requires registered identifiers, signing and provisioning, App Store Connect records, store artwork, accurate privacy answers, and signed Release archives.

## Repository software license is a separate choice

A code-signing certificate proves who produced a binary; a software license states what other people may do with the source. Before a public release, choose one of these deliberately:

- Keep all rights reserved: no open-source `LICENSE`; strongest control, but no SignPath Foundation eligibility.
- MIT or Apache-2.0: permissive open source and potentially eligible for free OSS signing, but others may reuse the code under the license terms.
- A custom source-available license: more control, but not OSI-approved and generally not eligible for free OSS-signing programs.

No source-license choice has been made automatically in this repository. This choice is now also a hard Apple distribution gate because the default KSPlayer 2.3.4 package is GPL-3.0; see [KSPlayer licensing](ksplayer-licensing.md).
