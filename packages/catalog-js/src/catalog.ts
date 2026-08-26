import { parseM3u } from "./m3u.js";
import { CATALOG_CONTRACT_VERSION, type CreateCatalogOptions, type StreamVueCatalog } from "./types.js";

export function createCatalogFromM3u(text: string, options: CreateCatalogOptions): StreamVueCatalog {
  const parsed = parseM3u(text, options);
  return {
    contractVersion: CATALOG_CONTRACT_VERSION,
    catalogId: options.catalogId,
    displayName: options.displayName,
    loadedAt: options.loadedAt ?? new Date().toISOString(),
    sources: [
      {
        id: options.sourceId,
        name: options.sourceName,
        type: options.sourceType,
        displayLocation: options.displayLocation,
        refreshOnLaunch: options.refreshOnLaunch
      }
    ],
    guideSources: parsed.guideSources,
    channels: parsed.channels
  };
}
