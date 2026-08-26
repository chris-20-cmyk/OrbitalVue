# StreamVue for Android and Google TV

This is the native Kotlin/Compose StreamVue 5.0 foundation for Android phones, tablets, Android TV, and Google TV. It uses AndroidX Media3 instead of embedding the Windows player or a browser playback engine.

## Build

Requirements:

- JDK 17
- Android SDK Platform 37.1 and Build Tools 37.0.0

StreamVue compiles against the 37.1 API surface required by its AndroidX dependencies while keeping `targetSdk = 36` for the current stable platform behavior.

From the repository root on Windows:

```powershell
.\platforms\android\gradlew.bat -p platforms\android testDebugUnitTest lintDebug assembleDebug bundleRelease
```

From macOS or Linux:

```bash
bash platforms/android/gradlew -p platforms/android testDebugUnitTest lintDebug assembleDebug bundleRelease
```

The wrapper pins Gradle 9.5.0 and verifies the official distribution SHA-256 before use.

## Outputs

- Personal test APK: `app/build/outputs/apk/debug/app-debug.apk`
- Google Play bundle: `app/build/outputs/bundle/release/app-release.aab`
- Unit-test report: `app/build/reports/tests/testDebugUnitTest/index.html`
- Android lint report: `app/build/reports/lint-results-debug.html`

The debug APK uses Android's generated debug key and can be installed on a personal device after enabling normal sideload installation for the file source. The release AAB is unsigned until a permanent Google Play upload-key strategy is selected.

## Current playback support

- HLS, progressive HTTP MPEG-TS/MP4, and RTSP
- Per-channel User-Agent and Referer headers
- MediaCodec hardware decoding with decoder fallback
- Adaptive/seamless frame-rate hints
- Auto/Fit, Fill, Zoom/Crop, 16:9, 4:3, and 21:9 framing
- Immersive fullscreen with a single preserved ExoPlayer session

RTMP entries remain in the portable catalog for compatibility but are not claimed as playable by Media3.

## Source privacy

Raw URL sources and cached playlists stay in app-private storage. Android cloud backup and device-to-device transfer are disabled for all StreamVue state because IPTV URLs can contain private account tokens. Displayed source locations show only the provider host and optional port.
