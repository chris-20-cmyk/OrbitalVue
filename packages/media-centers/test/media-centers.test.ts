import { describe, expect, it } from "vitest";
import {
  MEDIA_CENTER_CONTRACT_VERSION,
  EmbyClient,
  PlexClient,
  authenticateEmby,
  createFetchTransport,
  createMediaCenterCatalog,
  normalizeMediaCenterBaseUrl,
  parseMediaCenterPlaybackUri,
  type MediaCenterConnection,
  type MediaCenterHttpRequest,
  type MediaCenterHttpTransport
} from "../src/index.js";
import { resolveServerPath } from "../src/url.js";

function createMockTransport(
  respond: (request: MediaCenterHttpRequest) => unknown
): { requests: MediaCenterHttpRequest[]; transport: MediaCenterHttpTransport } {
  const requests: MediaCenterHttpRequest[] = [];
  return {
    requests,
    transport: async (request) => {
      requests.push(request);
      return { status: 200, body: JSON.stringify(respond(request)) };
    }
  };
}

describe("media-center URL boundaries", () => {
  it("normalizes server addresses and rejects credential-bearing or unsupported URLs", () => {
    expect(normalizeMediaCenterBaseUrl(" media.home:32400/plex/ "))
      .toBe("https://media.home:32400/plex");
    expect(normalizeMediaCenterBaseUrl("http://127.0.0.1:8096/emby/"))
      .toBe("http://127.0.0.1:8096/emby");

    expect(() => normalizeMediaCenterBaseUrl("ftp://media.home/library"))
      .toThrow(/HTTP or HTTPS/);
    expect(() => normalizeMediaCenterBaseUrl("https://user:password@media.home"))
      .toThrow(/credentials/);
    expect(() => normalizeMediaCenterBaseUrl("https://media.home?api_key=secret"))
      .toThrow(/query or fragment/);
    expect(() => normalizeMediaCenterBaseUrl("https://media.home/#secret"))
      .toThrow(/query or fragment/);
  });

  it("keeps provider-returned paths on the configured origin and strips secret query keys", () => {
    const resolved = resolveServerPath(
      "https://media.home:32400/plex",
      "/library/parts/1/file.ts?X-Plex-Token=upstream&API_KEY=wrong&Token=also-secret&quality=original"
    );
    const url = new URL(resolved);

    expect(url.origin).toBe("https://media.home:32400");
    expect(url.pathname).toBe("/plex/library/parts/1/file.ts");
    expect(url.searchParams.get("quality")).toBe("original");
    expect(url.search.toLowerCase()).not.toContain("token");
    expect(url.search.toLowerCase()).not.toContain("api_key");
    expect(() => resolveServerPath("https://media.home", "https://attacker.example/video"))
      .toThrow(/cross-origin/);
    expect(() => resolveServerPath("https://media.home", "\\\\attacker.example\\video"))
      .toThrow(/cross-origin/);
  });

  it("stops oversized fetch responses before buffering the full body", async () => {
    const fetchImplementation = async (): Promise<Response> => new Response("0123456789", {
      status: 200,
      headers: { "Content-Length": "10" }
    });
    const transport = createFetchTransport(fetchImplementation as typeof fetch, 4);

    await expect(transport({
      method: "GET",
      url: "https://media.home/identity",
      headers: {}
    })).rejects.toMatchObject({ code: "response-too-large", status: 200 });
  });
});

describe("Plex integration", () => {
  it("maps mocked libraries and items without persisting access tokens", async () => {
    const plexToken = "plex-client-token-never-persist";
    const upstreamToken = "plex-upstream-query-token";
    const connection: MediaCenterConnection = {
      contractVersion: MEDIA_CENTER_CONTRACT_VERSION,
      provider: "plex",
      serverId: "plex-server-1",
      displayName: "Living Room Plex",
      baseUrl: "https://plex.home:32400",
      displayLocation: `must-not-be-trusted?X-Plex-Token=${plexToken}`,
      credentialId: "vault-plex-1"
    };
    const mock = createMockTransport((request) => {
      const url = new URL(request.url);
      expect(url.search).not.toContain(plexToken);
      if (url.pathname === "/identity") {
        return { MediaContainer: { machineIdentifier: "plex-server-1", friendlyName: "Plex" } };
      }
      if (url.pathname === "/library/sections") {
        return {
          MediaContainer: {
            Directory: [{ key: "1", title: "Movies", type: "movie", totalSize: 1 }]
          }
        };
      }
      if (url.pathname === "/library/sections/1/all") {
        return {
          MediaContainer: {
            offset: 0,
            totalSize: 1,
            Metadata: [{
              ratingKey: "100",
              title: "A Test Movie",
              type: "movie",
              duration: 7_200_000,
              thumb: `/library/metadata/100/thumb?x-plex-token=${upstreamToken}`,
              Media: [{
                id: "media-100",
                container: "mkv",
                videoCodec: "hevc",
                audioCodec: "eac3",
                width: 3840,
                height: 2160,
                Part: [{
                  id: "part-100",
                  key: `/library/parts/100/file.mkv?X-Plex-Token=${upstreamToken}`,
                  Stream: [
                    { index: 0, streamType: 1, codec: "hevc", selected: true },
                    { index: 1, streamType: 2, codec: "eac3", languageCode: "eng", channels: 6 }
                  ]
                }]
              }]
            }]
          }
        };
      }
      throw new Error(`Unexpected Plex request: ${request.url}`);
    });
    const client = new PlexClient(mock.transport, {
      connection,
      token: plexToken,
      clientIdentifier: "streamvue-test-device",
      product: "StreamVue\r\nX-Injected: no",
      version: "5.1-test"
    });

    await expect(client.getIdentity()).resolves.toMatchObject({ serverId: "plex-server-1" });
    const libraries = await client.getLibraries();
    const page = await client.getItems(libraries[0]!, 0, 50);
    const item = page.items[0]!;
    const playback = client.getPlaybackPlan(item);
    const artwork = client.artworkRequest(item)!;
    const catalog = createMediaCenterCatalog(connection, libraries, page.items, "2026-08-26T12:00:00Z");
    const serializedCatalog = JSON.stringify(catalog);

    expect(playback.requestHeaders["X-Plex-Token"]).toBe(plexToken);
    expect(playback.sensitiveHeaderNames).toEqual(["X-Plex-Token"]);
    expect(playback.url).not.toContain(plexToken);
    expect(playback.url).not.toContain(upstreamToken);
    expect(artwork.url).not.toContain(upstreamToken);
    expect(mock.requests.every((request) => !request.headers["X-Plex-Product"]?.includes("\r")))
      .toBe(true);
    expect(catalog.sources[0]?.displayLocation).toBe("plex.home:32400");
    expect(catalog.channels[0]?.stream.requestHeaders).toEqual({});
    expect(catalog.channels[0]?.stream.uri).toBe("streamvue-media://plex/plex-server-1/100");
    expect(parseMediaCenterPlaybackUri(catalog.channels[0]!.stream.uri)).toEqual({
      provider: "plex",
      serverId: "plex-server-1",
      itemId: "100"
    });
    expect(serializedCatalog).not.toContain(plexToken);
    expect(serializedCatalog).not.toContain(upstreamToken);
    expect(serializedCatalog).not.toContain("X-Plex-Token");
  });
});

describe("Emby integration", () => {
  it("authenticates and maps playback while keeping passwords and tokens out of the catalog", async () => {
    const password = "emby-password-never-persist";
    const embyToken = "emby-access-token-never-persist";
    const upstreamToken = "emby-upstream-query-token";
    const mock = createMockTransport((request) => {
      const url = new URL(request.url);
      expect(url.search).not.toContain(password);
      expect(url.search).not.toContain(embyToken);
      if (url.pathname === "/emby/Users/AuthenticateByName") {
        expect(request.method).toBe("POST");
        expect(JSON.parse(request.body ?? "{}")).toEqual({
          Username: "chris",
          Pw: password
        });
        return {
          AccessToken: embyToken,
          ServerId: "emby-server-1",
          User: { Id: "user-1", Name: "Chris" }
        };
      }
      if (url.pathname === "/emby/Users/user-1/Views") {
        return { Items: [{ Id: "lib-1", Name: "Movies", CollectionType: "movies", ChildCount: 1 }] };
      }
      if (url.pathname === "/emby/Users/user-1/Items") {
        return {
          TotalRecordCount: 1,
          Items: [{
            Id: "item-1",
            Name: "Another Test Movie",
            Type: "Movie",
            RunTimeTicks: 36_000_000_000,
            UserData: { PlaybackPositionTicks: 12_000_000, Played: false },
            ImageTags: { Primary: "image-tag-1" },
            MediaSources: [{
              Id: "source-1",
              Container: "mkv",
              SupportsDirectPlay: true,
              SupportsDirectStream: true,
              SupportsTranscoding: true,
              MediaStreams: [
                { Index: 0, Type: "Video", Codec: "h264", Width: 1920, Height: 1080 },
                { Index: 1, Type: "Audio", Codec: "aac", Language: "eng", Channels: 6 }
              ]
            }]
          }]
        };
      }
      if (url.pathname === "/emby/Items/item-1/PlaybackInfo") {
        return {
          PlaySessionId: "play-session-1",
          MediaSources: [{
            Id: "source-1",
            Container: "mkv",
            SupportsDirectPlay: true,
            SupportsDirectStream: true,
            SupportsTranscoding: true,
            DirectStreamUrl: `/Videos/item-1/stream.mkv?API_KEY=${upstreamToken}&quality=original`,
            RequiredHttpHeaders: {
              Referer: "https://emby.home/player\r\nX-Injected: blocked",
              "X-Playback-Mode": "direct",
              Host: "attacker.example",
              Authorization: "Bearer server-controlled",
              Cookie: "server-cookie",
              "X-Emby-Token": "server-controlled-token"
            }
          }]
        };
      }
      throw new Error(`Unexpected Emby request: ${request.url}`);
    });
    const device = {
      client: "StreamVue",
      device: "Vitest",
      deviceId: "streamvue-test-device",
      version: "5.1-test"
    };
    const session = await authenticateEmby(mock.transport, {
      baseUrl: "https://emby.home",
      username: " chris ",
      password,
      device
    });
    const connection: MediaCenterConnection = {
      contractVersion: MEDIA_CENTER_CONTRACT_VERSION,
      provider: "emby",
      serverId: session.serverId,
      displayName: "Home Emby",
      baseUrl: "https://emby.home",
      displayLocation: `do-not-trust?api_key=${embyToken}`,
      credentialId: "vault-emby-1",
      userId: session.userId
    };
    const client = new EmbyClient(mock.transport, { connection, token: session.accessToken, device });
    const libraries = await client.getLibraries();
    const page = await client.getItems(libraries[0]!, Number.NaN, Number.POSITIVE_INFINITY);
    const item = page.items[0]!;
    const playback = await client.getPlaybackPlan(item);
    const catalog = createMediaCenterCatalog(connection, libraries, page.items, "2026-08-26T12:00:00Z");
    const serializedCatalog = JSON.stringify(catalog);

    expect(session).toMatchObject({ accessToken: embyToken, userId: "user-1" });
    expect(page.start).toBe(0);
    expect(new URL(mock.requests.find((request) => request.url.includes("/Items?"))!.url)
      .searchParams.get("Limit")).toBe("200");
    expect(playback.url).not.toContain(embyToken);
    expect(playback.url).not.toContain(upstreamToken);
    expect(playback.requestHeaders["X-Emby-Token"]).toBe(embyToken);
    expect(playback.requestHeaders["X-Emby-Authorization"]).toContain(embyToken);
    expect(playback.requestHeaders["X-Playback-Mode"]).toBe("direct");
    expect(playback.requestHeaders.Referer).not.toContain("\r");
    expect(playback.requestHeaders.Host).toBeUndefined();
    expect(playback.requestHeaders.Authorization).toBeUndefined();
    expect(playback.requestHeaders.Cookie).toBeUndefined();
    expect(playback.sensitiveHeaderNames).toEqual(["X-Emby-Token", "X-Emby-Authorization"]);
    expect(catalog.sources[0]?.displayLocation).toBe("emby.home");
    expect(catalog.channels[0]?.stream.requestHeaders).toEqual({});
    expect(catalog.channels[0]?.stream.uri).toBe("streamvue-media://emby/emby-server-1/item-1");
    expect(serializedCatalog).not.toContain(password);
    expect(serializedCatalog).not.toContain(embyToken);
    expect(serializedCatalog).not.toContain(upstreamToken);
    expect(serializedCatalog).not.toContain("X-Emby-Token");
    expect(serializedCatalog).not.toContain("X-Emby-Authorization");
  });
});
