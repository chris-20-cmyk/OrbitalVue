import Foundation
import Testing
@testable import StreamVueCore

@Suite("Media-center security and mapping")
struct MediaCenterTests {
    @Test("Persists a cache-safe media source and resolves playback after restart")
    func persistsMediaCenterSource() async throws {
        let token = "plex-repository-token-never-cache"
        let http = StubMediaCenterHTTPClient { request in
            switch request.url.path {
            case "/identity":
                return jsonResponse(#"{"MediaContainer":{"machineIdentifier":"plex-repository-server","friendlyName":"Home Plex"}}"#)
            case "/library/sections":
                return jsonResponse(#"{"MediaContainer":{"Directory":[{"key":"1","title":"Movies","type":"movie","totalSize":1}]}}"#)
            case "/library/sections/1/all":
                return jsonResponse(#"{"MediaContainer":{"offset":0,"totalSize":1,"Metadata":[{"ratingKey":"movie-1","title":"Repository Movie","type":"movie","Media":[{"id":"media-1","Part":[{"id":"part-1","key":"/library/parts/1/movie.mkv"}]}]}]}}"#)
            default:
                return MediaCenterHTTPResponse(statusCode: 404, body: Data())
            }
        }
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("StreamVueMediaCenterTests-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let service = MediaCenterService(
            httpClient: http,
            secretStore: MediaCenterMemorySecretStore(),
            plexClientIdentifier: "streamvue-repository-test"
        )
        let repository = MediaCenterRepository(directory: directory, service: service)

        let connected = try await repository.connectPlex(
            serverAddress: "https://plex.home:32400",
            token: token
        )
        let channel = try #require(connected.catalog.channels.first)
        let plan = try await repository.playbackPlan(for: channel.stream.uri)
        #expect(plan.requestHeaders["X-Plex-Token"] == token)

        let snapshotURL = directory.appendingPathComponent("media-center-source.json")
        let serialized = String(data: try Data(contentsOf: snapshotURL), encoding: .utf8) ?? ""
        #expect(!serialized.contains(token))
        #expect(!serialized.contains("X-Plex-Token"))

        let restarted = MediaCenterRepository(directory: directory, service: service)
        let restored = try await restarted.loadSaved()
        #expect(restored?.catalog.channels.map(\.name) == ["Repository Movie"])
        #expect(try await restarted.currentConnection()?.serverID == "plex-repository-server")
    }

    @Test("Requires explicit consent before sending credentials over HTTP")
    func requiresCleartextConsent() async throws {
        let http = StubMediaCenterHTTPClient { request in
            #expect(request.headers["X-Plex-Token"] == nil)
            return jsonResponse(#"{"MediaContainer":{"machineIdentifier":"plex-http-server","friendlyName":"Local Plex"}}"#)
        }
        let service = MediaCenterService(
            httpClient: http,
            secretStore: MediaCenterMemorySecretStore()
        )

        do {
            _ = try await service.connectPlex(
                serverAddress: "http://192.168.1.8:32400",
                token: "local-plex-token"
            )
            Issue.record("HTTP credentials require an explicit insecure-transport confirmation.")
        } catch let error as MediaCenterError {
            #expect(error == .insecureTransportConsentRequired)
        }
        #expect(await http.requests().isEmpty)

        let connection = try await service.connectPlex(
            serverAddress: "http://192.168.1.8:32400",
            token: "local-plex-token",
            allowInsecureHTTP: true
        )
        #expect(connection.serverID == "plex-http-server")
        #expect(try await service.hasCredential(for: connection))
    }

    @Test("Accepts only canonical credential-free internal playback locators")
    func canonicalPlaybackLocators() throws {
        let locator = MediaCenterPlaybackLocator(
            provider: .plex,
            serverID: "plex-server-1",
            itemID: "item:100"
        )
        let uri = try MediaCenterLocator.playbackURI(for: locator)
        #expect(try MediaCenterLocator.parsePlaybackURI(uri) == locator)

        for unsafe in [
            "streamvue-media://user:password@plex/plex-server-1/item-1",
            "streamvue-media://plex:123/plex-server-1/item-1",
            "streamvue-media://plex/plex-server-1/item-1?X-Plex-Token=secret",
            "streamvue-media://plex/plex-server-1/item-1#access-token",
            "streamvue-media://plex/plex-server-1/item-1/",
            " streamvue-media://plex/plex-server-1/item-1 "
        ] {
            #expect(throws: MediaCenterError.unsafeProviderURL) {
                try MediaCenterLocator.parsePlaybackURI(unsafe)
            }
        }
    }

    @Test("Binds Plex credentials to the verified server and address")
    func bindsPlexCredential() async throws {
        let token = "plex-server-token-never-cache"
        let http = StubMediaCenterHTTPClient { request in
            switch request.url.path {
            case "/identity":
                jsonResponse(#"{"MediaContainer":{"machineIdentifier":"plex-server-1","friendlyName":"Home Plex","version":"1.41"}}"#)
            case "/library/sections":
                jsonResponse(#"{"MediaContainer":{"Directory":[{"key":"1","title":"Movies","type":"movie","totalSize":1}]}}"#)
            default:
                MediaCenterHTTPResponse(statusCode: 404, body: Data())
            }
        }
        let secrets = MediaCenterMemorySecretStore()
        let service = MediaCenterService(
            httpClient: http,
            secretStore: secrets,
            device: .appleDefault,
            plexClientIdentifier: "streamvue-test-device"
        )

        let connection = try await service.connectPlex(
            serverAddress: "https://plex.home:32400",
            token: token
        )
        #expect(connection.serverID == "plex-server-1")
        #expect(try await service.hasCredential(for: connection))
        let libraries = try await service.libraries(for: connection)
        #expect(libraries.map(\.title) == ["Movies"])

        let requests = await http.requests()
        let identityRequests = requests.filter { $0.url.path == "/identity" }
        #expect(identityRequests.count == 2)
        #expect(identityRequests.allSatisfy { $0.headers["X-Plex-Token"] == nil })
        let libraryRequest = try #require(requests.first { $0.url.path == "/library/sections" })
        #expect(libraryRequest.headers["X-Plex-Token"] == token)

        let attackerConnection = try MediaCenterConnection(
            provider: .plex,
            serverID: connection.serverID,
            displayName: connection.displayName,
            baseURL: "https://attacker.example.invalid",
            credentialID: connection.credentialID
        )
        do {
            _ = try await service.libraries(for: attackerConnection)
            Issue.record("A credential must not be reusable on a different origin.")
        } catch let error as MediaCenterError {
            #expect(error == .invalidCredential)
        }

        let encodedConnection = String(
            data: try JSONEncoder().encode(connection),
            encoding: .utf8
        ) ?? ""
        #expect(!encodedConnection.contains(token))
    }

    @Test("Authenticates and maps Emby without persisting passwords or tokens")
    func mapsEmbySecurely() async throws {
        let password = "emby-password-never-cache"
        let token = "emby-token-never-cache"
        let http = StubMediaCenterHTTPClient { request in
            switch request.url.path {
            case "/emby/System/Info/Public":
                jsonResponse(#"{"Id":"emby-server-1","ServerName":"Home Emby","Version":"4.9"}"#)
            case "/emby/Users/AuthenticateByName":
                jsonResponse(#"{"AccessToken":"emby-token-never-cache","ServerId":"emby-server-1","User":{"Id":"user-1","Name":"Chris"}}"#)
            case "/emby/Users/user-1/Views":
                jsonResponse(#"{"Items":[{"Id":"library-1","Name":"Movies","CollectionType":"movies","ChildCount":1}]}"#)
            case "/emby/Users/user-1/Items":
                jsonResponse(#"{"TotalRecordCount":1,"Items":[{"Id":"item-1","Name":"A Test Movie","Type":"Movie","RunTimeTicks":72000000000,"UserData":{"PlaybackPositionTicks":120000000,"Played":false},"ImageTags":{"Primary":"image-tag"},"MediaSources":[{"Id":"source-1","Container":"mkv","SupportsDirectPlay":true,"SupportsDirectStream":true,"SupportsTranscoding":true,"DirectStreamUrl":"/Videos/item-1/stream.mkv?api_key=upstream-secret","MediaStreams":[{"Index":0,"Type":"Video","Codec":"hevc","Width":3840,"Height":2160},{"Index":1,"Type":"Audio","Codec":"eac3","Language":"eng","Channels":6}]}]}]}"#)
            case "/emby/Items/item-1/PlaybackInfo":
                jsonResponse(#"{"PlaySessionId":"session-1","MediaSources":[{"Id":"source-1","SupportsDirectPlay":true,"SupportsDirectStream":true,"SupportsTranscoding":true,"DirectStreamUrl":"/Videos/item-1/stream.mkv?api_key=upstream-secret","RequiredHttpHeaders":{"X-Test":"allowed","X-Emby-Token":"attacker-value"}}]}"#)
            default:
                MediaCenterHTTPResponse(statusCode: 404, body: Data())
            }
        }
        let secrets = MediaCenterMemorySecretStore()
        let service = MediaCenterService(httpClient: http, secretStore: secrets)

        let connection = try await service.connectEmby(
            serverAddress: "https://emby.home",
            username: "chris",
            password: password
        )
        let snapshot = try await service.snapshot(for: connection)
        let item = try #require(snapshot.items.first)
        let playback = try await service.playbackPlan(for: item, connection: connection)
        let catalog = try MediaCenterCatalogFactory.create(from: snapshot)
        let serializedSnapshot = String(
            data: try JSONEncoder().encode(snapshot),
            encoding: .utf8
        ) ?? ""
        let serializedCatalog = String(
            data: try JSONEncoder().encode(catalog),
            encoding: .utf8
        ) ?? ""

        #expect(connection.serverID == "emby-server-1")
        #expect(connection.userID == "user-1")
        #expect(snapshot.libraries.map(\.title) == ["Movies"])
        #expect(playback.requestHeaders["X-Emby-Token"] == token)
        #expect(playback.requestHeaders["X-Test"] == "allowed")
        #expect(playback.requestHeaders["X-Emby-Token"] != "attacker-value")
        #expect(playback.url.query?.lowercased().contains("api_key") != true)
        #expect(catalog.channels.first?.stream.uri == "streamvue-media://emby/emby-server-1/item-1")
        #expect(!serializedSnapshot.contains(password))
        #expect(!serializedSnapshot.contains(token))
        #expect(!serializedSnapshot.contains("upstream-secret"))
        #expect(!serializedCatalog.contains(password))
        #expect(!serializedCatalog.contains(token))

        let requests = await http.requests()
        let publicProbes = requests.filter { $0.url.path == "/emby/System/Info/Public" }
        #expect(publicProbes.allSatisfy {
            $0.headers["X-Emby-Token"] == nil && !$0.headers.values.contains(token)
        })
        #expect(requests.allSatisfy { !$0.url.absoluteString.contains(password) })
        #expect(requests.allSatisfy { !$0.url.absoluteString.contains(token) })
    }
}

private actor StubMediaCenterHTTPClient: MediaCenterHTTPClient {
    typealias Handler = @Sendable (MediaCenterHTTPRequest) -> MediaCenterHTTPResponse

    private let handler: Handler
    private var capturedRequests: [MediaCenterHTTPRequest] = []

    init(handler: @escaping Handler) {
        self.handler = handler
    }

    func send(_ request: MediaCenterHTTPRequest) async throws -> MediaCenterHTTPResponse {
        capturedRequests.append(request)
        return handler(request)
    }

    func requests() -> [MediaCenterHTTPRequest] {
        capturedRequests
    }
}

private actor MediaCenterMemorySecretStore: SourceSecretStore {
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

private func jsonResponse(_ value: String) -> MediaCenterHTTPResponse {
    MediaCenterHTTPResponse(
        statusCode: 200,
        headers: ["Content-Type": "application/json"],
        body: Data(value.utf8)
    )
}
