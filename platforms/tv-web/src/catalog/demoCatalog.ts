import { createCatalogFromM3u, type StreamVueCatalog } from "@streamvue/catalog";

const DEMO_PLAYLIST = `#EXTM3U
#EXTINF:-1 tvg-id="northstar.news" group-title="News",Northstar News
https://demo.invalid/live/northstar.m3u8
#EXTINF:-1 tvg-id="metro.sports" group-title="Sports",Metro Sports
https://demo.invalid/live/metro-sports.m3u8
#EXTINF:-1 tvg-id="cinema.one" group-title="Movies",Cinema One
https://demo.invalid/live/cinema-one.m3u8
#EXTINF:-1 tvg-id="world.report" group-title="News",World Report
https://demo.invalid/live/world-report.m3u8
#EXTINF:-1 tvg-id="kids.space" group-title="Kids",Kids Space
https://demo.invalid/live/kids-space.m3u8`;

export function createDemoCatalog(): StreamVueCatalog {
  return createCatalogFromM3u(DEMO_PLAYLIST, {
    catalogId: "streamvue-demo",
    displayName: "OrbitalVue demonstration",
    sourceId: "streamvue-demo-source",
    sourceName: "OrbitalVue demonstration",
    sourceType: "generated",
    displayLocation: "Built-in demonstration",
    refreshOnLaunch: false
  });
}
