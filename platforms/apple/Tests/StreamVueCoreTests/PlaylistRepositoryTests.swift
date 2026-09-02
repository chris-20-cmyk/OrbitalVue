import Foundation
import Testing
@testable import StreamVueCore

@Suite("Playlist source privacy and recovery")
struct PlaylistRepositoryTests {
    @Test("Normalizes HTTPS defaults and redacts paths, queries, and tokens")
    func normalizesAndRedactsURL() throws {
        let url = try PlaylistSourcePolicy.normalizeURL(
            "provider.example.invalid:8443/get.php?username=fixture&password=secret"
        )
        #expect(url.scheme == "https")
        #expect(PlaylistSourcePolicy.safeDisplayLocation(for: url) == "provider.example.invalid:8443")
        let cleartext = try PlaylistSourcePolicy.normalizeURL("http://legacy.example.invalid/list.m3u")
        #expect(cleartext.scheme == "http")
        #expect(PlaylistSourcePolicy.safeDisplayLocation(for: cleartext) == "legacy.example.invalid")
    }

    @Test("Rejects unsupported schemes and embedded user info")
    func rejectsUnsupportedURLForms() {
        #expect(throws: PlaylistSourcePolicyError.unsupportedURL) {
            try PlaylistSourcePolicy.normalizeURL("ftp://provider.example.invalid/list.m3u")
        }
        #expect(throws: PlaylistSourcePolicyError.unsupportedURL) {
            try PlaylistSourcePolicy.normalizeURL("https://user:secret@provider.example.invalid/list.m3u")
        }
    }

    @Test("Decodes UTF-8, UTF-16 LE, and UTF-16 BE playlists")
    func decodesSupportedText() throws {
        let value = "#EXTM3U\nhttps://stream.example.invalid/live"
        #expect(try PlaylistSourcePolicy.decode(Data(value.utf8)) == value)
        let little = Data([0xFF, 0xFE]) + value.data(using: .utf16LittleEndian)!
        let big = Data([0xFE, 0xFF]) + value.data(using: .utf16BigEndian)!
        #expect(try PlaylistSourcePolicy.decode(little) == value)
        #expect(try PlaylistSourcePolicy.decode(big) == value)
    }

    @Test("Imports a URL while keeping the address out of source.json")
    func protectsURLSource() async throws {
        let fixture = "#EXTM3U\n#EXTINF:-1 group-title=\"News\",One\nhttps://stream.example.invalid/one.m3u8"
        let client = StubHTTPClient(result: .success(Data(fixture.utf8)))
        let secrets = MemorySecretStore()
        let directory = temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let repository = PlaylistRepository(directory: directory, httpClient: client, secretStore: secrets)

        let loaded = try await repository.importURL(
            "https://provider.example.invalid/get.php?username=fixture&password=secret"
        )
        #expect(loaded.catalog.channels.count == 1)
        #expect(loaded.catalog.sources[0].displayLocation == "provider.example.invalid")
        let sourceJSON = try String(contentsOf: directory.appendingPathComponent("source.json"), encoding: .utf8)
        #expect(!sourceJSON.contains("username"))
        #expect(!sourceJSON.contains("password"))
        #expect(!sourceJSON.contains("get.php"))
    }

    @Test("Refreshes URL sources and falls back to the last working copy")
    func refreshesWithFallback() async throws {
        let initial = "#EXTM3U\n#EXTINF:-1,One\nhttps://stream.example.invalid/one.m3u8"
        let refreshed = "#EXTM3U\n#EXTINF:-1,Two\nhttps://stream.example.invalid/two.m3u8"
        let client = StubHTTPClient(result: .success(Data(initial.utf8)))
        let secrets = MemorySecretStore()
        let directory = temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let repository = PlaylistRepository(directory: directory, httpClient: client, secretStore: secrets)

        _ = try await repository.importURL("https://provider.example.invalid/list.m3u")
        await client.setResult(.success(Data(refreshed.utf8)))
        let onlineResult = try await repository.loadSaved()
        let online = try #require(onlineResult)
        #expect(online.catalog.channels[0].name == "Two")
        #expect(!online.usedCachedFallback)

        await client.setResult(.failure(StubError.offline))
        let fallbackResult = try await repository.loadSaved()
        let fallback = try #require(fallbackResult)
        #expect(fallback.catalog.channels[0].name == "Two")
        #expect(fallback.usedCachedFallback)
        #expect(fallback.notice?.contains("last working copy") == true)
    }

    @Test("Imports local files without retaining the external URL")
    func importsLocalFile() async throws {
        let fixture = "#EXTM3U\n#EXTINF:-1,One\nhttps://stream.example.invalid/one.m3u8"
        let directory = temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let repository = PlaylistRepository(
            directory: directory,
            httpClient: StubHTTPClient(result: .failure(StubError.offline)),
            secretStore: MemorySecretStore()
        )
        let loaded = try await repository.importFile(data: Data(fixture.utf8), displayName: "My Channels.m3u")
        #expect(loaded.catalog.sources[0].type == .m3uFile)
        #expect(loaded.catalog.sources[0].displayLocation == "My Channels.m3u")
        let reloaded = try await repository.loadSaved()
        #expect(reloaded?.catalog.channels.count == 1)
    }

    private func temporaryDirectory() -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("streamvue-apple-tests-\(UUID().uuidString)", isDirectory: true)
    }
}

private enum StubError: Error, Sendable {
    case offline
}

private actor StubHTTPClient: PlaylistHTTPClient {
    private var result: Result<Data, StubError>

    init(result: Result<Data, StubError>) {
        self.result = result
    }

    func setResult(_ result: Result<Data, StubError>) {
        self.result = result
    }

    func load(url: URL) async throws -> Data {
        _ = url
        return try result.get()
    }
}

private actor MemorySecretStore: SourceSecretStore {
    private var values: [String: String] = [:]

    func save(_ value: String, for key: String) {
        values[key] = value
    }

    func value(for key: String) -> String? {
        values[key]
    }

    func removeValue(for key: String) {
        values.removeValue(forKey: key)
    }
}
