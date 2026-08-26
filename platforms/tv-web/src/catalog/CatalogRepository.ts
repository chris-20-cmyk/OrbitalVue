import {
  createCatalogFromM3u,
  sha256Hex,
  type SourceType,
  type StreamVueCatalog
} from "@streamvue/catalog";
import { CatalogCache } from "./CatalogCache.js";
import { createDemoCatalog } from "./demoCatalog.js";

const MAX_PLAYLIST_BYTES = 64 * 1024 * 1024;
const DOWNLOAD_TIMEOUT_MS = 30_000;

export interface CatalogLoadResult {
  catalog: StreamVueCatalog;
  notice: string | null;
  refreshed: boolean;
}

export class CatalogRepository {
  constructor(private readonly cache = new CatalogCache()) {}

  async loadSaved(): Promise<CatalogLoadResult | null> {
    const saved = await this.cache.read();
    if (!saved) return null;
    if (!saved.sourceUrl) return { catalog: saved.catalog, notice: null, refreshed: false };

    try {
      const playlist = await this.download(saved.sourceUrl);
      const source = saved.catalog.sources[0];
      if (!source) throw new Error("The saved source is incomplete.");
      const catalog = this.buildCatalog(playlist, {
        catalogId: saved.catalog.catalogId,
        sourceId: source.id,
        sourceName: source.name,
        sourceType: "m3u-url",
        displayLocation: safeDisplayLocation(saved.sourceUrl),
        refreshOnLaunch: true
      });
      await this.cache.write(saved.sourceUrl, catalog);
      return { catalog, notice: "Source refreshed at launch", refreshed: true };
    } catch {
      return {
        catalog: saved.catalog,
        notice: "The source is offline. StreamVue kept the last working channel catalog.",
        refreshed: false
      };
    }
  }

  async connectUrl(rawValue: string): Promise<CatalogLoadResult> {
    const sourceUrl = normalizePlaylistUrl(rawValue);
    const playlist = await this.download(sourceUrl);
    const host = safeDisplayLocation(sourceUrl);
    const sourceId = `url-${sha256Hex(sourceUrl).slice(0, 24).toLowerCase()}`;
    const catalog = this.buildCatalog(playlist, {
      catalogId: sourceId,
      sourceId,
      sourceName: host,
      sourceType: "m3u-url",
      displayLocation: host,
      refreshOnLaunch: true
    });
    await this.cache.write(sourceUrl, catalog);
    return { catalog, notice: "Connected securely. Startup refresh is on.", refreshed: true };
  }

  async importFile(file: File): Promise<CatalogLoadResult> {
    if (file.size > MAX_PLAYLIST_BYTES) throw new Error("The playlist is larger than the 64 MB safety limit.");
    const bytes = new Uint8Array(await file.arrayBuffer());
    const playlist = decodePlaylist(bytes);
    const sourceName = file.name.replace(/\.(m3u8?|txt)$/i, "").trim() || "Imported playlist";
    const sourceId = `file-${sha256Hex(`${file.name}|${file.size}|${file.lastModified}`).slice(0, 24).toLowerCase()}`;
    const catalog = this.buildCatalog(playlist, {
      catalogId: sourceId,
      sourceId,
      sourceName,
      sourceType: "m3u-file",
      displayLocation: file.name,
      refreshOnLaunch: false
    });
    await this.cache.write(null, catalog);
    return { catalog, notice: "Playlist imported and kept privately on this television.", refreshed: false };
  }

  async useDemo(): Promise<CatalogLoadResult> {
    const catalog = createDemoCatalog();
    return { catalog, notice: "Demonstration catalog — connect your playlist to watch.", refreshed: false };
  }

  async clear(): Promise<void> {
    await this.cache.clear();
  }

  private buildCatalog(
    playlist: string,
    source: {
      catalogId: string;
      sourceId: string;
      sourceName: string;
      sourceType: SourceType;
      displayLocation: string;
      refreshOnLaunch: boolean;
    }
  ): StreamVueCatalog {
    return createCatalogFromM3u(playlist, {
      catalogId: source.catalogId,
      displayName: source.sourceName,
      sourceId: source.sourceId,
      sourceName: source.sourceName,
      sourceType: source.sourceType,
      displayLocation: source.displayLocation,
      refreshOnLaunch: source.refreshOnLaunch
    });
  }

  private async download(sourceUrl: string): Promise<string> {
    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), DOWNLOAD_TIMEOUT_MS);
    try {
      const response = await fetch(sourceUrl, {
        cache: "no-store",
        redirect: "follow",
        signal: controller.signal
      });
      if (!response.ok) throw new Error(`Playlist server returned HTTP ${response.status}.`);
      if (sourceUrl.toLowerCase().startsWith("https://") && response.url.toLowerCase().startsWith("http://")) {
        throw new Error("The playlist server attempted an insecure redirect.");
      }
      const announcedLength = Number.parseInt(response.headers.get("Content-Length") ?? "0", 10);
      if (announcedLength > MAX_PLAYLIST_BYTES) throw new Error("The playlist is larger than the 64 MB safety limit.");
      return decodePlaylist(await readLimitedBody(response));
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") {
        throw new Error("The playlist server did not respond within 30 seconds.");
      }
      throw error;
    } finally {
      window.clearTimeout(timeout);
    }
  }
}

async function readLimitedBody(response: Response): Promise<Uint8Array> {
  if (!response.body) {
    const bytes = new Uint8Array(await response.arrayBuffer());
    if (bytes.byteLength > MAX_PLAYLIST_BYTES) throw new Error("The playlist is larger than the 64 MB safety limit.");
    return bytes;
  }

  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let length = 0;
  while (true) {
    const result = await reader.read();
    if (result.done) break;
    if (!result.value) continue;
    length += result.value.byteLength;
    if (length > MAX_PLAYLIST_BYTES) {
      await reader.cancel();
      throw new Error("The playlist is larger than the 64 MB safety limit.");
    }
    chunks.push(result.value);
  }

  const combined = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    combined.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return combined;
}

function decodePlaylist(bytes: Uint8Array): string {
  if (bytes.length >= 2 && bytes[0] === 0xff && bytes[1] === 0xfe) {
    return new TextDecoder("utf-16le").decode(bytes.slice(2));
  }
  if (bytes.length >= 2 && bytes[0] === 0xfe && bytes[1] === 0xff) {
    const swapped = bytes.slice(2);
    for (let index = 0; index + 1 < swapped.length; index += 2) {
      const first = swapped[index] ?? 0;
      swapped[index] = swapped[index + 1] ?? 0;
      swapped[index + 1] = first;
    }
    return new TextDecoder("utf-16le").decode(swapped);
  }
  return new TextDecoder("utf-8").decode(bytes).replace(/^\uFEFF/, "");
}

export function normalizePlaylistUrl(rawValue: string): string {
  const trimmed = rawValue.trim();
  if (!trimmed) throw new Error("Enter a playlist URL.");
  const candidate = trimmed.includes("://") ? trimmed : `https://${trimmed}`;
  let parsed: URL;
  try {
    parsed = new URL(candidate);
  } catch {
    throw new Error("Enter a complete HTTP or HTTPS playlist URL.");
  }
  if (!(["http:", "https:"] as string[]).includes(parsed.protocol) || !parsed.hostname) {
    throw new Error("Enter a complete HTTP or HTTPS playlist URL.");
  }
  return parsed.toString();
}

export function safeDisplayLocation(sourceUrl: string): string {
  const parsed = new URL(sourceUrl);
  return parsed.host;
}
