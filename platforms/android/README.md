# OrbitalVue for Android and Google TV

This is the native Kotlin/Compose OrbitalVue 5.2 foundation for Android phones, tablets, Android TV, and Google TV. It uses AndroidX Media3 instead of embedding the Windows player or a browser playback engine.

## Build

Requirements:

- JDK 17
- Android SDK Platform 37.1 and Build Tools 37.0.0

OrbitalVue compiles against the 37.1 API surface required by its AndroidX dependencies while keeping `targetSdk = 36` for the current stable platform behavior.

From the repository root on Windows:

```powershell
.\platforms\android\gradlew.bat -p platforms\android testDebugUnitTest lintDebug assembleDebug bundleRelease
```

The default `personal` distribution includes Plex and Emby. To verify the fail-closed store surface before Google Play Billing is connected, append `-PorbitalVueDistributionMode=store`; that build will not collect media-server credentials or contact a saved media center. The foundation workflow deliberately leaves this locked AAB unsigned. See [premium access and store readiness](../../docs/premium-entitlements.md).

The Android/Google TV client includes Google Play Billing Library 9.1.0 for a one-time non-consumable unlock. A charge can be offered only when a real Play Console product and secure purchase verifier are both supplied:

```text
-PorbitalVueDistributionMode=store
-PorbitalVuePremiumProductId=<exact Play Console product ID>
-PorbitalVuePremiumVerificationUrl=https://<your verifier>/google-play/verify
```

The verifier must validate the purchase token with the Google Play Developer API. With either value missing, purchase and restore controls remain unavailable and Plex/Emby stay locked.

## Plex account connection

Personal builds offer **Sign in with Plex** as the recommended connection path. OrbitalVue creates a strong signed PIN, shows a QR code and browser action, discovers the Plex Media Servers authorized for that account, and lets the user choose a verified secure/local endpoint. Manual server-token entry remains available under the advanced option.

The account token exists only during the short discovery operation and never enters Compose UI state or the saved catalog. A selected server token is saved only after OrbitalVue rechecks the server identity and selected origin. The stable Ed25519 device identity is stored only as a Tink encrypted keyset whose wrapping key is created and tested in Android Keystore; the feature fails closed instead of falling back to a cleartext keyset. Android backup and device-to-device transfer remain disabled for this state.

## Google Play upload signing

Google Play App Signing uses two keys: Google protects the app-signing key used on delivered APKs, while OrbitalVue signs each uploaded AAB with a separate upload key. The upload key is self-generated and free; it is not a purchased public certificate. Keep its keystore and passwords outside the repository and back them up securely.

Create a long-lived RSA upload key once (4096 bits is used here; the workflow rejects keys below 2048 bits):

```powershell
keytool -genkeypair -v `
  -keystore orbitalvue-google-play-upload.jks `
  -alias orbitalvue-upload `
  -keyalg RSA `
  -keysize 4096 `
  -validity 10000

keytool -export -rfc `
  -keystore orbitalvue-google-play-upload.jks `
  -alias orbitalvue-upload `
  -file orbitalvue-google-play-upload-certificate.pem
```

Register the public certificate during Play App Signing setup. Never upload the `.jks` file anywhere except the protected GitHub secret used by this repository's candidate workflow. Configure these repository variables:

- `ORBITALVUE_ANDROID_PREMIUM_PRODUCT_ID` — exact one-time product ID from Play Console.
- `ORBITALVUE_ANDROID_PREMIUM_VERIFICATION_URL` — production HTTPS purchase verifier.
- `ORBITALVUE_ANDROID_UPLOAD_CERT_SHA256` — SHA-256 fingerprint of the registered upload certificate, with or without colons.

Configure these GitHub Actions secrets:

- `ORBITALVUE_ANDROID_UPLOAD_KEYSTORE_BASE64` — Base64 contents of the upload `.jks`.
- `ORBITALVUE_ANDROID_UPLOAD_STORE_PASSWORD`
- `ORBITALVUE_ANDROID_UPLOAD_KEY_ALIAS`
- `ORBITALVUE_ANDROID_UPLOAD_KEY_PASSWORD`

PowerShell can prepare the Base64 value without altering the keystore:

```powershell
[Convert]::ToBase64String(
  [IO.File]::ReadAllBytes((Resolve-Path .\orbitalvue-google-play-upload.jks))
)
```

Finally, set the Android entry in `store/premium-products.json` to the exact product ID, `verificationProvider` to `google-play-developer-api`, and `ready` to `true` only after the product and secure verifier are production-tested. Then run the manual **Build Google Play candidate** workflow with a new, never-before-uploaded version code.

That workflow fails closed unless every value matches, builds and tests the Store configuration, verifies the upload signature and registered certificate fingerprint, rejects packaged key files, and emits an AAB plus SHA-256 checksum for manual Play Console upload. It does not publish to a track automatically. Signing credentials are read only from the environment; supplying them to a personal build is rejected.

From macOS or Linux:

```bash
bash platforms/android/gradlew -p platforms/android testDebugUnitTest lintDebug assembleDebug bundleRelease
```

The wrapper pins Gradle 9.5.0 and verifies the official distribution SHA-256 before use.

## Outputs

- Personal test APK: `app/build/outputs/apk/debug/app-debug.apk`
- Locked foundation AAB: `app/build/outputs/bundle/release/app-release.aab` (unsigned by design)
- Readiness-gated Play candidate: workflow artifact `OrbitalVue-<version-name>-<version-code>-google-play-upload.aab`
- Unit-test report: `app/build/reports/tests/testDebugUnitTest/index.html`
- Android lint report: `app/build/reports/lint-results-debug.html`

The debug APK uses Android's generated debug key and can be installed on a personal device after enabling normal sideload installation for the file source. Only the gated candidate workflow restores the protected upload keystore and signs a Store AAB.

## Current playback support

- HLS, progressive HTTP MPEG-TS/MP4, and RTSP
- Per-channel User-Agent and Referer headers
- MediaCodec hardware decoding with decoder fallback
- Adaptive/seamless frame-rate hints
- Auto/Fit, Fill, Zoom/Crop, 16:9, 4:3, and 21:9 framing
- Immersive fullscreen with a single preserved ExoPlayer session

RTMP entries remain in the portable catalog for compatibility but are not claimed as playable by Media3.

## Source privacy

Raw URL sources and cached playlists stay in app-private storage. Android cloud backup and device-to-device transfer are disabled for all OrbitalVue state because IPTV URLs can contain private account tokens. Displayed source locations show only the provider host and optional port. Plex account discovery retains no account token; the chosen server token uses the existing Android Keystore-backed credential vault.
