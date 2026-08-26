# StreamVue for Samsung Tizen and LG webOS

This project is the shared television UI for StreamVue 5.0. It deliberately uses framework-free TypeScript and a small Vite bundle so remote navigation and startup remain responsive on embedded television browsers.

## What works

- M3U/M3U8 URL and file import with a 64 MB safety limit
- Automatic URL refresh at launch with an IndexedDB last-working catalog
- Exact playlist groups and grouped section labels in All Channels
- Large-library windowing, search, and private favorites
- Directional-pad, OK, Back, media-key, channel-key, and Magic Remote interaction
- A Samsung AVPlay adapter with User-Agent and Cookie support
- Native HTML5/HLS playback for LG webOS and browser development
- Auto, Fit, Fill, Zoom, 16:9, 4:3, and 21:9 framing
- Buffering shown only while the native player reports buffering

Raw playlist locations never appear in normal browsing; only the provider host and optional port are displayed. The full source URL remains in app-private television storage so it can refresh at launch.

Samsung AVPlay exposes User-Agent and Cookie streaming properties but not an arbitrary Referer property. LG's portable HTML5 video path cannot guarantee custom request headers. StreamVue reports that limitation instead of sending a private source through a proxy.

## Build and verify

From the repository root:

```powershell
pnpm install
pnpm tv:check
pnpm tv:test
pnpm tv:build
```

The build produces:

- `platforms/tv-web/dist/web` — browser QA build
- `platforms/tv-web/dist/samsung` — Tizen project contents
- `platforms/tv-web/dist/webos` — webOS package contents

## Personal television installation

Samsung requires Tizen Studio, its TV extensions, a device-author certificate profile, and the TV's developer mode. After `pnpm tv:build`:

```text
tizen build-web -- platforms/tv-web/dist/samsung
tizen package -t wgt -s YOUR_CERTIFICATE_PROFILE -- platforms/tv-web/dist/samsung/.buildResult
```

LG uses the free Developer Mode app and the current webOS CLI. After registering the TV with `ares-setup-device`:

```text
ares-package platforms/tv-web/dist/webos
ares-install --device YOUR_TV com.streamvue.player.tv_5.0.0_all.ipk
```

The generated Samsung application/package ID is provisional until the first Tizen Studio device project is paired. Keep the same IDs after store submission begins. LG and Samsung packages must be tested on real televisions because codecs, HLS variants, remote keys, and provider header requirements vary by model.
