import Foundation
import Testing
@testable import StreamVueCore

@Suite("Media-center security and mapping")
struct MediaCenterTests {
    @Test("Completes signed Plex account discovery without exposing account tokens")
    func signedPlexAccountDiscovery() async throws {
        let accountToken = "plex-account-token-never-in-url-or-ui"
        let serverToken = "plex-server-token-never-in-public-discovery"
        let now = try #require(ISO8601DateFormatter().date(from: "2026-08-30T12:00:00Z"))
        let signer = try PlexDeviceSigner(rawRepresentation: Data((0..<32).map { UInt8($0) }))
        let http = StubMediaCenterHTTPClient { request in
            let components = URLComponents(url: request.url, resolvingAgainstBaseURL: false)
            #expect(!request.url.absoluteString.contains(accountToken))
            #expect(!request.url.absoluteString.contains(serverToken))
            switch (request.url.host, request.url.path) {
            case ("clients.plex.tv", "/api/v2/pins"):
                let body = try? JSONSerialization.jsonObject(with: request.body ?? Data()) as? [String: Any]
                let jwk = body?["jwk"] as? [String: Any]
                #expect(request.method == .post)
                #expect(body?["strong"] as? Bool == true)
                #expect(jwk?["kty"] as? String == "OKP")
                #expect(jwk?["crv"] as? String == "Ed25519")
                #expect(jwk?["alg"] as? String == "EdDSA")
                #expect(jwk?["d"] == nil)
                return jsonResponse(#"{"id":42,"code":"SAFE-CODE","expiresIn":300}"#)
            case ("clients.plex.tv", "/api/v2/pins/42"):
                let proof = components?.queryItems?.first { $0.name == "deviceJWT" }?.value
                let segments = proof?.split(separator: ".") ?? []
                #expect(request.method == .get)
                #expect(segments.count == 3)
                if segments.count == 3,
                   let claimsData = Data(base64URLString: String(segments[1])),
                   let claims = try? JSONSerialization.jsonObject(with: claimsData) as? [String: Any] {
                    #expect(claims["aud"] as? String == "plex.tv")
                    #expect(claims["iss"] as? String == "streamvue-test-device")
                    #expect(claims["exp"] as? Int == (claims["iat"] as? Int ?? 0) + 300)
                }
                return jsonResponse(#"{"authToken":"plex-account-token-never-in-url-or-ui","expiresIn":604800}"#)
            case ("plex.tv", "/api/v2/user"):
                #expect(request.headers["X-Plex-Token"] == accountToken)
                return jsonResponse(#"{"id":1}"#)
            case ("clients.plex.tv", "/api/v2/resources"):
                #expect(request.headers["X-Plex-Token"] == accountToken)
                return jsonResponse(
                    #"[{"name":"Home Plex","clientIdentifier":"plex-server-1","provides":"server","owned":true,"accessToken":"plex-server-token-never-in-public-discovery","connections":[{"uri":"https://relay.example.invalid:443","local":false,"relay":true,"IPv6":false},{"uri":"http://192.168.1.8:32400","local":true,"relay":false,"IPv6":false},{"uri":"https://192-168-1-8.example.plex.direct:32400","local":true,"relay":false,"IPv6":false}]},{"name":"Living Room Player","clientIdentifier":"plex-player-1","provides":"player","accessToken":"ignored","connections":[{"uri":"https://player.example.invalid"}]},{"name":"Malicious resource","clientIdentifier":"plex-server-leak","provides":"server","accessToken":"server-secret","connections":[{"uri":"https://malicious.example.invalid/plex-account-token-never-in-url-or-ui"}]}]"#
                )
            default:
                return MediaCenterHTTPResponse(statusCode: 404, body: Data())
            }
        }
        let client = try PlexAccountClient(
            httpClient: http,
            clientIdentifier: "streamvue-test-device",
            product: "OrbitalVue\r\nX-Injected: blocked",
            version: "5.1-test",
            now: { now }
        )

        let challenge = try await client.createPin(signer: signer)
        let claimedToken = try await client.claimPin(challenge, signer: signer)
        let token = try #require(claimedToken)
        try await client.verifyAccountToken(token.value)
        let secrets = try await client.discoverServers(accountToken: token.value)
        let server = try #require(secrets.first?.server)

        #expect(challenge.authorizationURL.host == "app.plex.tv")
        #expect(challenge.authorizationURL.fragment?.contains("clientID=streamvue-test-device") == true)
        #expect(secrets.count == 1)
        #expect(server.serverID == "plex-server-1")
        #expect(server.preferredConnection?.url.absoluteString == "https://192-168-1-8.example.plex.direct:32400")
        #expect(!Mirror(reflecting: server).children.contains { $0.label == "accessToken" })
        let requests = await http.requests()
        #expect(requests.allSatisfy {
            !$0.url.absoluteString.contains(accountToken)
                && !$0.url.absoluteString.contains(serverToken)
                && $0.headers["X-Plex-Product"]?.contains("\r") != true
        })
    }

    @Test("Moves a discovered Plex server token directly into an origin-bound credential")
    func connectsDiscoveredPlexServerSecurely() async throws {
        let accountToken = "plex-account-transient-token"
        let serverToken = "plex-discovered-server-token"
        let http = StubMediaCenterHTTPClient { request in
            switch (request.url.host, request.url.path) {
            case ("clients.plex.tv", "/api/v2/pins"):
                return jsonResponse(#"{"id":7,"code":"DISCOVER","expiresIn":300}"#)
            case ("clients.plex.tv", "/api/v2/pins/7"):
                return jsonResponse(#"{"authToken":"plex-account-transient-token","expiresIn":604800}"#)
            case ("plex.tv", "/api/v2/user"):
                #expect(request.headers["X-Plex-Token"] == accountToken)
                return jsonResponse(#"{"id":1}"#)
            case ("clients.plex.tv", "/api/v2/resources"):
                #expect(request.headers["X-Plex-Token"] == accountToken)
                return jsonResponse(
                    #"[{"name":"Home Plex","clientIdentifier":"plex-server-7","provides":"server","owned":true,"accessToken":"plex-discovered-server-token","connections":[{"uri":"https://plex.home:32400","local":true,"relay":false,"IPv6":false}]}]"#
                )
            case ("plex.home", "/identity"):
                #expect(request.headers["X-Plex-Token"] == nil)
                return jsonResponse(#"{"MediaContainer":{"machineIdentifier":"plex-server-7","friendlyName":"Home Plex"}}"#)
            case ("plex.home", "/library/sections"):
                #expect(request.headers["X-Plex-Token"] == serverToken)
                return jsonResponse(#"{"MediaContainer":{"Directory":[]}}"#)
            default:
                return MediaCenterHTTPResponse(statusCode: 404, body: Data())
            }
        }
        let secrets = MediaCenterMemorySecretStore()
        let service = MediaCenterService(
            httpClient: http,
            secretStore: secrets,
            plexClientIdentifier: "streamvue-test-device"
        )

        let challenge = try await service.createPlexSignInChallenge()
        let completedDiscovery = try await service.completePlexSignIn(challenge: challenge)
        let discovery = try #require(completedDiscovery)
        let server = try #require(discovery.servers.first)
        let selected = try #require(server.preferredConnection)
        let connection = try await service.connectDiscoveredPlexServer(
            sessionID: discovery.sessionID,
            serverID: server.serverID,
            connectionURL: selected.url
        )
        let libraries = try await service.libraries(for: connection)
        let serializedConnection = String(
            data: try JSONEncoder().encode(connection),
            encoding: .utf8
        ) ?? ""
        let storedValues = await secrets.allValues()

        #expect(libraries.isEmpty)
        #expect(!serializedConnection.contains(accountToken))
        #expect(!serializedConnection.contains(serverToken))
        #expect(!String(reflecting: discovery).contains(accountToken))
        #expect(!String(reflecting: discovery).contains(serverToken))
        #expect(storedValues.values.allSatisfy { !$0.contains(accountToken) })
        #expect(storedValues.values.contains { $0.contains(serverToken) })

        await #expect(throws: MediaCenterError.discoverySessionExpired) {
            try await service.connectDiscoveredPlexServer(
                sessionID: discovery.sessionID,
                serverID: server.serverID,
                connectionURL: selected.url
            )
        }
    }

    @Test("Rejects a Plex connection when the selected server identity changes")
    func rejectsMismatchedDiscoveredPlexIdentity() async throws {
        let token = "must-not-be-saved-for-the-wrong-server"
        let http = StubMediaCenterHTTPClient { request in
            #expect(request.url.path == "/identity")
            #expect(request.headers["X-Plex-Token"] == nil)
            return jsonResponse(
                #"{"MediaContainer":{"machineIdentifier":"different-server","friendlyName":"Wrong Plex"}}"#
            )
        }
        let secrets = MediaCenterMemorySecretStore()
        let service = MediaCenterService(
            httpClient: http,
            secretStore: secrets,
            plexClientIdentifier: "streamvue-test-device"
        )

        await #expect(throws: MediaCenterError.providerMismatch) {
            try await service.connectPlex(
                serverAddress: "https://plex.home:32400",
                token: token,
                expectedServerID: "selected-server"
            )
        }

        let storedValues = await secrets.allValues()
        #expect(storedValues.values.allSatisfy { !$0.contains(token) })
    }

    @Test("Cancelling a Plex discovery connection rolls back its credential")
    func cancelledDiscoveryConnectionRollsBackCredential() async throws {
        let serverToken = "cancelled-discovery-server-token"
        let http = PausingPlexIdentityHTTPClient(serverToken: serverToken)
        let secrets = MediaCenterMemorySecretStore()
        let service = MediaCenterService(
            httpClient: http,
            secretStore: secrets,
            plexClientIdentifier: "streamvue-test-device"
        )
        let challenge = try await service.createPlexSignInChallenge()
        let completedDiscovery = try await service.completePlexSignIn(challenge: challenge)
        let discovery = try #require(completedDiscovery)
        let server = try #require(discovery.servers.first)
        let selected = try #require(server.preferredConnection)

        let connectionTask = Task {
            try await service.connectDiscoveredPlexServer(
                sessionID: discovery.sessionID,
                serverID: server.serverID,
                connectionURL: selected.url
            )
        }
        await http.waitUntilIdentityRequested()
        await service.cancelPlexDiscovery(sessionID: discovery.sessionID)
        connectionTask.cancel()
        await http.releaseIdentity()

        do {
            _ = try await connectionTask.value
            Issue.record("The cancelled discovery connection completed unexpectedly.")
        } catch is CancellationError {
        } catch {
            Issue.record("The cancelled discovery connection returned the wrong error: \(error)")
        }
        let storedValues = await secrets.allValues()
        #expect(storedValues.values.allSatisfy { !$0.contains(serverToken) })
    }

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
                return jsonResponse(#"{"MediaContainer":{"offset":0,"totalSize":1,"Metadata":[{"ratingKey":"movie-1","title":"Repository Movie","type":"movie","year":2026,"duration":7200000,"viewOffset":900000,"addedAt":1777593600,"lastViewedAt":1778457600,"Media":[{"id":"media-1","Part":[{"id":"part-1","key":"/library/parts/1/movie.mkv"}]}]}]}}"#)
            case "/:/timeline":
                return MediaCenterHTTPResponse(statusCode: 200, body: Data())
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
        #expect(channel.media?.year == 2026)
        #expect(channel.media?.addedAt == "2026-05-01T00:00:00.000Z")
        #expect(channel.media?.lastPlayedAt == "2026-05-11T00:00:00.000Z")
        #expect(channel.canResume)
        let plan = try await repository.playbackPlan(for: channel.stream.uri)
        #expect(plan.requestHeaders["X-Plex-Token"] == token)
        let sessionID = try #require(plan.playSessionID)
        try await repository.reportPlayback(
            sessionID: sessionID,
            report: MediaCenterPlaybackReport(
                kind: .started,
                state: .playing,
                positionMS: 900_000,
                durationMS: 7_200_000
            )
        )
        try await repository.reportPlayback(
            sessionID: sessionID,
            report: MediaCenterPlaybackReport(
                kind: .stopped,
                state: .playing,
                positionMS: 8_000_000,
                durationMS: 7_200_000
            )
        )
        let timelineRequests = await http.requests().filter { $0.url.path == "/:/timeline" }
        #expect(timelineRequests.count == 2)
        #expect(timelineRequests.map { queryValue("state", in: $0.url) } == ["playing", "stopped"])
        #expect(timelineRequests.map { queryValue("time", in: $0.url) } == ["900000", "7200000"])
        #expect(timelineRequests.allSatisfy {
            $0.headers["X-Plex-Session-Identifier"] == sessionID &&
                !$0.url.absoluteString.contains(token)
        })

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
                jsonResponse(#"{"TotalRecordCount":1,"Items":[{"Id":"item-1","Name":"A Test Movie","Type":"Movie","ProductionYear":2026,"DateCreated":"2026-05-01T00:00:00Z","RunTimeTicks":72000000000,"UserData":{"PlaybackPositionTicks":120000000,"Played":false,"LastPlayedDate":"2026-05-11T00:00:00Z"},"ImageTags":{"Primary":"image-tag"},"MediaSources":[{"Id":"source-1","Container":"mkv","SupportsDirectPlay":true,"SupportsDirectStream":true,"SupportsTranscoding":true,"DirectStreamUrl":"/Videos/item-1/stream.mkv?api_key=upstream-secret","MediaStreams":[{"Index":0,"Type":"Video","Codec":"hevc","Width":3840,"Height":2160},{"Index":1,"Type":"Audio","Codec":"eac3","Language":"eng","Channels":6}]}]}]}"#)
            case "/emby/Items/item-1/PlaybackInfo":
                jsonResponse(#"{"PlaySessionId":"session-1","MediaSources":[{"Id":"source-1","SupportsDirectPlay":true,"SupportsDirectStream":true,"SupportsTranscoding":true,"DirectStreamUrl":"/Videos/item-1/stream.mkv?api_key=upstream-secret","RequiredHttpHeaders":{"X-Test":"allowed","X-Emby-Token":"attacker-value"}}]}"#)
            case "/emby/Sessions/Playing", "/emby/Sessions/Playing/Progress", "/emby/Sessions/Playing/Stopped":
                MediaCenterHTTPResponse(statusCode: 200, body: Data())
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
        try await service.reportPlayback(
            plan: playback,
            report: MediaCenterPlaybackReport(
                kind: .started,
                state: .playing,
                positionMS: 12_000,
                durationMS: 7_200_000,
                volumePercent: 101
            ),
            connection: connection
        )
        try await service.reportPlayback(
            plan: playback,
            report: MediaCenterPlaybackReport(
                kind: .progress,
                state: .paused,
                positionMS: 8_000_000,
                durationMS: 7_200_000,
                event: .pause
            ),
            connection: connection
        )
        try await service.reportPlayback(
            plan: playback,
            report: MediaCenterPlaybackReport(
                kind: .stopped,
                state: .playing,
                positionMS: -1
            ),
            connection: connection
        )
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
        #expect(item.addedAt == "2026-05-01T00:00:00.000Z")
        #expect(item.lastPlayedAt == "2026-05-11T00:00:00.000Z")
        #expect(catalog.channels.first?.media?.year == 2026)
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
        let playbackReports = requests.filter { $0.url.path.hasPrefix("/emby/Sessions/Playing") }
        #expect(playbackReports.map(\.url.path) == [
            "/emby/Sessions/Playing",
            "/emby/Sessions/Playing/Progress",
            "/emby/Sessions/Playing/Stopped"
        ])
        let startedReport = jsonObject(playbackReports[0].body)
        let pausedReport = jsonObject(playbackReports[1].body)
        let stoppedReport = jsonObject(playbackReports[2].body)
        #expect(startedReport["PlaySessionId"] as? String == "session-1")
        #expect(startedReport["PositionTicks"] as? Int == 120_000_000)
        #expect(startedReport["VolumeLevel"] as? Int == 100)
        #expect(pausedReport["PositionTicks"] as? Int == 72_000_000_000)
        #expect(pausedReport["EventName"] as? String == "Pause")
        #expect(stoppedReport["PositionTicks"] as? Int == 0)
        let publicProbes = requests.filter { $0.url.path == "/emby/System/Info/Public" }
        #expect(publicProbes.allSatisfy {
            $0.headers["X-Emby-Token"] == nil && !$0.headers.values.contains(token)
        })
        #expect(requests.allSatisfy { !$0.url.absoluteString.contains(password) })
        #expect(requests.allSatisfy { !$0.url.absoluteString.contains(token) })
    }

    @Test("Every media-center item kind reaches the catalog, with audio presented as music")
    func mapsEveryItemKindToACatalogChannel() throws {
        let connection = try MediaCenterConnection(
            provider: .plex,
            serverID: "plex-server-1",
            displayName: "Plex",
            baseURL: "https://plex.home:32400",
            credentialID: "vault-plex-1"
        )
        let kinds = MediaCenterItemKind.allCases
        let items = kinds.enumerated().map { index, kind in
            MediaCenterItem(
                id: "item-\(index)",
                provider: .plex,
                serverID: "plex-server-1",
                libraryID: "library-1",
                libraryTitle: "Library",
                kind: kind,
                title: "Item \(index)",
                played: false,
                mediaSources: []
            )
        }
        let snapshot = MediaCenterSnapshot(
            loadedAt: "2026-09-01T12:00:00Z",
            connection: connection,
            libraries: [],
            items: items
        )

        let catalog = try MediaCenterCatalogFactory.create(from: snapshot)

        // A kind with no presentation is skipped, which is how a Plex or Emby music library
        // used to browse as empty. Nothing may drop out.
        #expect(catalog.channels.count == items.count)
        let audioIndex = try #require(kinds.firstIndex(of: .audio))
        #expect(catalog.channels[audioIndex].kind == .music)
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

private actor PausingPlexIdentityHTTPClient: MediaCenterHTTPClient {
    private let serverToken: String
    private var identityRequested = false
    private var identityContinuation: CheckedContinuation<Void, Never>?
    private var identityReleased = false

    init(serverToken: String) {
        self.serverToken = serverToken
    }

    func send(_ request: MediaCenterHTTPRequest) async throws -> MediaCenterHTTPResponse {
        switch (request.url.host, request.url.path) {
        case ("clients.plex.tv", "/api/v2/pins"):
            return jsonResponse(#"{"id":8,"code":"CANCEL","expiresIn":300}"#)
        case ("clients.plex.tv", "/api/v2/pins/8"):
            return jsonResponse(#"{"authToken":"transient-account-token","expiresIn":604800}"#)
        case ("plex.tv", "/api/v2/user"):
            return jsonResponse(#"{"id":1}"#)
        case ("clients.plex.tv", "/api/v2/resources"):
            return jsonResponse(
                #"[{"name":"Home Plex","clientIdentifier":"plex-server-cancel","provides":"server","owned":true,"accessToken":"\#(serverToken)","connections":[{"uri":"https://plex.cancel:32400","local":true,"relay":false,"IPv6":false}]}]"#
            )
        case ("plex.cancel", "/identity"):
            identityRequested = true
            if !identityReleased {
                await withCheckedContinuation { continuation in
                    identityContinuation = continuation
                }
            }
            return jsonResponse(
                #"{"MediaContainer":{"machineIdentifier":"plex-server-cancel","friendlyName":"Home Plex"}}"#
            )
        default:
            return MediaCenterHTTPResponse(statusCode: 404, body: Data())
        }
    }

    func waitUntilIdentityRequested() async {
        while !identityRequested {
            await Task.yield()
        }
    }

    func releaseIdentity() {
        identityReleased = true
        identityContinuation?.resume()
        identityContinuation = nil
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

    func allValues() -> [String: String] {
        values
    }
}

private func jsonResponse(_ value: String) -> MediaCenterHTTPResponse {
    MediaCenterHTTPResponse(
        statusCode: 200,
        headers: ["Content-Type": "application/json"],
        body: Data(value.utf8)
    )
}

private func queryValue(_ name: String, in url: URL) -> String? {
    URLComponents(url: url, resolvingAgainstBaseURL: false)?
        .queryItems?
        .first { $0.name == name }?
        .value
}

private func jsonObject(_ data: Data?) -> [String: Any] {
    guard let data,
          let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
        return [:]
    }
    return object
}

private extension Data {
    init?(base64URLString: String) {
        var value = base64URLString
            .replacingOccurrences(of: "-", with: "+")
            .replacingOccurrences(of: "_", with: "/")
        value.append(String(repeating: "=", count: (4 - value.count % 4) % 4))
        self.init(base64Encoded: value)
    }
}
