import { describe, expect, it } from "vitest";
import type { StreamVueCatalog } from "@streamvue/catalog";
import type {
  MediaCenterHttpRequest,
  MediaCenterHttpTransport,
  MediaCenterSnapshot
} from "@streamvue/media-centers";
import {
  createMediaCenterCatalog,
  createMediaCenterConnection
} from "@streamvue/media-centers";
import type {
  CatalogStore,
  SavedCatalogRecord
} from "../src/catalog/CatalogCache.js";
import { CatalogRepository } from "../src/catalog/CatalogRepository.js";
import {
  SessionCredentialVault,
  type ProtectedMediaCredential
} from "../src/catalog/CredentialVault.js";
import { evaluatePremiumAccess } from "../src/premium/PremiumAccess.js";

class MemoryCatalogStore implements CatalogStore {
  record: SavedCatalogRecord | null = null;

  async read(): Promise<SavedCatalogRecord | null> {
    return this.record === null
      ? null
      : JSON.parse(JSON.stringify(this.record)) as SavedCatalogRecord;
  }

  async write(sourceUrl: string | null, catalog: StreamVueCatalog): Promise<void> {
    this.record = {
      key: "active",
      sourceUrl,
      catalog,
      savedAt: new Date().toISOString(),
      sourceKind: sourceUrl ? "playlist-url" : "playlist-file"
    };
  }

  async writeMediaCenter(
    snapshot: MediaCenterSnapshot,
    catalog: StreamVueCatalog
  ): Promise<void> {
    this.record = {
      key: "active",
      sourceUrl: null,
      catalog,
      savedAt: new Date().toISOString(),
      sourceKind: "media-center",
      mediaCenterSnapshot: snapshot
    };
  }

  async clear(): Promise<void> {
    this.record = null;
  }
}

describe("television media-center repository", () => {
  it("keeps Plex credentials out of disk snapshots and materializes them only for playback", async () => {
    const accessToken = "plex-tv-access-token-never-on-disk";
    const requests: MediaCenterHttpRequest[] = [];
    const transport: MediaCenterHttpTransport = async (request) => {
      requests.push(request);
      const path = new URL(request.url).pathname;
      if (path === "/identity") {
        expect(request.headers["X-Plex-Token"]).toBeUndefined();
        return {
          status: 200,
          body: JSON.stringify({
            MediaContainer: {
              machineIdentifier: "plex-tv-server",
              friendlyName: "Living Room Plex"
            }
          })
        };
      }
      expect(request.headers["X-Plex-Token"]).toBe(accessToken);
      if (path === "/library/sections") {
        return {
          status: 200,
          body: JSON.stringify({
            MediaContainer: {
              Directory: [{ key: "movies", title: "Movies", type: "movie", totalSize: 1 }]
            }
          })
        };
      }
      if (path === "/library/sections/movies/all") {
        return {
          status: 200,
          body: JSON.stringify({
            MediaContainer: {
              offset: 0,
              totalSize: 1,
              Metadata: [{
                ratingKey: "movie-1",
                title: "Television Test Movie",
                type: "movie",
                viewOffset: 41_000,
                Media: [{
                  id: "media-1",
                  container: "mp4",
                  Part: [{
                    id: "part-1",
                    key: "/library/parts/movie-1/file.mp4?X-Plex-Token=upstream-token",
                    Stream: []
                  }]
                }]
              }]
            }
          })
        };
      }
      throw new Error(`Unexpected Plex request: ${request.url}`);
    };
    const cache = new MemoryCatalogStore();
    const vault = new SessionCredentialVault();
    const repository = new CatalogRepository(cache, vault, transport);

    const loaded = await repository.connectPlex({
      serverAddress: "https://plex.home:32400",
      accessToken,
      allowInsecureHttp: false
    });
    const savedBeforePlayback = JSON.stringify(cache.record);

    expect(loaded.catalog.channels).toHaveLength(1);
    expect(savedBeforePlayback).not.toContain(accessToken);
    expect(savedBeforePlayback).not.toContain("upstream-token");
    expect(savedBeforePlayback).not.toContain("X-Plex-Token");

    const resolved = await repository.resolvePlayback(loaded.catalog.channels[0]!);
    const resolvedUrl = new URL(resolved.channel.stream.uri);

    expect(resolvedUrl.searchParams.get("X-Plex-Token")).toBe(accessToken);
    expect(resolvedUrl.search).not.toContain("upstream-token");
    expect(resolved.channel.stream.requestHeaders["X-Plex-Token"]).toBeUndefined();
    expect(resolved.startPositionMs).toBe(41_000);
    expect(JSON.stringify(cache.record)).toBe(savedBeforePlayback);
    expect(requests.filter((request) => new URL(request.url).pathname === "/identity"))
      .toHaveLength(3);
  });

  it("validates and isolates credentials kept for a legacy television session", async () => {
    const vault = new SessionCredentialVault();
    const record: ProtectedMediaCredential = {
      schemaVersion: 1,
      binding: {
        contractVersion: "1.0",
        provider: "plex",
        serverId: "server-1",
        baseUrl: "https://plex.home:32400",
        credentialId: "mc-plex-test",
        allowInsecureHttp: false
      },
      accessToken: "session-secret"
    };
    await vault.write(record);
    record.accessToken = "mutated-after-save";

    await expect(vault.read("mc-plex-test")).resolves.toMatchObject({
      accessToken: "session-secret"
    });
    await expect(vault.read("different-reference")).resolves.toBeNull();
    await vault.remove("mc-plex-test");
    await expect(vault.read("mc-plex-test")).resolves.toBeNull();
  });

  it("refuses a credential bound to another server before making a network request", async () => {
    const connection = createMediaCenterConnection({
      provider: "plex",
      serverId: "expected-server",
      displayName: "Expected Plex",
      baseUrl: "https://expected.home:32400",
      credentialId: "mc-plex-binding-test"
    });
    const snapshot: MediaCenterSnapshot = {
      contractVersion: connection.contractVersion,
      loadedAt: new Date().toISOString(),
      connection,
      libraries: [],
      items: []
    };
    const cache = new MemoryCatalogStore();
    cache.record = {
      key: "active",
      sourceUrl: null,
      catalog: createMediaCenterCatalog(connection, [], [], snapshot.loadedAt),
      savedAt: snapshot.loadedAt,
      sourceKind: "media-center",
      mediaCenterSnapshot: snapshot
    };
    const vault = new SessionCredentialVault();
    await vault.write({
      schemaVersion: 1,
      binding: {
        contractVersion: connection.contractVersion,
        provider: "plex",
        serverId: "different-server",
        baseUrl: "https://different.home:32400",
        credentialId: connection.credentialId,
        allowInsecureHttp: false
      },
      accessToken: "never-send-this-token"
    });
    let requestCount = 0;
    const repository = new CatalogRepository(cache, vault, async () => {
      requestCount += 1;
      throw new Error("network should not be reached");
    });

    const restored = await repository.loadSaved();

    expect(restored?.refreshed).toBe(false);
    expect(restored?.notice).toContain("no longer matches this server");
    expect(requestCount).toBe(0);
  });

  it("blocks a store media-center restore before credentials or network are touched", async () => {
    const connection = createMediaCenterConnection({
      provider: "plex",
      serverId: "locked-server",
      displayName: "Locked Plex",
      baseUrl: "https://locked.home:32400",
      credentialId: "mc-plex-locked"
    });
    const snapshot: MediaCenterSnapshot = {
      contractVersion: connection.contractVersion,
      loadedAt: new Date().toISOString(),
      connection,
      libraries: [],
      items: []
    };
    const cache = new MemoryCatalogStore();
    cache.record = {
      key: "active",
      sourceUrl: null,
      catalog: createMediaCenterCatalog(connection, [], [], snapshot.loadedAt),
      savedAt: snapshot.loadedAt,
      sourceKind: "media-center",
      mediaCenterSnapshot: snapshot
    };
    let requestCount = 0;
    const repository = new CatalogRepository(
      cache,
      new SessionCredentialVault(),
      async () => {
        requestCount += 1;
        throw new Error("network should not be reached");
      },
      evaluatePremiumAccess("store", false)
    );

    await expect(repository.loadSaved()).rejects.toThrow("one-time store purchase");
    expect(requestCount).toBe(0);
  });
});
