import CryptoKit
import Foundation

/// Coordinates authentication, secure credential lookup, browsing, and
/// ephemeral playback resolution without ever adding credentials to a catalog.
public actor MediaCenterService {
    private let httpClient: any MediaCenterHTTPClient
    private let secretStore: any SourceSecretStore
    private let device: MediaCenterDeviceIdentity
    private let configuredPlexClientIdentifier: String?
    private var cachedPlexClientIdentifier: String?
    private var plexDiscoverySessions: [String: PlexAccountDiscoverySecret] = [:]
    private var plexDiscoveryConnectionsInFlight: Set<String> = []
    private var cancelledPlexDiscoverySessions: Set<String> = []

    public init(
        httpClient: any MediaCenterHTTPClient = URLSessionMediaCenterHTTPClient(),
        secretStore: any SourceSecretStore = KeychainSourceSecretStore(
            service: "com.streamvue.player.media-center"
        ),
        device: MediaCenterDeviceIdentity = .appleDefault,
        plexClientIdentifier: String? = nil
    ) {
        self.httpClient = httpClient
        self.secretStore = secretStore
        self.device = device
        self.configuredPlexClientIdentifier = plexClientIdentifier
    }

    /// Connects directly to one Plex Media Server with a user-supplied server
    /// token. Plex account PIN login and account-wide server discovery are a
    /// separate layer and are intentionally not required by this core API.
    public func connectPlex(
        serverAddress: String,
        token rawToken: String,
        displayName: String? = nil,
        allowInsecureHTTP: Bool = false,
        expectedServerID: String? = nil
    ) async throws -> MediaCenterConnection {
        let baseURL = try MediaCenterURLPolicy.normalizeBaseURL(serverAddress)
        try MediaCenterURLPolicy.requireAllowedTransport(
            baseURL,
            allowInsecureHTTP: allowInsecureHTTP
        )
        let token = try MediaCenterHeaderPolicy.credential(rawToken)
        let clientIdentifier = try await plexClientIdentifier()
        let identity = try await PlexMediaCenterClient.discoverIdentity(
            httpClient: httpClient,
            baseURL: baseURL,
            clientIdentifier: clientIdentifier,
            device: device
        )
        if let expectedServerID {
            let expected = try MediaCenterURLPolicy.requireIdentifier(
                expectedServerID,
                label: "Plex server"
            )
            guard identity.serverID == expected else {
                throw MediaCenterError.providerMismatch
            }
        }
        let requestedName = displayName?.trimmingCharacters(in: .whitespacesAndNewlines)
        let name = MediaCenterTextPolicy.metadata(
            requestedName?.isEmpty == false ? requestedName! : identity.name,
            redacting: token,
            maximumLength: 256
        )
        let credentialID = credentialReference(
            provider: .plex,
            serverID: identity.serverID,
            baseURL: baseURL,
            userID: nil
        )
        let connection = try MediaCenterConnection(
            provider: .plex,
            serverID: identity.serverID,
            displayName: name.nonEmptyMediaCenterText ?? "Plex",
            baseURL: baseURL.absoluteString,
            credentialID: credentialID
        )
        try await saveCredential(
            token,
            for: connection,
            allowInsecureHTTP: allowInsecureHTTP
        )
        return connection
    }

    /// Starts Plex's strong PIN flow with a stable Ed25519 device-registration
    /// key held in Keychain. The returned challenge contains no account or
    /// server token.
    public func createPlexSignInChallenge() async throws -> PlexPinChallenge {
        purgeExpiredPlexDiscoverySessions()
        let signer = try await plexDeviceSigner()
        let client = try await plexAccountClient()
        return try await client.createPin(signer: signer)
    }

    /// Polls a PIN once. A nil result means the user has not approved it yet.
    /// Once approved, the Plex account token is verified and discarded after
    /// server-scoped resources have been converted into an opaque session.
    public func completePlexSignIn(
        challenge: PlexPinChallenge
    ) async throws -> PlexServerDiscovery? {
        purgeExpiredPlexDiscoverySessions()
        let signer = try await plexDeviceSigner()
        let client = try await plexAccountClient()
        guard let accountToken = try await client.claimPin(challenge, signer: signer) else {
            return nil
        }
        try await client.verifyAccountToken(accountToken.value)
        let servers = try await client.discoverServers(accountToken: accountToken.value)
        guard !servers.isEmpty else { throw MediaCenterError.noDiscoveredServers }

        let expiresAt = min(
            accountToken.expiresAt ?? .distantFuture,
            Date().addingTimeInterval(10 * 60)
        )
        let sessionID = "plex-discovery-\(UUID().uuidString.lowercased())"
        plexDiscoverySessions[sessionID] = PlexAccountDiscoverySecret(
            servers: servers,
            expiresAt: expiresAt
        )
        return PlexServerDiscovery(
            sessionID: sessionID,
            servers: servers.map(\.server),
            expiresAt: expiresAt
        )
    }

    /// Moves one server-scoped token from the in-memory discovery lease into
    /// the existing origin-bound Keychain credential only after the selected
    /// server's public identity has been verified.
    public func connectDiscoveredPlexServer(
        sessionID: String,
        serverID: String,
        connectionURL: URL,
        allowInsecureHTTP: Bool = false
    ) async throws -> MediaCenterConnection {
        purgeExpiredPlexDiscoverySessions()
        guard let session = plexDiscoverySessions[sessionID], session.expiresAt > Date() else {
            plexDiscoverySessions.removeValue(forKey: sessionID)
            throw MediaCenterError.discoverySessionExpired
        }
        guard !plexDiscoveryConnectionsInFlight.contains(sessionID) else {
            throw MediaCenterError.discoveryConnectionInProgress
        }
        guard let secret = session.servers.first(where: { $0.server.serverID == serverID }),
              let selected = secret.server.connections.first(where: {
                  $0.url.absoluteString == connectionURL.absoluteString
              }) else {
            throw MediaCenterError.providerMismatch
        }
        plexDiscoveryConnectionsInFlight.insert(sessionID)
        defer {
            plexDiscoveryConnectionsInFlight.remove(sessionID)
            cancelledPlexDiscoverySessions.remove(sessionID)
        }
        let connection = try await connectPlex(
            serverAddress: selected.url.absoluteString,
            token: secret.accessToken,
            displayName: secret.server.name,
            allowInsecureHTTP: selected.isSecure ? false : allowInsecureHTTP,
            expectedServerID: secret.server.serverID
        )
        do {
            try Task.checkCancellation()
            if cancelledPlexDiscoverySessions.contains(sessionID) {
                throw CancellationError()
            }
            guard plexDiscoverySessions[sessionID] != nil else {
                throw MediaCenterError.discoverySessionExpired
            }
        } catch {
            try? await disconnect(connection)
            throw error
        }
        plexDiscoverySessions.removeValue(forKey: sessionID)
        return connection
    }

    public func cancelPlexDiscovery(sessionID: String) {
        plexDiscoverySessions.removeValue(forKey: sessionID)
        if plexDiscoveryConnectionsInFlight.contains(sessionID) {
            cancelledPlexDiscoverySessions.insert(sessionID)
        }
    }

    public func cancelAllPlexDiscovery() {
        cancelledPlexDiscoverySessions.formUnion(plexDiscoveryConnectionsInFlight)
        plexDiscoverySessions.removeAll(keepingCapacity: false)
    }

    /// Authenticates by name once. Only the returned access token is saved in
    /// the secure store; the password is never persisted.
    public func connectEmby(
        serverAddress: String,
        username rawUsername: String,
        password: String,
        displayName: String? = nil,
        allowInsecureHTTP: Bool = false
    ) async throws -> MediaCenterConnection {
        let baseURL = try MediaCenterURLPolicy.normalizeBaseURL(serverAddress)
        try MediaCenterURLPolicy.requireAllowedTransport(
            baseURL,
            allowInsecureHTTP: allowInsecureHTTP
        )
        let username = rawUsername.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !username.isEmpty, username.count <= 256,
              !password.isEmpty, password.utf8.count <= 16_384 else {
            throw MediaCenterError.invalidCredential
        }
        let authentication = try await EmbyMediaCenterClient.authenticate(
            httpClient: httpClient,
            baseURL: baseURL,
            username: username,
            password: password,
            device: device
        )
        let token = try MediaCenterHeaderPolicy.credential(authentication.accessToken)
        let requestedName = displayName?.trimmingCharacters(in: .whitespacesAndNewlines)
        let name = MediaCenterTextPolicy.metadata(
            requestedName?.isEmpty == false ? requestedName! : "Emby",
            redacting: token,
            maximumLength: 256
        )
        let credentialID = credentialReference(
            provider: .emby,
            serverID: authentication.serverID,
            baseURL: baseURL,
            userID: authentication.userID
        )
        let connection = try MediaCenterConnection(
            provider: .emby,
            serverID: authentication.serverID,
            displayName: name.nonEmptyMediaCenterText ?? "Emby",
            baseURL: baseURL.absoluteString,
            credentialID: credentialID,
            userID: authentication.userID
        )
        try await saveCredential(
            token,
            for: connection,
            allowInsecureHTTP: allowInsecureHTTP
        )
        return connection
    }

    public func libraries(for connection: MediaCenterConnection) async throws -> [MediaCenterLibrary] {
        let token = try await credential(for: connection)
        switch connection.provider {
        case .plex:
            let client = try await plexClient(connection: connection, token: token)
            return try await client.libraries()
        case .emby:
            return try await embyClient(connection: connection, token: token).libraries()
        }
    }

    public func items(
        in library: MediaCenterLibrary,
        for connection: MediaCenterConnection,
        start: Int = 0,
        size: Int = 200
    ) async throws -> MediaCenterPage<MediaCenterItem> {
        let page = MediaCenterPageBounds(start: start, size: size)
        let token = try await credential(for: connection)
        switch connection.provider {
        case .plex:
            let client = try await plexClient(connection: connection, token: token)
            return try await client.items(in: library, page: page)
        case .emby:
            return try await embyClient(connection: connection, token: token)
                .items(in: library, page: page)
        }
    }

    public func snapshot(
        for connection: MediaCenterConnection,
        maximumItemsPerLibrary: Int = 200,
        loadedAt: Date = Date()
    ) async throws -> MediaCenterSnapshot {
        guard maximumItemsPerLibrary > 0 else { throw MediaCenterError.invalidPage }
        let pageSize = min(maximumItemsPerLibrary, MediaCenterPageBounds.maximumSize)
        let libraries = try await libraries(for: connection)
        var allItems: [MediaCenterItem] = []
        for library in libraries {
            let page = try await items(
                in: library,
                for: connection,
                start: 0,
                size: pageSize
            )
            allItems.append(contentsOf: page.items)
        }
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return MediaCenterSnapshot(
            loadedAt: formatter.string(from: loadedAt),
            connection: connection,
            libraries: libraries,
            items: allItems
        )
    }

    public func loadCatalog(
        for connection: MediaCenterConnection,
        maximumItemsPerLibrary: Int = 200,
        loadedAt: Date = Date()
    ) async throws -> LoadedCatalog {
        let snapshot = try await snapshot(
            for: connection,
            maximumItemsPerLibrary: maximumItemsPerLibrary,
            loadedAt: loadedAt
        )
        return LoadedCatalog(
            catalog: try MediaCenterCatalogFactory.create(from: snapshot),
            notice: "\(connection.provider.displayName) library refreshed",
            usedCachedFallback: false
        )
    }

    public func playbackPlan(
        for item: MediaCenterItem,
        connection: MediaCenterConnection,
        mediaSourceID: String? = nil,
        startPositionMS: Int? = nil
    ) async throws -> MediaCenterPlaybackPlan {
        try requireOwnership(of: item, by: connection)
        let token = try await credential(for: connection)
        switch connection.provider {
        case .plex:
            let client = try await plexClient(connection: connection, token: token)
            return try client.playbackPlan(for: item, mediaSourceID: mediaSourceID)
        case .emby:
            return try await embyClient(connection: connection, token: token).playbackPlan(
                for: item,
                mediaSourceID: mediaSourceID,
                startPositionMS: max(0, startPositionMS ?? item.resumePositionMS ?? 0)
            )
        }
    }

    public func artworkPlan(
        for item: MediaCenterItem,
        connection: MediaCenterConnection,
        maximumWidth: Int = 640
    ) async throws -> MediaCenterPlaybackPlan? {
        try requireOwnership(of: item, by: connection)
        guard item.artworkPath != nil else { return nil }
        let token = try await credential(for: connection)
        switch connection.provider {
        case .plex:
            let client = try await plexClient(connection: connection, token: token)
            return try client.artworkPlan(for: item, maximumWidth: maximumWidth)
        case .emby:
            return try embyClient(connection: connection, token: token)
                .artworkPlan(for: item, maximumWidth: maximumWidth)
        }
    }

    public func hasCredential(for connection: MediaCenterConnection) async throws -> Bool {
        (try? await credential(for: connection)) != nil
    }

    public func disconnect(_ connection: MediaCenterConnection) async throws {
        try await secretStore.removeValue(for: connection.credentialID)
    }

    private func credential(for connection: MediaCenterConnection) async throws -> String {
        guard let rawValue = try await secretStore.value(for: connection.credentialID) else {
            throw MediaCenterError.missingCredential
        }
        let expectedBaseURL = try normalizedAddress(for: connection)
        guard let data = rawValue.data(using: .utf8),
              let stored = try? JSONDecoder().decode(MediaCenterVaultCredential.self, from: data),
              stored.contractVersion == streamVueMediaCenterContractVersion,
              stored.provider == connection.provider,
              stored.serverID == connection.serverID,
              stored.userID == connection.userID,
              stored.baseURL == expectedBaseURL else {
            throw MediaCenterError.invalidCredential
        }
        try MediaCenterURLPolicy.requireAllowedTransport(
            MediaCenterURLPolicy.normalizeBaseURL(stored.baseURL),
            allowInsecureHTTP: stored.allowInsecureHTTP
        )
        return try MediaCenterHeaderPolicy.credential(stored.value)
    }

    private func saveCredential(
        _ value: String,
        for connection: MediaCenterConnection,
        allowInsecureHTTP: Bool
    ) async throws {
        let stored = MediaCenterVaultCredential(
            provider: connection.provider,
            serverID: connection.serverID,
            userID: connection.userID,
            baseURL: try normalizedAddress(for: connection),
            allowInsecureHTTP: allowInsecureHTTP,
            value: try MediaCenterHeaderPolicy.credential(value)
        )
        let data = try JSONEncoder().encode(stored)
        guard let encoded = String(data: data, encoding: .utf8) else {
            throw MediaCenterError.invalidCredential
        }
        try await secretStore.save(encoded, for: connection.credentialID)
    }

    private func normalizedAddress(for connection: MediaCenterConnection) throws -> String {
        try MediaCenterURLPolicy.normalizeBaseURL(connection.baseURL).absoluteString
    }

    private func plexClient(
        connection: MediaCenterConnection,
        token: String
    ) async throws -> PlexMediaCenterClient {
        let clientIdentifier = try await plexClientIdentifier()
        return try PlexMediaCenterClient(
            httpClient: httpClient,
            connection: connection,
            token: token,
            clientIdentifier: clientIdentifier,
            device: device
        )
    }

    private func plexAccountClient() async throws -> PlexAccountClient {
        let clientIdentifier = try await plexClientIdentifier()
        return try PlexAccountClient(
            httpClient: httpClient,
            clientIdentifier: clientIdentifier,
            product: device.client,
            version: device.version
        )
    }

    private func plexClientIdentifier() async throws -> String {
        if let configuredPlexClientIdentifier {
            return try MediaCenterURLPolicy.requireIdentifier(
                configuredPlexClientIdentifier,
                label: "Plex client"
            )
        }
        if let cachedPlexClientIdentifier { return cachedPlexClientIdentifier }
        let reference = "plex-account-client-identifier-v1"
        if let stored = try await secretStore.value(for: reference) {
            let identifier = try MediaCenterURLPolicy.requireIdentifier(
                stored,
                label: "Plex client"
            )
            cachedPlexClientIdentifier = identifier
            return identifier
        }
        let identifier = "streamvue-apple-\(UUID().uuidString.lowercased())"
        let validated = try MediaCenterURLPolicy.requireIdentifier(identifier, label: "Plex client")
        try await secretStore.save(validated, for: reference)
        cachedPlexClientIdentifier = validated
        return validated
    }

    private func plexDeviceSigner() async throws -> PlexDeviceSigner {
        let identifier = try await plexClientIdentifier()
        let digest = SHA256.hash(data: Data(identifier.utf8))
            .map { String(format: "%02x", $0) }
            .joined()
        let reference = "plex-account-ed25519-v1-\(digest.prefix(32))"
        if let encoded = try await secretStore.value(for: reference) {
            guard let data = Data(base64Encoded: encoded), data.count == 32,
                  let signer = try? PlexDeviceSigner(rawRepresentation: data) else {
                throw MediaCenterError.invalidCredential
            }
            return signer
        }
        let signer = PlexDeviceSigner()
        try await secretStore.save(
            signer.rawRepresentation.base64EncodedString(),
            for: reference
        )
        return signer
    }

    private func purgeExpiredPlexDiscoverySessions(now: Date = Date()) {
        plexDiscoverySessions = plexDiscoverySessions.filter { $0.value.expiresAt > now }
    }

    private func embyClient(connection: MediaCenterConnection, token: String) throws -> EmbyMediaCenterClient {
        try EmbyMediaCenterClient(
            httpClient: httpClient,
            connection: connection,
            token: token,
            device: device
        )
    }

    private func requireOwnership(of item: MediaCenterItem, by connection: MediaCenterConnection) throws {
        guard item.provider == connection.provider, item.serverID == connection.serverID else {
            throw MediaCenterError.providerMismatch
        }
    }

    private func credentialReference(
        provider: MediaCenterProvider,
        serverID: String,
        baseURL: URL,
        userID: String?
    ) -> String {
        let identity = "\(provider.rawValue)|\(serverID)|\(baseURL.absoluteString)|\(userID ?? "server")"
        let digest = SHA256.hash(data: Data(identity.utf8))
            .map { String(format: "%02X", $0) }
            .joined()
        return "mc-\(provider.rawValue)-\(digest.prefix(48))"
    }
}

private struct MediaCenterVaultCredential: Codable, Sendable {
    let contractVersion: String
    let provider: MediaCenterProvider
    let serverID: String
    let userID: String?
    let baseURL: String
    let allowInsecureHTTP: Bool
    let value: String

    private enum CodingKeys: String, CodingKey {
        case contractVersion
        case provider
        case serverID = "serverId"
        case userID = "userId"
        case baseURL = "baseUrl"
        case allowInsecureHTTP
        case value
    }

    init(
        provider: MediaCenterProvider,
        serverID: String,
        userID: String?,
        baseURL: String,
        allowInsecureHTTP: Bool,
        value: String
    ) {
        self.contractVersion = streamVueMediaCenterContractVersion
        self.provider = provider
        self.serverID = serverID
        self.userID = userID
        self.baseURL = baseURL
        self.allowInsecureHTTP = allowInsecureHTTP
        self.value = value
    }
}

struct MediaCenterPageBounds: Sendable {
    static let maximumSize = 500

    let start: Int
    let size: Int

    init(start: Int, size: Int) {
        self.start = max(0, start)
        self.size = min(Self.maximumSize, max(1, size))
    }
}

enum MediaCenterAPI {
    static func json(
        _ request: MediaCenterHTTPRequest,
        using client: any MediaCenterHTTPClient
    ) async throws -> MediaCenterJSON {
        let response = try await client.send(request)
        guard response.body.count <= request.maximumResponseBytes else {
            throw MediaCenterError.responseTooLarge(maximumBytes: request.maximumResponseBytes)
        }
        guard (200...299).contains(response.statusCode) else {
            throw MediaCenterError.serverStatus(response.statusCode)
        }
        do {
            return try JSONDecoder().decode(MediaCenterJSON.self, from: response.body)
        } catch {
            throw MediaCenterError.invalidResponse
        }
    }
}

enum MediaCenterTextPolicy {
    static func metadata(_ rawValue: String, redacting credential: String, maximumLength: Int) -> String {
        var value = rawValue
            .map { $0.isNewline || $0.isASCIIControlCharacter ? " " : $0 }
            .reduce(into: "") { $0.append($1) }
            .trimmingCharacters(in: .whitespacesAndNewlines)
        if !credential.isEmpty {
            value = value.replacingOccurrences(of: credential, with: "[redacted]")
        }
        return String(value.prefix(maximumLength))
    }

    static func optionalMetadata(
        _ rawValue: String?,
        redacting credential: String,
        maximumLength: Int
    ) -> String? {
        guard let rawValue else { return nil }
        return metadata(rawValue, redacting: credential, maximumLength: maximumLength)
            .nonEmptyMediaCenterText
    }
}

extension String {
    var nonEmptyMediaCenterText: String? { isEmpty ? nil : self }
}

private extension Character {
    var isASCIIControlCharacter: Bool {
        unicodeScalars.allSatisfy { $0.value < 32 || $0.value == 127 }
    }
}
